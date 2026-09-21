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
  public void BansheeAndCherub_ApplyTheirCodexTerrainRules()
  {
    UnitRule banshee = UnitRules.GetRequired(nameof(PieceType.Banshee));
    UnitRule cherub = UnitRules.GetRequired(nameof(PieceType.Cherub));

    Assert.True(AbilityRules.IgnoresImpassableTerrain(banshee));
    Assert.True(AbilityRules.IgnoresStructures(banshee));
    Assert.True(AbilityRules.AttacksOverObstacles(banshee));
    Assert.True(AbilityRules.IgnoresImpassableTerrain(cherub));
    Assert.False(AbilityRules.IgnoresStructures(cherub));
  }

  [Fact]
  public void TerrainSpecialists_UseOnlyTheirSpecifiedTraversalRules()
  {
    UnitRule elf = UnitRules.GetRequired(nameof(PieceType.Elf));
    UnitRule fylgja = UnitRules.GetRequired(nameof(PieceType.Fylgja));
    UnitRule beelzebub = UnitRules.GetRequired(nameof(PieceType.Beelzebub));

    Assert.True(AbilityRules.IgnoresForests(elf));
    Assert.False(AbilityRules.IgnoresImpassableTerrain(elf));
    Assert.True(AbilityRules.IgnoresImpassableTerrain(fylgja));
    Assert.True(AbilityRules.CanTravelThroughUnit(fylgja, NetworkTeam.Red, NetworkTeam.Red));
    Assert.True(AbilityRules.IgnoresImpassableTerrain(beelzebub));
    Assert.True(AbilityRules.CanTravelThroughUnit(beelzebub, NetworkTeam.Red, NetworkTeam.Blue));
  }

  [Fact]
  public void Monk_CapsEverySingleIncomingDamageInstanceAtTwelve()
  {
    UnitRule monk = UnitRules.GetRequired(nameof(PieceType.Monk));
    UnitRule swordsman = UnitRules.GetRequired(nameof(PieceType.Swordsman));

    Assert.Equal(AbilityRules.MonkMaximumIncomingDamage, AbilityRules.LimitIncomingDamage(monk, 70));
    Assert.Equal(9, AbilityRules.LimitIncomingDamage(monk, 9));
    Assert.Equal(70, AbilityRules.LimitIncomingDamage(swordsman, 70));
  }

  [Theory]
  [InlineData(nameof(PieceType.Mercenary))]
  [InlineData(nameof(PieceType.Gargoyle))]
  [InlineData(nameof(PieceType.Valkyrie))]
  [InlineData(nameof(PieceType.Frontiersmen))]
  [InlineData(nameof(PieceType.Ophan))]
  public void NoMansLandUnits_AreDeclaredBySharedCodexRules(string unitType)
  {
    Assert.True(AbilityRules.MayPlaceInNoMansLand(unitType));
    Assert.False(AbilityRules.MayPlaceInNoMansLand(nameof(PieceType.Swordsman)));
  }

  [Fact]
  public void Barbarian_NotRaider_GainsTheForwardMovementBonusSpecifiedByTheCodex()
  {
    UnitRule barbarian = UnitRules.GetRequired(nameof(PieceType.Barbarian));
    UnitRule raider = UnitRules.GetRequired(nameof(PieceType.Raider));
    (int x, int y) forward = TeamRules.GetForwardDirection(NetworkTeam.Red);
    (int x, int y) destination = (3 + forward.x, 3 + forward.y);

    Assert.Equal(AbilityRules.BarbarianForwardMovementBonus,
      AbilityRules.GetMovementRangeBonus(barbarian, NetworkTeam.Red, (3, 3), destination));
    Assert.Equal(0,
      AbilityRules.GetMovementRangeBonus(raider, NetworkTeam.Red, (3, 3), destination));
    Assert.Equal(barbarian.MoveRange + AbilityRules.BarbarianForwardMovementBonus,
      barbarian.MoveRange + AbilityRules.GetMaximumMovementRangeBonus(barbarian));
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
  public void ZeusPlan_ChainsThroughAllAdjacentEnemiesIncludingDiagonals()
  {
    AbilityUnitSnapshot zeus = new("zeus", nameof(PieceType.Zeus), NetworkTeam.Red, 0, 0, 1, 1);
    AbilityUnitSnapshot target = new("a", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, 2, 1, 1);
    AbilityUnitSnapshot next = new("b", nameof(PieceType.Swordsman), NetworkTeam.Blue, 3, 2, 1, 1);
    AbilityUnitSnapshot chained = new("c", nameof(PieceType.Swordsman), NetworkTeam.Blue, 4, 2, 1, 1);
    AbilityUnitSnapshot diagonal = new("d", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, 1, 1, 1);
    AbilityUnitSnapshot friendly = new("friendly", nameof(PieceType.Swordsman), NetworkTeam.Red, 2, 1, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      zeus,
      target,
      [zeus, target, next, chained, diagonal, friendly]
    );

    Assert.Contains(plan.Damage, hit => hit.TargetId == next.Id && hit.FixedDamage == AbilityRules.ZeusChainDamage);
    Assert.Contains(plan.Damage, hit => hit.TargetId == chained.Id && hit.FixedDamage == AbilityRules.ZeusChainDamage);
    Assert.Contains(plan.Damage, hit => hit.TargetId == diagonal.Id && hit.FixedDamage == AbilityRules.ZeusChainDamage);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == friendly.Id);
  }

  [Fact]
  public void HwachaHitsSelectedTargetAndOnlyDirectlyAdjacentSplashUnits()
  {
    AbilityUnitSnapshot hwacha = new("hwacha", nameof(PieceType.Hwacha), NetworkTeam.Red, 0, 0, 1, 1);
    AbilityUnitSnapshot target = new("target", nameof(PieceType.Swordsman), NetworkTeam.Blue, 3, 0, 1, 1);
    AbilityUnitSnapshot orthogonal = new("orthogonal", nameof(PieceType.Swordsman), NetworkTeam.Red, 3, 1, 1, 1);
    AbilityUnitSnapshot diagonal = new("diagonal", nameof(PieceType.Swordsman), NetworkTeam.Blue, 4, 1, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      hwacha,
      target,
      [hwacha, target, orthogonal, diagonal]
    );

    Assert.Contains(plan.Damage, hit => hit.TargetId == target.Id && hit.Mode == AbilityDamageMode.NormalAttack);
    Assert.Contains(plan.Damage, hit => hit.TargetId == orthogonal.Id && hit.Mode == AbilityDamageMode.NormalAttack);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == diagonal.Id);
  }

  [Fact]
  public void TerroristAttack_DamagesEveryUnitInRangeThenSelfDestructs()
  {
    AbilityUnitSnapshot terrorist = new("terrorist", nameof(PieceType.Terrorist), NetworkTeam.Red, 2, 2, 1, 1);
    AbilityUnitSnapshot selectedEnemy = new("enemy", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, 3, 1, 1);
    AbilityUnitSnapshot adjacentFriendly = new("friendly", nameof(PieceType.Swordsman), NetworkTeam.Red, 3, 2, 1, 1);
    AbilityUnitSnapshot distant = new("distant", nameof(PieceType.Swordsman), NetworkTeam.Blue, 4, 4, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      terrorist,
      selectedEnemy,
      [terrorist, selectedEnemy, adjacentFriendly, distant]
    );

    Assert.True(plan.SelfDestructAfterAttack);
    Assert.Contains(plan.Damage, hit => hit.TargetId == selectedEnemy.Id && hit.Mode == AbilityDamageMode.NormalAttack);
    Assert.Contains(plan.Damage, hit => hit.TargetId == adjacentFriendly.Id && hit.Mode == AbilityDamageMode.NormalAttack);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == distant.Id);
  }

  [Fact]
  public void Orc_UsesCurrentWorkbookCostAndIsPurchasable()
  {
    Assert.Equal(105, PieceDefinitions.Orc.Cost);
    Assert.Contains(PieceDefinitions.Purchasable, definition => definition.Type == PieceType.Orc);
  }

  [Fact]
  public void UpdatedUnitUpkeep_UsesCodexAmountsAndFiresWhenUnpaid()
  {
    Assert.Equal(30, EconomyRules.ResolveAbilityUpkeep(nameof(PieceType.SummonedGolem), 50).Cost);
    Assert.Equal(20, EconomyRules.ResolveAbilityUpkeep(nameof(PieceType.HiredGun), 50).Cost);

    UnitUpkeepResult golem = EconomyRules.ResolveAbilityUpkeep(nameof(PieceType.SummonedGolem), 29);
    Assert.False(golem.Paid);
    Assert.Equal(UnpaidUnitUpkeepEffect.FireUnit, golem.UnpaidEffect);
  }

  [Fact]
  public void QilinVariablePrice_UsesCodexFormula()
  {
    foreach (int x in new[] { 40, 60, 80, 100, 120, 140, 160 })
    {
      Assert.True(AdvancedAbilityRules.IsValidQilinCost(x));
      Assert.Equal(10 + x / 4, AdvancedAbilityRules.GetQilinAttack(x));
      Assert.Equal(20 + x / 2, AdvancedAbilityRules.GetQilinHealth(x));
    }

    Assert.False(AdvancedAbilityRules.IsValidQilinCost(50));
    Assert.False(AdvancedAbilityRules.IsValidQilinCost(180));
  }

  [Fact]
  public void HermesGetsTwoMovesAndSniperCooldownCountsOwnerTurns()
  {
    UnitAbilityState hermes = new();
    Assert.True(AdvancedAbilityRules.CanMove(nameof(PieceType.Hermes), hermes, false));
    hermes = AdvancedAbilityRules.RecordMove(hermes);
    Assert.True(AdvancedAbilityRules.CanMove(nameof(PieceType.Hermes), hermes, true));
    hermes = AdvancedAbilityRules.RecordMove(hermes);
    Assert.False(AdvancedAbilityRules.CanMove(nameof(PieceType.Hermes), hermes, true));

    UnitAbilityState sniper = AdvancedAbilityRules.RecordAttack(nameof(PieceType.Sniper), new UnitAbilityState(), "enemy");
    Assert.False(AdvancedAbilityRules.CanAttack(nameof(PieceType.Sniper), sniper, false, "enemy"));
    sniper = AdvancedAbilityRules.StartOwnerTurn(sniper, 0, 0, 30);
    Assert.False(AdvancedAbilityRules.CanAttack(nameof(PieceType.Sniper), sniper, false, "enemy"));
    sniper = AdvancedAbilityRules.StartOwnerTurn(sniper, 0, 0, 30);
    Assert.True(AdvancedAbilityRules.CanAttack(nameof(PieceType.Sniper), sniper, false, "enemy"));
  }

  [Fact]
  public void SeraphCanAttackThreeDistinctTargetsOnly()
  {
    UnitAbilityState state = new();
    foreach (string id in new[] { "a", "b", "c" })
    {
      Assert.True(AdvancedAbilityRules.CanAttack(nameof(PieceType.Seraph), state, false, id));
      state = AdvancedAbilityRules.RecordAttack(nameof(PieceType.Seraph), state, id);
    }

    Assert.False(AdvancedAbilityRules.CanAttack(nameof(PieceType.Seraph), state, false, "d"));
    Assert.False(AdvancedAbilityRules.CanAttack(nameof(PieceType.Seraph), state, false, "a"));
  }

  [Fact]
  public void BaronSelectionAppliesOnlyToItsChosenFriendlyUnit()
  {
    var pieces = new[]
    {
      (nameof(PieceType.Baron), NetworkTeam.Red, (string?)"chosen"),
      (nameof(PieceType.Baron), NetworkTeam.Blue, (string?)"other")
    };
    Assert.True(AdvancedAbilityRules.IsBaronSelectedTarget(pieces, "chosen", NetworkTeam.Red));
    Assert.False(AdvancedAbilityRules.IsBaronSelectedTarget(pieces, "other", NetworkTeam.Red));
    Assert.Equal(30, AdvancedAbilityRules.ApplyBaronOutgoingBonus(20, true));
    Assert.Equal(10, AdvancedAbilityRules.ApplyBaronIncomingReduction(20, true));
  }

  [Fact]
  public void SeraphUsesThreeNormalAttackSlots()
  {
    Assert.Equal(3, AbilityRules.MaximumAttacksPerTurn(nameof(PieceType.Seraph)));

    AttackTurnState first = AbilityStateRules.RecordAttack(nameof(PieceType.Seraph), 0);
    AttackTurnState second = AbilityStateRules.RecordAttack(nameof(PieceType.Seraph), first.AttacksThisTurn);
    AttackTurnState third = AbilityStateRules.RecordAttack(nameof(PieceType.Seraph), second.AttacksThisTurn);

    Assert.False(first.HasAttackedThisTurn);
    Assert.False(second.HasAttackedThisTurn);
    Assert.True(third.HasAttackedThisTurn);
  }


  [Fact]
  public void MusePatternProgressionMatchesCodex()
  {
    Assert.Equal(RuleShape.Straight, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Line));
    Assert.Equal(RuleShape.Straight, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Diagonal));
    Assert.Equal(RuleShape.Circle, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Straight));
    Assert.Equal(RuleShape.Any, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Circle));
  }


}
