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

  public async Task<ActionResult> SelectDebugTeam(DebugTeamSelectionRequest request)
  {
    ActionResult result = matches.SelectDebugTeam(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
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

  public async Task<ActionResult> AttemptSpecial(SpecialActionRequest request)
  {
    ActionResult result = matches.TrySpecial(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> SkipTurn(SkipTurnRequest request)
  {
    ActionResult result = matches.TrySkipTurn(Context.ConnectionId);
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
  public const string DebugJoinCode = "DEBUG";
  private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  private const int ActionsPerTurn = MatchRules.ActionsPerTurn;
  private static readonly TimeSpan JoinAttemptCooldown = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan EmptyRoomLifetime = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan InactiveMatchLifetime = TimeSpan.FromHours(2);
  private readonly ConcurrentDictionary<string, Match> _matches = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, DateTimeOffset> _lastJoinAttemptByConnection = new();

  public MatchStore()
  {
    NetworkMatchConfiguration configuration = new(
      "Medium",
      "Standard",
      "Standard",
      "Regicide",
      20260801,
      1000,
      0.5f,
      0f,
      2,
      4,
      MatchRules.DefaultConquestWinScore
    );
    Match debugMatch = new(
      DebugJoinCode,
      configuration,
      new PlayerSlot(null, NetworkTeam.Red, configuration.StartingCash),
      isDebugMatch: true
    );
    debugMatch.AddPlayer(new PlayerSlot(null, NetworkTeam.Blue, configuration.StartingCash));
    _matches[DebugJoinCode] = debugMatch;
  }

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
    IReadOnlyList<NetworkTeam> activeTeams = TeamRules.GetActiveTeams(validConfiguration.PlayerCount);
    NetworkTeam hostTeam = activeTeams[Random.Shared.Next(activeTeams.Count)];
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
      if (match.IsDebugMatch)
      {
        if (request.DebugTeam is NetworkTeam requestedTeam && !Enum.IsDefined(requestedTeam))
        {
          return new(false, "That debug side is not valid.", null, null, null, null);
        }

        NetworkTeam debugTeam = request.DebugTeam ?? match.CurrentTurn;
        match.RegisterDebugController(connectionId, debugTeam);
        match.Touch();
        return match.ResultFor(match.FindPlayerByConnection(connectionId)!);
      }

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

      if (match.Players.Count >= match.Configuration.PlayerCount)
      {
        return new(false, "Room already has the configured number of players.", null, null, null, null);
      }

      NetworkTeam guestTeam = TeamRules.GetActiveTeams(match.Configuration.PlayerCount)
        .First(team => match.Players.All(player => player.Team != team));
      PlayerSlot player = new(connectionId, guestTeam, match.Configuration.StartingCash);
      match.AddPlayer(player);
      match.Touch();
      return match.ResultFor(player);
    }
  }

  public ActionResult SelectDebugTeam(string connectionId, DebugTeamSelectionRequest request)
  {
    if (request is null || !Enum.IsDefined(request.Team))
    {
      return new(false, "Choose Orange or Purple.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match))
    {
      return new(false, "Join a room first.", null);
    }

    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      if (!foundMatch.IsDebugMatch || !foundMatch.IsDebugController(connectionId))
      {
        return new(false, "Side switching is only available in the DEBUG room.", foundMatch.State());
      }

      foundMatch.RegisterDebugController(connectionId, request.Team);
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
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
        return new(false, "Every player must choose a royal first.", foundMatch.State());
      }

      if (foundMatch.InitialBuy is { IsComplete: false })
      {
        return new(false, "Finish the initial buy phase before moving units.", foundMatch.State());
      }

      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }

      int index = foundMatch.Pieces.FindIndex(piece => piece.Id == request.PieceId);
      if (index < 0 || foundMatch.Pieces[index].Team != player.Team)
      {
        return new(false, "You may only move your own pieces.", foundMatch.State());
      }

      NetworkPiece piece = foundMatch.Pieces[index];
      if (piece.HasMovedThisTurn && !AbilityRules.CanUseCavalierFollowUpMove(
        piece.Type, piece.CavalierFollowUpMoveAvailable))
      {
        return new(false, "That unit has already moved this turn.", foundMatch.State());
      }

      if (piece.AttachedToId is not null && piece.AttachmentKind == NetworkAttachmentKind.Guard)
      {
        return new(false, "An attached Guard moves only with the unit it protects.", foundMatch.State());
      }
      if (piece.AttachedToId is not null)
      {
        // Carrying is voluntary: moving the cargo itself dismounts it.
        piece = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      }

      if (!CanLandAt(foundMatch, piece, UnitRules.GetRequired(piece.Type), (request.ToX, request.ToY)))
      {
        return new(false, "That destination is not available.", foundMatch.State());
      }

      if (!TryGetLegalMovementPath(foundMatch, piece, request.ToX, request.ToY, out List<(int x, int y)> movementPath))
      {
        return new(false, "That move is blocked by the board, terrain, or unit movement rule.", foundMatch.State());
      }

      bool elephantDamagedAnEnemy = false;
      if (piece.Type == "Elephant" && UnitRules.TryGet(piece.Type, out UnitRule elephantRule))
      {
        foreach (NetworkPiece crossed in foundMatch.Pieces.Where(other => other.Id != piece.Id && other.Team != piece.Team).ToArray())
        {
          if (UnitRules.TryGet(crossed.Type, out UnitRule crossedRule) && AbilityRules.PathOverlapsUnit(
            elephantRule, movementPath, crossedRule, crossed.X, crossed.Y))
          {
            ResolvePieceDamage(foundMatch, piece, player, crossed.Id, 15);
            elephantDamagedAnEnemy = true;
          }
        }
      }

      int pieceIndex = foundMatch.Pieces.FindIndex(candidate => candidate.Id == piece.Id);
      if (pieceIndex < 0) return new(false, "That unit is no longer on the board.", foundMatch.State());
      int oldX = piece.X;
      int oldY = piece.Y;
      piece = piece with
      {
        X = request.ToX,
        Y = request.ToY,
        HasMovedThisTurn = true,
        HasAttackedThisTurn = elephantDamagedAnEnemy || piece.HasAttackedThisTurn,
        CavalierFollowUpMoveAvailable = false
      };
      foundMatch.Pieces[pieceIndex] = piece;
      MoveAttachedPieces(foundMatch, piece, oldX, oldY);
      MoveEmissaryCompanions(foundMatch, piece, oldX, oldY);
      TriggerMinesAlongMovement(foundMatch, piece, movementPath);
      if (foundMatch.Pieces.Any(candidate => candidate.Id == piece.Id))
      {
        TryDeliverTreasure(foundMatch, piece);
      }
      if (foundMatch.Pieces.Any(candidate => candidate.Id == piece.Id) &&
          IsEscortVictory(foundMatch, piece, request.ToX, request.ToY))
      {
        foundMatch.Winner = piece.Team;
      }
      if (foundMatch.Winner is null)
      {
        SpendAction(foundMatch, player);
      }

      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult TryAttack(string connectionId, AttackRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.AttackerId))
    {
      return new(false, "Choose an attacking unit and target.", null);
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

      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }

      int attackerIndex = foundMatch.Pieces.FindIndex(piece => piece.Id == request.AttackerId);
      if (attackerIndex < 0)
      {
        return new(false, "That attacking unit is no longer on the board.", foundMatch.State());
      }

      NetworkPiece attacker = foundMatch.Pieces[attackerIndex];
      if (attacker.Team != player.Team || attacker.HasAttackedThisTurn)
      {
        return new(false, "That attack is not available.", foundMatch.State());
      }

      NetworkPiece? target = string.IsNullOrWhiteSpace(request.TargetId)
        ? null
        : foundMatch.Pieces.FirstOrDefault(piece => piece.Id == request.TargetId);
      bool attackingBarricade = target is null && request.TargetX is int barricadeX && request.TargetY is int barricadeY &&
        foundMatch.Barricades.ContainsKey((barricadeX, barricadeY));
      if (target is null && !attackingBarricade)
      {
        return new(false, "That target is no longer on the board.", foundMatch.State());
      }

      (int x, int y) targetPosition = target is null
        ? (request.TargetX!.Value, request.TargetY!.Value)
        : (target.X, target.Y);
      if (target is not null &&
          (target.Team == attacker.Team || target.AttachedToId is not null || !NetworkAttackRules.IsLegal(attacker, target)))
      {
        return new(false, "That attack is not available.", foundMatch.State());
      }

      if (target is { Type: "Farm" } && IsFarmCoveredByUnit(foundMatch, target))
      {
        return new(false, "Defeat the unit on that farm before attacking the farm itself.", foundMatch.State());
      }

      if (target is null && !CanUseActionSquare(attacker, targetPosition.x, targetPosition.y))
      {
        return new(false, "That barricade is outside the unit's attack pattern.", foundMatch.State());
      }

      if (target is not null
        ? !HasClearAttackPath(foundMatch, attacker, target)
        : !HasClearAttackPath(foundMatch, attacker, targetPosition, null))
      {
        return new(false, "Terrain, a barricade, or another unit blocks that attack.", foundMatch.State());
      }

      if (NetworkAttackRules.GetDamage(attacker.Type) <= 0)
      {
        return new(false, "That unit cannot make a direct attack.", foundMatch.State());
      }

      foundMatch.Pieces[attackerIndex] = attacker with
      {
        HasAttackedThisTurn = true,
        CavalierFollowUpMoveAvailable = AbilityRules.GrantsCavalierFollowUpMove(
          attacker.Type, attacker.HasMovedThisTurn)
      };
      if (target is null)
      {
        DamageBarricade(foundMatch, attacker, targetPosition);
      }
      else if (attacker.Type == "Bombard")
      {
        ResolveBombardDamage(foundMatch, attacker, player, target);
      }
      else
      {
        ResolvePieceDamage(foundMatch, attacker, player, target.Id, null);
      }

      if (target is not null && attacker.Type == "Ballista" && UnitRules.TryGet(attacker.Type, out UnitRule ballistaRule))
      {
        foreach ((int x, int y) position in AbilityRules.GetPiercingRay(ballistaRule, attacker.X, attacker.Y, target.X, target.Y))
        {
          if (!NetworkBoardRules.Contains(foundMatch.Configuration, position.x, position.y) ||
              foundMatch.Terrain.IsForest(position) || foundMatch.Barricades.ContainsKey(position)) break;
          NetworkPiece? pierced = foundMatch.Pieces.FirstOrDefault(piece => piece.Id != attacker.Id && piece.Id != target.Id && piece.Team != attacker.Team && piece.Type != "Farm" &&
            piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) && UnitRules.FootprintsOverlap(piece.X, piece.Y, rule.Width, rule.Height, position.x, position.y, 1, 1));
          if (pierced is not null) ResolvePieceDamage(foundMatch, attacker, player, pierced.Id, null);
        }
      }

      if (foundMatch.Winner is null) SpendAction(foundMatch, player);
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult TrySpecial(string connectionId, SpecialActionRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.ActorId))
    {
      return new(false, "Choose a unit and target for its special action.", null);
    }

    if (!TryGetMatch(connectionId, out Match? match)) return new(false, "Join a room first.", null);
    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (!foundMatch.MatchReady || foundMatch.InitialBuy is { IsComplete: false } ||
          player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "That special action is not available now.", foundMatch.State());
      }
      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }

      int actorIndex = foundMatch.Pieces.FindIndex(piece => piece.Id == request.ActorId);
      int targetIndex = string.IsNullOrWhiteSpace(request.TargetId)
        ? -1
        : foundMatch.Pieces.FindIndex(piece => piece.Id == request.TargetId);
      if (actorIndex < 0 || foundMatch.Pieces[actorIndex].Team != player.Team)
      {
        return new(false, "You may only use your own unit's special action.", foundMatch.State());
      }

      NetworkPiece actor = foundMatch.Pieces[actorIndex];
      bool engineerDemolition = actor.Type == "Engineer" && AbilityRules.IsEngineerDemolition(request.Ability);
      bool plunderPickup = foundMatch.Configuration.GameMode == "Plunder" &&
        string.Equals(request.Ability, "PickUpTreasure", StringComparison.OrdinalIgnoreCase);
      if (actor.HasAttackedThisTurn && !engineerDemolition)
      {
        return new(false, "That unit has already acted this turn.", foundMatch.State());
      }
      NetworkPiece? target = targetIndex >= 0 ? foundMatch.Pieces[targetIndex] : null;
      if (!plunderPickup && actor.Type != "Mercenary" && !CanUseActionSquare(actor, request.TargetX, request.TargetY))
      {
        return new(false, "That square is outside the unit's special-action range.", foundMatch.State());
      }

      bool applied = plunderPickup
        ? TryPickUpTreasure(foundMatch, actorIndex, request.TargetX, request.TargetY)
        : actor.Type switch
        {
          "Spy" => TryMarkSpyTarget(foundMatch, actorIndex, target),
          "Engineer" => TryUseEngineerSpecial(foundMatch, actorIndex, request.Ability, request.TargetX, request.TargetY, target),
          "Guard" => TryAttachGuard(foundMatch, actorIndex, targetIndex),
          "Ox" => TryAttachOxCargo(foundMatch, actorIndex, targetIndex),
          "Mercenary" => TryFireMercenary(foundMatch, actorIndex, request.Ability),
          _ => false
        };
      if (!applied) return new(false, "That special action has no valid target.", foundMatch.State());

      SpendAction(foundMatch, player);
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult TrySkipTurn(string connectionId)
  {
    if (!TryGetMatch(connectionId, out Match? match)) return new(false, "Join a room first.", null);
    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (!foundMatch.MatchReady || foundMatch.InitialBuy is { IsComplete: false } ||
          player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "Use at least one action before ending the turn.", foundMatch.State());
      }
      if (Globals.ActionLimitsEnabled && player.ActionsRemaining >= ActionsPerTurn && player.ChosenRoyal != "Palace")
      {
        return new(false, "Use at least one action before ending the turn.", foundMatch.State());
      }
      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }
      if (Globals.ActionLimitsEnabled)
      {
        player.ActionsRemaining = 1;
        SpendAction(foundMatch, player);
      }
      else
      {
        CompleteTurn(foundMatch, player);
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
      if (player is null || foundMatch.Players.Count < foundMatch.Configuration.PlayerCount)
      {
        return new(false, "Wait for every player to join before choosing a royal.", foundMatch.State());
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

      (int width, int height, int health) = GetRoyalStats(foundMatch.Configuration, request.RoyalType);
      (int x, int y) position = request.X is int requestedX && request.Y is int requestedY
        ? (requestedX, requestedY)
        // Keep the hub compatible with older clients while the current client always provides a placement.
        : NetworkBoardRules.GetRoyalSpawn(foundMatch.Configuration, player.Team, width, height);
      if (!CanPlaceRoyal(foundMatch, player.Team, position.x, position.y, width, height))
      {
        return new(false, "Place your royal on an empty, traversable square in your territory.", foundMatch.State());
      }

      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), request.RoyalType, player.Team, position.x, position.y, health));
      player.ChosenRoyal = request.RoyalType;
      if (foundMatch.MatchReady)
      {
        foundMatch.InitialBuy ??= new OpeningBuyPhase(
          foundMatch.Configuration.InitialBuysPerTurn,
          foundMatch.Configuration.InitialBuyTurnsPerTeam,
          foundMatch.Configuration.PlayerCount,
          foundMatch.Configuration.FarmsEnabled
        );
        foundMatch.CurrentTurn = foundMatch.InitialBuy.CurrentTeam;
        foundMatch.ResetClockTimestamp();
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
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }

      if (!TryGetPurchasableUnit(foundMatch, request.PieceType, out UnitPurchaseInfo unit))
      {
        return new(false, "That unit is not available during the initial buy phase.", foundMatch.State());
      }

      if (buyPhase.IsFarmPlacementPhase && unit.Type != "Farm")
      {
        return new(false, "Place your two farms before buying units.", foundMatch.State());
      }

      bool isOpeningFarmPlacement = buyPhase.IsFarmPlacementPhase && unit.Type == "Farm";
      if ((!isOpeningFarmPlacement && player.Money < unit.Cost) ||
          !CanPlacePurchasedUnit(foundMatch, unit, player.Team, request.X, request.Y, initialBuy: true))
      {
        return new(false, "Place an affordable unit on an empty square on your side.", foundMatch.State());
      }

      if (!isOpeningFarmPlacement) player.Money = ClampCurrency((long)player.Money - unit.Cost);
      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), unit.Type, player.Team, request.X, request.Y, unit.Health));
      buyPhase.RecordPurchase();
      if (buyPhase.IsComplete)
      {
        StartFirstTurn(foundMatch);
      }
      else
      {
        foundMatch.CurrentTurn = buyPhase.CurrentTeam;
      }
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

      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }

      int mercenaryIndex = foundMatch.Pieces.FindIndex(piece =>
        piece.Type == "Mercenary" && piece.X == request.X && piece.Y == request.Y);
      if (mercenaryIndex >= 0)
      {
        NetworkPiece mercenary = foundMatch.Pieces[mercenaryIndex];
        if (mercenary.Team != NetworkTeam.Neutral)
        {
          return new(false, "Only neutral Mercenaries can be hired.", foundMatch.State());
        }
        int hireCost = PieceDefinitions.NeutralMercenaryHireCost;
        if (player.Money < hireCost)
        {
          return new(false, "You cannot afford to hire that Mercenary.", foundMatch.State());
        }
        player.Money = ClampCurrency((long)player.Money - hireCost);
        foundMatch.Pieces[mercenaryIndex] = mercenary with
        {
          Team = player.Team,
          LastBid = hireCost,
          HasMovedThisTurn = true,
          HasAttackedThisTurn = true,
          CannotContributeToConquestThisTurn = true
        };
        SpendAction(foundMatch, player);
        foundMatch.Version++;
        foundMatch.Touch();
        return new(true, null, foundMatch.State());
      }

      if (!TryGetPurchasableUnit(foundMatch, request.PieceType, out UnitPurchaseInfo unit, includeMercenary: true))
      {
        return new(false, "That unit is not available for purchase.", foundMatch.State());
      }

      if (player.Money < unit.Cost ||
          !CanPlacePurchasedUnit(foundMatch, unit, player.Team, request.X, request.Y, initialBuy: false))
      {
        return new(false, "Place an affordable unit on a valid empty square.", foundMatch.State());
      }

      player.Money = ClampCurrency((long)player.Money - unit.Cost);
      foundMatch.Pieces.Add(new NetworkPiece(
        Guid.NewGuid().ToString("N"), unit.Type, player.Team, request.X, request.Y, unit.Health,
        HasMovedThisTurn: true,
        HasAttackedThisTurn: true,
        LastBid: unit.Cost,
        CannotContributeToConquestThisTurn: true
      ));
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
      if (!foundMatch.AdvanceClock())
      {
        foundMatch.Version++;
        foundMatch.Touch();
        return new(false, "Your clock has expired.", foundMatch.State());
      }

      if (buyPhase.IsFarmPlacementPhase)
      {
        return new(false, "Place your two farms before ending the buy turn.", foundMatch.State());
      }

      buyPhase.StopCurrentBuyer();
      if (buyPhase.IsComplete)
      {
        StartFirstTurn(foundMatch);
      }
      else
      {
        foundMatch.CurrentTurn = buyPhase.CurrentTeam;
      }
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
        if (match.IsDebugMatch && match.RemoveDebugController(connectionId))
        {
          match.Touch();
          return;
        }

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
        if (match.IsDebugMatch)
        {
          continue;
        }

        bool playerTimedOut = match.Players.Any(player =>
          player.DisconnectedAt is DateTimeOffset disconnectedAt && now - disconnectedAt > DisconnectGracePeriod
        );
        bool roomTimedOut = match.Players.Count == 1 && match.Host.ConnectionId is null &&
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
        !new[] { "Preset", "Procedural", "None" }.Contains(configuration.TerrainSource) ||
        !new[] { "Regicide", "Conquest", "Escort", "Dominion", "Plunder" }.Contains(configuration.GameMode) ||
        !TeamRules.IsValidPlayerCount(configuration.PlayerCount) ||
        configuration.StartingCash < 0 ||
        configuration.InitialBuysPerTurn < 1 ||
        configuration.InitialBuyTurnsPerTeam < 1 ||
        configuration.ConquestWinScore < 1 ||
        configuration.DominionWinScore < 1 ||
        configuration.PlunderWinScore < 1 ||
        configuration.PlunderDeliveryScore < 1 ||
        configuration.PlunderRoyalKillPenalty < 0 ||
        (configuration.ChessTimerEnabled &&
          (configuration.ChessTimerMinutes < 0 || configuration.ChessTimerSeconds is < 0 or > 59 ||
           configuration.ChessTimerIncrementSeconds < 0 ||
           (configuration.ChessTimerMinutes == 0 && configuration.ChessTimerSeconds == 0))) ||
        configuration.UnitMaintenancePercent is < 0 or > 100 ||
        configuration.InterestPercent is < -100 or > 200 ||
        configuration.EscortRoyalHealthPercent is < 1 or > 100 ||
        !float.IsFinite(configuration.KillerRefundMultiplier) ||
        !float.IsFinite(configuration.DefeatedTeamRefundMultiplier))
    {
      error = "The proposed match settings are not valid.";
      return false;
    }

    sanitized = configuration;
    return true;
  }

  private static (int width, int height, int health) GetRoyalStats(NetworkMatchConfiguration configuration, string type)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    int health = configuration.GameMode == "Escort"
      ? Math.Max(1, (int)Math.Ceiling(rule.Health * (configuration.EscortRoyalHealthPercent / 100d)))
      : rule.Health;
    return (rule.Width, rule.Height, health);
  }

  private static readonly HashSet<string> RoyalTypes = UnitRules.Royals
    .Select(rule => rule.Type)
    .ToHashSet(StringComparer.Ordinal);

  private sealed record UnitPurchaseInfo(string Type, int Cost, int Health, int Width = 1, int Height = 1);

  private static bool TryGetPurchasableUnit(Match match, string type, out UnitPurchaseInfo unit, bool includeMercenary = false)
  {
    if (!UnitRules.TryGet(type, out UnitRule rule) ||
        !UnitRules.Purchasable.Contains(rule) ||
        (rule.Type == "Mercenary" && !includeMercenary) ||
        (rule.Type == "Farm" && !match.Configuration.FarmsEnabled))
    {
      unit = null!;
      return false;
    }

    unit = new UnitPurchaseInfo(
      rule.Type,
      GetUnitPrice(match, rule),
      rule.Health,
      rule.Width,
      rule.Height
    );
    return true;
  }

  private static int GetUnitCost(Match match, string type)
  {
    return TryGetPurchasableUnit(match, type, out UnitPurchaseInfo unit, includeMercenary: true) ? unit.Cost : 0;
  }

  private static int GetUnitPrice(Match match, UnitRule rule)
  {
    return rule.Type == "Farm"
      ? rule.Cost
      : EconomyRules.GetUnitPrice(rule.Cost, match.Configuration.UnitPricePercent);
  }

  private static int GetUnitMaintenance(Match match, UnitRule rule)
  {
    return rule.Type == "Farm"
      ? 0
      : EconomyRules.GetUnitMaintenance(
        rule.Cost,
        match.Configuration.UnitMaintenancePercent
      );
  }

  private static bool FootprintsOverlap(NetworkPiece existing, int x, int y, int width, int height)
  {
    UnitRule existingRule = UnitRules.GetRequired(existing.Type);
    return UnitRules.FootprintsOverlap(
      existing.X, existing.Y, existingRule.Width, existingRule.Height,
      x, y, width, height
    );
  }

  private static bool CanPlacePurchasedUnit(
    Match match,
    UnitPurchaseInfo unit,
    NetworkTeam team,
    int x,
    int y,
    bool initialBuy
  )
  {
    bool inValidTerritory = unit.Type == "Mercenary" && !initialBuy
      ? NetworkBoardRules.CanPlaceMercenary(match.Configuration, x, y)
      : NetworkBoardRules.CanPlaceForTeam(match.Configuration, team, x, y, unit.Width, unit.Height);
    if (!inValidTerritory) return false;

    for (int offsetY = 0; offsetY < unit.Height; offsetY++)
      for (int offsetX = 0; offsetX < unit.Width; offsetX++)
      {
        var square = (x: x + offsetX, y: y + offsetY);
        if (match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square)) return false;
      }

    return !match.Pieces.Any(piece =>
      (unit.Type == "Farm" || piece.Type != "Farm") &&
      FootprintsOverlap(piece, x, y, unit.Width, unit.Height));
  }

  private static bool CanPlaceRoyal(Match match, NetworkTeam team, int x, int y, int width, int height)
  {
    if (!NetworkBoardRules.CanPlaceForTeam(match.Configuration, team, x, y, width, height)) return false;
    for (int offsetY = 0; offsetY < height; offsetY++)
      for (int offsetX = 0; offsetX < width; offsetX++)
      {
        var square = (x: x + offsetX, y: y + offsetY);
        if (match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square)) return false;
      }

    return !match.Pieces.Any(piece => FootprintsOverlap(piece, x, y, width, height));
  }

  private static bool IsFarmCoveredByUnit(Match match, NetworkPiece farm)
  {
    if (!UnitRules.TryGet(farm.Type, out UnitRule farmRule)) return false;
    return match.Pieces.Any(piece => piece.Id != farm.Id && piece.AttachedToId is null && piece.Type != "Farm" &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      UnitRules.FootprintsOverlap(farm.X, farm.Y, farmRule.Width, farmRule.Height, piece.X, piece.Y, rule.Width, rule.Height));
  }

  private static bool TryGetLegalMovementPath(
    Match match,
    NetworkPiece piece,
    int destinationX,
    int destinationY,
    out List<(int x, int y)> path
  )
  {
    path = null!;
    if (!UnitRules.TryGet(piece.Type, out UnitRule rule)) return false;
    rule = GetEffectiveMovementRule(match, piece, rule);

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLandAt(match, piece, rule, destination),
      (from, to) => CanTravelThrough(match, piece, rule, from, to),
      destination => GetMovementCost(match, piece, rule, destination),
      (from, to) => CrossesRiver(match, rule, from, to)
    );
    return paths.TryGetValue((destinationX, destinationY), out path!);
  }

  private static bool CanLandAt(Match match, NetworkPiece piece, UnitRule rule, (int x, int y) destination)
  {
    if (!NetworkPieceRules.FootprintFitsBoard(match.Configuration, destination.x, destination.y, rule.Width, rule.Height)) return false;
    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      if ((piece.Type != "Elephant" && match.Terrain.IsLake(square)) || match.Barricades.ContainsKey(square)) return false;
    }

    HashSet<string> ignoredPieces = match.Pieces
      .Where(other => other.Id == piece.Id || other.AttachedToId == piece.Id)
      .Select(other => other.Id)
      .ToHashSet(StringComparer.Ordinal);
    if (match.Pieces.Any(other => !ignoredPieces.Contains(other.Id) &&
      (rule.Type == "Farm" || other.Type != "Farm") &&
      // An elephant may end its move on an enemy it tramples, but never on an ally.
      !(rule.Type == "Elephant" && other.Team != piece.Team) &&
      NetworkPieceRules.FootprintsOverlap(other, destination.x, destination.y, rule.Width, rule.Height))) return false;

    return true;
  }

  private static UnitRule GetEffectiveMovementRule(Match match, NetworkPiece piece, UnitRule rule)
  {
    if (rule.Type == "Ox")
    {
      NetworkPiece? cargo = match.Pieces.FirstOrDefault(other => other.AttachedToId == piece.Id &&
        other.AttachmentKind == NetworkAttachmentKind.Carried);
      if (cargo is not null && UnitRules.TryGet(cargo.Type, out UnitRule cargoRule))
      {
        rule = cargoRule with { MoveRange = cargoRule.MoveRange + 2 };
      }
    }

    if (match.TreasureCarrierId == piece.Id)
    {
      rule = rule with { MoveRange = Math.Max(1, rule.MoveRange - 1) };
    }

    return AbilityRules.CanUseCavalierFollowUpMove(piece.Type, piece.CavalierFollowUpMoveAvailable)
      ? rule with { MoveRange = 2, MovePattern = RuleShape.Straight }
      : rule;
  }

  private static bool CanTravelThrough(
    Match match,
    NetworkPiece piece,
    UnitRule rule,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    foreach ((int x, int y) position in PositionsBetween(from, destination))
      foreach ((int x, int y) square in OccupiedSquares(rule, position))
      {
        if (!NetworkBoardRules.Contains(match.Configuration, square.x, square.y) ||
            (piece.Type != "Elephant" && match.Terrain.IsLake(square)) || match.Barricades.ContainsKey(square)) return false;

        NetworkPiece? blocker = match.Pieces.FirstOrDefault(other => other.Id != piece.Id && other.AttachedToId != piece.Id && other.Type != "Farm" &&
          UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
          UnitRules.FootprintsOverlap(other.X, other.Y, otherRule.Width, otherRule.Height, square.x, square.y, 1, 1));
        if (blocker is not null && !(piece.Type == "Elephant" && blocker.Team != piece.Team)) return false;
      }

    return true;
  }

  private static int GetMovementCost(Match match, NetworkPiece piece, UnitRule rule, (int x, int y) destination)
  {
    if (rule.Type == "Elephant") return 1;
    int cost = 0;
    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      bool usesOwnedRoad = match.Roads.TryGetValue(square, out NetworkTeam roadOwner) && roadOwner == piece.Team;
      cost = Math.Max(cost, match.Terrain.IsForest(square) && !usesOwnedRoad
        ? 2
        : usesOwnedRoad && !match.Terrain.IsForest(square) ? 0 : 1);
    }
    return cost;
  }

  private static bool CrossesRiver(Match match, UnitRule rule, (int x, int y) from, (int x, int y) to)
  {
    if (rule.Type == "Elephant") return false;
    foreach ((int x, int y) fromSquare in OccupiedSquares(rule, from))
    {
      var toSquare = (fromSquare.x + to.x - from.x, fromSquare.y + to.y - from.y);
      foreach (((int x, int y) first, (int x, int y) second) edge in StepsBetween(fromSquare, toSquare))
      {
        if (match.Terrain.HasRiverBetween(edge.first, edge.second) && !match.RiverBridges.Contains(TileEdge.Between(edge.first, edge.second))) return true;
      }
    }
    return false;
  }

  private static bool HasClearAttackPath(Match match, NetworkPiece attacker, (int x, int y) targetPosition, string? targetId)
  {
    if (!UnitRules.TryGet(attacker.Type, out UnitRule attackerRule)) return false;
    if (attackerRule.Type == "Catapult") return true;

    return LineOfSightRules.HasClearAttackPath(
      attackerRule,
      OccupiedSquares(attackerRule, (attacker.X, attacker.Y)),
      targetPosition,
      match.Terrain.IsForest,
      match.Barricades.ContainsKey,
      square => match.Pieces.Any(other => other.Id != attacker.Id && other.Id != targetId && other.AttachedToId is null && other.Type != "Farm" &&
        !(attacker.Type == "Princess" && other.Team == attacker.Team) &&
        UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
        UnitRules.FootprintsOverlap(other.X, other.Y, otherRule.Width, otherRule.Height, square.x, square.y, 1, 1))
    );
  }

  private static bool HasClearAttackPath(Match match, NetworkPiece attacker, NetworkPiece target)
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule targetRule)) return false;
    foreach ((int x, int y) targetSquare in OccupiedSquares(targetRule, (target.X, target.Y)))
    {
      if (HasClearAttackPath(match, attacker, targetSquare, target.Id)) return true;
    }

    return false;
  }

  private static int GetAttackDamage(Match match, NetworkPiece attacker, NetworkPiece target)
  {
    int baseDamage = NetworkAttackRules.GetDamage(attacker.Type);
    if (baseDamage <= 0) return 0;
    return CombatRules.CalculateDamage(
      baseDamage,
      HasAdjacentUnit(match, attacker, attacker.Team, "Baron"),
      match.Pieces.Any(piece => piece.Type == "Spy" && piece.MarkedTargetId == target.Id),
      false,
      false,
      0
    );
  }

  private static void ResolvePieceDamage(
    Match match,
    NetworkPiece attacker,
    PlayerSlot attackingPlayer,
    string targetId,
    int? damageOverride
  )
  {
    int index = match.Pieces.FindIndex(piece => piece.Id == targetId);
    if (index < 0) return;
    NetworkPiece target = match.Pieces[index];
    NetworkPiece? guard = match.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard);
    NetworkPiece damagedPiece = guard ?? target;
    NetworkPiece? cargo = AbilityRules.SharesDamageWithCargo(target.Type)
      ? match.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
        piece.AttachmentKind == NetworkAttachmentKind.Carried)
      : null;
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(match, attacker, target);
    ApplyDamageToPiece(match, attacker, attackingPlayer, damagedPiece, unmitigatedDamage);
    if (cargo is not null && cargo.Id != damagedPiece.Id && match.Pieces.Any(piece => piece.Id == cargo.Id))
    {
      ApplyDamageToPiece(match, attacker, attackingPlayer, cargo, unmitigatedDamage);
    }

    for (int pieceIndex = 0; pieceIndex < match.Pieces.Count; pieceIndex++)
    {
      NetworkPiece piece = match.Pieces[pieceIndex];
      if (piece.MarkedTargetId == target.Id)
      {
        match.Pieces[pieceIndex] = piece with { MarkedTargetId = null };
      }
    }
  }

  private static void ApplyDamageToPiece(
    Match match,
    NetworkPiece attacker,
    PlayerSlot attackingPlayer,
    NetworkPiece damagedPiece,
    int unmitigatedDamage
  )
  {
    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(match, damagedPiece, damagedPiece.Team, "King"),
      IsInForest(match, damagedPiece),
      match.Terrain.ForestDamageReduction
    );
    int damagedIndex = match.Pieces.FindIndex(piece => piece.Id == damagedPiece.Id);
    if (damagedIndex < 0)
    {
      return;
    }
    if (damagedPiece.Health > damage)
    {
      match.Pieces[damagedIndex] = damagedPiece with { Health = damagedPiece.Health - damage };
    }
    else
    {
      HandlePieceDestroyed(match, damagedPiece, attackingPlayer);
    }
  }

  private static void ResolveBombardDamage(
    Match match,
    NetworkPiece attacker,
    PlayerSlot attackingPlayer,
    NetworkPiece target
  )
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule targetRule)) return;
    IReadOnlyList<NetworkPiece> affected = match.Pieces.Where(piece =>
    {
      if (piece.AttachedToId is not null || !UnitRules.TryGet(piece.Type, out UnitRule pieceRule)) return false;
      return OccupiedSquares(pieceRule, (piece.X, piece.Y)).Any(square =>
        OccupiedSquares(targetRule, (target.X, target.Y)).Any(targetSquare =>
          Math.Abs(square.x - targetSquare.x) <= 1 && Math.Abs(square.y - targetSquare.y) <= 1));
    }).ToArray();

    foreach (NetworkPiece affectedPiece in affected)
    {
      ResolvePieceDamage(match, attacker, attackingPlayer, affectedPiece.Id, 10);
    }
  }

  private static void DamageBarricade(Match match, NetworkPiece attacker, (int x, int y) position)
  {
    if (!match.Barricades.TryGetValue(position, out int health)) return;
    int damage = NetworkAttackRules.GetDamage(attacker.Type) +
      (HasAdjacentUnit(match, attacker, attacker.Team, "Baron") ? 5 : 0);
    health -= damage;
    if (health <= 0) match.Barricades.Remove(position);
    else match.Barricades[position] = health;
  }

  private static void TriggerMinesAlongMovement(Match match, NetworkPiece movingPiece, IReadOnlyList<(int x, int y)> path)
  {
    if (movingPiece.Type == "Engineer" || !UnitRules.TryGet(movingPiece.Type, out UnitRule movingRule)) return;
    List<((int x, int y) position, NetworkTeam owner)> triggered = [];
    foreach ((int x, int y) step in path)
    {
      foreach ((int x, int y) square in OccupiedSquares(movingRule, step))
      {
        if (match.Mines.TryGetValue(square, out NetworkTeam owner) && owner != movingPiece.Team)
        {
          triggered.Add((square, owner));
        }
      }
    }

    foreach (((int x, int y) position, NetworkTeam owner) mine in triggered.Distinct())
    {
      match.Mines.Remove(mine.position);
      PlayerSlot? owner = match.Players.FirstOrDefault(player => player.Team == mine.owner);
      if (owner is null) continue;
      List<NetworkPiece> affected = match.Pieces.Where(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        OccupiedSquares(rule, (piece.X, piece.Y)).Any(square =>
          Math.Abs(square.x - mine.position.x) <= 1 && Math.Abs(square.y - mine.position.y) <= 1)).ToList();
      if (match.Pieces.All(piece => piece.Id != movingPiece.Id)) affected.Add(movingPiece);
      foreach (NetworkPiece affectedPiece in affected)
      {
        ResolveMineDamage(match, affectedPiece.Id, owner);
      }
    }
  }

  private static void ResolveMineDamage(Match match, string targetId, PlayerSlot owner)
  {
    int index = match.Pieces.FindIndex(piece => piece.Id == targetId);
    if (index < 0) return;
    NetworkPiece target = match.Pieces[index];
    if (target.Health > 30)
    {
      match.Pieces[index] = target with { Health = target.Health - 30 };
    }
    else
    {
      HandlePieceDestroyed(match, target, owner);
    }
  }

  private static void HandlePieceDestroyed(Match match, NetworkPiece defeatedPiece, PlayerSlot attackingPlayer)
  {
    if (match.TreasureCarrierId == defeatedPiece.Id)
    {
      match.TreasureCarrierId = null;
      match.TreasurePosition = (defeatedPiece.X, defeatedPiece.Y);
    }

    if (defeatedPiece.Team == NetworkTeam.Neutral)
    {
      RemovePiece(match, defeatedPiece.Id);
      return;
    }

    int unitCost = GetUnitCost(match, defeatedPiece.Type);
    PlayerSlot? defeatedPlayer = match.Players.FirstOrDefault(player => player.Team == defeatedPiece.Team);
    if (attackingPlayer.Team != defeatedPiece.Team)
    {
      attackingPlayer.Money = ClampCurrency((long)attackingPlayer.Money +
        CombatRules.RoundCurrencyToNearestFive(unitCost * match.Configuration.KillerRefundMultiplier));
      if (defeatedPlayer is not null)
      {
        defeatedPlayer.Money = ClampCurrency((long)defeatedPlayer.Money +
          CombatRules.RoundCurrencyToNearestFive(unitCost * match.Configuration.DefeatedTeamRefundMultiplier));
      }
    }

    RemovePiece(match, defeatedPiece.Id);
    if (!UnitRules.TryGet(defeatedPiece.Type, out UnitRule rule) || rule.Category != RuleCategory.Royal) return;
    if (match.Configuration.GameMode == "Regicide" && attackingPlayer.Team != defeatedPiece.Team)
    {
      match.Winner = attackingPlayer.Team;
    }
    else if (match.Configuration.GameMode == "Escort" && defeatedPiece.Type != "Palace")
    {
      (int x, int y) respawn = FindRoyalRespawn(match, defeatedPiece.Team, rule);
      int health = GetRoyalStats(match.Configuration, defeatedPiece.Type).health;
      match.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), defeatedPiece.Type, defeatedPiece.Team, respawn.x, respawn.y, health));
    }
    else if (match.Configuration.GameMode == "Plunder" && attackingPlayer.Team != defeatedPiece.Team)
    {
      match.ModeScores[attackingPlayer.Team] = Math.Max(
        0,
        match.ModeScores[attackingPlayer.Team] - match.Configuration.PlunderRoyalKillPenalty
      );
    }
  }

  private static void RemovePiece(Match match, string pieceId)
  {
    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece piece = match.Pieces[index];
      if (piece.AttachedToId == pieceId)
      {
        match.Pieces[index] = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      }
      if (piece.MarkedTargetId == pieceId)
      {
        match.Pieces[index] = match.Pieces[index] with { MarkedTargetId = null };
      }
    }
    int indexToRemove = match.Pieces.FindIndex(piece => piece.Id == pieceId);
    if (indexToRemove >= 0) match.Pieces.RemoveAt(indexToRemove);
  }

  private static (int x, int y) FindRoyalRespawn(Match match, NetworkTeam team, UnitRule rule)
  {
    Board board = NetworkBoardRules.GetBoard(match.Configuration);
    NetworkPiece probe = new("respawn", rule.Type, team, 0, 0, rule.Health);
    foreach ((int x, int y) position in MatchRules.GetRoyalSpawnCandidates(
      board, team, rule.Width, rule.Height, match.Configuration.PlayerCount))
    {
      if (NetworkBoardRules.CanPlaceForTeam(match.Configuration, team, position.x, position.y, rule.Width, rule.Height) &&
          CanLandAt(match, probe, rule, position))
      {
        return position;
      }
    }

    throw new InvalidOperationException("Could not find an empty royal respawn square.");
  }

  private static bool IsEscortVictory(Match match, NetworkPiece piece, int x, int y)
  {
    if (match.Configuration.GameMode != "Escort" || !UnitRules.TryGet(piece.Type, out UnitRule rule) || rule.Category != RuleCategory.Royal) return false;
    Board board = NetworkBoardRules.GetBoard(match.Configuration);
    return OccupiedSquares(rule, (x, y)).Any(square => MatchRules.IsOnEnemyBackEdge(board, piece.Team, square));
  }

  private static bool TryDeliverTreasure(Match match, NetworkPiece piece)
  {
    if (match.Configuration.GameMode != "Plunder" || match.TreasureCarrierId != piece.Id ||
        !NetworkBoardRules.IsInTeamTerritory(match.Configuration, piece.Team, piece.X, piece.Y))
    {
      return false;
    }

    int score = Math.Clamp(
      match.ModeScores[piece.Team] + match.Configuration.PlunderDeliveryScore,
      0,
      match.Configuration.PlunderWinScore
    );
    match.ModeScores[piece.Team] = score;
    match.TreasureCarrierId = null;
    match.TreasurePosition = match.TreasureSpawn;
    if (score >= match.Configuration.PlunderWinScore)
    {
      match.Winner = piece.Team;
    }

    return true;
  }

  private static void MoveEmissaryCompanions(Match match, NetworkPiece emissary, int oldEmissaryX, int oldEmissaryY)
  {
    if (emissary.Type != "Emissary") return;
    int deltaX = emissary.X - oldEmissaryX;
    int deltaY = emissary.Y - oldEmissaryY;
    List<int> companions = match.Pieces
      .Select((piece, index) => (piece, index))
      .Where(entry => entry.piece.Id != emissary.Id && entry.piece.Id != match.TreasureCarrierId && entry.piece.Team == emissary.Team && entry.piece.AttachedToId is null &&
        UnitRules.TryGet(entry.piece.Type, out UnitRule rule) && rule.Width == 1 && rule.Height == 1 &&
        Math.Abs(entry.piece.X - oldEmissaryX) + Math.Abs(entry.piece.Y - oldEmissaryY) == 1)
      .Select(entry => entry.index).ToList();
    foreach (int index in companions)
    {
      NetworkPiece companion = match.Pieces[index];
      var destination = (x: companion.X + deltaX, y: companion.Y + deltaY);
      if (UnitRules.TryGet(companion.Type, out UnitRule companionRule) &&
          CanLandAt(match, companion, companionRule, destination))
      {
        match.Pieces[index] = companion with { X = destination.x, Y = destination.y, HasMovedThisTurn = true };
      }
    }
  }

  private static void MoveAttachedPieces(Match match, NetworkPiece host, int oldHostX, int oldHostY)
  {
    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece attachment = match.Pieces[index];
      if (attachment.AttachedToId != host.Id) continue;
      match.Pieces[index] = attachment with
      {
        X = host.X,
        Y = host.Y,
        HasMovedThisTurn = true
      };
    }
  }

  private static bool CanUseActionSquare(NetworkPiece actor, int targetX, int targetY)
  {
    if (!UnitRules.TryGet(actor.Type, out UnitRule rule) || rule.AttackPattern == RuleShape.None) return false;
    foreach ((int x, int y) origin in OccupiedSquares(rule, (actor.X, actor.Y)))
    {
      if (UnitRules.CanAttackOffset(
        rule.AttackPattern, rule.MinimumAttackRange, rule.AttackRange, actor.Team,
        targetX - origin.x, targetY - origin.y)) return true;
    }
    return false;
  }

  private static bool TryMarkSpyTarget(Match match, int actorIndex, NetworkPiece? target)
  {
    NetworkPiece actor = match.Pieces[actorIndex];
    if (target is null || target.Team == actor.Team) return false;
    match.Pieces[actorIndex] = actor with { MarkedTargetId = target.Id };
    return true;
  }

  private static bool TryPickUpTreasure(Match match, int actorIndex, int targetX, int targetY)
  {
    if (match.Configuration.GameMode != "Plunder" || match.TreasureCarrierId is not null ||
        !match.TreasurePosition.HasValue || match.TreasurePosition.Value != (targetX, targetY))
    {
      return false;
    }

    NetworkPiece actor = match.Pieces[actorIndex];
    if (actor.AttachedToId is not null || !UnitRules.TryGet(actor.Type, out UnitRule rule) ||
        rule.Width != 1 || rule.Height != 1 || rule.Category == RuleCategory.Royal ||
        Math.Abs(actor.X - targetX) + Math.Abs(actor.Y - targetY) != 1)
    {
      return false;
    }

    match.TreasureCarrierId = actor.Id;
    match.TreasurePosition = null;
    match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
    return true;
  }

  private static bool TryUseEngineerSpecial(
    Match match,
    int actorIndex,
    string ability,
    int targetX,
    int targetY,
    NetworkPiece? target
  )
  {
    NetworkPiece engineer = match.Pieces[actorIndex];
    bool demolition = AbilityRules.IsEngineerDemolition(ability);
    if ((!demolition && engineer.EngineerBuildsThisTurn >= 2) || target is not null ||
        (!AbilityRules.IsEngineerBuild(ability) && !demolition)) return false;
    if (demolition)
    {
      return match.Roads.Remove((targetX, targetY)) ||
        match.Barricades.Remove((targetX, targetY)) ||
        match.Mines.Remove((targetX, targetY));
    }
    if (!CanBuildImprovementAt(match, targetX, targetY))
    {
      return false;
    }

    if (string.Equals(ability, "Road", StringComparison.OrdinalIgnoreCase))
    {
      match.Roads[(targetX, targetY)] = engineer.Team;
    }
    else if (string.Equals(ability, "Barrier", StringComparison.OrdinalIgnoreCase))
    {
      match.Barricades[(targetX, targetY)] = 20;
    }
    else if (string.Equals(ability, "Mine", StringComparison.OrdinalIgnoreCase))
    {
      match.Mines[(targetX, targetY)] = engineer.Team;
    }
    int buildsUsed = engineer.EngineerBuildsThisTurn + 1;
    match.Pieces[actorIndex] = engineer with
    {
      EngineerBuildsThisTurn = buildsUsed,
      HasAttackedThisTurn = buildsUsed >= 2
    };
    return true;
  }

  private static bool CanBuildImprovementAt(Match match, int x, int y)
  {
    var position = (x, y);
    return NetworkBoardRules.Contains(match.Configuration, x, y) &&
      !match.Terrain.IsLake(position) && !match.Roads.ContainsKey(position) &&
      !match.Barricades.ContainsKey(position) &&
      !match.Mines.ContainsKey(position) &&
      !match.Pieces.Any(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        UnitRules.FootprintsOverlap(piece.X, piece.Y, rule.Width, rule.Height, x, y, 1, 1));
  }

  private static bool TryAttachGuard(Match match, int actorIndex, int targetIndex)
  {
    if (targetIndex < 0) return false;
    NetworkPiece guard = match.Pieces[actorIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    if (target.Id == guard.Id || target.Id == match.TreasureCarrierId || target.Team != guard.Team || !UnitRules.TryGet(guard.Type, out UnitRule guardRule) ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule) ||
        !AbilityRules.CanGuardAttach(
          guardRule,
          targetRule,
          guard.AttachedToId is not null,
          match.Pieces.Any(piece => piece.AttachedToId == target.Id && piece.AttachmentKind == NetworkAttachmentKind.Guard)
        )) return false;
    match.Pieces[actorIndex] = guard with { AttachedToId = target.Id, AttachmentKind = NetworkAttachmentKind.Guard, X = target.X, Y = target.Y };
    return true;
  }

  private static bool TryFireMercenary(Match match, int actorIndex, string ability)
  {
    if (!string.Equals(ability, "Fire", StringComparison.OrdinalIgnoreCase)) return false;
    NetworkPiece mercenary = match.Pieces[actorIndex];
    if (mercenary.Type != "Mercenary" || mercenary.Team == NetworkTeam.Neutral) return false;
    match.Pieces[actorIndex] = mercenary with
    {
      Team = NetworkTeam.Neutral,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = true
    };
    return true;
  }

  private static bool TryAttachOxCargo(Match match, int actorIndex, int targetIndex)
  {
    if (targetIndex < 0 || !UnitRules.TryGet(match.Pieces[targetIndex].Type, out UnitRule targetRule)) return false;
    NetworkPiece ox = match.Pieces[actorIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    bool hasCargo = match.Pieces.Any(piece => piece.AttachedToId == ox.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Carried);
    if (!UnitRules.TryGet(ox.Type, out UnitRule oxRule) || target.Team != ox.Team || target.Id == ox.Id || target.Id == match.TreasureCarrierId ||
        !AbilityRules.CanOxAttach(oxRule, targetRule, target.AttachedToId is not null, hasCargo)) return false;

    match.Pieces[targetIndex] = target with
    {
      AttachedToId = ox.Id,
      AttachmentKind = NetworkAttachmentKind.Carried,
      X = ox.X,
      Y = ox.Y
    };
    return true;
  }

  private static bool HasAdjacentUnit(Match match, NetworkPiece piece, NetworkTeam team, string type)
  {
    if (!UnitRules.TryGet(piece.Type, out UnitRule pieceRule)) return false;
    return match.Pieces.Any(candidate => candidate.Id != piece.Id && candidate.Team == team && candidate.Type == type &&
      UnitRules.TryGet(candidate.Type, out UnitRule candidateRule) &&
      OccupiedSquares(pieceRule, (piece.X, piece.Y)).Any(first =>
        OccupiedSquares(candidateRule, (candidate.X, candidate.Y)).Any(second =>
          Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y) == 1)));
  }

  private static bool IsInForest(Match match, NetworkPiece piece)
  {
    return UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      OccupiedSquares(rule, (piece.X, piece.Y)).Any(match.Terrain.IsForest);
  }

  private static IEnumerable<(int x, int y)> OccupiedSquares(UnitRule rule, (int x, int y) position)
  {
    for (int y = 0; y < rule.Height; y++)
      for (int x = 0; x < rule.Width; x++) yield return (position.x + x, position.y + y);
  }

  private static IEnumerable<(int x, int y)> PositionsBetween((int x, int y) from, (int x, int y) destination)
  {
    int steps = Math.Max(Math.Abs(destination.x - from.x), Math.Abs(destination.y - from.y));
    for (int step = 1; step <= steps; step++)
      yield return (
        from.x + (int)MathF.Round((destination.x - from.x) * step / (float)steps),
        from.y + (int)MathF.Round((destination.y - from.y) * step / (float)steps)
      );
  }

  private static IEnumerable<((int x, int y) first, (int x, int y) second)> StepsBetween(
    (int x, int y) from,
    (int x, int y) to
  )
  {
    int steps = Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.y - from.y));
    (int x, int y) current = from;
    for (int step = 1; step <= steps; step++)
    {
      (int x, int y) next = (
        from.x + (int)MathF.Round((to.x - from.x) * step / (float)steps),
        from.y + (int)MathF.Round((to.y - from.y) * step / (float)steps)
      );
      if (next.x != current.x && next.y != current.y)
      {
        yield return (current, (next.x, current.y));
        yield return ((next.x, current.y), next);
        yield return (current, (current.x, next.y));
        yield return ((current.x, next.y), next);
      }
      else yield return (current, next);
      current = next;
    }
  }

  private static void ResetTurnActions(Match match, NetworkTeam team)
  {
    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece piece = match.Pieces[index];
      if (piece.Team == team)
      {
        match.Pieces[index] = piece with
        {
          HasMovedThisTurn = false,
          HasAttackedThisTurn = false,
          CavalierFollowUpMoveAvailable = false,
          EngineerBuildsThisTurn = 0,
          CannotContributeToConquestThisTurn = false
        };
      }
    }
  }

  private static void SpendAction(Match match, PlayerSlot player)
  {
    if (!Globals.ActionLimitsEnabled)
    {
      return;
    }

    player.ActionsRemaining--;
    if (player.ActionsRemaining > 0)
    {
      return;
    }

    CompleteTurn(match, player);
  }

  private static void CompleteTurn(Match match, PlayerSlot player)
  {
    if (match.Configuration.GameMode == "Conquest" && match.Configuration.PlayerCount == 2)
    {
      Board board = NetworkBoardRules.GetBoard(match.Configuration);
      int pressure = match.Pieces.Count(piece => piece.AttachmentKind == NetworkAttachmentKind.None &&
        !piece.CannotContributeToConquestThisTurn && piece.Team == player.Team &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && OccupiedSquares(rule, (piece.X, piece.Y)).Any(square => MatchRules.IsConquestSquare(board, square)));
      if (player.Team == NetworkTeam.Red) pressure = -pressure;
      match.ConquestScore = Math.Clamp(match.ConquestScore + pressure, -match.Configuration.ConquestWinScore, match.Configuration.ConquestWinScore);
      if (Math.Abs(match.ConquestScore) >= match.Configuration.ConquestWinScore)
      {
        match.Winner = match.ConquestScore < 0 ? NetworkTeam.Red : NetworkTeam.Blue;
        return;
      }
    }
    else if (match.Configuration.GameMode == "Conquest" && match.Configuration.PlayerCount > 2)
    {
      Board board = NetworkBoardRules.GetBoard(match.Configuration);
      int pressure = match.Pieces.Count(piece => piece.AttachmentKind == NetworkAttachmentKind.None &&
        !piece.CannotContributeToConquestThisTurn && piece.Team == player.Team &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && OccupiedSquares(rule, (piece.X, piece.Y)).Any(square => MatchRules.IsConquestSquare(board, square)));
      match.ConquestScores[player.Team] = Math.Clamp(
        match.ConquestScores[player.Team] + pressure,
        0,
        match.Configuration.ConquestWinScore
      );
      if (match.ConquestScores[player.Team] >= match.Configuration.ConquestWinScore)
      {
        match.Winner = player.Team;
        return;
      }
    }
    else if (match.Configuration.GameMode == "Dominion")
    {
      int score = Math.Clamp(
        match.ModeScores[player.Team] + GetDominionControlledPointCount(match, player.Team),
        0,
        match.Configuration.DominionWinScore
      );
      match.ModeScores[player.Team] = score;
      if (score >= match.Configuration.DominionWinScore)
      {
        match.Winner = player.Team;
        return;
      }
    }

    match.CompleteTimedTurn(player.Team);
    player.ActionsRemaining = ActionsPerTurn;
    match.CurrentTurn = TeamRules.GetNextTeam(match.CurrentTurn, match.Configuration.PlayerCount);
    ApplyTurnEconomy(match, match.CurrentTurn);
    ResetTurnActions(match, match.CurrentTurn);
  }

  private static int GetDominionControlledPointCount(Match match, NetworkTeam team)
  {
    Board board = NetworkBoardRules.GetBoard(match.Configuration);
    int controlledPoints = 0;
    foreach ((int x, int y) point in MatchRules.GetDominionControlPoints(board))
    {
      bool friendlyTouching = match.Pieces.Any(piece =>
        piece.Team == team && piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        OccupiedSquares(rule, (piece.X, piece.Y)).Contains(point));
      bool enemyTouching = match.Pieces.Any(piece =>
        piece.Team is not NetworkTeam.Neutral && piece.Team != team && piece.AttachedToId is null &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && OccupiedSquares(rule, (piece.X, piece.Y)).Contains(point));
      if (friendlyTouching && !enemyTouching)
      {
        controlledPoints++;
      }
    }

    return controlledPoints;
  }

  private static void ApplyTurnEconomy(Match match, NetworkTeam team)
  {
    PlayerSlot? player = match.Players.FirstOrDefault(candidate => candidate.Team == team);
    if (player is null) return;
    if (match.Configuration.InterestEnabled && match.Configuration.InterestPercent != 0)
    {
      player.Money = ClampCurrency((long)player.Money + EconomyRules.GetInterest(player.Money, match.Configuration.InterestPercent));
    }

    int farmCount = match.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null && piece.Type == "Farm");
    int palaceCount = match.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null && piece.Type == "Palace");
    long income = farmCount * (long)match.Configuration.FarmIncomePerTurn + palaceCount * 5L;
    if (income != 0)
    {
      player.Money = ClampCurrency((long)player.Money + income);
    }

    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece mercenary = match.Pieces[index];
      if (mercenary.Team != team || mercenary.AttachedToId is not null || mercenary.Type != "Mercenary")
      {
        continue;
      }

      const int mercenaryPayroll = 10;
      if (player.Money < mercenaryPayroll)
      {
        match.Pieces[index] = mercenary with
        {
          Team = NetworkTeam.Neutral,
          HasMovedThisTurn = true,
          HasAttackedThisTurn = true
        };
        continue;
      }

      player.Money = ClampCurrency((long)player.Money - mercenaryPayroll);
    }

    if (!match.Configuration.UnitMaintenanceEnabled || match.Configuration.UnitMaintenancePercent <= 0) return;
    long upkeep = match.Pieces
      .Where(piece => piece.Team == team && piece.AttachedToId is null)
      .Sum(piece => UnitRules.TryGet(piece.Type, out UnitRule rule)
        ? (long)GetUnitMaintenance(match, rule)
        : 0L);
    if (upkeep > 0)
    {
      player.Money = ClampCurrency((long)player.Money - upkeep);
    }
  }

  private static void StartFirstTurn(Match match)
  {
    match.CurrentTurn = TeamRules.GetFirstTeam(match.Configuration.PlayerCount);
    foreach (PlayerSlot player in match.Players)
    {
      player.ActionsRemaining = ActionsPerTurn;
    }
    ApplyTurnEconomy(match, match.CurrentTurn);
    ResetTurnActions(match, match.CurrentTurn);
    match.ResetClockTimestamp();
  }

  private static int ClampCurrency(long amount) => (int)Math.Clamp(amount, int.MinValue, int.MaxValue);

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

  private sealed class Match(
    string code,
    NetworkMatchConfiguration configuration,
    PlayerSlot host,
    bool isDebugMatch = false
  )
  {
    internal object Sync { get; } = new();
    internal string Code { get; } = code;
    internal NetworkMatchConfiguration Configuration { get; } = configuration;
    internal PlayerSlot Host { get; } = host;
    internal bool IsDebugMatch { get; } = isDebugMatch;
    private readonly List<PlayerSlot> _players = [host];
    internal IReadOnlyList<PlayerSlot> Players => _players;
    internal List<NetworkPiece> Pieces { get; } = [];
    internal BattlefieldTerrain Terrain { get; } = TerrainRules.Create(
      NetworkBoardRules.GetBoard(configuration), configuration.TerrainSeed, configuration.ForestDensity, configuration.WaterwayDensity,
      configuration.PlayerCount, configuration.TerrainSource, configuration.BoardSize
    );
    internal Dictionary<(int x, int y), NetworkTeam> Roads { get; } = [];
    internal Dictionary<(int x, int y), int> Barricades { get; } = [];
    internal Dictionary<(int x, int y), NetworkTeam> Mines { get; } = [];
    internal HashSet<TileEdge> RiverBridges { get; } = [];
    internal NetworkTeam? Winner { get; set; }
    internal int ConquestScore { get; set; }
    internal Dictionary<NetworkTeam, int> ConquestScores { get; } = TeamRules.GetActiveTeams(configuration.PlayerCount)
      .ToDictionary(team => team, _ => 0);
    internal Dictionary<NetworkTeam, int> ModeScores { get; } = TeamRules.GetActiveTeams(configuration.PlayerCount)
      .ToDictionary(team => team, _ => 0);
    internal (int x, int y) TreasureSpawn { get; } = MatchRules.GetTreasureSpawn(NetworkBoardRules.GetBoard(configuration));
    internal (int x, int y)? TreasurePosition { get; set; } = configuration.GameMode == "Plunder"
      ? MatchRules.GetTreasureSpawn(NetworkBoardRules.GetBoard(configuration))
      : null;
    internal string? TreasureCarrierId { get; set; }
    internal OpeningBuyPhase? InitialBuy { get; set; }
    internal NetworkTeam CurrentTurn { get; set; } = TeamRules.GetFirstTeam(configuration.PlayerCount);
    internal long Version { get; set; }
    internal Dictionary<NetworkTeam, long> ClockMilliseconds { get; } = TeamRules.GetActiveTeams(configuration.PlayerCount)
      .ToDictionary(team => team, _ => Math.Max(0L, ((long)configuration.ChessTimerMinutes * 60L + configuration.ChessTimerSeconds) * 1000L));
    internal DateTimeOffset ClockUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    internal DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    internal DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    private readonly HashSet<string> _debugControllers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkTeam> _debugTeams = new(StringComparer.Ordinal);

    internal bool MatchReady => Players.Count == Configuration.PlayerCount && Players.All(player => player.ChosenRoyal is not null);
    internal void AddPlayer(PlayerSlot player) => _players.Add(player);
    internal PlayerSlot? FindPlayerByConnection(string connectionId)
    {
      if (IsDebugMatch && _debugTeams.TryGetValue(connectionId, out NetworkTeam debugTeam))
      {
        return Players.FirstOrDefault(player => player.Team == debugTeam);
      }

      return Players.FirstOrDefault(player => player.ConnectionId == connectionId);
    }

    internal bool IsDebugController(string connectionId) => _debugControllers.Contains(connectionId);
    internal void RegisterDebugController(string connectionId, NetworkTeam team)
    {
      _debugControllers.Add(connectionId);
      _debugTeams[connectionId] = team;
    }

    internal bool RemoveDebugController(string connectionId)
    {
      _debugTeams.Remove(connectionId);
      return _debugControllers.Remove(connectionId);
    }

    internal PlayerSlot? FindPlayerByToken(string token) => Players.FirstOrDefault(player => player.ReconnectToken == token);
    internal void Touch() => LastActivity = DateTimeOffset.UtcNow;
    internal void ResetClockTimestamp() => ClockUpdatedAt = DateTimeOffset.UtcNow;
    internal bool AdvanceClock()
    {
      if (!Configuration.ChessTimerEnabled || Winner is not null) return true;
      DateTimeOffset now = DateTimeOffset.UtcNow;
      long elapsed = Math.Max(0L, (long)(now - ClockUpdatedAt).TotalMilliseconds);
      ClockUpdatedAt = now;
      if (!ClockMilliseconds.TryGetValue(CurrentTurn, out long remaining) || remaining > elapsed)
      {
        ClockMilliseconds[CurrentTurn] = Math.Max(0L, remaining - elapsed);
        return true;
      }

      ClockMilliseconds[CurrentTurn] = 0;
      Winner = TeamRules.GetNextTeam(CurrentTurn, Configuration.PlayerCount);
      return false;
    }

    internal void CompleteTimedTurn(NetworkTeam team)
    {
      if (!Configuration.ChessTimerEnabled) return;
      ClockMilliseconds[team] = Math.Max(0L, ClockMilliseconds.GetValueOrDefault(team)) +
        (long)Configuration.ChessTimerIncrementSeconds * 1000L;
      ClockUpdatedAt = DateTimeOffset.UtcNow;
    }

    private NetworkClockState? ClockState() => !Configuration.ChessTimerEnabled ? null : new(
      ClockMilliseconds.Select(pair => new NetworkClockTeamState(pair.Key, pair.Value)).ToArray(),
      CurrentTurn,
      ClockUpdatedAt.ToUnixTimeMilliseconds()
    );
    internal NetworkGameState State() => new(
      Code,
      CurrentTurn,
      Pieces.ToArray(),
      Players.Select(player => new NetworkTeamState(player.Team, player.Money, player.ActionsRemaining, player.ChosenRoyal)).ToArray(),
      Configuration,
      Version,
      Players.Count,
      MatchReady,
      InitialBuy?.ToNetworkState(),
      [
        .. Roads.Select(pair => new NetworkImprovement("Road", pair.Key.x, pair.Key.y, 0, pair.Value)),
        .. Barricades.Select(pair => new NetworkImprovement("Barrier", pair.Key.x, pair.Key.y, pair.Value)),
        .. Mines.Select(pair => new NetworkImprovement("Mine", pair.Key.x, pair.Key.y, 0, pair.Value))
      ],
      Winner,
      ConquestScore,
      ConquestScores.Select(pair => new NetworkConquestTeamState(pair.Key, pair.Value)).ToArray(),
      ModeScores.Select(pair => new NetworkModeTeamState(pair.Key, pair.Value)).ToArray(),
      Configuration.GameMode == "Plunder"
        ? new NetworkTreasureState(TreasurePosition?.x, TreasurePosition?.y, TreasureCarrierId)
        : null,
      ClockState()
    );
    internal RoomJoinResult ResultFor(PlayerSlot player) => new(true, null, Code, player.Team, player.ReconnectToken, State());
  }

  private sealed class OpeningBuyPhase(int purchasesPerTurn, int buyTurnsPerTeam, int playerCount, bool farmsEnabled = false)
  {
    private readonly IReadOnlyList<NetworkTeam> _teams = TeamRules.GetActiveTeams(playerCount);
    private readonly Dictionary<NetworkTeam, int> _buyTurnsUsed = TeamRules.GetActiveTeams(playerCount)
      .ToDictionary(team => team, _ => 0);
    private readonly Dictionary<NetworkTeam, int> _farmsPlaced = TeamRules.GetActiveTeams(playerCount)
      .ToDictionary(team => team, _ => 0);
    private readonly HashSet<NetworkTeam> _stoppedTeams = [];

    internal NetworkTeam CurrentTeam { get; private set; } = TeamRules.GetFirstTeam(playerCount);
    internal int PurchasesThisTurn { get; private set; }
    internal int PurchasesPerTurn { get; } = Math.Max(1, purchasesPerTurn);
    internal int BuyTurnsPerTeam { get; } = Math.Max(1, buyTurnsPerTeam);
    internal bool IsComplete { get; private set; }
    internal bool IsFarmPlacementPhase { get; private set; } = farmsEnabled;

    internal void RecordPurchase()
    {
      if (IsFarmPlacementPhase)
      {
        _farmsPlaced[CurrentTeam]++;
        if (_farmsPlaced[CurrentTeam] >= 2)
        {
          FinishFarmPlacement();
        }
        return;
      }

      PurchasesThisTurn++;
      if (PurchasesThisTurn >= PurchasesPerTurn)
      {
        FinishCurrentTurn(false);
      }
    }

    internal void StopCurrentBuyer()
    {
      if (!IsFarmPlacementPhase) FinishCurrentTurn(true);
    }

    internal NetworkInitialBuyState ToNetworkState() => new(
      CurrentTeam,
      PurchasesThisTurn,
      PurchasesPerTurn,
      GetBuyTurnsUsed(NetworkTeam.Red),
      GetBuyTurnsUsed(NetworkTeam.Blue),
      BuyTurnsPerTeam,
      _stoppedTeams.Contains(NetworkTeam.Red),
      _stoppedTeams.Contains(NetworkTeam.Blue),
      IsComplete,
      _teams.Select(team => new NetworkInitialBuyTeamState(team, GetBuyTurnsUsed(team), _stoppedTeams.Contains(team), GetFarmsPlaced(team))).ToArray(),
      IsFarmPlacementPhase
    );

    private void FinishCurrentTurn(bool stopped)
    {
      if (IsComplete)
      {
        return;
      }

      if (stopped) _stoppedTeams.Add(CurrentTeam); else _buyTurnsUsed[CurrentTeam]++;

      PurchasesThisTurn = 0;
      if (_teams.All(team => !CanKeepBuying(team)))
      {
        IsComplete = true;
        return;
      }

      NetworkTeam nextTeam = CurrentTeam;
      for (int offset = 0; offset < _teams.Count; offset++)
      {
        nextTeam = TeamRules.GetNextTeam(nextTeam, _teams.Count);
        if (CanKeepBuying(nextTeam))
        {
          CurrentTeam = nextTeam;
          return;
        }
      }
    }

    private void FinishFarmPlacement()
    {
      PurchasesThisTurn = 0;
      if (_teams.All(team => GetFarmsPlaced(team) >= 2))
      {
        IsFarmPlacementPhase = false;
        CurrentTeam = _teams[0];
        return;
      }

      NetworkTeam nextTeam = CurrentTeam;
      for (int offset = 0; offset < _teams.Count; offset++)
      {
        nextTeam = TeamRules.GetNextTeam(nextTeam, _teams.Count);
        if (GetFarmsPlaced(nextTeam) < 2)
        {
          CurrentTeam = nextTeam;
          return;
        }
      }
    }

    private int GetBuyTurnsUsed(NetworkTeam team) => _buyTurnsUsed.TryGetValue(team, out int turns) ? turns : 0;
    private int GetFarmsPlaced(NetworkTeam team) => _farmsPlaced.TryGetValue(team, out int count) ? count : 0;
    private bool CanKeepBuying(NetworkTeam team) =>
      !_stoppedTeams.Contains(team) && GetBuyTurnsUsed(team) < BuyTurnsPerTeam;
  }
}

