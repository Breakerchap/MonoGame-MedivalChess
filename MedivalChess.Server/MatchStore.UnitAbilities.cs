using MedivalChess.Shared;

namespace MedivalChess.Server;

/// <summary>
/// Authoritative-server adapter for the deterministic ability rules in MedivalChess.Shared.
/// This class applies shared plans to the server's match storage; it does not redefine abilities.
/// </summary>
public sealed partial class MatchStore
{
  private static AbilityUnitSnapshot SnapshotAbilityUnit(NetworkPiece piece) =>
    AbilityAttackRules.Snapshot(piece);

  private static UnitRule ApplySharedServerAttachmentBonuses(Match match, NetworkPiece host, UnitRule rule)
  {
    bool hasImp = match.Pieces.Any(piece =>
      piece.AttachedToId == host.Id && piece.AttachmentKind == NetworkAttachmentKind.Imp);
    int museCount = match.Pieces.Count(piece =>
      piece.AttachedToId == host.Id && piece.AttachmentKind == NetworkAttachmentKind.Muse);
    rule = AdvancedAbilityRules.ApplyAttachmentBonuses(rule, hasImp, museCount);
    rule = AdvancedAbilityRules.ApplyPersistentBonuses(rule, host.AbilityState);
    return AbilityEntityRules.ApplyAuraBonuses(rule, match.AbilityEntities, host);
  }

