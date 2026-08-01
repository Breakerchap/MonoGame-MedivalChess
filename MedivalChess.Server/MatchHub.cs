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

  public async Task<ActionResult> CompleteCavalierActivation(CompleteCavalierActivationRequest request)
  {
    ActionResult result = matches.TryCompleteCavalierActivation(Context.ConnectionId, request);
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
    )
    {
      Guest = new PlayerSlot(null, NetworkTeam.Blue, configuration.StartingCash)
    };
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
        return new(false, "Both players must choose a royal first.", foundMatch.State());
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

      if (piece.AttachedToId is not null && piece.AttachmentKind == NetworkAttachmentKind.Guard)
      {
        return new(false, "An attached Guard moves only with the unit it protects.", foundMatch.State());
      }
      if (piece.AttachedToId is not null)
      {
        // Carrying and towing are voluntary: moving the cargo itself dismounts it.
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
      piece = piece with { X = request.ToX, Y = request.ToY, HasMovedThisTurn = true, HasAttackedThisTurn = elephantDamagedAnEnemy || piece.HasAttackedThisTurn };
      foundMatch.Pieces[pieceIndex] = piece;
      MoveAttachedPieces(foundMatch, piece, oldX, oldY);
      MoveEmissaryCompanions(foundMatch, piece, oldX, oldY);
      if (IsEscortVictory(foundMatch, piece, request.ToX, request.ToY))
      {
        foundMatch.Winner = piece.Team;
      }
      if (foundMatch.Winner is null && piece.Type != "Cavalier")
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

      foundMatch.Pieces[attackerIndex] = attacker with { HasAttackedThisTurn = true };
      if (target is null)
      {
        DamageBarricade(foundMatch, attacker, targetPosition);
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
          NetworkPiece? pierced = foundMatch.Pieces.FirstOrDefault(piece => piece.Id != attacker.Id && piece.Id != target.Id && piece.Team != attacker.Team &&
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

      int actorIndex = foundMatch.Pieces.FindIndex(piece => piece.Id == request.ActorId);
      int targetIndex = string.IsNullOrWhiteSpace(request.TargetId)
        ? -1
        : foundMatch.Pieces.FindIndex(piece => piece.Id == request.TargetId);
      if (actorIndex < 0 || foundMatch.Pieces[actorIndex].Team != player.Team)
      {
        return new(false, "You may only use your own unit's special action.", foundMatch.State());
      }

      NetworkPiece actor = foundMatch.Pieces[actorIndex];
      NetworkPiece? target = targetIndex >= 0 ? foundMatch.Pieces[targetIndex] : null;
      if (!CanUseActionSquare(actor, request.TargetX, request.TargetY))
      {
        return new(false, "That square is outside the unit's special-action range.", foundMatch.State());
      }

      bool applied = actor.Type switch
      {
        "Spy" => TryMarkSpyTarget(foundMatch, actorIndex, target),
        "Engineer" => TryUseEngineerSpecial(foundMatch, actorIndex, request.Ability, request.TargetX, request.TargetY, target),
        "Guard" => TryAttachGuard(foundMatch, actorIndex, targetIndex),
        "Ox" => TryAttachOxCargo(foundMatch, actorIndex, targetIndex),
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
          player is null || player.Team != foundMatch.CurrentTurn || player.ActionsRemaining >= ActionsPerTurn)
      {
        return new(false, "Use at least one action before ending the turn.", foundMatch.State());
      }
      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }
      player.ActionsRemaining = 1;
      SpendAction(foundMatch, player);
      foundMatch.Version++;
      foundMatch.Touch();
      return new(true, null, foundMatch.State());
    }
  }

  public ActionResult TryCompleteCavalierActivation(string connectionId, CompleteCavalierActivationRequest request)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.PieceId))
    {
      return new(false, "Choose the Cavalier that finished its activation.", null);
    }
    if (!TryGetMatch(connectionId, out Match? match)) return new(false, "Join a room first.", null);
    Match foundMatch = match!;
    lock (foundMatch.Sync)
    {
      PlayerSlot? player = foundMatch.FindPlayerByConnection(connectionId);
      if (!foundMatch.MatchReady || foundMatch.InitialBuy is { IsComplete: false } || foundMatch.Winner is not null ||
          player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "That Cavalier activation is not available now.", foundMatch.State());
      }

      NetworkPiece? cavalier = foundMatch.Pieces.FirstOrDefault(piece => piece.Id == request.PieceId);
      if (cavalier is null || cavalier.Team != player.Team || cavalier.Type != "Cavalier" ||
          !cavalier.HasMovedThisTurn || cavalier.HasAttackedThisTurn)
      {
        return new(false, "That Cavalier has no pending activation.", foundMatch.State());
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
          !CanPlacePurchasedUnit(foundMatch, unit, player.Team, request.X, request.Y, initialBuy: true))
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

      if (foundMatch.Winner is not null)
      {
        return new(false, "This match has already ended.", foundMatch.State());
      }

      if (player is null || player.Team != foundMatch.CurrentTurn)
      {
        return new(false, "It is not your turn.", foundMatch.State());
      }

      int mercenaryIndex = foundMatch.Pieces.FindIndex(piece =>
        piece.Type == "Mercenary" && piece.Team != player.Team && piece.X == request.X && piece.Y == request.Y);
      if (mercenaryIndex >= 0)
      {
        NetworkPiece mercenary = foundMatch.Pieces[mercenaryIndex];
        long bid = (long)Math.Max(mercenary.LastBid, GetUnitCost("Mercenary")) + 10;
        PlayerSlot? previousOwner = foundMatch.Players.FirstOrDefault(candidate => candidate.Team == mercenary.Team);
        if (bid > int.MaxValue || player.Money < bid || previousOwner is null)
        {
          return new(false, "You cannot afford to outbid that Mercenary.", foundMatch.State());
        }
        player.Money -= (int)bid;
        previousOwner.Money += (int)bid;
        foundMatch.Pieces[mercenaryIndex] = mercenary with { Team = player.Team, LastBid = (int)bid };
        SpendAction(foundMatch, player);
        foundMatch.Version++;
        foundMatch.Touch();
        return new(true, null, foundMatch.State());
      }

      if (!TryGetPurchasableUnit(request.PieceType, out UnitPurchaseInfo unit, includeMercenary: true))
      {
        return new(false, "That unit is not available for purchase.", foundMatch.State());
      }

      if (player.Money < unit.Cost ||
          !CanPlacePurchasedUnit(foundMatch, unit, player.Team, request.X, request.Y, initialBuy: false))
      {
        return new(false, "Place an affordable unit on a valid empty square.", foundMatch.State());
      }

      player.Money -= unit.Cost;
      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), unit.Type, player.Team, request.X, request.Y, unit.Health, LastBid: unit.Cost));
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

  private static (int width, int height, int health) GetRoyalStats(string type)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return (rule.Width, rule.Height, rule.Health);
  }

  private static readonly HashSet<string> RoyalTypes = UnitRules.Royals
    .Select(rule => rule.Type)
    .ToHashSet(StringComparer.Ordinal);

  private sealed record UnitPurchaseInfo(string Type, int Cost, int Health, int Width = 1, int Height = 1);

  private static bool TryGetPurchasableUnit(string type, out UnitPurchaseInfo unit, bool includeMercenary = false)
  {
    if (!UnitRules.TryGet(type, out UnitRule rule) ||
        !UnitRules.Purchasable.Contains(rule) ||
        (rule.Type == "Mercenary" && !includeMercenary))
    {
      unit = null!;
      return false;
    }

    unit = new UnitPurchaseInfo(rule.Type, rule.Cost, rule.Health, rule.Width, rule.Height);
    return true;
  }

  private static int GetUnitCost(string type)
  {
    return TryGetPurchasableUnit(type, out UnitPurchaseInfo unit, includeMercenary: true) ? unit.Cost : 0;
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

    return !match.Pieces.Any(piece => FootprintsOverlap(piece, x, y, unit.Width, unit.Height));
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

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLandAt(match, piece, rule, destination),
      (from, to) => CanTravelThrough(match, piece, rule, from, to),
      destination => GetMovementCost(match, rule, destination),
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
      NetworkPieceRules.FootprintsOverlap(other, destination.x, destination.y, rule.Width, rule.Height))) return false;

    NetworkPiece? towedPiece = match.Pieces.FirstOrDefault(other => other.AttachedToId == piece.Id &&
      other.AttachmentKind == NetworkAttachmentKind.Towed);
    if (towedPiece is null || !UnitRules.TryGet(towedPiece.Type, out UnitRule towedRule)) return true;

    var towedDestination = (
      x: towedPiece.X + destination.x - piece.X,
      y: towedPiece.Y + destination.y - piece.Y
    );
    if (!NetworkPieceRules.FootprintFitsBoard(match.Configuration, towedDestination.x, towedDestination.y, towedRule.Width, towedRule.Height) ||
        UnitRules.FootprintsOverlap(destination.x, destination.y, rule.Width, rule.Height,
          towedDestination.x, towedDestination.y, towedRule.Width, towedRule.Height)) return false;
    foreach ((int x, int y) square in OccupiedSquares(towedRule, towedDestination))
    {
      if (match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square)) return false;
    }

    return !match.Pieces.Any(other => !ignoredPieces.Contains(other.Id) &&
      NetworkPieceRules.FootprintsOverlap(other, towedDestination.x, towedDestination.y, towedRule.Width, towedRule.Height));
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

        NetworkPiece? blocker = match.Pieces.FirstOrDefault(other => other.Id != piece.Id && other.AttachedToId != piece.Id &&
          UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
          UnitRules.FootprintsOverlap(other.X, other.Y, otherRule.Width, otherRule.Height, square.x, square.y, 1, 1));
        if (blocker is not null && !(piece.Type == "Elephant" && blocker.Team != piece.Team)) return false;
      }

    return true;
  }

  private static int GetMovementCost(Match match, UnitRule rule, (int x, int y) destination)
  {
    if (rule.Type == "Elephant") return 1;
    int cost = 0;
    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      cost = Math.Max(cost, match.Terrain.IsForest(square) && !match.Roads.Contains(square)
        ? 2
        : match.Roads.Contains(square) && !match.Terrain.IsForest(square) ? 0 : 1);
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

    return LineOfSightRules.HasClearAttackPath(
      attackerRule,
      OccupiedSquares(attackerRule, (attacker.X, attacker.Y)),
      targetPosition,
      match.Terrain.IsForest,
      match.Barricades.ContainsKey,
      square => match.Pieces.Any(other => other.Id != attacker.Id && other.Id != targetId && other.AttachedToId is null &&
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
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(match, attacker, target);
    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(match, damagedPiece, damagedPiece.Team, "King"),
      IsInForest(match, damagedPiece),
      match.Terrain.ForestDamageReduction
    );
    int damagedIndex = match.Pieces.FindIndex(piece => piece.Id == damagedPiece.Id);
    if (damagedIndex < 0) return;
    if (damagedPiece.Health > damage)
    {
      match.Pieces[damagedIndex] = damagedPiece with { Health = damagedPiece.Health - damage };
    }
    else
    {
      HandlePieceDestroyed(match, damagedPiece, attackingPlayer);
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

  private static void DamageBarricade(Match match, NetworkPiece attacker, (int x, int y) position)
  {
    if (!match.Barricades.TryGetValue(position, out int health)) return;
    int damage = NetworkAttackRules.GetDamage(attacker.Type) +
      (HasAdjacentUnit(match, attacker, attacker.Team, "Baron") ? 5 : 0);
    health -= damage;
    if (health <= 0) match.Barricades.Remove(position);
    else match.Barricades[position] = health;
  }

  private static void HandlePieceDestroyed(Match match, NetworkPiece defeatedPiece, PlayerSlot attackingPlayer)
  {
    int unitCost = GetUnitCost(defeatedPiece.Type);
    PlayerSlot? defeatedPlayer = match.Players.FirstOrDefault(player => player.Team == defeatedPiece.Team);
    attackingPlayer.Money += CombatRules.RoundCurrencyToNearestFive(unitCost * match.Configuration.KillerRefundMultiplier);
    if (defeatedPlayer is not null)
    {
      defeatedPlayer.Money += CombatRules.RoundCurrencyToNearestFive(unitCost * match.Configuration.DefeatedTeamRefundMultiplier);
    }

    RemovePiece(match, defeatedPiece.Id);
    if (!UnitRules.TryGet(defeatedPiece.Type, out UnitRule rule) || rule.Category != RuleCategory.Royal) return;
    if (match.Configuration.GameMode == "Regicide")
    {
      match.Winner = defeatedPiece.Team == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
    }
    else if (match.Configuration.GameMode == "Escort" && defeatedPiece.Type != "Palace")
    {
      (int x, int y) respawn = FindRoyalRespawn(match, defeatedPiece.Team, rule);
      match.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), defeatedPiece.Type, defeatedPiece.Team, respawn.x, respawn.y, rule.Health));
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
    foreach ((int x, int y) position in MatchRules.GetRoyalSpawnCandidates(board, team, rule.Width, rule.Height))
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
    int enemyBackRow = piece.Team == NetworkTeam.Red ? board.MinY : board.MinY + board.BoardArray.GetLength(0) - 1;
    return OccupiedSquares(rule, (x, y)).Any(square => square.y == enemyBackRow);
  }

  private static void MoveEmissaryCompanions(Match match, NetworkPiece emissary, int oldEmissaryX, int oldEmissaryY)
  {
    if (emissary.Type != "Emissary") return;
    int deltaX = emissary.X - oldEmissaryX;
    int deltaY = emissary.Y - oldEmissaryY;
    List<int> companions = match.Pieces
      .Select((piece, index) => (piece, index))
      .Where(entry => entry.piece.Id != emissary.Id && entry.piece.Team == emissary.Team && entry.piece.AttachedToId is null &&
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
    int deltaX = host.X - oldHostX;
    int deltaY = host.Y - oldHostY;
    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece attachment = match.Pieces[index];
      if (attachment.AttachedToId != host.Id) continue;
      match.Pieces[index] = attachment with
      {
        X = attachment.AttachmentKind == NetworkAttachmentKind.Towed ? attachment.X + deltaX : host.X,
        Y = attachment.AttachmentKind == NetworkAttachmentKind.Towed ? attachment.Y + deltaY : host.Y,
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
    if (engineer.HasAttackedThisTurn || target is not null ||
        !NetworkBoardRules.Contains(match.Configuration, targetX, targetY) ||
        match.Terrain.IsLake((targetX, targetY)) ||
        match.Roads.Contains((targetX, targetY)) || match.Barricades.ContainsKey((targetX, targetY))) return false;

    if (!AbilityRules.IsEngineerBuild(ability)) return false;
    if (string.Equals(ability, "Road", StringComparison.OrdinalIgnoreCase))
    {
      match.Roads.Add((targetX, targetY));
    }
    else if (string.Equals(ability, "Barrier", StringComparison.OrdinalIgnoreCase))
    {
      match.Barricades[(targetX, targetY)] = 20;
    }
    match.Pieces[actorIndex] = engineer with { HasAttackedThisTurn = true };
    return true;
  }

  private static bool TryAttachGuard(Match match, int actorIndex, int targetIndex)
  {
    if (targetIndex < 0) return false;
    NetworkPiece guard = match.Pieces[actorIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    if (target.Id == guard.Id || target.Team != guard.Team || !UnitRules.TryGet(guard.Type, out UnitRule guardRule) ||
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

  private static bool TryAttachOxCargo(Match match, int actorIndex, int targetIndex)
  {
    if (targetIndex < 0 || !UnitRules.TryGet(match.Pieces[targetIndex].Type, out UnitRule targetRule)) return false;
    NetworkPiece ox = match.Pieces[actorIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    bool hasCargo = match.Pieces.Any(piece => piece.AttachedToId == ox.Id &&
      piece.AttachmentKind is NetworkAttachmentKind.Carried or NetworkAttachmentKind.Towed);
    if (!UnitRules.TryGet(ox.Type, out UnitRule oxRule) || target.Team != ox.Team || target.Id == ox.Id ||
        !AbilityRules.CanOxAttach(oxRule, targetRule, target.AttachedToId is not null, hasCargo)) return false;

    NetworkAttachmentKind kind = targetRule.Category == RuleCategory.Mechanical
      ? NetworkAttachmentKind.Towed : NetworkAttachmentKind.Carried;
    match.Pieces[targetIndex] = target with
    {
      AttachedToId = ox.Id,
      AttachmentKind = kind,
      X = kind == NetworkAttachmentKind.Carried ? ox.X : target.X,
      Y = kind == NetworkAttachmentKind.Carried ? ox.Y : target.Y
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

    if (match.Configuration.GameMode == "Conquest" && player.Team == NetworkTeam.Blue)
    {
      Board board = NetworkBoardRules.GetBoard(match.Configuration);
      int pressure = match.Pieces.Count(piece => piece.AttachmentKind == NetworkAttachmentKind.None && piece.Team == NetworkTeam.Blue &&
          UnitRules.TryGet(piece.Type, out UnitRule rule) && OccupiedSquares(rule, (piece.X, piece.Y)).Any(square => MatchRules.IsConquestSquare(board, square))) -
        match.Pieces.Count(piece => piece.AttachmentKind == NetworkAttachmentKind.None && piece.Team == NetworkTeam.Red &&
          UnitRules.TryGet(piece.Type, out UnitRule rule) && OccupiedSquares(rule, (piece.X, piece.Y)).Any(square => MatchRules.IsConquestSquare(board, square)));
      match.ConquestScore = Math.Clamp(match.ConquestScore + pressure, -match.Configuration.ConquestWinScore, match.Configuration.ConquestWinScore);
      if (Math.Abs(match.ConquestScore) >= match.Configuration.ConquestWinScore)
      {
        match.Winner = match.ConquestScore < 0 ? NetworkTeam.Red : NetworkTeam.Blue;
        return;
      }
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
    internal PlayerSlot? Guest { get; set; }
    internal List<PlayerSlot> Players => Guest is null ? [Host] : [Host, Guest];
    internal List<NetworkPiece> Pieces { get; } = [];
    internal BattlefieldTerrain Terrain { get; } = TerrainRules.Create(
      NetworkBoardRules.GetBoard(configuration), configuration.TerrainSeed, configuration.ForestDensity, configuration.WaterwayDensity
    );
    internal HashSet<(int x, int y)> Roads { get; } = [];
    internal Dictionary<(int x, int y), int> Barricades { get; } = [];
    internal HashSet<TileEdge> RiverBridges { get; } = [];
    internal NetworkTeam? Winner { get; set; }
    internal int ConquestScore { get; set; }
    internal OpeningBuyPhase? InitialBuy { get; set; }
    internal NetworkTeam CurrentTurn { get; set; } = NetworkTeam.Red;
    internal long Version { get; set; }
    internal DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    internal DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    private readonly HashSet<string> _debugControllers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkTeam> _debugTeams = new(StringComparer.Ordinal);

    internal bool MatchReady => Guest is not null && Host.ChosenRoyal is not null && Guest.ChosenRoyal is not null;
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
    internal NetworkGameState State() => new(
      Code,
      CurrentTurn,
      Pieces.ToArray(),
      Players.Select(player => new NetworkTeamState(player.Team, player.Money, player.ActionsRemaining, player.ChosenRoyal)).ToArray(),
      Configuration,
      Version,
      Guest is null ? 1 : 2,
      MatchReady,
      InitialBuy?.ToNetworkState(),
      [
        .. Roads.Select(position => new NetworkImprovement("Road", position.x, position.y)),
        .. Barricades.Select(pair => new NetworkImprovement("Barrier", pair.Key.x, pair.Key.y, pair.Value))
      ],
      Winner,
      ConquestScore
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
  private static readonly Dictionary<string, Board> Boards = new(StringComparer.Ordinal)
  {
    ["Small"] = new Board("board_small.json"),
    ["Medium"] = new Board("board_medium.json"),
    ["Large"] = new Board("board_large.json")
  };

  internal static Board GetBoard(NetworkMatchConfiguration configuration) => Boards[configuration.BoardSize];

  internal static bool Contains(NetworkMatchConfiguration configuration, int x, int y)
  {
    return GetBoard(configuration).ContainsCell((x, y));
  }

  internal static (int x, int y) GetRoyalSpawn(NetworkMatchConfiguration configuration, NetworkTeam team, int width, int height)
  {
    Board board = GetBoard(configuration);
    foreach ((int x, int y) position in MatchRules.GetRoyalSpawnCandidates(board, team, width, height))
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
    Board board = GetBoard(configuration);
    for (int offsetY = 0; offsetY < height; offsetY++)
    {
      int arrayY = y + offsetY - board.MinY;
      if (MatchRules.GetTeamForArrayRow(board, configuration.GameMode, arrayY) != team ||
          !board.ContainsCell((x, y + offsetY)))
      {
        return false;
      }
    }

    return Enumerable.Range(0, width).All(offsetX => Enumerable.Range(0, height).All(offsetY =>
      board.ContainsCell((x + offsetX, y + offsetY))));
  }

  internal static bool CanPlaceMercenary(NetworkMatchConfiguration configuration, int x, int y)
  {
    Board board = GetBoard(configuration);
    int arrayY = y - board.MinY;
    bool inNoMansLand = MatchRules.GetTeamForArrayRow(board, configuration.GameMode, arrayY) is null;
    bool onEdge = new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) }
      .Any(neighbour => !board.ContainsCell(neighbour));
    return inNoMansLand && onEdge && Contains(configuration, x, y);
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
  )
  {
    for (int offsetY = 0; offsetY < height; offsetY++)
    {
      for (int offsetX = 0; offsetX < width; offsetX++)
      {
        if (!NetworkBoardRules.Contains(configuration, x + offsetX, y + offsetY)) return false;
      }
    }
    return true;
  }

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
