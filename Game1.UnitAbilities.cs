using MedivalChess.GameBoard;
using MedivalChess.Shared;

namespace MedivalChess;

/// <summary>
/// Local-game adapter for shared unit ability rules. Target selection, state transitions and
/// ability values live in MedivalChess.Shared; this partial only maps them onto local Piece state.
/// </summary>
internal sealed partial class Game1
{
  private AbilityUnitSnapshot SnapshotAbilityUnit(Piece piece) => new(
    piece.NetworkId,
    piece.Definition.Type.ToString(),
    piece.Team.ToNetworkTeam(),
    piece.Position.x,
    piece.Position.y,
    piece.Definition.Size.x,
    piece.Definition.Size.y
  );

  private void PerformSharedUnitAttack(Piece attacker, Piece selectedTarget)
  {
    if (attacker.Definition.Type == PieceType.Tank)
    {
      TankAttackDecision tank = AbilityStateRules.ResolveTankAttackAttempt(
        attacker.Team.ToNetworkTeam(),
        attacker.Facing.x,
        attacker.Facing.y,
        attacker.Position,
        selectedTarget.Position
      );
      attacker.Facing = (tank.FacingX, tank.FacingY);
      if (!tank.MayFire)
      {
        return;
      }
    }

    AbilityUnitSnapshot[] snapshots = pieceSetup.Pieces
      .Where(piece => piece.AttachedTo is null)
      .Select(SnapshotAbilityUnit)
      .ToArray();
    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      SnapshotAbilityUnit(attacker),
      SnapshotAbilityUnit(selectedTarget),
      snapshots
    );

