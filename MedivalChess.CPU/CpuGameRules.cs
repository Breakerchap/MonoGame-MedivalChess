using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// Rule adapter for CPU simulation. Geometry, movement, combat, economy, terrain, and board zones
/// are delegated to the same shared rule APIs used by the authoritative match implementation.
/// </summary>
public static class CpuGameRules
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
      EndTurnAction => state.InitialBuy is null && state.ActionsRemaining is > 0 and < MatchRules.ActionsPerTurn,
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
    if (!UnitRules.TryGet(piece.Type, out UnitRule rule) || piece.HasMovedThisTurn ||
        piece.AttachmentKind == NetworkAttachmentKind.Guard)
    {
      return new Dictionary<(int x, int y), List<(int x, int y)>>();
    }

    return GetLegalMovementPaths(state, state.Pieces, piece, rule);
  }

  /// <summary>Checks whether a unit can directly attack a target using shared attack geometry and line of sight.</summary>
  public static bool CanDirectlyAttack(CpuGameState state, NetworkPiece attacker, NetworkPiece target)
  {
    return attacker.AttachedToId is null && attacker.Team != target.Team && target.AttachedToId is null &&
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
      HasAdjacentUnit(state, state.Pieces, damaged, damaged.Team, "King"),
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

  private static bool IsLegalMove(CpuGameState state, MoveAction action)
  {
    NetworkPiece? piece = FindPiece(state.Pieces, action.PieceId);
    if (piece is null || piece.Team != action.Team || piece.HasMovedThisTurn ||
        piece.AttachmentKind == NetworkAttachmentKind.Guard)
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
    if (target is null)
    {
      return action.TargetPieceId is null && state.Barricades.ContainsKey((action.TargetX, action.TargetY)) &&
        CanUseActionSquare(attacker, action.TargetX, action.TargetY) &&
        HasClearAttackPath(state, state.Pieces, attacker, (action.TargetX, action.TargetY), null, state.Barricades);
    }

    return target.Team != attacker.Team && target.AttachedToId is null &&
      UnitRules.TryGet(target.Type, out UnitRule targetRule) &&
      UnitRules.CanAttack(attackerRule, attacker.X, attacker.Y, attacker.Team, targetRule, target.X, target.Y) &&
      HasClearAttackPath(state, state.Pieces, attacker, target, state.Barricades);
  }

  private static bool IsLegalPurchase(CpuGameState state, PurchaseAction action)
  {
    if (!UnitRules.TryGet(action.UnitType, out UnitRule rule) || !UnitRules.Purchasable.Contains(rule) ||
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
          (occupyingMercenary.Team != NetworkTeam.Neutral &&
           !BoardRules.IsInTeamTerritory(state.Configuration, action.Team, action.X, action.Y)))
      {
        return false;
      }

      int cost = occupyingMercenary.Team == NetworkTeam.Neutral
        ? Math.Max(1, occupyingMercenary.LastBid)
        : Math.Max(occupyingMercenary.LastBid, GetUnitPrice(state.Configuration, rule)) + 10;
      return buyer.Money >= cost;
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
      ? BoardRules.CanPlaceMercenary(state.Configuration, action.X, action.Y)
      : BoardRules.CanPlaceForTeam(state.Configuration, action.Team, action.X, action.Y, rule.Width, rule.Height);
    return inValidZone && CanPlace(state, state.Pieces, rule, action.X, action.Y);
  }

  private static bool IsLegalAbility(CpuGameState state, UseAbilityAction action)
  {
    NetworkPiece? actor = FindPiece(state.Pieces, action.ActorId);
    if (actor is null || actor.Team != action.Team)
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

    bool plunderPickup = state.Configuration.GameMode == "Plunder" &&
      string.Equals(action.Ability, "PickUpTreasure", StringComparison.OrdinalIgnoreCase);
    if (!plunderPickup && actor.Type != "Mercenary" && !CanUseActionSquare(actor, action.TargetX, action.TargetY))
    {
      return false;
    }

    return actor.Type switch
    {
      "Spy" => target is not null && target.Team != actor.Team && actor.AttachedToId is null,
      "Engineer" => IsLegalEngineerAbility(state, actor, action, target),
      "Guard" => target is not null && target.Team == actor.Team && target.Id != state.TreasureCarrierId &&
        UnitRules.TryGet(target.Type, out UnitRule targetRule) && UnitRules.TryGet(actor.Type, out UnitRule guardRule) &&
        AbilityRules.CanGuardAttach(guardRule, targetRule, actor.AttachedToId is not null,
          state.Pieces.Any(piece => piece.AttachedToId == target.Id && piece.AttachmentKind == NetworkAttachmentKind.Guard)),
      "Ox" => target is not null && target.Team == actor.Team && target.Id != actor.Id && target.Id != state.TreasureCarrierId &&
        UnitRules.TryGet(target.Type, out UnitRule cargoRule) && UnitRules.TryGet(actor.Type, out UnitRule oxRule) &&
        AbilityRules.CanOxAttach(oxRule, cargoRule, target.AttachedToId is not null,
          state.Pieces.Any(piece => piece.AttachedToId == actor.Id && piece.AttachmentKind == NetworkAttachmentKind.Carried)),
      "Mercenary" => string.Equals(action.Ability, "Fire", StringComparison.OrdinalIgnoreCase) && actor.Team != NetworkTeam.Neutral,
      _ when plunderPickup => CanPickUpTreasure(state, actor, action.TargetX, action.TargetY),
      _ => false
    };
  }

  private static bool IsLegalEngineerAbility(CpuGameState state, NetworkPiece actor, UseAbilityAction action, NetworkPiece? target)
  {
    bool demolition = AbilityRules.IsEngineerDemolition(action.Ability);
    if ((!demolition && actor.EngineerBuildsThisTurn >= 2) || target is not null ||
        (!demolition && !AbilityRules.IsEngineerBuild(action.Ability)))
    {
      return false;
    }

    (int x, int y) position = (action.TargetX, action.TargetY);
    if (demolition)
    {
      return state.Roads.Contains(position) || state.Barricades.ContainsKey(position) || state.Mines.ContainsKey(position);
    }

    return BoardRules.Contains(state.Configuration, action.TargetX, action.TargetY) &&
      !state.Terrain.IsLake(position) && !state.Roads.Contains(position) &&
      !state.Barricades.ContainsKey(position) && !state.Mines.ContainsKey(position) &&
      !PieceOccupies(state.Pieces, position);
  }

  private static bool IsLegalStopInitialBuying(CpuGameState state, NetworkTeam team) => state.InitialBuy is { IsComplete: false, IsFarmPlacementPhase: false } initialBuy &&
    IsCurrentInitialBuyer(initialBuy, team);

  private static void ApplyMove(CpuMutableGameState state, MoveAction action)
  {
    int index = FindPieceIndex(state.Pieces, action.PieceId);
    NetworkPiece piece = state.Pieces[index];
    if (piece.AttachedToId is not null)
    {
      piece = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      state.Pieces[index] = piece;
    }

    IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> paths = GetLegalMovementPaths(state.Source, state.Pieces, piece, UnitRules.GetRequired(piece.Type));
    List<(int x, int y)> path = paths[(action.DestinationX, action.DestinationY)];
    int oldX = piece.X;
    int oldY = piece.Y;
    bool elephantDamaged = false;
    if (piece.Type == "Elephant" && UnitRules.TryGet(piece.Type, out UnitRule elephantRule))
    {
      foreach (NetworkPiece crossed in state.Pieces.Where(other => other.Id != piece.Id && other.Team != piece.Team).ToArray())
      {
        if (UnitRules.TryGet(crossed.Type, out UnitRule crossedRule) &&
            AbilityRules.PathOverlapsUnit(elephantRule, path, crossedRule, crossed.X, crossed.Y))
        {
          ResolvePieceDamage(state, piece, action.Team, crossed.Id, 15);
          elephantDamaged = true;
        }
      }
    }

    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    piece = state.Pieces[index] with
    {
      X = action.DestinationX,
      Y = action.DestinationY,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = elephantDamaged || state.Pieces[index].HasAttackedThisTurn
    };
    state.Pieces[index] = piece;
    MoveAttachedPieces(state, piece);
    MoveEmissaryCompanions(state, piece, oldX, oldY);
    TriggerMinesAlongMovement(state, piece, path);

    NetworkPiece? moved = FindPiece(state.Pieces, piece.Id);
    if (moved is not null)
    {
      TryDeliverTreasure(state, moved);
      if (state.Winner is null && IsEscortVictory(state, moved))
      {
        state.Winner = moved.Team;
      }
    }

    if (state.Winner is null)
    {
      SpendAction(state, action.Team);
    }
  }

  private static void ApplyAttack(CpuMutableGameState state, AttackAction action)
  {
    int attackerIndex = FindPieceIndex(state.Pieces, action.AttackerId);
    NetworkPiece attacker = state.Pieces[attackerIndex] with { HasAttackedThisTurn = true };
    state.Pieces[attackerIndex] = attacker;
    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    if (target is null)
    {
      DamageBarricade(state, attacker, (action.TargetX, action.TargetY));
    }
    else if (attacker.Type == "Bombard")
    {
      ResolveBombardDamage(state, attacker, action.Team, target);
    }
    else
    {
      ResolvePieceDamage(state, attacker, action.Team, target.Id, null);
    }

    if (target is not null && attacker.Type == "Ballista" && UnitRules.TryGet(attacker.Type, out UnitRule ballistaRule))
    {
      foreach ((int x, int y) position in AbilityRules.GetPiercingRay(ballistaRule, attacker.X, attacker.Y, target.X, target.Y))
      {
        if (!BoardRules.Contains(state.Source.Configuration, position.x, position.y) ||
            state.Source.Terrain.IsForest(position) || state.Barricades.ContainsKey(position))
        {
          break;
        }

        NetworkPiece? pierced = state.Pieces.FirstOrDefault(piece => piece.Id != attacker.Id && piece.Id != target.Id &&
          piece.Team != attacker.Team && piece.Type != "Farm" && piece.AttachedToId is null &&
          UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, position));
        if (pierced is not null)
        {
          ResolvePieceDamage(state, attacker, action.Team, pierced.Id, null);
        }
      }
    }

    if (state.Winner is null)
    {
      SpendAction(state, action.Team);
    }
  }

  private static void ApplyAbility(CpuMutableGameState state, UseAbilityAction action)
  {
    int actorIndex = FindPieceIndex(state.Pieces, action.ActorId);
    NetworkPiece actor = state.Pieces[actorIndex];
    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    bool plunderPickup = state.Source.Configuration.GameMode == "Plunder" &&
      string.Equals(action.Ability, "PickUpTreasure", StringComparison.OrdinalIgnoreCase);

    if (plunderPickup)
    {
      state.TreasureCarrierId = actor.Id;
      state.TreasurePosition = null;
      state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
    }
    else
    {
      switch (actor.Type)
      {
        case "Spy":
          state.Pieces[actorIndex] = actor with { MarkedTargetId = target!.Id };
          break;
        case "Engineer":
          ApplyEngineerAbility(state, actorIndex, action);
          break;
        case "Guard":
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Guard,
            X = target.X,
            Y = target.Y
          };
          break;
        case "Ox":
          int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
          state.Pieces[targetIndex] = target with
          {
            AttachedToId = actor.Id,
            AttachmentKind = NetworkAttachmentKind.Carried,
            X = actor.X,
            Y = actor.Y
          };
          break;
        case "Mercenary":
          state.Pieces[actorIndex] = actor with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true
          };
          break;
      }
    }

    SpendAction(state, action.Team);
  }

  private static void ApplyEngineerAbility(CpuMutableGameState state, int actorIndex, UseAbilityAction action)
  {
    NetworkPiece engineer = state.Pieces[actorIndex];
    (int x, int y) position = (action.TargetX, action.TargetY);
    if (AbilityRules.IsEngineerDemolition(action.Ability))
    {
      _ = state.Roads.Remove(position) || state.Barricades.Remove(position) || state.Mines.Remove(position);
      return;
    }

    if (string.Equals(action.Ability, "Road", StringComparison.OrdinalIgnoreCase))
    {
      state.Roads.Add(position);
    }
    else if (string.Equals(action.Ability, "Barrier", StringComparison.OrdinalIgnoreCase))
    {
      state.Barricades[position] = 20;
    }
    else if (string.Equals(action.Ability, "Mine", StringComparison.OrdinalIgnoreCase))
    {
      state.Mines[position] = engineer.Team;
    }

    int buildsUsed = engineer.EngineerBuildsThisTurn + 1;
    state.Pieces[actorIndex] = engineer with
    {
      EngineerBuildsThisTurn = buildsUsed,
      HasAttackedThisTurn = buildsUsed >= 2
    };
  }

  private static void ApplyPurchase(CpuMutableGameState state, PurchaseAction action)
  {
    NetworkPiece? mercenary = state.Pieces.FirstOrDefault(piece => piece.Type == "Mercenary" &&
      piece.Team != action.Team && piece.X == action.X && piece.Y == action.Y);
    if (mercenary is not null)
    {
      int index = FindPieceIndex(state.Pieces, mercenary.Id);
      int cost = mercenary.Team == NetworkTeam.Neutral
        ? Math.Max(1, mercenary.LastBid)
        : Math.Max(mercenary.LastBid, GetUnitPrice(state.Source.Configuration, UnitRules.GetRequired("Mercenary"))) + 10;
      SpendMoney(state, action.Team, cost);
      if (mercenary.Team != NetworkTeam.Neutral)
      {
        AddMoney(state, mercenary.Team, cost);
      }
      state.Pieces[index] = mercenary with
      {
        Team = action.Team,
        LastBid = cost,
        HasMovedThisTurn = true,
        HasAttackedThisTurn = true,
        CannotContributeToConquestThisTurn = true
      };
      SpendAction(state, action.Team);
      return;
    }

    UnitRule rule = UnitRules.GetRequired(action.UnitType);
    bool openingFarmPlacement = state.InitialBuy?.IsFarmPlacementPhase == true && rule.Type == "Farm";
    if (!openingFarmPlacement)
    {
      SpendMoney(state, action.Team, GetUnitPrice(state.Source.Configuration, rule));
    }

    state.Pieces.Add(new NetworkPiece(
      CreatePieceId(state, rule.Type),
      rule.Type,
      action.Team,
      action.X,
      action.Y,
      rule.Health,
      HasMovedThisTurn: state.InitialBuy is null,
      HasAttackedThisTurn: state.InitialBuy is null,
      LastBid: GetUnitPrice(state.Source.Configuration, rule),
      CannotContributeToConquestThisTurn: state.InitialBuy is null
    ));

    if (state.InitialBuy is null)
    {
      SpendAction(state, action.Team);
    }
    else
    {
      RecordInitialPurchase(state, action.Team);
    }
  }

  private static void ApplyEndTurn(CpuMutableGameState state, NetworkTeam team)
  {
    CpuTeamState current = state.Teams[team];
    state.Teams[team] = current with { ActionsRemaining = 1 };
    SpendAction(state, team);
  }

  private static void ApplyStopInitialBuying(CpuMutableGameState state, NetworkTeam team)
  {
    NetworkInitialBuyState current = state.InitialBuy!;
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records = GetInitialBuyRecords(current, state.Source.Configuration.PlayerCount);
    (int turnsUsed, bool _, int farmsPlaced) = records[team];
    records[team] = (turnsUsed, true, farmsPlaced);
    AdvanceInitialBuyer(state, records, current.PurchasesThisTurn, current.IsFarmPlacementPhase);
  }

  private static void ResolveBombardDamage(CpuMutableGameState state, NetworkPiece attacker, NetworkTeam attackerTeam, NetworkPiece target)
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return;
    }

    foreach (NetworkPiece affected in state.Pieces.Where(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      OccupiedSquares(rule, (piece.X, piece.Y)).Any(square =>
        OccupiedSquares(targetRule, (target.X, target.Y)).Any(targetSquare =>
          Math.Abs(square.x - targetSquare.x) <= 1 && Math.Abs(square.y - targetSquare.y) <= 1))).ToArray())
    {
      ResolvePieceDamage(state, attacker, attackerTeam, affected.Id, 10);
    }
  }

  private static void ResolvePieceDamage(CpuMutableGameState state, NetworkPiece attacker, NetworkTeam attackerTeam, string targetId, int? damageOverride)
  {
    NetworkPiece? target = FindPiece(state.Pieces, targetId);
    if (target is null)
    {
      return;
    }

    NetworkPiece damaged = state.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard) ?? target;
    int unmitigated = damageOverride ?? GetAttackDamage(state, attacker, target);
    int damage = CombatRules.CalculateDamage(
      unmitigated,
      false,
      false,
      HasAdjacentUnit(state, damaged, damaged.Team, "King"),
      IsInForest(state, damaged),
      state.Source.Terrain.ForestDamageReduction
    );
    int damagedIndex = FindPieceIndex(state.Pieces, damaged.Id);
    if (damaged.Health > damage)
    {
      state.Pieces[damagedIndex] = damaged with { Health = damaged.Health - damage };
    }
    else
    {
      HandlePieceDestroyed(state, damaged, attackerTeam);
    }

    for (int index = 0; index < state.Pieces.Count; index++)
    {
      if (state.Pieces[index].MarkedTargetId == target.Id)
      {
        state.Pieces[index] = state.Pieces[index] with { MarkedTargetId = null };
      }
    }
  }

  private static void HandlePieceDestroyed(CpuMutableGameState state, NetworkPiece piece, NetworkTeam? attackingTeam)
  {
    if (state.TreasureCarrierId == piece.Id)
    {
      state.TreasureCarrierId = null;
      state.TreasurePosition = (piece.X, piece.Y);
    }

    if (piece.Team != NetworkTeam.Neutral && attackingTeam is NetworkTeam attacker && attacker != piece.Team)
    {
      int unitPrice = UnitRules.TryGet(piece.Type, out UnitRule rule) ? GetUnitPrice(state.Source.Configuration, rule) : 0;
      AddMoney(state, attacker, CombatRules.RoundCurrencyToNearestFive(unitPrice * state.Source.Configuration.KillerRefundMultiplier));
      AddMoney(state, piece.Team, CombatRules.RoundCurrencyToNearestFive(unitPrice * state.Source.Configuration.DefeatedTeamRefundMultiplier));
    }

    RemovePiece(state, piece.Id);
    if (!UnitRules.TryGet(piece.Type, out UnitRule destroyedRule) || destroyedRule.Category != RuleCategory.Royal)
    {
      return;
    }

    if (state.Source.Configuration.GameMode == "Regicide" && attackingTeam is NetworkTeam winner && winner != piece.Team)
    {
      state.Winner = winner;
    }
    else if (state.Source.Configuration.GameMode == "Escort")
    {
      RespawnEscortRoyal(state, piece, destroyedRule);
    }
    else if (state.Source.Configuration.GameMode == "Plunder" && attackingTeam is NetworkTeam plunderAttacker && plunderAttacker != piece.Team)
    {
      state.ModeScores[plunderAttacker] = Math.Max(0, state.ModeScores.GetValueOrDefault(plunderAttacker) - state.Source.Configuration.PlunderRoyalKillPenalty);
    }
  }

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
      if (BoardRules.CanPlaceForTeam(state.Source.Configuration, defeated.Team, position.x, position.y, rule.Width, rule.Height) &&
          CanPlace(state.Source, state.Pieces, rule, position.x, position.y))
      {
        int health = Math.Max(1, (int)Math.Ceiling(rule.Health * (state.Source.Configuration.EscortRoyalHealthPercent / 100d)));
        state.Pieces.Add(new NetworkPiece(CreatePieceId(state, rule.Type), rule.Type, defeated.Team, position.x, position.y, health));
        return;
      }
    }
  }

  private static void DamageBarricade(CpuMutableGameState state, NetworkPiece attacker, (int x, int y) position)
  {
    if (!state.Barricades.TryGetValue(position, out int health))
    {
      return;
    }

    int damage = UnitRules.GetRequired(attacker.Type).Attack +
      (HasAdjacentUnit(state, attacker, attacker.Team, "Baron") ? 5 : 0);
    if (health <= damage)
    {
      state.Barricades.Remove(position);
    }
    else
    {
      state.Barricades[position] = health - damage;
    }
  }

  private static void TriggerMinesAlongMovement(CpuMutableGameState state, NetworkPiece movingPiece, IReadOnlyList<(int x, int y)> path)
  {
    if (movingPiece.Type == "Engineer" || !UnitRules.TryGet(movingPiece.Type, out UnitRule rule))
    {
      return;
    }

    List<((int x, int y) position, NetworkTeam owner)> triggered = [];
    foreach ((int x, int y) step in path)
    {
      foreach ((int x, int y) square in OccupiedSquares(rule, step))
      {
        if (state.Mines.TryGetValue(square, out NetworkTeam owner) && owner != movingPiece.Team)
        {
          triggered.Add((square, owner));
        }
      }
    }

    foreach (((int x, int y) position, NetworkTeam owner) mine in triggered.Distinct())
    {
      state.Mines.Remove(mine.position);
      foreach (NetworkPiece affected in state.Pieces.Where(piece => UnitRules.TryGet(piece.Type, out UnitRule affectedRule) &&
        OccupiedSquares(affectedRule, (piece.X, piece.Y)).Any(square =>
          Math.Abs(square.x - mine.position.x) <= 1 && Math.Abs(square.y - mine.position.y) <= 1)).ToArray())
      {
        ResolveMineDamage(state, affected.Id, mine.owner);
      }
    }
  }

  private static void ResolveMineDamage(CpuMutableGameState state, string targetId, NetworkTeam owner)
  {
    NetworkPiece? target = FindPiece(state.Pieces, targetId);
    if (target is null)
    {
      return;
    }

    int damage = 30;
    int index = FindPieceIndex(state.Pieces, target.Id);
    if (target.Health > damage)
    {
      state.Pieces[index] = target with { Health = target.Health - damage };
    }
    else
    {
      HandlePieceDestroyed(state, target, owner);
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

  private static void MoveEmissaryCompanions(CpuMutableGameState state, NetworkPiece emissary, int oldX, int oldY)
  {
    if (emissary.Type != "Emissary")
    {
      return;
    }

    int deltaX = emissary.X - oldX;
    int deltaY = emissary.Y - oldY;
    List<string> companionIds = state.Pieces
      .Where(piece => piece.Id != emissary.Id && piece.Id != state.TreasureCarrierId && piece.Team == emissary.Team &&
        piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Width == 1 && rule.Height == 1 &&
        Math.Abs(piece.X - oldX) + Math.Abs(piece.Y - oldY) == 1)
      .Select(piece => piece.Id)
      .ToList();
    foreach (string companionId in companionIds)
    {
      int index = FindPieceIndex(state.Pieces, companionId);
      if (index < 0 || !UnitRules.TryGet(state.Pieces[index].Type, out UnitRule rule))
      {
        continue;
      }

      NetworkPiece companion = state.Pieces[index];
      if (CanPlace(state.Source, state.Pieces, rule, companion.X + deltaX, companion.Y + deltaY, companion.Id))
      {
        state.Pieces[index] = companion with { X = companion.X + deltaX, Y = companion.Y + deltaY, HasMovedThisTurn = true };
      }
    }
  }

  private static void TryDeliverTreasure(CpuMutableGameState state, NetworkPiece piece)
  {
    if (state.Source.Configuration.GameMode != "Plunder" || state.TreasureCarrierId != piece.Id ||
        !BoardRules.IsInTeamTerritory(state.Source.Configuration, piece.Team, piece.X, piece.Y))
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
    CpuTeamState current = state.Teams[team];
    state.Teams[team] = current with { ActionsRemaining = current.ActionsRemaining - 1 };
    if (state.Teams[team].ActionsRemaining > 0)
    {
      return;
    }

    ApplyEndOfTurnObjectives(state, team);
    if (state.Winner is not null)
    {
      return;
    }

    state.Teams[team] = state.Teams[team] with { ActionsRemaining = MatchRules.ActionsPerTurn };
    state.CurrentTurn = TeamRules.GetNextTeam(team, state.Source.Configuration.PlayerCount);
    state.TurnNumber++;
    ApplyTurnEconomy(state, state.CurrentTurn);
    ResetTurnActions(state, state.CurrentTurn);
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
    int palaces = state.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null && piece.Type == "Palace");
    int mercenaries = state.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null && piece.Type == "Mercenary");
    money = ClampCurrency((long)money + farms * (long)state.Source.Configuration.FarmIncomePerTurn + palaces * 5L - mercenaries * 10L);
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

  private static IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> GetLegalMovementPaths(
    CpuGameState source,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule
  )
  {
    rule = GetEffectiveMovementRule(source, pieces, piece, rule);
    return MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLand(source, pieces, piece, rule, destination),
      (from, destination) => CanTravelThrough(source, pieces, piece, rule, from, destination),
      destination => GetMovementCost(source, rule, destination),
      (from, destination) => CrossesRiver(source, rule, from, destination)
    );
  }

  private static UnitRule GetEffectiveMovementRule(CpuGameState state, IReadOnlyList<NetworkPiece> pieces, NetworkPiece piece, UnitRule rule)
  {
    if (rule.Type == "Ox")
    {
      NetworkPiece? cargo = pieces.FirstOrDefault(other => other.AttachedToId == piece.Id && other.AttachmentKind == NetworkAttachmentKind.Carried);
      if (cargo is not null && UnitRules.TryGet(cargo.Type, out UnitRule cargoRule) && cargoRule.Category == RuleCategory.Mechanical)
      {
        rule = rule with { MoveRange = 3, MovePattern = RuleShape.Any };
      }
    }
    return state.TreasureCarrierId == piece.Id ? rule with { MoveRange = Math.Max(1, rule.MoveRange - 1) } : rule;
  }

  private static bool CanLand(CpuGameState state, IReadOnlyList<NetworkPiece> pieces, NetworkPiece piece, UnitRule rule, (int x, int y) destination) =>
    CanPlace(state, pieces, rule, destination.x, destination.y, piece.Id, piece.Type == "Elephant");

  private static bool CanPlace(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    UnitRule rule,
    int x,
    int y,
    string? ignoredPieceId = null,
    bool elephantCanIgnoreLakes = false
  )
  {
    if (!BoardRules.FootprintFitsBoard(state.Configuration, x, y, rule.Width, rule.Height))
    {
      return false;
    }
    foreach ((int x, int y) square in OccupiedSquares(rule, (x, y)))
    {
      if ((!elephantCanIgnoreLakes && state.Terrain.IsLake(square)) || state.Barricades.ContainsKey(square))
      {
        return false;
      }
    }

    HashSet<string> ignored = pieces.Where(piece => piece.Id == ignoredPieceId || piece.AttachedToId == ignoredPieceId)
      .Select(piece => piece.Id)
      .ToHashSet(StringComparer.Ordinal);
    return !pieces.Any(piece => !ignored.Contains(piece.Id) && (rule.Type == "Farm" || piece.Type != "Farm") &&
      UnitRules.TryGet(piece.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(piece.X, piece.Y, otherRule.Width, otherRule.Height, x, y, rule.Width, rule.Height));
  }

  private static bool CanTravelThrough(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece piece,
    UnitRule rule,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    foreach ((int x, int y) position in PositionsBetween(from, destination))
    {
      foreach ((int x, int y) square in OccupiedSquares(rule, position))
      {
        if (!BoardRules.Contains(state.Configuration, square.x, square.y) ||
            (piece.Type != "Elephant" && state.Terrain.IsLake(square)) || state.Barricades.ContainsKey(square))
        {
          return false;
        }
        NetworkPiece? blocker = pieces.FirstOrDefault(other => other.Id != piece.Id && other.AttachedToId != piece.Id &&
          other.Type != "Farm" && UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square));
        if (blocker is not null && !(piece.Type == "Elephant" && blocker.Team != piece.Team))
        {
          return false;
        }
      }
    }
    return true;
  }

  private static int GetMovementCost(CpuGameState state, UnitRule rule, (int x, int y) destination)
  {
    if (rule.Type == "Elephant")
    {
      return 1;
    }
    int cost = 0;
    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      cost = Math.Max(cost, state.Terrain.IsForest(square) && !state.Roads.Contains(square)
        ? 2
        : state.Roads.Contains(square) && !state.Terrain.IsForest(square) ? 0 : 1);
    }
    return cost;
  }

  private static bool CrossesRiver(CpuGameState state, UnitRule rule, (int x, int y) from, (int x, int y) to)
  {
    if (rule.Type == "Elephant")
    {
      return false;
    }
    foreach ((int x, int y) fromSquare in OccupiedSquares(rule, from))
    {
      (int x, int y) toSquare = (fromSquare.x + to.x - from.x, fromSquare.y + to.y - from.y);
      if (StepsBetween(fromSquare, toSquare).Any(edge => state.Terrain.HasRiverBetween(edge.first, edge.second) &&
        !state.RiverBridges.Contains(TileEdge.Between(edge.first, edge.second))))
      {
        return true;
      }
    }
    return false;
  }

  private static bool HasClearAttackPath(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece attacker,
    NetworkPiece target,
    IReadOnlyDictionary<(int x, int y), int> barricades
  )
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return false;
    }
    return OccupiedSquares(targetRule, (target.X, target.Y)).Any(targetSquare =>
      HasClearAttackPath(state, pieces, attacker, targetSquare, target.Id, barricades));
  }

  private static bool HasClearAttackPath(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece attacker,
    (int x, int y) target,
    string? targetId,
    IReadOnlyDictionary<(int x, int y), int> barricades
  )
  {
    if (!UnitRules.TryGet(attacker.Type, out UnitRule rule))
    {
      return false;
    }
    if (rule.Type == "Catapult")
    {
      return true;
    }
    return LineOfSightRules.HasClearAttackPath(
      rule,
      OccupiedSquares(rule, (attacker.X, attacker.Y)),
      target,
      state.Terrain.IsForest,
      barricades.ContainsKey,
      square => pieces.Any(other => other.Id != attacker.Id && other.Id != targetId && other.AttachedToId is null &&
        other.Type != "Farm" && !(attacker.Type == "Princess" && other.Team == attacker.Team) &&
        UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square))
    );
  }

  private static bool CanPickUpTreasure(CpuGameState state, NetworkPiece actor, int targetX, int targetY) =>
    state.Configuration.GameMode == "Plunder" && state.TreasureCarrierId is null && state.TreasurePosition == (targetX, targetY) &&
    actor.AttachedToId is null && UnitRules.TryGet(actor.Type, out UnitRule rule) && rule.Width == 1 && rule.Height == 1 &&
    rule.Category != RuleCategory.Royal && Math.Abs(actor.X - targetX) + Math.Abs(actor.Y - targetY) == 1;

  private static bool PieceOccupies(IEnumerable<NetworkPiece> pieces, (int x, int y) position) => pieces.Any(piece =>
    UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, position));

  private static bool Occupies(UnitRule rule, NetworkPiece piece, (int x, int y) position) =>
    position.x >= piece.X && position.x < piece.X + rule.Width && position.y >= piece.Y && position.y < piece.Y + rule.Height;

  private static IEnumerable<(int x, int y)> OccupiedSquares(UnitRule rule, (int x, int y) position)
  {
    for (int offsetY = 0; offsetY < rule.Height; offsetY++)
    {
      for (int offsetX = 0; offsetX < rule.Width; offsetX++)
      {
        yield return (position.x + offsetX, position.y + offsetY);
      }
    }
  }

  private static IEnumerable<(int x, int y)> PositionsBetween((int x, int y) from, (int x, int y) destination)
  {
    int steps = Math.Max(Math.Abs(destination.x - from.x), Math.Abs(destination.y - from.y));
    for (int step = 1; step <= steps; step++)
    {
      yield return (
        from.x + (int)MathF.Round((destination.x - from.x) * step / (float)steps),
        from.y + (int)MathF.Round((destination.y - from.y) * step / (float)steps)
      );
    }
  }

  private static IEnumerable<((int x, int y) first, (int x, int y) second)> StepsBetween((int x, int y) from, (int x, int y) to)
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
      else
      {
        yield return (current, next);
      }
      current = next;
    }
  }

  private static int GetUnitPrice(NetworkMatchConfiguration configuration, UnitRule rule) => rule.Type == "Farm"
    ? rule.Cost
    : EconomyRules.GetUnitPrice(rule.Cost, configuration.UnitPricePercent);

  private static int GetUnitMaintenance(NetworkMatchConfiguration configuration, UnitRule rule) => rule.Type == "Farm"
    ? 0
    : EconomyRules.GetUnitMaintenance(rule.Cost, configuration.UnitMaintenancePercent);

  private static void SpendMoney(CpuMutableGameState state, NetworkTeam team, int amount) => AddMoney(state, team, -amount);

  private static void AddMoney(CpuMutableGameState state, NetworkTeam team, int amount)
  {
    if (state.Teams.TryGetValue(team, out CpuTeamState? existing))
    {
      state.Teams[team] = existing with { Money = ClampCurrency((long)existing.Money + amount) };
    }
  }

  private static int ClampCurrency(long amount) => (int)Math.Clamp(amount, int.MinValue, int.MaxValue);

  private static NetworkPiece? FindPiece(IEnumerable<NetworkPiece> pieces, string id) =>
    pieces.FirstOrDefault(piece => string.Equals(piece.Id, id, StringComparison.Ordinal));

  private static int FindPieceIndex(IReadOnlyList<NetworkPiece> pieces, string id)
  {
    for (int index = 0; index < pieces.Count; index++)
    {
      if (string.Equals(pieces[index].Id, id, StringComparison.Ordinal))
      {
        return index;
      }
    }
    return -1;
  }

  private static string CreatePieceId(CpuMutableGameState state, string type)
  {
    int suffix = 1;
    string id;
    do
    {
      id = $"cpu-{type.ToLowerInvariant()}-{state.TurnNumber}-{suffix++}";
    } while (state.Pieces.Any(piece => piece.Id == id));
    return id;
  }

  private static bool IsCurrentInitialBuyer(NetworkInitialBuyState initialBuy, NetworkTeam team) => initialBuy.CurrentTeam == team;

  private static void RecordInitialPurchase(CpuMutableGameState state, NetworkTeam team)
  {
    NetworkInitialBuyState current = state.InitialBuy!;
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records = GetInitialBuyRecords(current, state.Source.Configuration.PlayerCount);
    if (current.IsFarmPlacementPhase)
    {
      (int farmTurnsUsed, bool farmStopped, int farmCount) = records[team];
      records[team] = (farmTurnsUsed, farmStopped, farmCount + 1);
      bool farmsDone = records.Values.All(value => value.farmsPlaced >= 2);
      if (farmsDone)
      {
        state.InitialBuy = BuildInitialBuyState(state, records, TeamRules.GetFirstTeam(state.Source.Configuration.PlayerCount), 0, false, false);
        state.CurrentTurn = state.InitialBuy.CurrentTeam;
      }
      else
      {
        AdvanceInitialBuyer(state, records, 0, true, requireFarms: true);
      }
      return;
    }

    int purchases = current.PurchasesThisTurn + 1;
    if (purchases < current.PurchasesPerTurn)
    {
      state.InitialBuy = BuildInitialBuyState(state, records, team, purchases, false, false);
      state.CurrentTurn = team;
      return;
    }

    (int turnsUsed, bool stopped, int farmsPlaced) = records[team];
    records[team] = (turnsUsed + 1, stopped, farmsPlaced);
    AdvanceInitialBuyer(state, records, 0, false);
  }

  private static void AdvanceInitialBuyer(
    CpuMutableGameState state,
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records,
    int purchasesThisTurn,
    bool farmPlacement,
    bool requireFarms = false
  )
  {
    IReadOnlyList<NetworkTeam> teams = TeamRules.GetActiveTeams(state.Source.Configuration.PlayerCount);
    bool canContinue(NetworkTeam team) => requireFarms
      ? records[team].farmsPlaced < 2
      : !records[team].stopped && records[team].turnsUsed < state.InitialBuy!.BuyTurnsPerTeam;
    if (teams.All(team => !canContinue(team)))
    {
      state.InitialBuy = BuildInitialBuyState(state, records, TeamRules.GetFirstTeam(state.Source.Configuration.PlayerCount), 0, farmPlacement, true);
      state.CurrentTurn = state.InitialBuy.CurrentTeam;
      if (!farmPlacement)
      {
        state.InitialBuy = null;
        state.CurrentTurn = TeamRules.GetFirstTeam(state.Source.Configuration.PlayerCount);
        foreach (NetworkTeam activeTeam in teams)
        {
          state.Teams[activeTeam] = state.Teams[activeTeam] with { ActionsRemaining = MatchRules.ActionsPerTurn };
          ResetTurnActions(state, activeTeam);
        }
      }
      return;
    }

    int currentIndex = Array.IndexOf(teams.ToArray(), state.InitialBuy!.CurrentTeam);
    for (int offset = 1; offset <= teams.Count; offset++)
    {
      NetworkTeam next = teams[(currentIndex + offset) % teams.Count];
      if (canContinue(next))
      {
        state.InitialBuy = BuildInitialBuyState(state, records, next, purchasesThisTurn, farmPlacement, false);
        state.CurrentTurn = next;
        return;
      }
    }
  }

  private static Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> GetInitialBuyRecords(
    NetworkInitialBuyState initialBuy,
    int playerCount
  )
  {
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> result = TeamRules.GetActiveTeams(playerCount)
      .ToDictionary(team => team, _ => (0, false, 0));
    if (initialBuy.TeamStates is not null)
    {
      foreach (NetworkInitialBuyTeamState entry in initialBuy.TeamStates)
      {
        result[entry.Team] = (entry.BuyTurnsUsed, entry.Stopped, entry.FarmsPlaced);
      }
    }
    else
    {
      result[NetworkTeam.Red] = (initialBuy.RedBuyTurnsUsed, initialBuy.RedStopped, 0);
      result[NetworkTeam.Blue] = (initialBuy.BlueBuyTurnsUsed, initialBuy.BlueStopped, 0);
    }
    return result;
  }

  private static NetworkInitialBuyState BuildInitialBuyState(
    CpuMutableGameState state,
    IReadOnlyDictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records,
    NetworkTeam currentTeam,
    int purchasesThisTurn,
    bool farmPlacement,
    bool complete
  ) => new(
    currentTeam,
    purchasesThisTurn,
    state.InitialBuy!.PurchasesPerTurn,
    records.GetValueOrDefault(NetworkTeam.Red).turnsUsed,
    records.GetValueOrDefault(NetworkTeam.Blue).turnsUsed,
    state.InitialBuy.BuyTurnsPerTeam,
    records.GetValueOrDefault(NetworkTeam.Red).stopped,
    records.GetValueOrDefault(NetworkTeam.Blue).stopped,
    complete,
    records.Select(pair => new NetworkInitialBuyTeamState(pair.Key, pair.Value.turnsUsed, pair.Value.stopped, pair.Value.farmsPlaced)).ToArray(),
    farmPlacement
  );
}