internal static class NetworkBoardRules
{
  internal static Board GetBoard(NetworkMatchConfiguration configuration) => BoardRules.GetBoard(configuration);

  internal static bool Contains(NetworkMatchConfiguration configuration, int x, int y) =>
    BoardRules.Contains(configuration, x, y);

  internal static (int x, int y) GetRoyalSpawn(NetworkMatchConfiguration configuration, NetworkTeam team, int width, int height)
  {
    Board board = GetBoard(configuration);
    foreach ((int x, int y) position in MatchRules.GetRoyalSpawnCandidates(board, team, width, height, configuration.PlayerCount))
    {
      return position;
    }

    throw new InvalidOperationException("The shared board has no valid royal spawn.");
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
    return BoardRules.CanPlaceForTeam(configuration, team, x, y, width, height);
  }

  internal static bool CanPlaceMercenary(NetworkMatchConfiguration configuration, int x, int y)
  {
    return BoardRules.CanPlaceMercenary(configuration, x, y);
  }

  internal static bool IsInTeamTerritory(NetworkMatchConfiguration configuration, NetworkTeam team, int x, int y)
  {
    return BoardRules.IsInTeamTerritory(configuration, team, x, y);
  }
}

internal static class NetworkMovementRules
{
  internal static bool IsLegal(NetworkPiece piece, int toX, int toY)
  {
    return UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      UnitRules.CanMove(rule, piece.X, piece.Y, toX, toY);
  }
}

internal static class NetworkPieceRules
{
  internal static (int width, int height) GetSize(string type)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return (rule.Width, rule.Height);
  }

  internal static bool FootprintFitsBoard(
    NetworkMatchConfiguration configuration, int x, int y, int width, int height
  ) => BoardRules.FootprintFitsBoard(configuration, x, y, width, height);

  internal static bool FootprintsOverlap(NetworkPiece existing, int x, int y, int width, int height)
  {
    (int existingWidth, int existingHeight) = GetSize(existing.Type);
    return UnitRules.FootprintsOverlap(existing.X, existing.Y, existingWidth, existingHeight, x, y, width, height);
  }
}

internal static class NetworkAttackRules
{
  internal static int GetDamage(string type) => UnitRules.TryGet(type, out UnitRule rule) ? rule.Attack : 0;

  internal static bool IsLegal(NetworkPiece attacker, NetworkPiece target)
  {
    return UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) &&
      UnitRules.TryGet(target.Type, out UnitRule targetRule) &&
      UnitRules.CanAttack(
        attackerRule, attacker.X, attacker.Y, attacker.Team,
        targetRule, target.X, target.Y
      );
  }
}
