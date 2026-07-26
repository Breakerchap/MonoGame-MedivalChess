using System.Collections.Concurrent;
using MedivalChess.Shared;
using Microsoft.AspNetCore.SignalR;

namespace MedivalChess.Server;

public sealed class MatchHub(MatchStore matches) : Hub
{
  public async Task<RoomJoinResult> CreateGame(CreateGameRequest request)
  {
    RoomJoinResult result = matches.Create(Context.ConnectionId, request);
    if (result.Accepted && result.JoinCode is not null)
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, result.JoinCode);
    }

    return result;
  }

  public async Task<RoomJoinResult> JoinGame(JoinGameRequest request)
  {
    RoomJoinResult result = matches.Join(Context.ConnectionId, request);
    if (result.Accepted && result.JoinCode is not null && result.State is not null)
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, result.JoinCode);
      await Clients.Group(result.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> AttemptMove(MoveRequest request)
  {
    ActionResult result = matches.TryMove(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> AttemptAttack(AttackRequest request)
  {
    ActionResult result = matches.TryAttack(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> ChooseRoyal(RoyalSelectionRequest request)
  {
    ActionResult result = matches.ChooseRoyal(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> PurchaseInitialUnit(PurchaseRequest request)
  {
    ActionResult result = matches.PurchaseInitialUnit(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> PurchaseUnit(PurchaseRequest request)
  {
    ActionResult result = matches.PurchaseUnit(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> StopInitialBuying()
  {
    ActionResult result = matches.StopInitialBuying(Context.ConnectionId);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public override Task OnDisconnectedAsync(Exception? exception)
  {
    matches.Disconnect(Context.ConnectionId);
    return base.OnDisconnectedAsync(exception);
  }
}

public sealed class MatchStore
{
  private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  private const int ActionsPerTurn = 3;
  private static readonly TimeSpan JoinAttemptCooldown = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan EmptyRoomLifetime = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan InactiveMatchLifetime = TimeSpan.FromHours(2);
  private readonly ConcurrentDictionary<string, Match> _matches = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, DateTimeOffset> _lastJoinAttemptByConnection = new();

  public RoomJoinResult Create(string connectionId, CreateGameRequest request)
  {
    CleanupExpired();
    if (request is null)
    {
      return new(false, "Choose match settings before hosting.", null, null, null, null);
    }

    if (!TryValidateConfiguration(request.Configuration, out NetworkMatchConfiguration? configuration, out string? error))
    {
      return new(false, error, null, null, null, null);
    }

    NetworkMatchConfiguration validConfiguration = configuration!;

    string code;
    NetworkTeam hostTeam = Random.Shared.Next(2) == 0 ? NetworkTeam.Red : NetworkTeam.Blue;
    do
    {
      code = string.Concat(Enumerable.Range(0, 5).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]));
    } while (!_matches.TryAdd(code, new Match(
      code,
      validConfiguration,
      new PlayerSlot(connectionId, hostTeam, validConfiguration.StartingCash)
    )));

    Match match = _matches[code];
    return match.ResultFor(match.Host);
  }

  public RoomJoinResult Join(string connectionId, JoinGameRequest request)
  {
    CleanupExpired();
    if (request is null)
    {
      return new(false, "Enter a room code.", null, null, null, null);
    }

    if (!CanAttemptJoin(connectionId))
    {
      return new(false, "Please wait half a second before trying another room code.", null, null, null, null);
    }

    if (string.IsNullOrWhiteSpace(request.JoinCode) ||
        !_matches.TryGetValue(request.JoinCode.Trim().ToUpperInvariant(), out Match? match))
    {
      return new(false, "Room not found.", null, null, null, null);
    }

    lock (match.Sync)
    {
      if (!string.IsNullOrWhiteSpace(request.ReconnectToken))
      {
        PlayerSlot? reconnectingPlayer = match.FindPlayerByToken(request.ReconnectToken);
        if (reconnectingPlayer is null)
        {
          return new(false, "Reconnect token is not valid for this room.", null, null, null, null);
        }

        reconnectingPlayer.ConnectionId = connectionId;
        reconnectingPlayer.DisconnectedAt = null;
        match.Touch();
        return match.ResultFor(reconnectingPlayer);
      }

      if (match.Guest is not null)
      {
        return new(false, "Room already has two players.", null, null, null, null);
      }

      NetworkTeam guestTeam = match.Host.Team == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
      match.Guest = new PlayerSlot(connectionId, guestTeam, match.Configuration.StartingCash);
      match.Touch();
      return match.ResultFor(match.Guest);
    }
  }

  public ActionResult TryMove(string connectionId, MoveRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.PieceId))
    {
      return new(false, "Choose a piece and destination.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (!foundMatch.MatchReady)
      {
        return new(false, "Both players must choose a royal first.", foundMatch.State());
      }

      if (foundMatch.InitialBuy is { IsComplete: false })
      {
        return new(false, "Finish the initial buy phase before moving units.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }

      int index = foundMatch.Pieces.FindIndex(piece => piece.Id == request.PieceId);
      if (index < 0 || foundMatch.Pieces[index].Team != player.Team)
      {
        return new(false, "You may only move your own pieces.", foundMatch.State());
      }

      NetworkPiece piece = foundMatch.Pieces[index];
      if (piece.HasMovedThisTurn)
      {
        return new(false, "That unit has already moved this turn.", foundMatch.State());
      }

      if (!NetworkBoardRules.Contains(foundMatch.Configuration, request.ToX, request.ToY) ||
          foundMatch.Pieces.Any(other => other.Id != piece.Id && other.X == request.ToX && other.Y == request.ToY))
      {
        return new(false, "That destination is not available.", foundMatch.State());
      }

      if (!NetworkMovementRules.IsLegal(piece, request.ToX, request.ToY))
      {
        return new(false, "That move is outside the unit's movement rule.", foundMatch.State());
      }

      foundMatch.Pieces[index] = piece with { X = request.ToX, Y = request.ToY, HasMovedThisTurn = true };
      SpendAction(foundMatch, player);

      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult TryAttack(string connectionId, AttackRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.AttackerId) || string.IsNullOrWhiteSpace(request.TargetId))
    {
      return new(false, "Choose an attacking unit and an enemy target.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (!foundMatch.MatchReady || foundMatch.InitialBuy is { IsComplete: false })
      {
        return new(false, "The match is not ready for attacks.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }

      int attackerIndex = foundMatch.Pieces.FindIndex(piece => piece.Id == request.AttackerId);
      int targetIndex = foundMatch.Pieces.FindIndex(piece => piece.Id == request.TargetId);
      if (attackerIndex < 0 || targetIndex < 0)
      {
        return new(false, "That unit is no longer on the board.", foundMatch.State());
      }

      NetworkPiece attacker = foundMatch.Pieces[attackerIndex];
      NetworkPiece target = foundMatch.Pieces[targetIndex];
      if (attacker.Team != player.Team || target.Team == player.Team || attacker.HasAttackedThisTurn)
      {
        return new(false, "That attack is not available.", foundMatch.State());
      }

      if (!NetworkAttackRules.IsLegal(attacker, target))
      {
        return new(false, "That target is outside the unit's attack pattern.", foundMatch.State());
      }

      int damage = NetworkAttackRules.GetDamage(attacker.Type);
      if (damage <= 0)
      {
        return new(false, "That unit cannot make a direct attack.", foundMatch.State());
      }

      foundMatch.Pieces[attackerIndex] = attacker with { HasAttackedThisTurn = true };
      int remainingHealth = target.Health - damage;
      if (remainingHealth <= 0)
      {
        foundMatch.Pieces.RemoveAt(targetIndex);
      }
      else
      {
        foundMatch.Pieces[targetIndex] = target with { Health = remainingHealth };
      }

      SpendAction(foundMatch, player);
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult ChooseRoyal(string connectionId, RoyalSelectionRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.RoyalType))
    {
      return new(false, "Choose a royal.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (player is null || foundMatch.Guest is null)
      {
        return new(false, "Wait for the other player before choosing a royal.", foundMatch.State());
      }

      if (player.ChosenRoyal is not null)
      {
        return new(false, "You have already chosen your royal.", foundMatch.State());
      }

      if (!RoyalTypes.Contains(request.RoyalType) ||
          (foundMatch.Configuration.GameMode == "Escort" && request.RoyalType == "Palace"))
      {
        return new(false, "That royal is not available for this match.", foundMatch.State());
      }

      (int width, int height, int health) = GetRoyalStats(request.RoyalType);
      (int x, int y) spawn = NetworkBoardRules.GetRoyalSpawn(foundMatch.Configuration, player.Team, width, height);
      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), request.RoyalType, player.Team, spawn.x, spawn.y, health));
      player.ChosenRoyal = request.RoyalType;
      if (foundMatch.MatchReady)
      {
        foundMatch.InitialBuy ??= new OpeningBuyPhase(
          foundMatch.Configuration.InitialBuysPerTurn,
          foundMatch.Configuration.InitialBuyTurnsPerTeam
        );
        foundMatch.CurrentTurn = foundMatch.InitialBuy.CurrentTeam;
      }
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult PurchaseInitialUnit(string connectionId, PurchaseRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.PieceType))
    {
      return new(false, "Choose a unit and placement square.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      OpeningBuyPhase? buyPhase = foundMatch.InitialBuy;
      if (player is null || buyPhase is null || buyPhase.IsComplete)
      {
        return new(false, "The initial buy phase is not active.", foundMatch.State());
      }

      if (player.Team != buyPhase.CurrentTeam)
      {
        return new(false, "It is not your initial buy turn.", foundMatch.State());
      }

      if (!TryGetPurchasableUnit(request.PieceType, out UnitPurchaseInfo unit))
      {
        return new(false, "That unit is not available during the initial buy phase.", foundMatch.State());
      }

      if (player.Money < unit.Cost ||
          !NetworkBoardRules.CanPlaceForTeam(foundMatch.Configuration, player.Team, request.X, request.Y, unit.Width, unit.Height) ||
          foundMatch.Pieces.Any(piece => FootprintsOverlap(piece, request.X, request.Y, unit.Width, unit.Height)))
      {
        return new(false, "Place an affordable unit on an empty square on your side.", foundMatch.State());
      }

      player.Money -= unit.Cost;
      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), unit.Type, player.Team, request.X, request.Y, unit.Health));
      buyPhase.RecordPurchase();
      foundMatch.CurrentTurn = buyPhase.IsComplete ? NetworkTeam.Red : buyPhase.CurrentTeam;
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult PurchaseUnit(string connectionId, PurchaseRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.PieceType))
    {
      return new(false, "Choose a unit and placement square.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (!foundMatch.MatchReady || foundMatch.InitialBuy is { IsComplete: false })
      {
        return new(false, "The opening buy phase must finish first.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }

      if (!TryGetPurchasableUnit(request.PieceType, out UnitPurchaseInfo unit, includeMercenary: true))
      {
        return new(false, "That unit is not available for purchase.", foundMatch.State());
      }

      bool validPlacement = unit.Type == "Mercenary"
        ? NetworkBoardRules.CanPlaceMercenary(foundMatch.Configuration, request.X, request.Y)
        : NetworkBoardRules.CanPlaceForTeam(foundMatch.Configuration, player.Team, request.X, request.Y, unit.Width, unit.Height);
      if (player.Money < unit.Cost || !validPlacement ||
          foundMatch.Pieces.Any(piece => FootprintsOverlap(piece, request.X, request.Y, unit.Width, unit.Height)))
      {
        return new(false, "Place an affordable unit on a valid empty square.", foundMatch.State());
      }

      player.Money -= unit.Cost;
      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), unit.Type, player.Team, request.X, request.Y, unit.Health));
      SpendAction(foundMatch, player);
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult StopInitialBuying(string connectionId)
  {
    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      OpeningBuyPhase? buyPhase = foundMatch.InitialBuy;
      if (player is null || buyPhase is null || buyPhase.IsComplete || player.Team != buyPhase.CurrentTeam)
      {
        return new(false, "It is not your initial buy turn.", foundMatch.State());
      }

      buyPhase.StopCurrentBuyer();
      foundMatch.CurrentTurn = buyPhase.IsComplete ? NetworkTeam.Red : buyPhase.CurrentTeam;
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public void Disconnect(string connectionId)
  {
    foreach (Match match in _matches.Values)
    {
      lock (match.Sync)
      {
        PlayerSlot? player = match.FindPlayerByConnection(connectionId);
        if (player is not null)
        {
          player.ConnectionId = null;
          player.DisconnectedAt = DateTimeOffset.UtcNow;
          match.Touch();
          return;
        }
      }
    }
  }

  public void CleanupExpired()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    foreach ((string code, Match match) in _matches)
    {
      lock (match.Sync)
      {
        bool playerTimedOut = match.Players.Any(player =>
          player.DisconnectedAt is DateTimeOffset disconnectedAt && now - disconnectedAt > DisconnectGracePeriod
        );
        bool roomTimedOut = match.Guest is null && match.Host.ConnectionId is null &&
          now - match.CreatedAt > EmptyRoomLifetime;
        bool matchInactive = match.Players.All(player => player.ConnectionId is null) &&
          now - match.LastActivity > InactiveMatchLifetime;
        if (playerTimedOut || roomTimedOut || matchInactive)
        {
          _matches.TryRemove(code, out _);
        }
      }
    }

    foreach ((string connectionId, DateTimeOffset lastAttempt) in _lastJoinAttemptByConnection)
    {
      if (now - lastAttempt > DisconnectGracePeriod)
      {
        _lastJoinAttemptByConnection.TryRemove(connectionId, out _);
      }
    }
  }

  private bool CanAttemptJoin(string connectionId)
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    if (_lastJoinAttemptByConnection.TryGetValue(connectionId, out DateTimeOffset lastAttempt) &&
        now - lastAttempt < JoinAttemptCooldown)
    {
      return false;
    }

    _lastJoinAttemptByConnection[connectionId] = now;
    return true;
  }

  private bool TryGetMatch(string connectionId, out Match? match)
  {
    foreach (Match candidate in _matches.Values)
    {
      lock (candidate.Sync)
      {
        if (candidate.FindPlayerByConnection(connectionId) is not null)
        {
          match = candidate;
          return true;
        }
      }
    }

    match = null;
    return false;
  }

  private static bool TryValidateConfiguration(
    NetworkMatchConfiguration configuration,
    out NetworkMatchConfiguration? sanitized,
    out string? error
  )
  {
    sanitized = null;
    error = null;
    if (configuration is null ||
        !new[] { "Small", "Medium", "Large" }.Contains(configuration.BoardSize) ||
        !new[] { "Light", "Standard", "Heavy" }.Contains(configuration.ForestDensity) ||
        !new[] { "Light", "Standard", "Heavy" }.Contains(configuration.WaterwayDensity) ||
        !new[] { "Regicide", "Conquest", "Escort" }.Contains(configuration.GameMode) ||
        configuration.StartingCash is < 0 or > 5000 ||
        configuration.InitialBuysPerTurn < 1 ||
        configuration.InitialBuyTurnsPerTeam < 1 ||
        configuration.ConquestWinScore < 1 ||
        !float.IsFinite(configuration.KillerRefundMultiplier) ||
        !float.IsFinite(configuration.DefeatedTeamRefundMultiplier) ||
        configuration.KillerRefundMultiplier is < -10 or > 10 ||
        configuration.DefeatedTeamRefundMultiplier is < -10 or > 10)
    {
      error = "The proposed match settings are not valid.";
      return false;
    }

    sanitized = configuration;
    return true;
  }

  private static (int width, int height, int health) GetRoyalStats(string type) => type switch
  {
    "King" => (1, 1, 120),
    "Princess" => (1, 1, 80),
    "Palace" => (3, 2, 160),
    "Baron" => (1, 1, 100),
    "Emissary" => (1, 1, 80),
    _ => throw new InvalidOperationException("Unknown royal.")
  };

  private static readonly HashSet<string> RoyalTypes = new(StringComparer.Ordinal)
  {
    "King", "Princess", "Palace", "Baron", "Emissary"
  };

  private sealed record UnitPurchaseInfo(string Type, int Cost, int Health, int Width = 1, int Height = 1);

  private static bool TryGetPurchasableUnit(string type, out UnitPurchaseInfo unit, bool includeMercenary = false)
  {
    UnitPurchaseInfo? found = type switch
    {
      "Soldier" => new(type, 20, 15),
      "Defender" => new(type, 20, 30),
      "Archer" => new(type, 25, 10),
      "Spearman" => new(type, 20, 15),
      "Knight" => new(type, 40, 25),
      "Crossbowman" => new(type, 40, 15),
      "Cavalier" => new(type, 40, 20),
      "Chariot" => new(type, 35, 25),
      "Cannon" => new(type, 40, 25, 1, 2),
      "Spy" => new(type, 35, 15),
      "Catapult" => new(type, 50, 20, 2, 2),
      "Teacher" => new(type, 30, 10),
      "Ox" => new(type, 40, 25),
      "Engineer" => new(type, 45, 15),
      "Ballista" => new(type, 55, 20, 2, 2),
      "Elephant" => new(type, 55, 50, 2, 2),
      "Guard" => new(type, 25, 25),
      "Mercenary" when includeMercenary => new(type, 45, 20),
      _ => null
    };
    if (found is null)
    {
      unit = null!;
      return false;
    }

    unit = found;
    return true;
  }

  private static bool FootprintsOverlap(NetworkPiece existing, int x, int y, int width, int height)
  {
    (int existingWidth, int existingHeight) = existing.Type switch
    {
      "Palace" => (3, 2),
      "Cannon" => (1, 2),
      "Catapult" or "Ballista" or "Elephant" => (2, 2),
      _ => (1, 1)
    };
    return existing.X < x + width && existing.X + existingWidth > x &&
      existing.Y < y + height && existing.Y + existingHeight > y;
  }

  private static void ResetTurnActions(Match match, NetworkTeam team)
  {
    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece piece = match.Pieces[index];
      if (piece.Team == team)
      {
        match.Pieces[index] = piece with { HasMovedThisTurn = false, HasAttackedThisTurn = false };
      }
    }
  }

  private static void SpendAction(Match match, PlayerSlot player)
  {
    player.ActionsRemaining--;
    if (player.ActionsRemaining > 0)
    {
      return;
    }

    player.ActionsRemaining = ActionsPerTurn;
    match.CurrentTurn = match.CurrentTurn == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
    ResetTurnActions(match, match.CurrentTurn);
  }

  private sealed class PlayerSlot(string? connectionId, NetworkTeam team, int money)
  {
    internal string? ConnectionId { get; set; } = connectionId;
    internal NetworkTeam Team { get; } = team;
    internal string ReconnectToken { get; } = Convert.ToHexString(Guid.NewGuid().ToByteArray());
    internal DateTimeOffset? DisconnectedAt { get; set; }
    internal int Money { get; set; } = money;
    internal int ActionsRemaining { get; set; } = ActionsPerTurn;
    internal string? ChosenRoyal { get; set; }
  }

  private sealed class Match(string code, NetworkMatchConfiguration configuration, PlayerSlot host)
  {
    internal object Sync { get; } = new();
    internal string Code { get; } = code;
    internal NetworkMatchConfiguration Configuration { get; } = configuration;
    internal PlayerSlot Host { get; } = host;
    internal PlayerSlot? Guest { get; set; }
    internal List<PlayerSlot> Players => Guest is null ? [Host] : [Host, Guest];
    internal List<NetworkPiece> Pieces { get; } = [];
    internal OpeningBuyPhase? InitialBuy { get; set; }
    internal NetworkTeam CurrentTurn { get; set; } = NetworkTeam.Red;
    internal long Version { get; set; }
    internal DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    internal DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    internal bool MatchReady => Guest is not null && Host.ChosenRoyal is not null && Guest.ChosenRoyal is not null;
    internal PlayerSlot? FindPlayerByConnection(string connectionId) => Players.FirstOrDefault(player => player.ConnectionId == connectionId);
    internal PlayerSlot? FindPlayerByToken(string token) => Players.FirstOrDefault(player => player.ReconnectToken == token);
    internal void Touch() => LastActivity = DateTimeOffset.UtcNow;
    internal NetworkGameState State() => new(
      Code,
      CurrentTurn,
      Pieces.ToArray(),
      Players.Select(player => new NetworkTeamState(player.Team, player.Money, player.ActionsRemaining, player.ChosenRoyal)).ToArray(),
      Configuration,
      Version,
      Guest is null ? 1 : 2,
      MatchReady,
      InitialBuy?.ToNetworkState()
    );
    internal RoomJoinResult ResultFor(PlayerSlot player) => new(true, null, Code, player.Team, player.ReconnectToken, State());
  }

  private sealed class OpeningBuyPhase(int purchasesPerTurn, int buyTurnsPerTeam)
  {
    private int _redBuyTurnsUsed;
    private int _blueBuyTurnsUsed;
    private bool _redStopped;
    private bool _blueStopped;

    internal NetworkTeam CurrentTeam { get; private set; } = NetworkTeam.Red;
    internal int PurchasesThisTurn { get; private set; }
    internal int PurchasesPerTurn { get; } = Math.Max(1, purchasesPerTurn);
    internal int BuyTurnsPerTeam { get; } = Math.Max(1, buyTurnsPerTeam);
    internal bool IsComplete { get; private set; }

    internal void RecordPurchase()
    {
      PurchasesThisTurn++;
      if (PurchasesThisTurn >= PurchasesPerTurn)
      {
        FinishCurrentTurn(false);
      }
    }

    internal void StopCurrentBuyer() => FinishCurrentTurn(true);

    internal NetworkInitialBuyState ToNetworkState() => new(
      CurrentTeam,
      PurchasesThisTurn,
      PurchasesPerTurn,
      _redBuyTurnsUsed,
      _blueBuyTurnsUsed,
      BuyTurnsPerTeam,
      _redStopped,
      _blueStopped,
      IsComplete
    );

    private void FinishCurrentTurn(bool stopped)
    {
      if (IsComplete)
      {
        return;
      }

      if (CurrentTeam == NetworkTeam.Red)
      {
        if (stopped) _redStopped = true; else _redBuyTurnsUsed++;
      }
      else
      {
        if (stopped) _blueStopped = true; else _blueBuyTurnsUsed++;
      }

      PurchasesThisTurn = 0;
      if (!CanKeepBuying(NetworkTeam.Red) && !CanKeepBuying(NetworkTeam.Blue))
      {
        IsComplete = true;
        return;
      }

      NetworkTeam other = CurrentTeam == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
      if (CanKeepBuying(other))
      {
        CurrentTeam = other;
      }
    }

    private bool CanKeepBuying(NetworkTeam team) => team == NetworkTeam.Red
      ? !_redStopped && _redBuyTurnsUsed < BuyTurnsPerTeam
      : !_blueStopped && _blueBuyTurnsUsed < BuyTurnsPerTeam;
  }
}

internal static class NetworkBoardRules
{
  private static (int minX, int minY, int width, int height) GetBounds(NetworkMatchConfiguration configuration) => configuration.BoardSize switch
  {
    "Small" => (-9, -11, 19, 23),
    "Large" => (-11, -13, 23, 27),
    _ => (-10, -12, 21, 25)
  };

  internal static bool Contains(NetworkMatchConfiguration configuration, int x, int y)
  {
    (int minX, int minY, int width, int height) = GetBounds(configuration);
    return x >= minX && x < minX + width && y >= minY && y < minY + height;
  }

  internal static (int x, int y) GetRoyalSpawn(NetworkMatchConfiguration configuration, NetworkTeam team, int width, int height)
  {
    (int minX, int minY, int boardWidth, int boardHeight) = GetBounds(configuration);
    return (
      minX + (boardWidth - width) / 2,
      team == NetworkTeam.Red ? minY + boardHeight - height : minY
    );
  }

  internal static bool CanPlaceForTeam(
    NetworkMatchConfiguration configuration,
    NetworkTeam team,
    int x,
    int y,
    int width,
    int height
  )
  {
    (int minX, int minY, int boardWidth, int boardHeight) = GetBounds(configuration);
    int centreRow = boardHeight / 2;
    int noMansLandHalfHeight = configuration.GameMode == "Conquest" ? 4 : 3;
    for (int offsetY = 0; offsetY < height; offsetY++)
    {
      int arrayY = y + offsetY - minY;
      bool ownedByTeam = team == NetworkTeam.Red
        ? arrayY > centreRow + noMansLandHalfHeight
        : arrayY < centreRow - noMansLandHalfHeight;
      if (!ownedByTeam)
      {
        return false;
      }
    }

    return x >= minX && x + width <= minX + boardWidth && y >= minY && y + height <= minY + boardHeight;
  }

  internal static bool CanPlaceMercenary(NetworkMatchConfiguration configuration, int x, int y)
  {
    (int minX, int minY, int boardWidth, int boardHeight) = GetBounds(configuration);
    int arrayY = y - minY;
    int centreRow = boardHeight / 2;
    int noMansLandHalfHeight = configuration.GameMode == "Conquest" ? 4 : 3;
    bool inNoMansLand = arrayY >= centreRow - noMansLandHalfHeight &&
      arrayY <= centreRow + noMansLandHalfHeight;
    bool onEdge = x == minX || x == minX + boardWidth - 1 || y == minY || y == minY + boardHeight - 1;
    return inNoMansLand && onEdge && Contains(configuration, x, y);
  }
}

internal static class NetworkMovementRules
{
  internal static bool IsLegal(NetworkPiece piece, int toX, int toY)
  {
    int dx = Math.Abs(toX - piece.X);
    int dy = Math.Abs(toY - piece.Y);
    if (dx == 0 && dy == 0)
    {
      return false;
    }

    (int range, bool straight) = piece.Type switch
    {
      "Soldier" or "Defender" or "King" => (piece.Type == "King" ? 1 : 2, true),
      "Chariot" or "Cannon" or "Ballista" => (piece.Type == "Ballista" ? 1 : 2, true),
      "Peasant" => (1, true),
      "Knight" => (3, false),
      "Cavalier" => (4, false),
      "Scout" or "Spy" or "Ambulance" => (piece.Type == "Scout" ? 4 : 5, false),
      "Teacher" => (4, false),
      "Engineer" => (3, false),
      "Elephant" => (2, true),
      "Emissary" => (3, false),
      "Palace" => (0, false),
      _ => (2, false)
    };
    return straight ? (dx == 0 || dy == 0) && dx + dy <= range : Math.Max(dx, dy) <= range;
  }
}

internal static class NetworkAttackRules
{
  private enum Pattern { None, Any, Straight, Forward, ForwardDiagonal }

  internal static int GetDamage(string type) => type switch
  {
    "Soldier" => 10, "Defender" => 5, "Archer" => 10, "Scout" => 5,
    "Spearman" => 15, "Peasant" => 5, "Knight" => 20, "Crossbowman" => 20,
    "Cavalier" => 15, "Chariot" => 15, "Cannon" => 30, "Catapult" => 20,
    "Ox" => 5, "Ballista" => 25, "Guard" => 10, "Mercenary" => 25,
    "Assassin" => 30, "King" => 15, "Princess" => 15, "Baron" => 5, "Emissary" => 5,
    _ => 0
  };

  internal static bool IsLegal(NetworkPiece attacker, NetworkPiece target)
  {
    (int range, int minimumRange, Pattern pattern) = attacker.Type switch
    {
      "Soldier" or "Defender" => (1, 1, Pattern.Straight),
      "Archer" => (4, 2, Pattern.Any),
      "Scout" => (1, 1, Pattern.Straight),
      "Spearman" or "Peasant" => (1, 1, Pattern.ForwardDiagonal),
      "Knight" or "Cavalier" or "Guard" or "Mercenary" or "Assassin" or "King" or "Baron" or "Emissary" => (1, 1, Pattern.Any),
      "Crossbowman" or "Princess" => (attacker.Type == "Princess" ? 3 : 3, 1, Pattern.Any),
      "Chariot" => (1, 1, Pattern.Straight),
      "Cannon" => (5, 2, Pattern.Straight),
      "Catapult" => (6, 3, Pattern.Any),
      "Ox" => (1, 1, Pattern.Forward),
      "Ballista" => (5, 2, Pattern.Straight),
      _ => (0, 0, Pattern.None)
    };
    int dx = target.X - attacker.X;
    int dy = target.Y - attacker.Y;
    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
    if (distance < minimumRange || distance > range)
    {
      return false;
    }

    return pattern switch
    {
      Pattern.Any => true,
      Pattern.Straight => dx == 0 || dy == 0,
      Pattern.Forward => dx == 0 && dy == (attacker.Team == NetworkTeam.Red ? -distance : distance),
      Pattern.ForwardDiagonal => dy == (attacker.Team == NetworkTeam.Red ? -distance : distance) && Math.Abs(dx) <= distance,
      _ => false
    };
  }
}
