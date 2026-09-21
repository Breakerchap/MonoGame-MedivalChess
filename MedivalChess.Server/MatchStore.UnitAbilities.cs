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

  private static bool CanSharedServerDamage(NetworkPiece attacker, NetworkPiece target) =>
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
          Health = Math.Min(attackerRule.Health, liveAttacker.Health + plan.HealAttacker)
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
}
