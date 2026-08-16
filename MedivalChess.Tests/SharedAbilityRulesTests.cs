using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class SharedAbilityRulesTests
{
  [Fact]
  public void LethalAbilities_AreResolvedBySharedStateRules()
  {
    LethalAbilityOutcome shield = AbilityStateRules.ResolveLethalDamage(nameof(PieceType.Spartan), false);
    Assert.Equal(LethalAbilityOutcomeKind.Survive, shield.Kind);
    Assert.Equal(20, shield.ResultingHealth);
    Assert.True(shield.HasRevived);

    LethalAbilityOutcome emperor = AbilityStateRules.ResolveLethalDamage(nameof(PieceType.Emperor), false);
    Assert.Equal(LethalAbilityOutcomeKind.Transform, emperor.Kind);
    Assert.Equal(nameof(PieceType.TerracottaWarrior), emperor.ResultingType);
    Assert.Equal(PieceDefinitions.TerracottaWarrior.Health, emperor.ResultingHealth);

    LethalAbilityOutcome zombie = AbilityStateRules.ResolveLethalDamage(nameof(PieceType.Zombie), false);
    Assert.Equal(LethalAbilityOutcomeKind.Transform, zombie.Kind);
    Assert.Equal(nameof(PieceType.Flesh), zombie.ResultingType);
  }

  [Fact]
  public void Ninja_AttackCountIsSharedAndAllowsThreeAttacks()
  {
    AttackTurnState first = AbilityStateRules.RecordAttack(nameof(PieceType.Ninja), 0);
    AttackTurnState second = AbilityStateRules.RecordAttack(nameof(PieceType.Ninja), first.AttacksThisTurn);
    AttackTurnState third = AbilityStateRules.RecordAttack(nameof(PieceType.Ninja), second.AttacksThisTurn);

    Assert.False(first.HasAttackedThisTurn);
    Assert.False(second.HasAttackedThisTurn);
    Assert.True(third.HasAttackedThisTurn);
    Assert.Equal(3, third.AttacksThisTurn);
  }

  [Fact]
  public void Tank_OffAxisAttemptTurnsWithoutFiring()
  {
    TankAttackDecision turn = AbilityStateRules.ResolveTankAttackAttempt(
      NetworkTeam.Red,
      0,
      -1,
      (4, 4),
      (6, 4)
    );
    Assert.False(turn.MayFire);
    Assert.Equal((1, 0), (turn.FacingX, turn.FacingY));

    TankAttackDecision fire = AbilityStateRules.ResolveTankAttackAttempt(
      NetworkTeam.Red,
      turn.FacingX,
      turn.FacingY,
      (4, 4),
      (6, 4)
    );
    Assert.True(fire.MayFire);
  }

  [Fact]
  public void AbilityUpkeep_PaysPresidentBeforeMercenaries()
  {
    UnitUpkeepSequenceResult result = EconomyRules.ResolveAbilityUpkeepSequence(
      25,
      [
        new UnitUpkeepRequest("merc", nameof(PieceType.Mercenary)),
        new UnitUpkeepRequest("pres", nameof(PieceType.President))
      ]
    );

    Assert.Equal(20, result.RemainingMoney);
    Assert.Equal("pres", result.Decisions[0].UnitId);
    Assert.True(result.Decisions[0].Paid);
    Assert.Equal("merc", result.Decisions[1].UnitId);
    Assert.False(result.Decisions[1].Paid);
    Assert.Equal(UnpaidUnitUpkeepEffect.FireUnit, result.Decisions[1].UnpaidEffect);
  }

  [Fact]
  public void Ox_AttachmentRulesComeFromSharedAbilityRules()
  {
    UnitRule ox = UnitRules.GetRequired(nameof(PieceType.Ox));
    UnitRule soldier = UnitRules.GetRequired(nameof(PieceType.Swordsman));

    Assert.True(AbilityRules.CanOxAttach(ox, soldier, false, false));
    Assert.Equal(2, AbilityRules.GetAttachmentMovementBonus(nameof(PieceType.Ox)));
    Assert.True(AbilityRules.SharesIncomingDamageWithHost(nameof(PieceType.Ox)));
  }

  [Fact]
  public void ElephantAndSleipnir_UseTheSameSharedTerrainAndTraversalRules()
  {
    UnitRule elephant = UnitRules.GetRequired(nameof(PieceType.Elephant));
    UnitRule sleipnir = UnitRules.GetRequired(nameof(PieceType.Sleipnir));

    Assert.Equal(1, AbilityRules.ApplyTerrainMovementCost(elephant, 2));
    Assert.Equal(1, AbilityRules.ApplyTerrainMovementCost(sleipnir, 2));
    Assert.True(AbilityRules.CanTravelThroughUnit(elephant, NetworkTeam.Red, NetworkTeam.Blue));
    Assert.False(AbilityRules.CanTravelThroughUnit(elephant, NetworkTeam.Red, NetworkTeam.Red));
    Assert.True(AbilityRules.CanTravelThroughUnit(sleipnir, NetworkTeam.Red, NetworkTeam.Red));
    Assert.True(AbilityRules.IgnoresRivers(elephant));
    Assert.True(AbilityRules.IgnoresRivers(sleipnir));
  }

  [Fact]
  public void BombardPlan_UsesSharedTwentyDamageSplashIncludingFriendlies()
  {
    AbilityUnitSnapshot attacker = new("bomb", nameof(PieceType.Bombard), NetworkTeam.Red, 0, 0, 1, 1);
    AbilityUnitSnapshot target = new("target", nameof(PieceType.Swordsman), NetworkTeam.Blue, 3, 0, 1, 1);
    AbilityUnitSnapshot friendlySplash = new("friendly", nameof(PieceType.Swordsman), NetworkTeam.Red, 3, 1, 1, 1);
    AbilityUnitSnapshot distant = new("distant", nameof(PieceType.Swordsman), NetworkTeam.Blue, 8, 8, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      attacker,
      target,
      [attacker, target, friendlySplash, distant]
    );

    AbilityDamageInstruction direct = Assert.Single(plan.Damage, hit => hit.TargetId == target.Id);
    Assert.Equal(AbilityDamageMode.NormalAttack, direct.Mode);

    AbilityDamageInstruction splash = Assert.Single(plan.Damage, hit => hit.TargetId == friendlySplash.Id);
    Assert.Equal(AbilityDamageMode.Fixed, splash.Mode);
    Assert.Equal(AbilityRules.BombardSplashDamage, splash.FixedDamage);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == distant.Id);
  }

  [Fact]
  public void ZeusPlan_ChainsOnlyThroughOrthogonallyAdjacentEnemies()
  {
    AbilityUnitSnapshot zeus = new("zeus", nameof(PieceType.Zeus), NetworkTeam.Red, 0, 0, 1, 1);
    AbilityUnitSnapshot target = new("a", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, 2, 1, 1);
    AbilityUnitSnapshot next = new("b", nameof(PieceType.Swordsman), NetworkTeam.Blue, 3, 2, 1, 1);
    AbilityUnitSnapshot chained = new("c", nameof(PieceType.Swordsman), NetworkTeam.Blue, 4, 2, 1, 1);
    AbilityUnitSnapshot diagonalOnly = new("d", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, 1, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      zeus,
      target,
      [zeus, target, next, chained, diagonalOnly]
    );

    Assert.Contains(plan.Damage, hit => hit.TargetId == next.Id && hit.FixedDamage == AbilityRules.ZeusChainDamage);
    Assert.Contains(plan.Damage, hit => hit.TargetId == chained.Id && hit.FixedDamage == AbilityRules.ZeusChainDamage);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == diagonalOnly.Id);
  }

  [Fact]
  public void Orc_UsesCurrentWorkbookCostAndIsPurchasable()
  {
    Assert.Equal(105, PieceDefinitions.Orc.Cost);
    Assert.Contains(PieceDefinitions.Orc, PieceDefinitions.Purchasable);
  }

}