  private static bool CanSharedServerDamage(NetworkPiece attacker, NetworkPiece target) =>
    AdvancedAbilityRules.CanTakeDirectDamage(target.Type, target.AbilityState) &&
    UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) &&
    UnitRules.TryGet(target.Type, out UnitRule targetRule) &&
    AbilityRules.CanDamageTarget(attackerRule, targetRule);

  private static int GetSharedServerAttackDamage(Match match, NetworkPiece attacker, NetworkPiece target)
  {
    if (!UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return 0;
    }

    attackerRule = ApplySharedServerAttachmentBonuses(match, attacker, attackerRule);
    int baseDamage = AbilityRules.GetBaseAttack(attackerRule, attacker.Health);
    (int x, int y) targetFacing = AbilityStateRules.GetFacing(
      target.Team,
      target.FacingX,
      target.FacingY
    );
    baseDamage += AbilityRules.GetAttackAbilityBonus(
      attackerRule,
      targetRule,
      IsInForest(match, target),
      targetFacing,
      (attacker.X, attacker.Y),
      (target.X, target.Y)
    );

    bool selectedByBaron = AdvancedAbilityRules.IsBaronSelectedTarget(
      match.Pieces.Select(piece => (piece.Type, piece.Team, piece.AbilityState?.SelectedTargetId)),
      attacker.Id,
      attacker.Team);
    baseDamage = AdvancedAbilityRules.ApplyBaronOutgoingBonus(baseDamage, selectedByBaron);
    return CombatRules.CalculateDamage(
      baseDamage,
      false,
      match.Pieces.Any(piece => piece.Type == nameof(PieceType.Spy) && piece.MarkedTargetId == target.Id),
      false,
      false,
      0
    );
  }

  private static bool PrepareSharedServerAttack(
    Match match,
    int attackerIndex,
    (int x, int y) targetPosition,
    string? targetId,
    out NetworkPiece attacker,
    out bool mayFire
  )
  {
    attacker = match.Pieces[attackerIndex];
    AttackTurnState attackState = AbilityStateRules.RecordAttack(
      attacker.Type,
      attacker.AttacksThisTurn
    );
    attacker = attacker with
    {
      AttacksThisTurn = attackState.AttacksThisTurn,
      HasAttackedThisTurn = attackState.HasAttackedThisTurn,
      CavalierFollowUpMoveAvailable = AbilityRules.GrantsCavalierFollowUpMove(
        attacker.Type,
        attacker.HasMovedThisTurn),
      AbilityState = AdvancedAbilityRules.RecordAttack(attacker.Type, attacker.AbilityState, targetId)
    };

    mayFire = true;
    if (attacker.Type == nameof(PieceType.Tank))
    {
      TankAttackDecision tank = AbilityStateRules.ResolveTankAttackAttempt(
        attacker.Team,
        attacker.FacingX,
        attacker.FacingY,
        (attacker.X, attacker.Y),
        targetPosition
      );
      attacker = attacker with { FacingX = tank.FacingX, FacingY = tank.FacingY };
      mayFire = tank.MayFire;
    }

    match.Pieces[attackerIndex] = attacker;
    return true;
  }

  private static void ResolveSharedServerAttack(
    Match match,
    NetworkPiece attacker,
    PlayerSlot attackingPlayer,
    NetworkPiece selectedTarget
  )
  {
    AbilityUnitSnapshot[] snapshots = match.Pieces
      .Where(piece => piece.AttachedToId is null)
      .Select(SnapshotAbilityUnit)
      .ToArray();
    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      SnapshotAbilityUnit(attacker),
      SnapshotAbilityUnit(selectedTarget),
      snapshots
    );

    foreach (AbilityDamageInstruction instruction in plan.Damage)
    {
      int? damageOverride = instruction.Mode == AbilityDamageMode.Fixed
        ? instruction.FixedDamage
        : null;
      ResolvePieceDamage(match, attacker, attackingPlayer, instruction.TargetId, damageOverride);
    }

    ApplySharedServerDisplacements(match, plan);

    if (plan.HealAttacker > 0 && UnitRules.TryGet(attacker.Type, out UnitRule attackerRule))
    {
      int liveAttackerIndex = match.Pieces.FindIndex(piece => piece.Id == attacker.Id);
      if (liveAttackerIndex >= 0)
      {
        NetworkPiece liveAttacker = match.Pieces[liveAttackerIndex];
        match.Pieces[liveAttackerIndex] = liveAttacker with
        {
          Health = Math.Min(
            AdvancedAbilityRules.GetEffectiveMaximumHealth(attackerRule, liveAttacker.AbilityState),
            liveAttacker.Health + plan.HealAttacker)
        };
      }
    }

    if (plan.SelfDestructAfterAttack)
    {
      int liveAttackerIndex = match.Pieces.FindIndex(piece => piece.Id == attacker.Id);
      if (liveAttackerIndex >= 0)
      {
        NetworkPiece liveAttacker = match.Pieces[liveAttackerIndex] with { Health = 0 };
        match.Pieces[liveAttackerIndex] = liveAttacker;
        HandlePieceDestroyed(match, liveAttacker, attackingPlayer);
      }
    }
  }

  private static void ApplySharedServerDisplacements(Match match, AbilityAttackPlan plan)
  {
    foreach (AbilityDisplacementInstruction instruction in plan.Displacements ?? Array.Empty<AbilityDisplacementInstruction>())
    {
      int index = match.Pieces.FindIndex(piece => piece.Id == instruction.UnitId);
      if (index < 0) continue;
      NetworkPiece moving = match.Pieces[index];
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
        candidate => CanDisplaceServerPieceTo(match, moving, rule, candidate),
        instruction.RequireFullDistance);
      if (destination == start) continue;

      NetworkPiece displaced = moving with { X = destination.x, Y = destination.y };
      match.Pieces[index] = displaced;
      for (int attachmentIndex = 0; attachmentIndex < match.Pieces.Count; attachmentIndex++)
      {
        NetworkPiece attachment = match.Pieces[attachmentIndex];
        if (attachment.AttachedToId == moving.Id)
        {
          match.Pieces[attachmentIndex] = attachment with { X = destination.x, Y = destination.y };
        }
      }
    }
  }

  private static bool CanDisplaceServerPieceTo(
    Match match,
    NetworkPiece moving,
    UnitRule rule,
    (int x, int y) destination)
  {
    if (!NetworkPieceRules.FootprintFitsBoard(
      match.Configuration, destination.x, destination.y, rule.Width, rule.Height))
    {
      return false;
    }

    foreach ((int x, int y) square in OccupiedSquares(rule, destination))
    {
      if (match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square) ||
          match.AbilityEntities.Any(entity =>
            entity.X == square.x && entity.Y == square.y &&
            AbilityEntityRules.BlocksLandingFor(entity, moving.Team)))
      {
        return false;
      }
    }

    HashSet<string> ignored = match.Pieces
      .Where(piece => piece.Id == moving.Id || piece.AttachedToId == moving.Id)
      .Select(piece => piece.Id)
      .ToHashSet(StringComparer.Ordinal);
    return !match.Pieces.Any(piece =>
      !ignored.Contains(piece.Id) && piece.AttachedToId is null && piece.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(piece.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(
        piece.X, piece.Y, otherRule.Width, otherRule.Height,
        destination.x, destination.y, rule.Width, rule.Height));
  }

  /// <summary>Returns true when a lethal hit was consumed by a revive/transform ability.</summary>
  private static bool TryApplySharedServerLethalAbility(Match match, NetworkPiece defeatedPiece)
  {
    if (defeatedPiece.AbilityState?.OdinProtectionAvailable == true)
    {
      int protectedIndex = match.Pieces.FindIndex(piece => piece.Id == defeatedPiece.Id);
      if (protectedIndex >= 0)
      {
        match.Pieces[protectedIndex] = defeatedPiece with
        {
          Health = 1,
          AbilityState = AdvancedAbilityRules.ConsumeOdinProtection(defeatedPiece.AbilityState)
        };
      }
      return true;
    }

    LethalAbilityOutcome outcome = AbilityStateRules.ResolveLethalDamage(
      defeatedPiece.Type,
      defeatedPiece.HasRevived
    );
    if (outcome.Kind == LethalAbilityOutcomeKind.Die)
    {
      return false;
    }

    int index = match.Pieces.FindIndex(piece => piece.Id == defeatedPiece.Id);
    if (index < 0)
    {
      return true;
    }

    match.Pieces[index] = defeatedPiece with
    {
      Type = outcome.ResultingType,
      Health = outcome.ResultingHealth,
      HasRevived = outcome.HasRevived,
      TurnsInCurrentForm = 0,
      HasMovedThisTurn = false,
      HasAttackedThisTurn = false,
      AttacksThisTurn = 0
    };
    return true;
  }

  private static IReadOnlyList<AbilityDamageInstruction> GetSharedServerDeathExplosion(
    Match match,
    NetworkPiece defeatedPiece
  ) => AbilityAttackRules.BuildDeathExplosion(
    SnapshotAbilityUnit(defeatedPiece),
    match.Pieces.Where(piece => piece.AttachedToId is null).Select(SnapshotAbilityUnit).ToArray()
  );

  private static void ApplySharedServerDeathExplosion(
    Match match,
    NetworkPiece defeatedPiece,
    PlayerSlot sourcePlayer,
    IReadOnlyList<AbilityDamageInstruction> explosion
  )
  {
    foreach (AbilityDamageInstruction instruction in explosion)
    {
      ResolvePieceDamage(
        match,
        defeatedPiece,
        sourcePlayer,
        instruction.TargetId,
        instruction.FixedDamage
      );
    }
  }

  private static void ApplySharedServerStartOfTurnEffects(Match match, NetworkTeam activeTeam)
  {
    match.AbilityEntities.RemoveAll(entity =>
      AbilityEntityEffectRules.ShouldExpireAtOwnerTurnStart(entity, activeTeam));

    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece piece = match.Pieces[index];
      if (piece.Team == activeTeam && piece.AbilityState?.OdinProtectionAvailable == true)
      {
        match.Pieces[index] = piece with
        {
          AbilityState = piece.AbilityState with
          {
            OdinProtectedById = null,
            OdinProtectionAvailable = false
          }
        };
      }
    }
    TriggerServerPoisonCloudsAtOwnerTurnStart(match, activeTeam);
    foreach (string pieceId in match.Pieces.Select(piece => piece.Id).ToArray())
    {
      int index = match.Pieces.FindIndex(piece => piece.Id == pieceId);
      if (index < 0) continue;
      NetworkPiece piece = match.Pieces[index];
      var pending = AbilityStateRules.SplitPendingDamageForTurn(piece.PendingDamage, activeTeam);
      match.Pieces[index] = piece with { PendingDamage = pending.Remaining };

      foreach (NetworkPendingDamage effect in pending.Triggered)
      {
        index = match.Pieces.FindIndex(candidate => candidate.Id == pieceId);
        if (index < 0) break;
        NetworkPiece live = match.Pieces[index];
        PlayerSlot? source = match.Players.FirstOrDefault(player => player.Team == effect.SourceTeam);
        if (source is null) continue;
        int damage = CombatRules.CalculateDamage(
          effect.Damage,
          false,
          false,
          false,
          IsInForest(match, live),
          match.Terrain.ForestDamageReduction
        );
        bool protectedByBaron = AdvancedAbilityRules.IsBaronSelectedTarget(
          match.Pieces.Select(candidate => (
            candidate.Type,
            candidate.Team,
            candidate.AbilityState?.SelectedTargetId)),
          live.Id,
          live.Team);
        damage = AdvancedAbilityRules.ApplyBaronIncomingReduction(damage, protectedByBaron);
        if (live.Health > damage)
        {
          match.Pieces[index] = live with { Health = live.Health - damage };
        }
        else
        {
          match.Pieces[index] = live with { Health = 0 };
          HandlePieceDestroyed(match, match.Pieces[index], source);
        }
      }
    }

    foreach (NetworkPiece imp in match.Pieces
      .Where(piece => piece.Team == activeTeam && piece.AttachmentKind == NetworkAttachmentKind.Imp &&
        piece.AttachedToId is not null)
      .ToArray())
    {
      int hostIndex = match.Pieces.FindIndex(piece => piece.Id == imp.AttachedToId);
      if (hostIndex < 0) continue;
      NetworkPiece host = match.Pieces[hostIndex];
      int remaining = host.Health - AdvancedAbilityRules.ImpHealthDrain;
      if (remaining > 0)
      {
        match.Pieces[hostIndex] = host with { Health = remaining };
      }
      else
      {
        PlayerSlot? source = match.Players.FirstOrDefault(player => player.Team != host.Team) ??
          match.Players.FirstOrDefault(player => player.Team == host.Team);
        match.Pieces[hostIndex] = host with { Health = 0 };
        if (source is not null)
        {
          HandlePieceDestroyed(match, match.Pieces[hostIndex], source);
        }
      }
    }

    foreach (string pieceId in match.Pieces.Where(piece => piece.Team == activeTeam).Select(piece => piece.Id).ToArray())
    {
      int index = match.Pieces.FindIndex(piece => piece.Id == pieceId);
      if (index < 0) continue;
      NetworkPiece piece = match.Pieces[index];
      OwnerTurnState state = AbilityStateRules.AdvanceOwnerTurn(
        piece.Type,
        piece.Health,
        piece.TurnsInCurrentForm
      );
      if (state.RemovePiece)
      {
        PlayerSlot? owner = match.Players.FirstOrDefault(player => player.Team == piece.Team);
        if (owner is not null)
        {
          match.Pieces[index] = piece with { Health = 0 };
          HandlePieceDestroyed(match, match.Pieces[index], owner);
        }
        continue;
      }

      match.Pieces[index] = piece with
      {
        Type = state.ResultingType,
        Health = state.ResultingHealth,
        TurnsInCurrentForm = state.TurnsInCurrentForm
      };
    }
  }

  private static bool ApplySharedServerAbilityUpkeep(Match match, NetworkTeam team, PlayerSlot player)
  {
    UnitUpkeepSequenceResult result = EconomyRules.ResolveAbilityUpkeepSequence(
      player.Money,
      match.Pieces
        .Where(piece => piece.Team == team && piece.AttachedToId is null)
        .Select(piece => new UnitUpkeepRequest(piece.Id, piece.Type))
    );
    player.Money = result.RemainingMoney;

    foreach (UnitUpkeepDecision decision in result.Decisions)
    {
      if (decision.Paid) continue;
      if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.FireUnit)
      {
        int index = match.Pieces.FindIndex(piece => piece.Id == decision.UnitId);
        if (index >= 0)
        {
          NetworkPiece mercenary = match.Pieces[index];
          match.Pieces[index] = mercenary with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true,
            AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(mercenary.Type),
            AbilityState = (mercenary.AbilityState ?? new UnitAbilityState()) with { CannotActThisTurn = true, CannotMoveThisTurn = true }
          };
        }
      }
      else if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.LoseMatch)
      {
        match.Winner = TeamRules.GetActiveTeams(match.Configuration.PlayerCount)
          .First(candidate => candidate != team);
        return false;
      }
    }

    return true;
  }

  private static int GetSharedServerAttachmentMovementBonus(Match match, NetworkPiece host) =>
    match.Pieces
      .Where(piece => piece.AttachedToId == host.Id)
      .Sum(piece => AbilityRules.GetAttachmentMovementBonus(piece.Type));

  private static bool IsServerDuelistAttackLegal(
    Match match,
    NetworkPiece attacker,
    NetworkPiece? intendedTarget)
  {
    NetworkPiece[] forcingDuelists = match.Pieces
      .Where(piece =>
        piece.Type == nameof(PieceType.Duelist) &&
        piece.Team != attacker.Team &&
        string.Equals(piece.AbilityState?.SelectedTargetId, attacker.Id, StringComparison.Ordinal) &&
        CanUseActionTarget(match, piece, attacker))
      .ToArray();
    return forcingDuelists.Length == 0 ||
      (intendedTarget is not null && forcingDuelists.Any(piece => piece.Id == intendedTarget.Id));
  }

  private static void ClearServerPetrificationBy(Match match, string medusaId)
  {
    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece piece = match.Pieces[index];
      if (string.Equals(piece.AbilityState?.PetrifiedById, medusaId, StringComparison.Ordinal))
      {
        match.Pieces[index] = piece with
        {
          AbilityState = AdvancedAbilityRules.SetPetrified(piece.AbilityState, null)
        };
      }
    }
  }

  private static void ReleaseServerPetrificationIfBroken(Match match, NetworkPiece medusa)
  {
    if (medusa.Type != nameof(PieceType.Medusa) || !UnitRules.TryGet(medusa.Type, out UnitRule medusaRule))
    {
      return;
    }

    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece target = match.Pieces[index];
      if (!string.Equals(target.AbilityState?.PetrifiedById, medusa.Id, StringComparison.Ordinal) ||
          !UnitRules.TryGet(target.Type, out UnitRule targetRule))
      {
        continue;
      }

      if (!AdvancedAbilityRules.IsPetrificationMaintained(
            medusaRule, (medusa.X, medusa.Y), targetRule, (target.X, target.Y)))
      {
        match.Pieces[index] = target with
        {
          AbilityState = AdvancedAbilityRules.SetPetrified(target.AbilityState, null)
        };
      }
    }
  }

  private static void PerformServerMissileSiloAttack(
    Match match,
    NetworkPiece attacker,
    PlayerSlot attackingPlayer,
    (int x, int y) centre)
  {
    int damage = UnitRules.GetRequired(attacker.Type).Attack;
    foreach (NetworkPiece victim in match.Pieces.ToArray())
    {
      if (!UnitRules.TryGet(victim.Type, out UnitRule victimRule) ||
          !OccupiedSquares(victimRule, (victim.X, victim.Y)).Any(square =>
            Math.Max(Math.Abs(square.x - centre.x), Math.Abs(square.y - centre.y)) <= 2) ||
          !CanSharedServerDamage(attacker, victim))
      {
        continue;
      }

      ResolvePieceDamage(match, attacker, attackingPlayer, victim.Id, damage);
    }

    HashSet<(int x, int y)> structurePositions = new();
    foreach (AbilityEntity entity in match.AbilityEntities)
    {
      if (Math.Max(Math.Abs(entity.X - centre.x), Math.Abs(entity.Y - centre.y)) <= 2)
      {
        structurePositions.Add((entity.X, entity.Y));
      }
    }
    foreach ((int x, int y) position in match.Barricades.Keys.Concat(match.Roads.Keys).Concat(match.Mines.Keys))
    {
      if (Math.Max(Math.Abs(position.x - centre.x), Math.Abs(position.y - centre.y)) <= 2)
      {
        structurePositions.Add(position);
      }
    }

    foreach ((int x, int y) position in structurePositions)
    {
      if (!TryDestroyAbilityEntity(match, position.x, position.y))
      {
        match.Barricades.Remove(position);
        match.Roads.Remove(position);
        match.Mines.Remove(position);
      }
    }
  }



  private static void PushServerUnitsOutOfFafnirFootprint(
    Match match,
    NetworkPiece dragon,
    UnitRule dragonRule,
    PlayerSlot source)
  {
    string[] farmIds = match.Pieces
      .Where(piece => piece.Id != dragon.Id && piece.AttachedToId is null &&
        piece.Type == nameof(PieceType.Farm) &&
        UnitRules.TryGet(piece.Type, out UnitRule farmRule) &&
        UnitRules.FootprintsOverlap(
          piece.X, piece.Y, farmRule.Width, farmRule.Height,
          dragon.X, dragon.Y, dragonRule.Width, dragonRule.Height))
      .Select(piece => piece.Id)
      .ToArray();
    foreach (string farmId in farmIds)
    {
      NetworkPiece farm = match.Pieces.FirstOrDefault(piece => piece.Id == farmId);
      if (farm is not null)
      {
        HandlePieceDestroyed(match, farm with { Health = 0 }, source);
      }
    }

    string[] overlappingIds = match.Pieces
      .Where(piece => piece.Id != dragon.Id && piece.AttachedToId is null &&
        piece.Type != nameof(PieceType.Farm) &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        UnitRules.FootprintsOverlap(
          piece.X, piece.Y, rule.Width, rule.Height,
          dragon.X, dragon.Y, dragonRule.Width, dragonRule.Height))
      .Select(piece => piece.Id)
      .ToArray();

    var board = BoardRules.GetBoard(match.Configuration);
    foreach (string pieceId in overlappingIds)
    {
      int index = match.Pieces.FindIndex(piece => piece.Id == pieceId);
      if (index < 0) continue;
      NetworkPiece moving = match.Pieces[index];
      if (!UnitRules.TryGet(moving.Type, out UnitRule movingRule)) continue;

      (int x, int y)? destination = null;
      foreach ((int x, int y) candidate in board.Cells
        .OrderBy(position => Math.Max(
          Math.Abs(position.x - moving.X),
          Math.Abs(position.y - moving.Y)))
        .ThenBy(position => position.y)
        .ThenBy(position => position.x))
      {
        if (CanDisplaceServerPieceTo(match, moving, movingRule, candidate))
        {
          destination = candidate;
          break;
        }
      }
      if (destination is null) continue;

      match.Pieces[index] = moving with
      {
        X = destination.Value.x,
        Y = destination.Value.y
      };
      for (int attachmentIndex = 0; attachmentIndex < match.Pieces.Count; attachmentIndex++)
      {
        NetworkPiece attachment = match.Pieces[attachmentIndex];
        if (attachment.AttachedToId == moving.Id)
        {
          match.Pieces[attachmentIndex] = attachment with
          {
            X = destination.Value.x,
            Y = destination.Value.y
          };
        }
      }
    }
  }



  private static NetworkPiece? GetServerLandingAttackTarget(
    Match match,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) destination)
  {
    if (!AdvancedAbilityRules.IsLandingAttackUnit(mover.Type))
    {
      return null;
    }

    NetworkPiece[] targets = match.Pieces
      .Where(piece =>
        piece.Id != mover.Id &&
        piece.AttachedToId is null &&
        piece.Type != nameof(PieceType.Farm) &&
        piece.Team != mover.Team &&
        AdvancedAbilityRules.CanTakeDirectDamage(piece.Type, piece.AbilityState) &&
        UnitRules.TryGet(piece.Type, out UnitRule targetRule) &&
        UnitRules.FootprintsOverlap(
          destination.x, destination.y, moverRule.Width, moverRule.Height,
          piece.X, piece.Y, targetRule.Width, targetRule.Height))
      .ToArray();
    return targets.Length == 1 ? targets[0] : null;
  }

  private static bool CanServerLandingAttackLand(
    Match match,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) destination) =>
    GetServerLandingAttackTarget(match, mover, moverRule, destination) is not null;

  private static bool CanContinueServerSpecialLandingPath(
    Match match,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) position) =>
    GetServerLandingAttackTarget(match, mover, moverRule, position) is null;

  private static (int x, int y) ResolveServerLandingAttack(
    Match match,
    NetworkPiece mover,
    PlayerSlot player,
    UnitRule moverRule,
    IReadOnlyList<(int x, int y)> path,
    (int x, int y) requestedDestination)
  {
    NetworkPiece? target = GetServerLandingAttackTarget(
      match, mover, moverRule, requestedDestination);
    if (target is null)
    {
      return requestedDestination;
    }

    (int x, int y) origin = (mover.X, mover.Y);
    ResolvePieceDamage(match, mover, player, target.Id, null);
    int targetIndex = match.Pieces.FindIndex(piece => piece.Id == target.Id);
    bool targetSurvived = targetIndex >= 0;

    if (targetSurvived)
    {
      target = match.Pieces[targetIndex];
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
          candidate => CanDisplaceServerPieceTo(match, target, targetRule, candidate));
        if (pushed != (target.X, target.Y))
        {
          match.Pieces[targetIndex] = target with { X = pushed.x, Y = pushed.y };
          for (int attachmentIndex = 0; attachmentIndex < match.Pieces.Count; attachmentIndex++)
          {
            NetworkPiece attachment = match.Pieces[attachmentIndex];
            if (attachment.AttachedToId == target.Id)
            {
              match.Pieces[attachmentIndex] = attachment with
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
      int moverIndex = match.Pieces.FindIndex(piece => piece.Id == mover.Id);
      if (moverIndex >= 0)
      {
        NetworkPiece liveMover = match.Pieces[moverIndex];
        AttackTurnState attackState = AbilityStateRules.RecordAttack(
          liveMover.Type, liveMover.AttacksThisTurn);
        match.Pieces[moverIndex] = liveMover with
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

  private static bool CanServerMimicSwap(
    Match match,
    NetworkPiece mimic,
    NetworkPiece target)
  {
    if (mimic.Type != nameof(PieceType.Mimic) ||
        target.Id == mimic.Id || target.AttachedToId is not null ||
        !UnitRules.TryGet(mimic.Type, out UnitRule mimicRule) ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule) ||
        mimicRule.Width != 1 || mimicRule.Height != 1 ||
        targetRule.Width != 1 || targetRule.Height != 1 ||
        !AdvancedAbilityRules.CanUseMovementAbility(
          mimic.AbilityState, mimic.HasMovedThisTurn) ||
        !UnitRules.CanMove(mimicRule, mimic.X, mimic.Y, target.X, target.Y))
    {
      return false;
    }

    return CanSwapServerPieceTo(match, mimic, mimicRule, target, (target.X, target.Y)) &&
      CanSwapServerPieceTo(match, target, targetRule, mimic, (mimic.X, mimic.Y));
  }

  private static bool CanSwapServerPieceTo(
    Match match,
    NetworkPiece moving,
    UnitRule movingRule,
    NetworkPiece ignoredOther,
    (int x, int y) destination)
  {
    if (!NetworkPieceRules.FootprintFitsBoard(
          match.Configuration,
          destination.x,
          destination.y,
          movingRule.Width,
          movingRule.Height))
    {
      return false;
    }

    foreach ((int x, int y) square in OccupiedSquares(movingRule, destination))
    {
      if ((!AbilityRules.IgnoresImpassableTerrain(movingRule) &&
           match.Terrain.IsLake(square) && !HasServerBridgeAt(match, square)) ||
          (!AbilityRules.IgnoresStructures(movingRule) &&
           match.Barricades.ContainsKey(square)) ||
          match.AbilityEntities.Any(entity =>
            entity.X == square.x && entity.Y == square.y &&
            AbilityEntityRules.BlocksLandingFor(entity, moving.Team) &&
            (entity.Kind == AbilityEntityKind.Bramble ||
             !AbilityRules.IgnoresStructures(movingRule))))
      {
        return false;
      }
    }

    return !match.Pieces.Any(piece =>
      piece.Id != moving.Id &&
      piece.Id != ignoredOther.Id &&
      piece.AttachedToId is null &&
      piece.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(piece.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(
        piece.X, piece.Y, otherRule.Width, otherRule.Height,
        destination.x, destination.y, movingRule.Width, movingRule.Height));
  }

  private static bool TryServerMimicSwap(
    Match match,
    int mimicIndex,
    int targetIndex)
  {
    if (mimicIndex < 0 || targetIndex < 0) return false;
    NetworkPiece mimic = match.Pieces[mimicIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    if (!CanServerMimicSwap(match, mimic, target))
    {
      return false;
    }

    int mimicX = mimic.X;
    int mimicY = mimic.Y;
    match.Pieces[mimicIndex] = mimic with
    {
      X = target.X,
      Y = target.Y,
      HasMovedThisTurn = true,
      AbilityState = AdvancedAbilityRules.RecordMove(mimic.AbilityState)
    };
    match.Pieces[targetIndex] = target with { X = mimicX, Y = mimicY };

    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece attachment = match.Pieces[index];
      if (attachment.AttachedToId == mimic.Id)
      {
        match.Pieces[index] = attachment with { X = target.X, Y = target.Y };
      }
      else if (attachment.AttachedToId == target.Id)
      {
        match.Pieces[index] = attachment with { X = mimicX, Y = mimicY };
      }
    }

    ReleaseServerPetrificationIfBroken(match, match.Pieces[mimicIndex]);
    return true;
  }



  private static NetworkPiece? GetServerSpecialPurchaseHost(
    Match match,
    NetworkTeam team,
    int x,
    int y,
    bool requireNonRoyal)
  {
    return match.Pieces.FirstOrDefault(piece =>
      piece.Team == team &&
      piece.AttachedToId is null &&
      piece.Type != nameof(PieceType.Farm) &&
      (!requireNonRoyal || !RoyalAbilityRules.IsRoyal(piece.Type, piece.IsRoyalProxy, piece.PossessedUnitId)) &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      UnitRules.FootprintsOverlap(
        piece.X, piece.Y, rule.Width, rule.Height,
        x, y, 1, 1));
  }

  private static bool TryGetServerArchdemonPlacement(
    Match match,
    NetworkTeam team,
    int clickedX,
    int clickedY,
    int width,
    int height,
    out NetworkPiece? sacrifice,
    out (int x, int y) placement)
  {
    sacrifice = GetServerSpecialPurchaseHost(
      match, team, clickedX, clickedY, requireNonRoyal: false);
    placement = sacrifice is null ? (clickedX, clickedY) : (sacrifice.X, sacrifice.Y);
    if (sacrifice is null ||
        !NetworkPieceRules.FootprintFitsBoard(
          match.Configuration, placement.x, placement.y, width, height))
    {
      return false;
    }

    for (int y = 0; y < height; y++)
    for (int x = 0; x < width; x++)
    {
      (int x, int y) square = (placement.x + x, placement.y + y);
      if (match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square))
      {
        return false;
      }
    }

    return !match.Pieces.Any(piece =>
      piece.Id != sacrifice.Id &&
      piece.AttachedToId is null &&
      piece.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) &&
      UnitRules.FootprintsOverlap(
        piece.X, piece.Y, rule.Width, rule.Height,
        placement.x, placement.y, width, height));
  }

  private static void RemoveServerShadowsAttachedTo(Match match, string hostId)
  {
    foreach (string shadowId in match.Pieces
      .Where(piece =>
        piece.AttachedToId == hostId &&
        piece.AttachmentKind == NetworkAttachmentKind.Shadow)
      .Select(piece => piece.Id)
      .ToArray())
    {
      RemovePiece(match, shadowId);
    }
  }


}