    foreach (AbilityDamageInstruction instruction in plan.Damage)
    {
      Piece target = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == instruction.TargetId);
      if (target is null) continue;
      int? fixedDamage = instruction.Mode == AbilityDamageMode.Fixed
        ? instruction.FixedDamage
        : null;
      ResolveDamage(attacker, target, fixedDamage);
    }

    if (plan.ScheduleDragonbornBurn && pieceSetup.Pieces.Contains(selectedTarget))
    {
      selectedTarget.PendingDamage = AbilityStateRules.AddDragonbornBurn(
        selectedTarget.PendingDamage,
        attacker.Team.ToNetworkTeam(),
        attacker.Team.ToNetworkTeam()
      );
    }

    if (plan.HealAttacker > 0 && pieceSetup.Pieces.Contains(attacker) && attacker.CurrentHealth > 0)
    {
      attacker.CurrentHealth = Math.Min(
        attacker.Definition.Health,
        attacker.CurrentHealth + plan.HealAttacker
      );
    }

    if (plan.SelfDestructAfterAttack && pieceSetup.Pieces.Contains(attacker))
    {
      attacker.CurrentHealth = 0;
      HandlePieceDestroyed(attacker, null);
    }
  }

  private int GetSharedLocalAttackDamage(Piece attacker, Piece target)
  {
    UnitRule attackerRule = UnitRules.FromPieceDefinition(attacker.Definition);
    UnitRule targetRule = UnitRules.FromPieceDefinition(target.Definition);
    int baseDamage = AbilityRules.GetBaseAttack(attackerRule, attacker.CurrentHealth);
    baseDamage += AbilityRules.GetAttackAbilityBonus(
      attackerRule,
      targetRule,
      IsPieceInForest(target),
      target.Facing,
      attacker.Position,
      target.Position
    );

    bool hasBaronBonus = HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team);
    bool isSpyMarked = pieceSetup.Pieces.Any(spy =>
      spy.Definition.Type == PieceType.Spy && spy.MarkedTarget == target);
    return CombatRules.CalculateDamage(
      baseDamage,
      hasBaronBonus,
      isSpyMarked,
      false,
      false,
      0
    );
  }

  private bool CanSharedAttackDamage(Piece attacker, Piece target) =>
    AbilityRules.CanDamageTarget(
      UnitRules.FromPieceDefinition(attacker.Definition),
      UnitRules.FromPieceDefinition(target.Definition)
    );

  private void ApplySharedDeathExplosion(Piece destroyedPiece)
  {
    AbilityUnitSnapshot[] snapshots = pieceSetup.Pieces
      .Where(piece => piece.AttachedTo is null)
      .Select(SnapshotAbilityUnit)
      .ToArray();
    IReadOnlyList<AbilityDamageInstruction> explosion = AbilityAttackRules.BuildDeathExplosion(
      SnapshotAbilityUnit(destroyedPiece),
      snapshots
    );

    foreach (AbilityDamageInstruction instruction in explosion)
    {
      Piece target = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == instruction.TargetId);
      if (target is null) continue;
      ResolveDamage(destroyedPiece, target, instruction.FixedDamage);
    }
  }

  private void ApplySharedStartOfTurnEffects(TeamName activeTeam)
  {
    NetworkTeam networkTeam = activeTeam.ToNetworkTeam();
    foreach (Piece target in pieceSetup.Pieces.ToArray())
    {
      var pending = AbilityStateRules.SplitPendingDamageForTurn(target.PendingDamage, networkTeam);
      target.PendingDamage = pending.Remaining;
      foreach (NetworkPendingDamage effect in pending.Triggered)
      {
        if (!pieceSetup.Pieces.Contains(target)) break;
        int damage = CombatRules.CalculateDamage(
          effect.Damage,
          false,
          false,
          HasAdjacentPieceOfType(target, PieceType.Baron, target.Team),
          IsPieceInForest(target),
          _terrain.ForestDamageReduction
        );
        target.CurrentHealth -= damage;
        HandlePieceDestroyed(target, effect.SourceTeam.ToTeamName());
      }
    }
  }

  private void ApplySharedAbilityUpkeep(TeamName teamName, Team team)
  {
    UnitUpkeepSequenceResult result = EconomyRules.ResolveAbilityUpkeepSequence(
      team.Money,
      pieceSetup.Pieces
        .Where(piece => piece.Team == teamName && piece.AttachedTo is null)
        .Select(piece => new UnitUpkeepRequest(piece.NetworkId, piece.Definition.Type.ToString()))
    );
    team.Money = result.RemainingMoney;

    foreach (UnitUpkeepDecision decision in result.Decisions)
    {
      if (decision.Paid) continue;
      Piece piece = pieceSetup.Pieces.FirstOrDefault(candidate => candidate.NetworkId == decision.UnitId);
      if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.FireUnit && piece is not null)
      {
        piece.Team = TeamName.Neutral;
        piece.HasMovedThisTurn = true;
        piece.HasAttackedThisTurn = true;
      }
      else if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.LoseMatch)
      {
        _winningTeam = Team.ActiveTeams.First(candidate => candidate != teamName);
        _screen = Screen.GameOver;
        selectedPiece = null;
        return;
      }
    }
  }

  private bool TryUseSharedRoyalAbility(Piece actor, Piece target)
  {
    if (actor.Definition.Type != PieceType.Phantom)
    {
      return false;
    }

    if (!string.IsNullOrEmpty(actor.PossessedUnitId))
    {
      Piece possessed = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == actor.PossessedUnitId);
      if (target != actor && target != possessed)
      {
        return false;
      }

      if (possessed is not null)
      {
        possessed.IsRoyalProxy = false;
      }
      PhantomPossessionState state = RoyalAbilityRules.Unpossess();
      actor.PossessedUnitId = state.PhantomPossessedUnitId;
      CompleteAction();
      return true;
    }

    if (target is null || !RoyalAbilityRules.CanPhantomPossess(
      actor.Definition.Type.ToString(),
      actor.Team.ToNetworkTeam(),
      actor.PossessedUnitId,
      target.NetworkId,
      target.Definition.Type.ToString(),
      target.Team.ToNetworkTeam(),
      target.IsRoyalProxy))
    {
      return false;
    }

    PhantomPossessionState possession = RoyalAbilityRules.Possess(target.NetworkId);
    actor.PossessedUnitId = possession.PhantomPossessedUnitId;
    target.IsRoyalProxy = possession.TargetIsRoyalProxy;
    CompleteAction();
    return true;
  }

  private bool CanPlaceSharedRoyalGroup(PieceDefinition royal, (int x, int y) anchor, TeamName team) =>
    RoyalAbilityRules.GetRoyalSpawnOffsets(royal.Type.ToString()).All(offset =>
      CanPlacePiece(royal, (anchor.x + offset.x, anchor.y + offset.y), team));

  private void AddSharedRoyalGroup(PieceDefinition royal, (int x, int y) anchor, TeamName team)
  {
    foreach ((int x, int y) offset in RoyalAbilityRules.GetRoyalSpawnOffsets(royal.Type.ToString()))
    {
      pieceSetup.AddPiece(new Piece(royal, (anchor.x + offset.x, anchor.y + offset.y), team)
      {
        CurrentHealth = GetRoyalStartingHealth(royal)
      });
    }
  }

  private bool IsSharedRoyalDeath(Piece defeatedPiece)
  {
    bool sameTeamGoblinRemains = pieceSetup.Pieces.Any(piece =>
      piece != defeatedPiece &&
      piece.Team == defeatedPiece.Team &&
      piece.Definition.Type == PieceType.GoblinRoyalty);
    return RoyalAbilityRules.IsRoyalDeath(
      defeatedPiece.Definition.Type.ToString(),
      defeatedPiece.IsRoyal,
      sameTeamGoblinRemains
    );
  }
}
