using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// Rule adapter for CPU simulation. Geometry, movement, combat, economy, terrain, and board zones
/// are delegated to the same shared rule APIs used by the authoritative match implementation.
/// </summary>
public static partial class CpuGameRules
{
  public static bool IsLegal(CpuGameState state, ICpuGameAction action)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(action);
    if (state.IsFinished || action.Team == NetworkTeam.Neutral || action.Team != state.CurrentTurn ||
        !state.Teams.ContainsKey(action.Team) || state.Scenario?.Restrictions.Allows(state, action) == false)
    {
      return false;
    }

    return action switch
    {
      MoveAction move => IsLegalMove(state, move),
      AttackAction attack => IsLegalAttack(state, attack),
      PurchaseAction purchase => IsLegalPurchase(state, purchase),
      UseAbilityAction ability => IsLegalAbility(state, ability),
      EndTurnAction => state.InitialBuy is null && state.Teams.TryGetValue(action.Team, out CpuTeamState? team) &&
        (!Globals.ActionLimitsEnabled || (team.ActionsRemaining is > 0 &&
          (team.ActionsRemaining < team.ActionLimit || team.ChosenRoyal == "Palace"))),
      StopInitialBuyingAction => IsLegalStopInitialBuying(state, action.Team),
      _ => false
    };
  }

  public static CpuGameState Apply(CpuGameState state, ICpuGameAction action)
  {
    if (!IsLegal(state, action))
    {
      throw new InvalidOperationException($"Illegal CPU action: {action.Describe()}");
    }

    return ApplyLegal(state, action);
  }

  /// <summary>
  /// Applies an action that was produced by <see cref="CpuActionGenerator"/> for this exact
  /// snapshot. Search uses this after its membership check, avoiding a second, expensive rules
  /// validation for every branch. Public callers must use <see cref="Apply"/> instead.
  /// </summary>
  internal static CpuGameState ApplyLegal(CpuGameState state, ICpuGameAction action)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(action);

    CpuMutableGameState mutable = state.ToMutable();
    switch (action)
    {
      case MoveAction move: ApplyMove(mutable, move); break;
      case AttackAction attack: ApplyAttack(mutable, attack); break;
      case PurchaseAction purchase: ApplyPurchase(mutable, purchase); break;
      case UseAbilityAction ability: ApplyAbility(mutable, ability); break;
      case EndTurnAction: ApplyEndTurn(mutable, action.Team); break;
      case StopInitialBuyingAction: ApplyStopInitialBuying(mutable, action.Team); break;
      default: throw new ArgumentOutOfRangeException(nameof(action));
    }
    return mutable.Freeze();
  }

  /// <summary>Returns all legal terrain-aware destinations for one unit without mutating the state.</summary>
  public static IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> GetLegalMovementPaths(
    CpuGameState state,
    NetworkPiece piece
  )
  {
    if (!UnitRules.TryGet(piece.Type, out UnitRule rule) || (piece.HasMovedThisTurn &&
        !AbilityRules.CanUseCavalierFollowUpMove(piece.Type, piece.CavalierFollowUpMoveAvailable)) ||
        piece.AttachmentKind is NetworkAttachmentKind.Guard ||
        (piece.AttachmentKind == NetworkAttachmentKind.Carried && piece.Type != nameof(PieceType.Ox)))
    {
      return new Dictionary<(int x, int y), List<(int x, int y)>>();
    }

    return GetLegalMovementPaths(state, state.Pieces, piece, rule);
  }

  /// <summary>Checks whether a unit can directly attack a target using shared attack geometry and line of sight.</summary>
  public static bool CanDirectlyAttack(CpuGameState state, NetworkPiece attacker, NetworkPiece target)
  {
    bool carriedCargoMayAttackHost = attacker.AttachmentKind == NetworkAttachmentKind.Carried && attacker.AttachedToId == target.Id;
    return (attacker.AttachedToId is null || carriedCargoMayAttackHost) && attacker.Team != target.Team && target.AttachedToId is null &&
      UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) &&
      UnitRules.TryGet(target.Type, out UnitRule targetRule) &&
      attackerRule.Attack > 0 && !attacker.HasAttackedThisTurn &&
      UnitRules.CanAttack(attackerRule, attacker.X, attacker.Y, attacker.Team, targetRule, target.X, target.Y) &&
      HasClearAttackPath(state, state.Pieces, attacker, target, state.Barricades);
  }

  /// <summary>Checks whether an unoccupied board square is currently threatened by an attacker.</summary>
  public static bool CanDirectlyAttackSquare(CpuGameState state, NetworkPiece attacker, int targetX, int targetY)
  {
    string? occupiedTargetId = state.Pieces.FirstOrDefault(piece => piece.Id != attacker.Id &&
      UnitRules.TryGet(piece.Type, out UnitRule targetRule) && Occupies(targetRule, piece, (targetX, targetY)))?.Id;
    return attacker.AttachedToId is null && UnitRules.TryGet(attacker.Type, out UnitRule rule) &&
      rule.Attack > 0 && !attacker.HasAttackedThisTurn &&
      CanUseActionSquare(attacker, targetX, targetY) &&
      HasClearAttackPath(state, state.Pieces, attacker, (targetX, targetY), occupiedTargetId, state.Barricades);
  }

  /// <summary>Estimates direct-combat damage using the same shared combat modifiers as simulation.</summary>
  public static int EstimateAttackDamage(CpuGameState state, NetworkPiece attacker, NetworkPiece target)
  {
    NetworkPiece damaged = state.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard) ?? target;
    int unmitigated = CombatRules.CalculateDamage(
      UnitRules.GetRequired(attacker.Type).Attack,
      HasAdjacentUnit(state, state.Pieces, attacker, attacker.Team, "Baron"),
      state.Pieces.Any(piece => piece.Type == "Spy" && piece.MarkedTargetId == target.Id),
      false,
      false,
      0
    );
    return CombatRules.CalculateDamage(
      unmitigated,
      false,
      false,
      HasAdjacentUnit(state, state.Pieces, damaged, damaged.Team, "Baron"),
      IsInForest(state, damaged),
      state.Terrain.ForestDamageReduction
    );
  }

  internal static bool CanUseActionSquare(NetworkPiece actor, int targetX, int targetY)
  {
    if (!UnitRules.TryGet(actor.Type, out UnitRule rule) || rule.AttackPattern == RuleShape.None)
    {
      return false;
    }

    foreach ((int x, int y) origin in OccupiedSquares(rule, (actor.X, actor.Y)))
    {
      if (UnitRules.CanAttackOffset(
        rule.AttackPattern,
        rule.MinimumAttackRange,
        rule.AttackRange,
        actor.Team,
        targetX - origin.x,
        targetY - origin.y))
      {
        return true;
      }
    }

    return false;
  }

  private static NetworkPiece? FindUnitOnAttackedSquare(
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece attacker,
    int targetX,
    int targetY
  )
  {
    return pieces.FirstOrDefault(piece => piece.Id != attacker.Id && piece.Team != attacker.Team &&
      piece.AttachedToId is null && piece.Type != "Farm" &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      Occupies(rule, piece, (targetX, targetY)));
  }

  private static bool IsLegalMove(CpuGameState state, MoveAction action)
  {
    NetworkPiece? piece = FindPiece(state.Pieces, action.PieceId);
    if (piece is null || piece.Team != action.Team || (piece.HasMovedThisTurn &&
        !AbilityRules.CanUseCavalierFollowUpMove(piece.Type, piece.CavalierFollowUpMoveAvailable)) ||
        piece.AttachmentKind == NetworkAttachmentKind.Guard ||
        (piece.AttachmentKind == NetworkAttachmentKind.Carried && piece.Type != nameof(PieceType.Ox)))
    {
      return false;
    }

    return GetLegalMovementPaths(state, piece).ContainsKey((action.DestinationX, action.DestinationY));
  }

  private static bool IsLegalAttack(CpuGameState state, AttackAction action)
  {
    NetworkPiece? attacker = FindPiece(state.Pieces, action.AttackerId);
    if (attacker is null || attacker.Team != action.Team || attacker.HasAttackedThisTurn ||
        !UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) || attackerRule.Attack <= 0)
    {
      return false;
    }

    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    bool carriedCargoMayAttackHost = attacker.AttachmentKind == NetworkAttachmentKind.Carried && attacker.AttachedToId == target?.Id;
    if (attacker.AttachedToId is not null && !carriedCargoMayAttackHost)
    {
      return false;
    }
    if (target is { Type: "Farm" })
    {
      target = FindUnitOnAttackedSquare(state.Pieces, attacker, action.TargetX, action.TargetY) ?? target;
    }
    if (target is null)
    {
      return action.TargetPieceId is null && state.Barricades.ContainsKey((action.TargetX, action.TargetY)) &&
        CanUseActionSquare(attacker, action.TargetX, action.TargetY) &&
        HasClearAttackPath(state, state.Pieces, attacker, (action.TargetX, action.TargetY), null, state.Barricades);
    }

    return target.Team != attacker.Team && target.AttachedToId is null &&
      UnitRules.TryGet(target.Type, out UnitRule targetRule) &&
      Occupies(targetRule, target, (action.TargetX, action.TargetY)) &&
      CanUseActionSquare(attacker, action.TargetX, action.TargetY) &&
      HasClearAttackPath(state, state.Pieces, attacker, (action.TargetX, action.TargetY), target.Id, state.Barricades);
  }

  private static bool IsLegalPurchase(CpuGameState state, PurchaseAction action)
  {
    if (!UnitRules.TryGet(action.UnitType, out UnitRule rule) || !UnitRules.Purchasable.Contains(rule) ||
        !PackRules.IsAllowed(action.UnitType, state.Configuration.AllowedPacks) ||
        (rule.Type == "Farm" && !state.Configuration.FarmsEnabled))
    {
      return false;
    }

    CpuTeamState buyer = state.Teams[action.Team];
    NetworkPiece? occupyingMercenary = state.Pieces.FirstOrDefault(piece =>
      piece.Type == "Mercenary" && piece.Team != action.Team && piece.X == action.X && piece.Y == action.Y);
    if (occupyingMercenary is not null)
    {
      if (state.InitialBuy is not null || action.UnitType != "Mercenary" ||
          occupyingMercenary.Team != NetworkTeam.Neutral)
      {
        return false;
      }

      return buyer.Money >= PieceDefinitions.NeutralMercenaryHireCost;
    }

    bool initialBuy = state.InitialBuy is not null;
    if (initialBuy && rule.Type == "Mercenary")
    {
      return false;
    }

    if (initialBuy && !IsCurrentInitialBuyer(state.InitialBuy!, action.Team))
    {
      return false;
    }

    bool openingFarmPlacement = state.InitialBuy?.IsFarmPlacementPhase == true && rule.Type == "Farm";
    if (state.InitialBuy?.IsFarmPlacementPhase == true && !openingFarmPlacement)
    {
      return false;
    }

    if (!openingFarmPlacement && buyer.Money < GetUnitPrice(state.Configuration, rule))
    {
      return false;
    }

    bool inValidZone = rule.Type == "Mercenary" && !initialBuy
      ? BoardRules.CanPlaceMercenary(state.Board, state.Configuration.GameMode, state.Configuration.PlayerCount, action.X, action.Y)
      : BoardRules.CanPlaceForTeam(state.Board, state.Configuration.GameMode, state.Configuration.PlayerCount, action.Team, action.X, action.Y, rule.Width, rule.Height);
    // Ordinary units may share a Farm footprint, but the live Mercenary rule is stricter: a
    // newly hired Mercenary must occupy a completely empty No-Man's-Land square. Hiring a
    // neutral Mercenary above is the only intentional occupied-square exception.
    bool mercenarySquareIsEmpty = rule.Type != "Mercenary" || !state.Pieces.Any(piece =>
      UnitRules.TryGet(piece.Type, out UnitRule existingRule) &&
      UnitRules.FootprintsOverlap(action.X, action.Y, rule.Width, rule.Height,
        piece.X, piece.Y, existingRule.Width, existingRule.Height));
    return inValidZone && mercenarySquareIsEmpty && CanPlace(state, state.Pieces, rule, action.X, action.Y);
  }

  private static bool IsLegalAbility(CpuGameState state, UseAbilityAction action)
  {
    NetworkPiece? actor = FindPiece(state.Pieces, action.ActorId);
    if (actor is null || actor.Team != action.Team || actor.AttachedToId is not null)
    {
      return false;
    }

    bool demolition = actor.Type == "Engineer" && AbilityRules.IsEngineerDemolition(action.Ability);
    if (actor.HasAttackedThisTurn && !demolition)
    {
      return false;
    }

    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    if (action.TargetPieceId is not null && target is null)
    {
      return false;
    }

    if (target is not null && (!UnitRules.TryGet(target.Type, out UnitRule actionTargetRule) ||
      !Occupies(actionTargetRule, target, (action.TargetX, action.TargetY))))
    {
      return false;
    }

    bool plunderPickup = state.Configuration.GameMode == "Plunder" &&
      string.Equals(action.Ability, "PickUpTreasure", StringComparison.OrdinalIgnoreCase);
    bool isCarryThrowUnit = AbilityRules.IsCarryThrowUnit(actor.Type);
    if (!plunderPickup && actor.Type is not ("Mercenary" or "Phantom") && !isCarryThrowUnit &&
        !CanUseActionSquare(actor, action.TargetX, action.TargetY))
    {
      return false;
    }

    // Treasure pickup is available to every eligible non-royal 1x1 unit, including a
    // Mercenary. Check it before unit-specific abilities so a Mercenary may either fire or
    // pick up Plunder treasure, matching the live action flow.
    if (plunderPickup)
    {
      return CanPickUpTreasure(state, actor, action.TargetX, action.TargetY);
    }

    return actor.Type switch
    {
      "Spy" => string.Equals(action.Ability, "Mark", StringComparison.OrdinalIgnoreCase) &&
        target is not null && target.Team != actor.Team && actor.AttachedToId is null,
      "Engineer" => IsLegalEngineerAbility(state, actor, action, target),
      "Guard" => string.Equals(action.Ability, "Attach", StringComparison.OrdinalIgnoreCase) &&
        target is not null && target.Team == actor.Team && target.Id != state.TreasureCarrierId &&
        UnitRules.TryGet(target.Type, out UnitRule targetRule) && UnitRules.TryGet(actor.Type, out UnitRule guardRule) &&
        AbilityRules.CanGuardAttach(guardRule, targetRule, actor.AttachedToId is not null,
          state.Pieces.Any(piece => piece.AttachedToId == target.Id && piece.AttachmentKind == NetworkAttachmentKind.Guard)),
      "Ox" => string.Equals(action.Ability, "Attach", StringComparison.OrdinalIgnoreCase) &&
        target is not null && target.Team == actor.Team && target.Id != actor.Id && target.Id != state.TreasureCarrierId &&
        UnitRules.TryGet(target.Type, out UnitRule oxTargetRule) && UnitRules.TryGet(actor.Type, out UnitRule oxRule) &&
        AbilityRules.CanOxAttach(oxRule, oxTargetRule, actor.AttachedToId is not null,
          state.Pieces.Any(piece => piece.AttachedToId == target.Id && piece.Type == nameof(PieceType.Ox))),
      nameof(PieceType.Giant) or nameof(PieceType.Cyclops) =>
        string.Equals(action.Ability, "Carry", StringComparison.OrdinalIgnoreCase)
          ? target is not null && CanCarryUnit(state, actor, target)
          : string.Equals(action.Ability, "Throw", StringComparison.OrdinalIgnoreCase) &&
            target is null && CanThrowUnit(state, actor, action.TargetX, action.TargetY),
      "Mercenary" => string.Equals(action.Ability, "Fire", StringComparison.OrdinalIgnoreCase) &&
        actor.Team != NetworkTeam.Neutral && action.TargetPieceId is null &&
        action.TargetX == actor.X && action.TargetY == actor.Y,
      "Phantom" => string.Equals(action.Ability, "Unpossess", StringComparison.OrdinalIgnoreCase)
        ? !string.IsNullOrEmpty(actor.PossessedUnitId)
        : string.Equals(action.Ability, "Possess", StringComparison.OrdinalIgnoreCase) && target is not null &&
          RoyalAbilityRules.CanPhantomPossess(
            actor.Type, actor.Team, actor.PossessedUnitId,
            target.Id, target.Type, target.Team, target.IsRoyalProxy),
      _ => false
    };
  }

  internal static NetworkPiece? GetCarriedUnit(CpuGameState state, NetworkPiece carrier) =>
    state.Pieces.FirstOrDefault(piece => piece.AttachedToId == carrier.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Carried);

  private static bool CanCarryUnit(CpuGameState state, NetworkPiece carrier, NetworkPiece target)
  {
    return target.Id != carrier.Id && target.AttachedToId is null && target.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(carrier.Type, out UnitRule carrierRule) && UnitRules.TryGet(target.Type, out UnitRule targetRule) &&
      AbilityRules.CanCarry(carrierRule, (carrier.X, carrier.Y), targetRule, (target.X, target.Y),
        carrier.AttachedToId is not null, target.AttachedToId is not null, GetCarriedUnit(state, carrier) is not null);
  }

  private static bool CanThrowUnit(CpuGameState state, NetworkPiece carrier, int targetX, int targetY)
  {
    NetworkPiece? cargo = GetCarriedUnit(state, carrier);
    return cargo is not null && UnitRules.TryGet(carrier.Type, out UnitRule carrierRule) &&
      UnitRules.TryGet(cargo.Type, out UnitRule cargoRule) &&
      BoardRules.Contains(state.Board, targetX, targetY) &&
      AbilityRules.CanThrow(carrierRule, cargoRule, (carrier.X, carrier.Y), (targetX, targetY)) &&
      CanPlace(state, state.Pieces, cargoRule, targetX, targetY, cargo.Id);
  }

  private static bool IsLegalEngineerAbility(CpuGameState state, NetworkPiece actor, UseAbilityAction action, NetworkPiece? target)
  {
    bool demolition = AbilityRules.IsEngineerDemolition(action.Ability);
    if ((!demolition && actor.EngineerBuildsThisTurn >= AbilityRules.EngineerBuildsPerTurn) || target is not null ||
        (!demolition && !AbilityRules.IsEngineerBuild(action.Ability)))
    {
      return false;
    }

    (int x, int y) position = (action.TargetX, action.TargetY);
    if (demolition)
    {
      return state.Roads.ContainsKey(position) || state.Barricades.ContainsKey(position) || state.Mines.ContainsKey(position);
    }

    return BoardRules.Contains(state.Board, action.TargetX, action.TargetY) &&
      !state.Terrain.IsLake(position) && !state.Roads.ContainsKey(position) &&
      !state.Barricades.ContainsKey(position) && !state.Mines.ContainsKey(position) &&
      !PieceOccupies(state.Pieces, position);
  }

  private static bool IsLegalStopInitialBuying(CpuGameState state, NetworkTeam team) => state.InitialBuy is { IsComplete: false, IsFarmPlacementPhase: false } initialBuy &&
    IsCurrentInitialBuyer(initialBuy, team);

  private static void RemovePiece(CpuMutableGameState state, string pieceId)
  {
    foreach (int index in Enumerable.Range(0, state.Pieces.Count).Reverse())
    {
      NetworkPiece piece = state.Pieces[index];
      if (piece.AttachedToId == pieceId)
      {
        state.Pieces[index] = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      }
      if (piece.MarkedTargetId == pieceId)
      {
        state.Pieces[index] = state.Pieces[index] with { MarkedTargetId = null };
      }
    }

    int removeIndex = FindPieceIndex(state.Pieces, pieceId);
    if (removeIndex >= 0)
    {
      state.Pieces.RemoveAt(removeIndex);
    }
  }

  private static void RespawnEscortRoyal(CpuMutableGameState state, NetworkPiece defeated, UnitRule rule)
  {
    if (rule.Type == "Palace")
    {
      return;
    }

    foreach ((int x, int y) position in MatchRules.GetRoyalSpawnCandidates(
      state.Source.Board,
      defeated.Team,
      rule.Width,
      rule.Height,
      state.Source.Configuration.PlayerCount))
    {
      if (BoardRules.CanPlaceForTeam(state.Source.Board, state.Source.Configuration.GameMode, state.Source.Configuration.PlayerCount, defeated.Team, position.x, position.y, rule.Width, rule.Height) &&
          CanPlace(state.Source, state.Pieces, rule, position.x, position.y))
      {
        int health = Math.Max(1, (int)Math.Ceiling(rule.Health * (state.Source.Configuration.EscortRoyalHealthPercent / 100d)));
        state.Pieces.Add(new NetworkPiece(CreatePieceId(state, rule.Type), rule.Type, defeated.Team, position.x, position.y, health));
        return;
      }
    }
  }

  private static void MoveAttachedPieces(CpuMutableGameState state, NetworkPiece host)
  {
    for (int index = 0; index < state.Pieces.Count; index++)
    {
      NetworkPiece attachment = state.Pieces[index];
      if (attachment.AttachedToId == host.Id)
      {
        state.Pieces[index] = attachment with { X = host.X, Y = host.Y, HasMovedThisTurn = true };
      }
    }
  }

  private static void MoveHeraldCompanions(CpuMutableGameState state, NetworkPiece herald, int oldX, int oldY)
  {
    if (herald.Type != nameof(PieceType.Herald)) return;
    int deltaX = herald.X - oldX;
    int deltaY = herald.Y - oldY;
    List<string> companionIds = state.Pieces
      .Where(piece => piece.Id != herald.Id && piece.Id != state.TreasureCarrierId && piece.Team == herald.Team &&
        piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        AbilityRules.IsHeraldCompanion(rule, (oldX, oldY), (piece.X, piece.Y)))
      .Select(piece => piece.Id)
      .ToList();
    foreach (string companionId in companionIds)
    {
      int index = FindPieceIndex(state.Pieces, companionId);
      if (index < 0 || !UnitRules.TryGet(state.Pieces[index].Type, out UnitRule rule)) continue;
      NetworkPiece companion = state.Pieces[index];
      int destinationX = companion.X + deltaX;
      int destinationY = companion.Y + deltaY;
      if (CanPlace(state.Source, state.Pieces, rule, destinationX, destinationY, companion.Id))
      {
        state.Pieces[index] = companion with { X = destinationX, Y = destinationY, HasMovedThisTurn = true };
      }
    }
  }

  private static void TryDeliverTreasure(CpuMutableGameState state, NetworkPiece piece)
  {
    if (state.Source.Configuration.GameMode != "Plunder" || state.TreasureCarrierId != piece.Id ||
        !BoardRules.IsInTeamTerritory(state.Source.Board, state.Source.Configuration.GameMode, state.Source.Configuration.PlayerCount, piece.Team, piece.X, piece.Y))
    {
      return;
    }

    int score = Math.Clamp(
      state.ModeScores.GetValueOrDefault(piece.Team) + state.Source.Configuration.PlunderDeliveryScore,
      0,
      state.Source.Configuration.PlunderWinScore
    );
    state.ModeScores[piece.Team] = score;
    state.TreasureCarrierId = null;
    state.TreasurePosition = MatchRules.GetTreasureSpawn(state.Source.Board);
    if (score >= state.Source.Configuration.PlunderWinScore)
    {
      state.Winner = piece.Team;
    }
  }

  private static bool IsEscortVictory(CpuMutableGameState state, NetworkPiece piece) =>
    state.Source.Configuration.GameMode == "Escort" && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    rule.Category == RuleCategory.Royal && OccupiedSquares(rule, (piece.X, piece.Y))
      .Any(square => MatchRules.IsOnEnemyBackEdge(state.Source.Board, piece.Team, square));

  private static void SpendAction(CpuMutableGameState state, NetworkTeam team)
  {
    if (!Globals.ActionLimitsEnabled)
    {
      return;
    }

    CpuTeamState current = state.Teams[team];
    state.Teams[team] = current with { ActionsRemaining = current.ActionsRemaining - 1 };
    if (state.Teams[team].ActionsRemaining > 0)
    {
      return;
    }

    CompleteTurn(state, team);
  }

  private static void CompleteTurn(CpuMutableGameState state, NetworkTeam team)
  {
    ApplyEndOfTurnObjectives(state, team);
    if (state.Winner is not null)
    {
      return;
    }

    state.Teams[team] = state.Teams[team] with { ActionsRemaining = state.Teams[team].ActionLimit };
    state.CurrentTurn = TeamRules.GetNextTeam(team, state.Source.Configuration.PlayerCount);
    state.TurnNumber++;
    ApplyScenarioReinforcements(state);
    ApplyScenarioTurnLimit(state);
    if (state.Winner is not null || state.Source.Scenario?.IsTerminal(state.Freeze()) == true)
    {
      return;
    }
    ApplyTurnEconomy(state, state.CurrentTurn);
    ResetTurnActions(state, state.CurrentTurn);
  }

  private static void ApplyScenarioReinforcements(CpuMutableGameState state)
  {
    CpuScenarioDefinition? scenario = state.Source.Scenario;
    if (scenario is null || scenario.ScriptedReinforcements.Count == 0)
    {
      return;
    }

    foreach (CpuScriptedReinforcement reinforcement in scenario.ScriptedReinforcements.Where(
      candidate => candidate.TurnNumber == state.TurnNumber))
    {
      if (reinforcement.Team == NetworkTeam.Neutral || !UnitRules.TryGet(reinforcement.UnitType, out UnitRule rule) ||
          !BoardRules.Contains(state.Source.Board, reinforcement.X, reinforcement.Y) ||
          !CanPlace(state.Source, state.Pieces, rule, reinforcement.X, reinforcement.Y))
      {
        continue;
      }

      string id = string.IsNullOrWhiteSpace(reinforcement.PieceId)
        ? CreatePieceId(state, rule.Type)
        : reinforcement.PieceId;
      if (FindPiece(state.Pieces, id) is not null)
      {
        continue;
      }

      int health = Math.Clamp(reinforcement.Health ?? rule.Health, 1, rule.Health);
      state.Pieces.Add(new NetworkPiece(id, rule.Type, reinforcement.Team, reinforcement.X, reinforcement.Y, health));
    }
  }

  private static void ApplyScenarioTurnLimit(CpuMutableGameState state)
  {
    CpuScenarioDefinition? scenario = state.Source.Scenario;
    if (scenario?.TurnLimit is int limit && state.TurnNumber >= Math.Max(0, limit) &&
        scenario.WinnerOnTurnLimit is NetworkTeam winner && state.Teams.ContainsKey(winner))
    {
      state.Winner = winner;
    }
  }

  private static void ApplyEndOfTurnObjectives(CpuMutableGameState state, NetworkTeam team)
  {
    if (state.Source.Configuration.GameMode == "Conquest")
    {
      int pressure = state.Pieces.Count(piece => piece.AttachmentKind == NetworkAttachmentKind.None &&
        !piece.CannotContributeToConquestThisTurn && piece.Team == team && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        OccupiedSquares(rule, (piece.X, piece.Y)).Any(square => MatchRules.IsConquestSquare(state.Source.Board, square)));
      if (state.Source.Configuration.PlayerCount == 2)
      {
        state.ConquestScore = Math.Clamp(state.ConquestScore + (team == NetworkTeam.Red ? -pressure : pressure),
          -state.Source.Configuration.ConquestWinScore, state.Source.Configuration.ConquestWinScore);
        if (Math.Abs(state.ConquestScore) >= state.Source.Configuration.ConquestWinScore)
        {
          state.Winner = state.ConquestScore < 0 ? NetworkTeam.Red : NetworkTeam.Blue;
        }
      }
      else
      {
        int score = Math.Clamp(state.ConquestScores.GetValueOrDefault(team) + pressure, 0, state.Source.Configuration.ConquestWinScore);
        state.ConquestScores[team] = score;
        if (score >= state.Source.Configuration.ConquestWinScore)
        {
          state.Winner = team;
        }
      }
    }
    else if (state.Source.Configuration.GameMode == "Dominion")
    {
      int score = Math.Clamp(state.ModeScores.GetValueOrDefault(team) + GetDominionControlledPoints(state, team),
        0, state.Source.Configuration.DominionWinScore);
      state.ModeScores[team] = score;
      if (score >= state.Source.Configuration.DominionWinScore)
      {
        state.Winner = team;
      }
    }

    if (state.Winner is null && state.Source.Scenario is CpuScenarioDefinition scenario)
    {
      if (scenario.VictoryGoals.Any(goal => goal.GetStatus(state.Freeze(), team) == CpuGoalStatus.Completed))
      {
        state.Winner = team;
      }
      else if (scenario.DefeatConditions.Any(goal => goal.GetStatus(state.Freeze(), team) == CpuGoalStatus.Failed))
      {
        state.Winner = TeamRules.GetActiveTeams(state.Source.Configuration.PlayerCount).First(candidate => candidate != team);
      }
    }
  }

  private static int GetDominionControlledPoints(CpuMutableGameState state, NetworkTeam team)
  {
    int count = 0;
    foreach ((int x, int y) point in MatchRules.GetDominionControlPoints(state.Source.Board))
    {
      bool friendly = state.Pieces.Any(piece => piece.Team == team && piece.AttachedToId is null &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, point));
      bool enemy = state.Pieces.Any(piece => piece.Team is not NetworkTeam.Neutral && piece.Team != team &&
        piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, point));
      if (friendly && !enemy)
      {
        count++;
      }
    }
    return count;
  }

  private static void ApplyTurnEconomy(CpuMutableGameState state, NetworkTeam team)
  {
    if (!state.Teams.TryGetValue(team, out CpuTeamState? current))
    {
      return;
    }

    int money = current.Money;
    if (state.Source.Configuration.InterestEnabled && state.Source.Configuration.InterestPercent != 0)
    {
      money = ClampCurrency((long)money + EconomyRules.GetInterest(money, state.Source.Configuration.InterestPercent));
    }
    int farms = state.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null && piece.Type == "Farm");
    money = ClampCurrency((long)money + farms * (long)state.Source.Configuration.FarmIncomePerTurn);
    for (int index = 0; index < state.Pieces.Count; index++)
    {
      NetworkPiece mercenary = state.Pieces[index];
      if (mercenary.Team != team || mercenary.AttachedToId is not null || mercenary.Type != "Mercenary")
      {
        continue;
      }

      const int mercenaryPayroll = 10;
      if (money < mercenaryPayroll)
      {
        state.Pieces[index] = mercenary with
        {
          Team = NetworkTeam.Neutral,
          HasMovedThisTurn = true,
          HasAttackedThisTurn = true
        };
        continue;
      }

      money = ClampCurrency((long)money - mercenaryPayroll);
    }
    if (state.Source.Configuration.UnitMaintenanceEnabled && state.Source.Configuration.UnitMaintenancePercent > 0)
    {
      long upkeep = state.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null)
        .Sum(piece => UnitRules.TryGet(piece.Type, out UnitRule rule)
          ? (long)GetUnitMaintenance(state.Source.Configuration, rule)
          : 0L);
      money = ClampCurrency((long)money - upkeep);
    }
    state.Teams[team] = current with { Money = money };
  }

  private static void ResetTurnActions(CpuMutableGameState state, NetworkTeam team)
  {
    for (int index = 0; index < state.Pieces.Count; index++)
    {
      NetworkPiece piece = state.Pieces[index];
      if (piece.Team == team)
      {
        state.Pieces[index] = piece with
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

  private static int GetAttackDamage(CpuMutableGameState state, NetworkPiece attacker, NetworkPiece target) =>
    CombatRules.CalculateDamage(
      UnitRules.GetRequired(attacker.Type).Attack,
      HasAdjacentUnit(state, attacker, attacker.Team, "Baron"),
      state.Pieces.Any(piece => piece.Type == "Spy" && piece.MarkedTargetId == target.Id),
      false,
      false,
      0
    );

  private static bool HasAdjacentUnit(CpuMutableGameState state, NetworkPiece piece, NetworkTeam team, string type) =>
    UnitRules.TryGet(piece.Type, out UnitRule pieceRule) && state.Pieces.Any(candidate => candidate.Id != piece.Id &&
      candidate.Team == team && candidate.Type == type && UnitRules.TryGet(candidate.Type, out UnitRule candidateRule) &&
      OccupiedSquares(pieceRule, (piece.X, piece.Y)).Any(first => OccupiedSquares(candidateRule, (candidate.X, candidate.Y))
        .Any(second => Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y) == 1)));

  private static bool HasAdjacentUnit(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    NetworkTeam team,
    string type
  ) => UnitRules.TryGet(piece.Type, out UnitRule pieceRule) && pieces.Any(candidate => candidate.Id != piece.Id &&
    candidate.Team == team && candidate.Type == type && UnitRules.TryGet(candidate.Type, out UnitRule candidateRule) &&
    OccupiedSquares(pieceRule, (piece.X, piece.Y)).Any(first => OccupiedSquares(candidateRule, (candidate.X, candidate.Y))
      .Any(second => Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y) == 1)));

  private static bool IsInForest(CpuMutableGameState state, NetworkPiece piece) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    OccupiedSquares(rule, (piece.X, piece.Y)).Any(state.Source.Terrain.IsForest);

  private static bool IsInForest(CpuGameState state, NetworkPiece piece) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    OccupiedSquares(rule, (piece.X, piece.Y)).Any(state.Terrain.IsForest);

}
