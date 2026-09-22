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
    if (piece.AttachedToId is not null && piece.Type == nameof(PieceType.Ox))
    {
      piece = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      state.Pieces[index] = piece;
    }

    IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> paths = GetLegalMovementPaths(state.Source, state.Pieces, piece, UnitRules.GetRequired(piece.Type));
    List<(int x, int y)> path = paths[(action.DestinationX, action.DestinationY)];
    UnitRule movementRule = UnitRules.GetRequired(piece.Type);
    NetworkPiece? chessCaptureTarget = GetChessCaptureTarget(
      state.Pieces, piece, movementRule, (action.DestinationX, action.DestinationY));
    int oldX = piece.X;
    int oldY = piece.Y;
    (int x, int y) landingResolvedDestination = ResolveCpuLandingAttack(
      state,
      piece,
      action.Team,
      movementRule,
      path,
      (action.DestinationX, action.DestinationY));
    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    piece = state.Pieces[index];
    bool landingFallback =
      landingResolvedDestination != (action.DestinationX, action.DestinationY);
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
    else if (piece.Type == nameof(PieceType.Stagecoach) && UnitRules.TryGet(piece.Type, out UnitRule stagecoachRule))
    {
      foreach (NetworkPiece crossed in state.Pieces.Where(other => other.Id != piece.Id && other.Team != piece.Team).ToArray())
      {
        if (UnitRules.TryGet(crossed.Type, out UnitRule crossedRule) &&
            AbilityRules.PathOverlapsUnit(stagecoachRule, path, crossedRule, crossed.X, crossed.Y))
        {
          ResolveSharedPieceDamage(
            state,
            piece,
            action.Team,
            crossed.Id,
            AdvancedAbilityRules.StagecoachTrampleDamage);
        }
      }
    }

    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    bool chessCaptureSurvived = false;
    if (chessCaptureTarget is not null)
    {
      ResolveSharedPieceDamage(state, piece, action.Team, chessCaptureTarget.Id, null);
      chessCaptureSurvived = FindPiece(state.Pieces, chessCaptureTarget.Id) is not null;
    }

    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0) return;
    (int finalX, int finalY) = chessCaptureSurvived
      ? ChessAbilityRules.GetFailedCaptureFallback((oldX, oldY), path)
      : landingResolvedDestination;
    bool movementFellBack = chessCaptureSurvived || landingFallback;
    List<(int x, int y)> actualPath =
      movementFellBack && path.Count > 0 ? path[..^1] : path;
    piece = state.Pieces[index] with
    {
      X = finalX,
      Y = finalY,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = chessCaptureTarget is not null || elephantDamaged || state.Pieces[index].HasAttackedThisTurn,
      CavalierFollowUpMoveAvailable = false,
      AbilityState = AdvancedAbilityRules.RecordMove(state.Pieces[index].AbilityState)
    };
    state.Pieces[index] = piece;
    state.RecordMove(action.Team, piece.Id, oldX, oldY, finalX, finalY);
    MoveAttachedPieces(state, piece);
    MoveHeraldCompanions(state, piece, oldX, oldY);
    TriggerSharedMinesAlongMovement(state, piece, actualPath);
    TriggerAbilityEntitiesAlongMovement(state, piece.Id, actualPath);

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

  private static (int x, int y) ResolveCpuLandingAttack(
    CpuMutableGameState state,
    NetworkPiece mover,
    NetworkTeam attackingTeam,
    UnitRule moverRule,
    IReadOnlyList<(int x, int y)> path,
    (int x, int y) requestedDestination)
  {
    CpuGameState snapshot = state.Freeze();
    NetworkPiece? target = GetCpuLandingAttackTarget(
      snapshot, state.Pieces, mover, moverRule, requestedDestination);
    if (target is null)
    {
      return requestedDestination;
    }

    (int x, int y) origin = (mover.X, mover.Y);
    ResolveSharedPieceDamage(state, mover, attackingTeam, target.Id, null);
    int targetIndex = FindPieceIndex(state.Pieces, target.Id);
    bool targetSurvived = targetIndex >= 0;

    if (targetSurvived)
    {
      target = state.Pieces[targetIndex];
      UnitRule targetRule = UnitRules.GetRequired(target.Type);
      int sourceCentreX2 = origin.x * 2 + moverRule.Width - 1;
      int sourceCentreY2 = origin.y * 2 + moverRule.Height - 1;
      int targetCentreX2 = target.X * 2 + targetRule.Width - 1;
      int targetCentreY2 = target.Y * 2 + targetRule.Height - 1;
      int directionX = Math.Sign(targetCentreX2 - sourceCentreX2);
      int directionY = Math.Sign(targetCentreY2 - sourceCentreY2);
      int pushDistance = AdvancedAbilityRules.GetLandingAttackPushDistance(mover.Type);

      if ((directionX != 0 || directionY != 0) && pushDistance > 0)
      {
        (int x, int y) pushed = DisplacementRules.GetFurthestLegalPosition(
          (target.X, target.Y),
          directionX,
          directionY,
          pushDistance,
          candidate => CanDisplaceCpuPieceTo(state, target, targetRule, candidate));
        if (pushed != (target.X, target.Y))
        {
          state.Pieces[targetIndex] = target with { X = pushed.x, Y = pushed.y };
          for (int attachmentIndex = 0;
               attachmentIndex < state.Pieces.Count;
               attachmentIndex++)
          {
            NetworkPiece attachment = state.Pieces[attachmentIndex];
            if (attachment.AttachedToId == target.Id)
            {
              state.Pieces[attachmentIndex] = attachment with
              {
                X = pushed.x,
                Y = pushed.y
              };
            }
          }
        }
      }
    }

    if (AdvancedAbilityRules.LandingAttackConsumesNormalAttack(mover.Type))
    {
      int moverIndex = FindPieceIndex(state.Pieces, mover.Id);
      if (moverIndex >= 0)
      {
        NetworkPiece liveMover = state.Pieces[moverIndex];
        AttackTurnState attackState = AbilityStateRules.RecordAttack(
          liveMover.Type, liveMover.AttacksThisTurn);
        state.Pieces[moverIndex] = liveMover with
        {
          AttacksThisTurn = attackState.AttacksThisTurn,
          HasAttackedThisTurn = attackState.HasAttackedThisTurn,
          AbilityState = AdvancedAbilityRules.RecordAttack(
            liveMover.Type, liveMover.AbilityState, target.Id)
        };
      }
    }

    return targetSurvived
      ? ChessAbilityRules.GetFailedCaptureFallback(origin, path)
      : requestedDestination;
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
        originalAttacker.HasMovedThisTurn),
      AbilityState = AdvancedAbilityRules.RecordAttack(
        originalAttacker.Type,
        originalAttacker.AbilityState,
        action.TargetPieceId)
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

    if (target is not null)
    {
      ApplyCpuPickpocketSteal(state, attacker, target);
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
            state.Terrain.IsForest(position) || state.Barricades.ContainsKey(position) ||
            state.AbilityEntities.Any(entity =>
              entity.X == position.x && entity.Y == position.y &&
              AbilityEntityRules.BlocksAttackFor(entity, attacker.Team)))
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

    if (abilityPlan is not null)
    {
      ApplySharedDisplacements(state, abilityPlan);
    }

    if (abilityPlan is { HealAttacker: > 0 })
    {
      attackerIndex = FindPieceIndex(state.Pieces, attacker.Id);
      if (attackerIndex >= 0 && UnitRules.TryGet(attacker.Type, out UnitRule attackerRule))
      {
        NetworkPiece liveAttacker = state.Pieces[attackerIndex];
        state.Pieces[attackerIndex] = liveAttacker with
        {
          Health = Math.Min(
            AdvancedAbilityRules.GetEffectiveMaximumHealth(attackerRule, liveAttacker.AbilityState),
            liveAttacker.Health + abilityPlan.HealAttacker)
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

  private static void ApplyCpuPickpocketSteal(
    CpuMutableGameState state,
    NetworkPiece attacker,
    NetworkPiece target)
  {
    if (attacker.Type != nameof(PieceType.Pickpocket) ||
        target.Team == attacker.Team || target.Team == NetworkTeam.Neutral ||
        !state.Teams.TryGetValue(attacker.Team, out CpuTeamState? attackerTeam) ||
        !state.Teams.TryGetValue(target.Team, out CpuTeamState? targetTeam))
    {
      return;
    }

    int stolen = Math.Min(AdvancedAbilityRules.PickpocketGold, Math.Max(0, targetTeam.Money));
    if (stolen <= 0) return;
    state.Teams[target.Team] = targetTeam with { Money = targetTeam.Money - stolen };
    state.Teams[attacker.Team] = attackerTeam with { Money = ClampCurrency((long)attackerTeam.Money + stolen) };
  }

  private static void ApplySharedDisplacements(CpuMutableGameState state, AbilityAttackPlan plan)
  {
    foreach (AbilityDisplacementInstruction instruction in plan.Displacements ?? Array.Empty<AbilityDisplacementInstruction>())
    {
      int index = FindPieceIndex(state.Pieces, instruction.UnitId);
      if (index < 0) continue;
      NetworkPiece moving = state.Pieces[index];
      if (moving.AttachedToId is not null || !DisplacementRules.CanBePushed(moving.Type) ||
          !UnitRules.TryGet(moving.Type, out UnitRule rule))
      {
        continue;
      }

      (int x, int y) start = (moving.X, moving.Y);
      (int x, int y) destination = DisplacementRules.GetFurthestLegalPosition(
        start,
        instruction.DirectionX,
        instruction.DirectionY,
        instruction.MaximumDistance,
        candidate => CanDisplaceCpuPieceTo(state, moving, rule, candidate),
        instruction.RequireFullDistance);
      if (destination == start) continue;

      NetworkPiece displaced = moving with { X = destination.x, Y = destination.y };
      state.Pieces[index] = displaced;
      for (int attachmentIndex = 0; attachmentIndex < state.Pieces.Count; attachmentIndex++)
      {
        NetworkPiece attachment = state.Pieces[attachmentIndex];
        if (attachment.AttachedToId == moving.Id)
        {
          state.Pieces[attachmentIndex] = attachment with { X = destination.x, Y = destination.y };
        }
      }
    }
  }

  private static bool CanDisplaceCpuPieceTo(
    CpuMutableGameState state,
    NetworkPiece moving,
    UnitRule rule,
    (int x, int y) destination)
  {
    if (!BoardRules.FootprintFitsBoard(
      state.Source.Board, destination.x, destination.y, rule.Width, rule.Height))
    {
      return false;
    }

    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      if (state.Terrain.IsLake(square) || state.Barricades.ContainsKey(square) ||
          state.AbilityEntities.Any(entity =>
            entity.X == square.x && entity.Y == square.y &&
            AbilityEntityRules.BlocksLandingFor(entity, moving.Team)))
      {
        return false;
      }
    }

    HashSet<string> ignored = state.Pieces
      .Where(piece => piece.Id == moving.Id || piece.AttachedToId == moving.Id)
      .Select(piece => piece.Id)
      .ToHashSet(StringComparer.Ordinal);
    return !state.Pieces.Any(piece =>
      !ignored.Contains(piece.Id) && piece.AttachedToId is null && piece.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(piece.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(
        piece.X, piece.Y, otherRule.Width, otherRule.Height,
        destination.x, destination.y, rule.Width, rule.Height));
  }

  private static void ApplyAbility(CpuMutableGameState state, UseAbilityAction action)
  {
    int actorIndex = FindPieceIndex(state.Pieces, action.ActorId);
    NetworkPiece actor = state.Pieces[actorIndex];
    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    bool plunderPickup = state.Source.Configuration.GameMode == "Plunder" &&
      string.Equals(action.Ability, "PickUpTreasure", StringComparison.OrdinalIgnoreCase);

    bool spendAction = true;
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
          {
            AttackTurnState attackState = AbilityStateRules.RecordAttack(actor.Type, actor.AttacksThisTurn);
            state.Pieces[actorIndex] = actor with
            {
              MarkedTargetId = target!.Id,
              AttacksThisTurn = attackState.AttacksThisTurn,
              HasAttackedThisTurn = attackState.HasAttackedThisTurn,
              AbilityState = AdvancedAbilityRules.RecordAttack(actor.Type, actor.AbilityState, target.Id)
            };
            break;
          }
        case nameof(PieceType.Harvester):
          state.Terrain.DestroyTile((action.TargetX, action.TargetY));
          AddMoney(state, action.Team, AdvancedAbilityRules.HarvesterGold);
          state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          break;
        case nameof(PieceType.Witch):
          state.AbilityEntities.RemoveAll(entity =>
            entity.Kind == AbilityEntityKind.PoisonCloud && entity.SourcePieceId == actor.Id);
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.PoisonCloud, actor.Team, action.TargetX, action.TargetY, actor.Id));
          state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          break;
        case nameof(PieceType.Druid):
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.Bramble, actor.Team, action.TargetX, action.TargetY, actor.Id));
          state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          break;
        case nameof(PieceType.Phoenix):
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.Fire, actor.Team, action.TargetX, action.TargetY, actor.Id));
          state.Pieces[actorIndex] = actor with
          {
            Health = actor.Health - AdvancedAbilityRules.PhoenixFireHealthCost,
            AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
          };
          break;
        case nameof(PieceType.Engineer):
          ApplyEngineerAbility(state, actorIndex, action);
          break;
        case nameof(PieceType.Baron):
          state.Pieces[actorIndex] = actor with
          {
            AbilityState = AdvancedAbilityRules.SelectTarget(actor.AbilityState, target!.Id)
          };
          break;
        case nameof(PieceType.WarDrum):
          {
            int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
            NetworkPiece refreshed = state.Pieces[targetIndex];
            state.Pieces[targetIndex] = refreshed with
            {
              HasMovedThisTurn = false,
              AbilityState = AdvancedAbilityRules.RefreshByWarDrum(refreshed.AbilityState)
            };
            state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
            break;
          }
        case nameof(PieceType.WillOWisp):
          if (string.Equals(action.Ability, "Settle", StringComparison.OrdinalIgnoreCase))
          {
            state.Pieces[actorIndex] = actor with
            {
              AbilityState = AdvancedAbilityRules.SetSettled(actor.AbilityState)
            };
          }
          else
          {
            UnitRule wispRule = UnitRules.GetRequired(nameof(PieceType.Wisp));
            state.Pieces.Add(new NetworkPiece(
              CreatePieceId(state, wispRule.Type), wispRule.Type, actor.Team,
              action.TargetX, action.TargetY, wispRule.Health));
            state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          }
          break;
        case nameof(PieceType.Odin):
          {
            int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
            NetworkPiece protectedPiece = state.Pieces[targetIndex];
            state.Pieces[targetIndex] = protectedPiece with
            {
              AbilityState = AdvancedAbilityRules.ProtectWithOdin(protectedPiece.AbilityState, actor.Id)
            };
            state.Pieces[actorIndex] = actor with
            {
              AbilityState = AdvancedAbilityRules.StartCooldown(
                actor.AbilityState, AdvancedAbilityRules.OdinCooldownTurns)
            };
            break;
          }
        case nameof(PieceType.Hacker):
          {
            int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
            NetworkPiece hacked = state.Pieces[targetIndex];
            state.Pieces[targetIndex] = hacked with
            {
              AbilityState = AdvancedAbilityRules.DisableAbilities(hacked.AbilityState, 1)
            };
            state.Pieces[actorIndex] = actor with
            {
              AbilityState = AdvancedAbilityRules.StartCooldown(
                actor.AbilityState, AdvancedAbilityRules.HackerCooldownTurns)
            };
            break;
          }
        case nameof(PieceType.Muse):
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Muse,
            X = target.X,
            Y = target.Y,
            HasAttackedThisTurn = true
          };
          break;
        case nameof(PieceType.Shieldsman):
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Shieldsman,
            X = target.X,
            Y = target.Y,
            HasAttackedThisTurn = true
          };
          break;
        case nameof(PieceType.Imp):
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Imp,
            X = target.X,
            Y = target.Y,
            AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
          };
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
        case nameof(PieceType.Giant):
        case nameof(PieceType.Cyclops):
          if (string.Equals(action.Ability, "Carry", StringComparison.OrdinalIgnoreCase))
          {
            int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
            NetworkPiece cargo = state.Pieces[targetIndex];
            state.Pieces[targetIndex] = cargo with
            {
              AttachedToId = actor.Id,
              AttachmentKind = NetworkAttachmentKind.Carried,
              X = actor.X,
              Y = actor.Y,
              FacingX = actor.FacingX,
              FacingY = actor.FacingY
            };
          }
          else
          {
            int cargoIndex = FindPieceIndex(state.Pieces, GetCarriedUnit(state.Freeze(), actor)!.Id);
            NetworkPiece cargo = state.Pieces[cargoIndex];
            state.Pieces[cargoIndex] = cargo with
            {
              AttachedToId = null,
              AttachmentKind = NetworkAttachmentKind.None,
              X = action.TargetX,
              Y = action.TargetY,
              HasMovedThisTurn = true,
              FacingX = actor.FacingX,
              FacingY = actor.FacingY
            };
          }
          state.Pieces[actorIndex] = state.Pieces[actorIndex] with { HasAttackedThisTurn = true };
          break;
        case nameof(PieceType.Mercenary):
        case nameof(PieceType.SummonedGolem):
        case nameof(PieceType.HiredGun):
          state.Pieces[actorIndex] = actor with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true,
            AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(actor.Type),
            AbilityState = (actor.AbilityState ?? new UnitAbilityState()) with
            {
              CannotActThisTurn = true,
              CannotMoveThisTurn = true
            }
          };
          break;
        case nameof(PieceType.Phantom):
          ApplySharedPhantomAbility(state, actorIndex, target, action.Ability);
          break;
        default:
          spendAction = ApplyCodexAdvancedAbility(state, actorIndex, target, action);
          break;
      }
    }

    if (spendAction)
    {
      SpendSharedAction(state, action.Team);
    }
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
      SpendMoney(
        state,
        action.Team,
        GetUnitPrice(state.Source.Configuration, rule) +
          AdvancedAbilityRules.GetImmediateGoldUpkeep(rule.Type));
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
    if (rule.Type == nameof(PieceType.Prison))
    {
      TryLinkCpuPurchasedSheriffPrison(state, state.Pieces.Count - 1);
    }

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
