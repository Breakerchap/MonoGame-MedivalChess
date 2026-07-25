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

  public async Task<ActionResult> ChooseRoyal(RoyalSelectionRequest request)
  {
    ActionResult result = matches.ChooseRoyal(Context.ConnectionId, request);
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
      if (!NetworkBoardRules.Contains(foundMatch.Configuration, request.ToX, request.ToY) ||
          foundMatch.Pieces.Any(other => other.Id != piece.Id && other.X == request.ToX && other.Y == request.ToY))
      {
        return new(false, "That destination is not available.", foundMatch.State());
      }

      if (!NetworkMovementRules.IsLegal(piece, request.ToX, request.ToY))
      {
        return new(false, "That move is outside the unit's movement rule.", foundMatch.State());
      }

      foundMatch.Pieces[index] = piece with { X = request.ToX, Y = request.ToY };
      player.ActionsRemaining--;
      if (player.ActionsRemaining <= 0)
      {
        player.ActionsRemaining = ActionsPerTurn;
        foundMatch.CurrentTurn = foundMatch.CurrentTurn == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
      }

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
      MatchReady
    );
    internal RoomJoinResult ResultFor(PlayerSlot player) => new(true, null, Code, player.Team, player.ReconnectToken, State());
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
