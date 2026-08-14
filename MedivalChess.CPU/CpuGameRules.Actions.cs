using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>State-changing action application for CPU simulation; legality remains in the core facade.</summary>
public static partial class CpuGameRules
{
  private static void ApplyMove(CpuMutableGameState state, MoveAction action)
  {
    int index = FindPieceIndex(state.Pieces, action.PieceId);
    NetworkPiece piece = state.Pieces[index];
    bool usesCavalierFollowUpMove = AbilityRules.CanUseCavalierFollowUpMove(
      piece.Type, piece.CavalierFollowUpMoveAvailable);
    if (piece.AttachedToId is not null)
    {
      piece = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      state.Pieces[index] = piece;
    }

    IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> paths = GetLegalMovementPaths(state.Source, state.Pieces, piece, UnitRules.GetRequired(piece.Type));
    List<(int x, int y)> path = paths[(action.DestinationX, action.DestinationY)];
    NetworkPiece? chessCaptureTarget = GetChessCaptureTarget(
      state.Pieces, piece, UnitRules.GetRequired(piece.Type), (action.DestinationX, action.DestinationY));
    int oldX = piece.X;
    int oldY = piece.Y;
    bool elephantDamaged = false;
    if (piece.Type == nameof(PieceType.Elephant) && UnitRules.TryGet(piece.Type, out UnitRule elephantRule))
    {
      foreach (NetworkPiece crossed in state.Pieces.Where(other => other.Id != piece.Id && other.Team != piece.Team).ToArray())
      {
        if (UnitRules.TryGet(crossed.Type, out UnitRule crossedRule) &&
            AbilityRules.PathOverlapsUnit(elephantRule, path, crossedRule, crossed.X, crossed.Y))
        {
          ResolveSharedPieceDamage(state, piece, action.Team, crossed.Id, AbilityRules.ElephantTrampleDamage);
          elephantDamaged = true;
        }
      }
    }

    bool chessCaptureSurvived = false;
    if (chessCaptureTarget is not null)
    {
      ResolveSharedPieceDamage(state, piece, action.Team, chessCaptureTarget.Id, null);
      chessCaptureSurvived = FindPiece(state.Pieces, chessCaptureTarget.Id) is not null;
    }

    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    (int finalX, int finalY) = chessCaptureSurvived
      ? ChessAbilityRules.GetFailedCaptureFallback((oldX, oldY), path)
      : (action.DestinationX, action.DestinationY);
    List<(int x, int y)> actualPath = chessCaptureSurvived && path.Count > 0 ? path[..^1] : path;
    piece = state.Pieces[index] with
    {
      X = finalX,
      Y = finalY,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = chessCaptureTarget is not null || elephantDamaged || state.Pieces[index].HasAttackedThisTurn,
      CavalierFollowUpMoveAvailable = false
    };
    state.Pieces[index] = piece;
    state.RecordMove(action.Team, piece.Id, oldX, oldY, finalX, finalY);
    MoveAttachedPieces(state, piece);
    MoveEmissaryCompanions(state, piece, oldX, oldY);
    TriggerSharedMinesAlongMovement(state, piece, actualPath);

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
      SpendSharedAction(state, action.Team);
    }
  }

  private static void ApplyAttack(CpuMutableGameState state, AttackAction action)
  {
    int attackerIndex = FindPieceIndex(state.Pieces, action.AttackerId);
    NetworkPiece originalAttacker = state.Pieces[attackerIndex];
    AttackTurnState attackState = AbilityStateRules.RecordAttack(
      originalAttacker.Type,
      originalAttacker.AttacksThisTurn
    );
    NetworkPiece attacker = originalAttacker with
    {
      AttacksThisTurn = attackState.AttacksThisTurn,
      HasAttackedThisTurn = attackState.HasAttackedThisTurn,
      CavalierFollowUpMoveAvailable = AbilityRules.GrantsCavalierFollowUpMove(
        originalAttacker.Type,
        originalAttacker.HasMovedThisTurn)
    };

    if (attacker.Type == nameof(PieceType.Tank))
    {
      TankAttackDecision tank = AbilityStateRules.ResolveTankAttackAttempt(
        attacker.Team,
        attacker.FacingX,
        attacker.FacingY,
        (attacker.X, attacker.Y),
        (action.TargetX, action.TargetY)
      );
      attacker = attacker with { FacingX = tank.FacingX, FacingY = tank.FacingY };
      state.Pieces[attackerIndex] = attacker;
      if (!tank.MayFire)
      {
        if (state.Winner is null) SpendSharedAction(state, action.Team);
        return;
      }
    }
    else
    {
      state.Pieces[attackerIndex] = attacker;
    }

    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    if (target is { Type: nameof(PieceType.Farm) })
    {
      target = FindUnitOnAttackedSquare(state.Pieces, attacker, action.TargetX, action.TargetY) ?? target;
    }

    AbilityAttackPlan? abilityPlan = null;
    if (target is null)
    {
      DamageSharedBarricade(state, attacker, (action.TargetX, action.TargetY));
    }
    else
    {
      AbilityUnitSnapshot[] snapshots = state.Pieces
        .Where(piece => piece.AttachedToId is null)
        .Select(AbilityAttackRules.Snapshot)
        .ToArray();
      abilityPlan = AbilityAttackRules.BuildAttackPlan(
        AbilityAttackRules.Snapshot(attacker),
        AbilityAttackRules.Snapshot(target),
        snapshots
      );

      foreach (AbilityDamageInstruction instruction in abilityPlan.Damage)
      {
        int? damageOverride = instruction.Mode == AbilityDamageMode.Fixed
          ? instruction.FixedDamage
          : null;
        ResolveSharedPieceDamage(state, attacker, action.Team, instruction.TargetId, damageOverride);
      }
    }

    if (target is not null && attacker.Type == nameof(PieceType.Ballista) && UnitRules.TryGet(attacker.Type, out UnitRule ballistaRule))
    {
      foreach ((int x, int y) position in AbilityRules.GetPiercingRay(ballistaRule, attacker.X, attacker.Y, target.X, target.Y))
      {
        if (!BoardRules.Contains(state.Source.Board, position.x, position.y) ||
            state.Source.Terrain.IsForest(position) || state.Barricades.ContainsKey(position))
        {
          break;
        }

        NetworkPiece? pierced = state.Pieces.FirstOrDefault(piece => piece.Id != attacker.Id && piece.Id != target.Id &&
          piece.Team != attacker.Team && piece.Type != nameof(PieceType.Farm) && piece.AttachedToId is null &&
          UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, position));
        if (pierced is not null)
        {
          ResolveSharedPieceDamage(state, attacker, action.Team, pierced.Id, null);
        }
      }
    }

    if (abilityPlan?.ScheduleDragonbornBurn == true && target is not null)
    {
      int targetIndex = FindPieceIndex(state.Pieces, target.Id);
      if (targetIndex >= 0)
      {
        NetworkPiece liveTarget = state.Pieces[targetIndex];
        state.Pieces[targetIndex] = liveTarget with
        {
          PendingDamage = AbilityStateRules.AddDragonbornBurn(
            liveTarget.PendingDamage,
            attacker.Team,
            attacker.Team
          )
        };
      }
    }

    if (abilityPlan is { HealAttacker: > 0 })
    {
      attackerIndex = FindPieceIndex(state.Pieces, attacker.Id);
      if (attackerIndex >= 0 && UnitRules.TryGet(attacker.Type, out UnitRule attackerRule))
      {
        NetworkPiece liveAttacker = state.Pieces[attackerIndex];
        state.Pieces[attackerIndex] = liveAttacker with
        {
          Health = Math.Min(attackerRule.Health, liveAttacker.Health + abilityPlan.HealAttacker)
        };
      }
    }

    if (abilityPlan?.SelfDestructAfterAttack == true)
    {
      attackerIndex = FindPieceIndex(state.Pieces, attacker.Id);
      if (attackerIndex >= 0)
      {
        HandleSharedPieceDestroyed(state, state.Pieces[attackerIndex], null);
      }
    }

    if (state.Winner is null)
    {
      SpendSharedAction(state, action.Team);
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
        case nameof(PieceType.Spy):
          state.Pieces[actorIndex] = actor with { MarkedTargetId = target!.Id };
          break;
        case nameof(PieceType.Engineer):
          ApplyEngineerAbility(state, actorIndex, action);
          break;
        case nameof(PieceType.Guard):
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Guard,
            X = target.X,
            Y = target.Y
          };
          break;
        case nameof(PieceType.Ox):
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Carried,
            X = target.X,
            Y = target.Y
          };
          break;
        case nameof(PieceType.Mercenary):
          state.Pieces[actorIndex] = actor with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true
          };
          break;
        case nameof(PieceType.Phantom):
          ApplySharedPhantomAbility(state, actorIndex, target, action.Ability);
          break;
        case nameof(PieceType.Phantom):
          ApplySharedPhantomAbility(state, actorIndex, target, action.Ability);
          break;
      }
    }

    SpendSharedAction(state, action.Team);
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
      state.Roads[position] = engineer.Team;
    }
    else if (string.Equals(action.Ability, "Barrier", StringComparison.OrdinalIgnoreCase))
    {
      state.Barricades[position] = AbilityRules.EngineerBarrierHealth;
    }
    else if (string.Equals(action.Ability, "Mine", StringComparison.OrdinalIgnoreCase))
    {
      state.Mines[position] = engineer.Team;
    }

    int buildsUsed = engineer.EngineerBuildsThisTurn + 1;
    state.Pieces[actorIndex] = engineer with
    {
      EngineerBuildsThisTurn = buildsUsed,
      HasAttackedThisTurn = buildsUsed >= AbilityRules.EngineerBuildsPerTurn
    };
  }

  private static void ApplyPurchase(CpuMutableGameState state, PurchaseAction action)
  {
    NetworkPiece? mercenary = state.Pieces.FirstOrDefault(piece => piece.Type == nameof(PieceType.Mercenary) &&
      piece.Team == NetworkTeam.Neutral && piece.X == action.X && piece.Y == action.Y);
    if (mercenary is not null)
    {
      int index = FindPieceIndex(state.Pieces, mercenary.Id);
      int cost = PieceDefinitions.NeutralMercenaryHireCost;
      SpendMoney(state, action.Team, cost);
      state.Pieces[index] = mercenary with
      {
        Team = action.Team,
        LastBid = cost,
        HasMovedThisTurn = true,
        HasAttackedThisTurn = true,
        CannotContributeToConquestThisTurn = true
      };
      SpendSharedAction(state, action.Team);
      return;
    }

    UnitRule rule = UnitRules.GetRequired(action.UnitType);
    bool openingFarmPlacement = state.InitialBuy?.IsFarmPlacementPhase == true && rule.Type == nameof(PieceType.Farm);
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
      SpendSharedAction(state, action.Team);
    }
    else
    {
      RecordInitialPurchase(state, action.Team);
    }
  }

  private static void ApplyEndTurn(CpuMutableGameState state, NetworkTeam team)
  {
    if (!Globals.ActionLimitsEnabled)
    {
      CompleteSharedTurn(state, team);
      return;
    }

    CpuTeamState current = state.Teams[team];
    state.Teams[team] = current with { ActionsRemaining = 1 };
    SpendSharedAction(state, team);
  }

  private static void ApplyStopInitialBuying(CpuMutableGameState state, NetworkTeam team)
  {
    NetworkInitialBuyState current = state.InitialBuy!;
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records = GetInitialBuyRecords(current, state.Source.Configuration.PlayerCount);
    (int turnsUsed, bool _, int farmsPlaced) = records[team];
    records[team] = (turnsUsed, true, farmsPlaced);
    AdvanceInitialBuyer(state, records, current.PurchasesThisTurn, current.IsFarmPlacementPhase);
  }
}
