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
  public void WizardAttackDamagesEveryOtherUnitInTargetThreeByThree()
  {
    AbilityUnitSnapshot wizard = new("wizard", nameof(PieceType.Wizard), NetworkTeam.Red, 0, 0, 1, 1);
    AbilityUnitSnapshot target = new("target", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -2, 1, 1);
    AbilityUnitSnapshot friendly = new("friendly", nameof(PieceType.Swordsman), NetworkTeam.Red, 1, -2, 1, 1);
    AbilityUnitSnapshot diagonal = new("diagonal", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, -3, 1, 1);
    AbilityUnitSnapshot distant = new("distant", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, -4, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      wizard, target, [wizard, target, friendly, diagonal, distant]);

    Assert.Contains(plan.Damage, hit => hit.TargetId == target.Id);
    Assert.Contains(plan.Damage, hit => hit.TargetId == friendly.Id);
    Assert.Contains(plan.Damage, hit => hit.TargetId == diagonal.Id);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == distant.Id);
  }

  [Fact]
  public void OrcAttackHitsEveryUnitInItsAttackRangeIncludingFriendlies()
  {
    AbilityUnitSnapshot orc = new("orc", nameof(PieceType.Orc), NetworkTeam.Red, 0, 0, 1, 1);
    AbilityUnitSnapshot selected = new("selected", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -1, 1, 1);
    AbilityUnitSnapshot friendly = new("friendly", nameof(PieceType.Swordsman), NetworkTeam.Red, 1, 0, 1, 1);
    AbilityUnitSnapshot diagonal = new("diagonal", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, -1, 1, 1);
    AbilityUnitSnapshot distant = new("distant", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, 0, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      orc, selected, [orc, selected, friendly, diagonal, distant]);

    Assert.Contains(plan.Damage, hit => hit.TargetId == selected.Id);
    Assert.Contains(plan.Damage, hit => hit.TargetId == friendly.Id);
    Assert.Contains(plan.Damage, hit => hit.TargetId == diagonal.Id);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == distant.Id);
  }

  [Fact]
  public void DragonAttackHitsEveryUnitInItsForwardLineRange()
  {
    AbilityUnitSnapshot dragon = new("dragon", nameof(PieceType.Dragon), NetworkTeam.Red, 0, 0, 2, 3);
    AbilityUnitSnapshot selected = new("selected", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -1, 1, 1);
    AbilityUnitSnapshot secondLine = new("second", nameof(PieceType.Swordsman), NetworkTeam.Red, 1, -2, 1, 1);
    AbilityUnitSnapshot outsideLine = new("outside", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, -1, 1, 1);

    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      dragon, selected, [dragon, selected, secondLine, outsideLine]);

    Assert.Contains(plan.Damage, hit => hit.TargetId == selected.Id);
    Assert.Contains(plan.Damage, hit => hit.TargetId == secondLine.Id);
    Assert.DoesNotContain(plan.Damage, hit => hit.TargetId == outsideLine.Id);
  }

  [Fact]
  public void ArtemisGetsTenBonusDamageAgainstTargetsInForests()
  {
    UnitRule artemis = UnitRules.GetRequired(nameof(PieceType.Artemis));
    UnitRule target = UnitRules.GetRequired(nameof(PieceType.Swordsman));

    Assert.Equal(AbilityRules.ArtemisForestBonus, AbilityRules.GetAttackAbilityBonus(
      artemis, target, true, (0, 1), (0, 0), (0, -2)));
    Assert.Equal(0, AbilityRules.GetAttackAbilityBonus(
      artemis, target, false, (0, 1), (0, 0), (0, -2)));
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
  public void AttachmentBonusesApplyImpStatsAndMusePatterns()
  {
    UnitRule baseRule = new(
      "TestHost",
      RuleCategory.Melee,
      2,
      RuleShape.Line,
      20,
      30,
      1,
      1,
      3,
      RuleShape.Diagonal,
      50
    );

    UnitRule effective = AdvancedAbilityRules.ApplyAttachmentBonuses(
      baseRule, hasImp: true, museCount: 1);

    Assert.Equal(3, effective.MoveRange);
    Assert.Equal(35, effective.Attack);
    Assert.Equal(RuleShape.Straight, effective.MovePattern);
    Assert.Equal(RuleShape.Straight, effective.AttackPattern);
  }

  [Fact]
  public void MusePatternProgressionMatchesCodex()
  {
    Assert.Equal(RuleShape.Straight, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Line));
    Assert.Equal(RuleShape.Straight, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Diagonal));
    Assert.Equal(RuleShape.Circle, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Straight));
    Assert.Equal(RuleShape.Any, AdvancedAbilityRules.ImproveMusePattern(RuleShape.Circle));
  }


  [Fact]
  public void PersistentAndAuraBonusesProduceEffectiveCodexStats()
  {
    UnitRule baseRule = UnitRules.GetRequired(nameof(PieceType.Swordsman));
    UnitAbilityState upgraded = new()
    {
      AttackBonus = 10,
      MoveBonus = 1,
      AttackRangeBonus = 1,
      MaxHealthBonus = 20
    };
    UnitRule persistent = AdvancedAbilityRules.ApplyPersistentBonuses(baseRule, upgraded);

    Assert.Equal(baseRule.Attack + 10, persistent.Attack);
    Assert.Equal(baseRule.MoveRange + 1, persistent.MoveRange);
    Assert.Equal(baseRule.AttackRange + 1, persistent.AttackRange);
    Assert.Equal(baseRule.Health + 20, AdvancedAbilityRules.GetEffectiveMaximumHealth(baseRule, upgraded));

    NetworkPiece unit = new(
      "unit", nameof(PieceType.Swordsman), NetworkTeam.Red, 5, 5, baseRule.Health);
    AbilityEntity[] entities =
    [
      new("attack", AbilityEntityKind.RuneAttack, NetworkTeam.Red, 5, 6, 5),
      new("move", AbilityEntityKind.RuneMovement, NetworkTeam.Red, 4, 5, 5),
      new("health", AbilityEntityKind.RuneHealth, NetworkTeam.Red, 6, 5, 5),
      new("range", AbilityEntityKind.RuneRange, NetworkTeam.Red, 4, 4, 5),
      new("tower", AbilityEntityKind.Watchtower, NetworkTeam.Red, 5, 5, 15)
    ];

    UnitRule aura = AbilityEntityRules.ApplyAuraBonuses(baseRule, entities, unit);
    Assert.Equal(baseRule.Attack + AdvancedAbilityRules.RuneAttackBonus, aura.Attack);
    Assert.Equal(baseRule.MoveRange + AdvancedAbilityRules.RuneMoveBonus, aura.MoveRange);
    Assert.Equal(baseRule.AttackRange + AdvancedAbilityRules.RuneRangeBonus + 2, aura.AttackRange);
    Assert.Equal(AdvancedAbilityRules.RuneDamageReduction,
      AbilityEntityRules.GetDamageReduction(entities, unit));
  }

  [Fact]
  public void SnareLocksExactlyNextOwnerMovementAndSealExpiresForItsOwner()
  {
    NetworkPiece unit = new(
      "unit", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 0, 30);
    AbilityEntity snare = new("snare", AbilityEntityKind.Snare, NetworkTeam.Blue, 0, 0);
    AbilityEntityEntryEffect effect = AbilityEntityEffectRules.GetEntryEffect(snare, unit);

    Assert.True(effect.ConsumeEntity);
    Assert.True(effect.LockMovementNextOwnerTurn);

    UnitAbilityState state = new() { SkipMovementOwnerTurns = 2 };
    state = AdvancedAbilityRules.StartOwnerTurn(state, 0, 0, 30);
    Assert.False(AdvancedAbilityRules.CanMove(nameof(PieceType.Swordsman), state, false));
    state = AdvancedAbilityRules.StartOwnerTurn(state, 0, 0, 30);
    Assert.True(AdvancedAbilityRules.CanMove(nameof(PieceType.Swordsman), state, false));

    AbilityEntity seal = new("seal", AbilityEntityKind.Seal, NetworkTeam.Red, 1, 1);
    Assert.True(AbilityEntityEffectRules.ShouldExpireAtOwnerTurnStart(seal, NetworkTeam.Red));
    Assert.False(AbilityEntityEffectRules.ShouldExpireAtOwnerTurnStart(seal, NetworkTeam.Blue));
  }



  [Fact]
  public void BuilderPendingSelectionsAccumulateAndSwitchingModeResetsThem()
  {
    UnitAbilityState state = AdvancedAbilityRules.AddPendingSelection(
      new UnitAbilityState(), "StoneWall", new AbilitySelection(null, 1, 2));
    state = AdvancedAbilityRules.AddPendingSelection(
      state, "StoneWall", new AbilitySelection(null, 2, 2));

    Assert.Equal("StoneWall", state.PendingAbility);
    Assert.Equal(2, state.PendingSelections.Count);

    state = AdvancedAbilityRules.AddPendingSelection(
      state, "Gatehouse", new AbilitySelection(null, 3, 2));

    Assert.Equal("Gatehouse", state.PendingAbility);
    AbilitySelection selection = Assert.Single(state.PendingSelections);
    Assert.Equal((3, 2), (selection.X, selection.Y));

    state = AdvancedAbilityRules.ClearPendingSelections(state);
    Assert.Null(state.PendingAbility);
    Assert.Empty(state.PendingSelections);
  }



  [Fact]
  public void DuelistAttackRecordsExactlyOneMarkedTarget()
  {
    UnitAbilityState state = AdvancedAbilityRules.RecordAttack(
      nameof(PieceType.Duelist), new UnitAbilityState(), "first");

    Assert.Equal("first", state.SelectedTargetId);

    state = AdvancedAbilityRules.RecordAttack(nameof(PieceType.Duelist), state, "second");
    Assert.Equal("second", state.SelectedTargetId);
  }

  [Fact]
  public void PetrifiedUnitsCannotTakeDirectAttackDamageAndRangeBreaksBeyondFiveTiles()
  {
    UnitAbilityState petrified = AdvancedAbilityRules.SetPetrified(new UnitAbilityState(), "medusa");

    Assert.False(AdvancedAbilityRules.CanTakeDirectDamage(nameof(PieceType.Swordsman), petrified));
    Assert.True(AdvancedAbilityRules.IsPetrificationMaintained(
      UnitRules.GetRequired(nameof(PieceType.Medusa)), (0, 0),
      UnitRules.GetRequired(nameof(PieceType.Swordsman)), (5, 0)));
    Assert.False(AdvancedAbilityRules.IsPetrificationMaintained(
      UnitRules.GetRequired(nameof(PieceType.Medusa)), (0, 0),
      UnitRules.GetRequired(nameof(PieceType.Swordsman)), (6, 0)));
  }

  [Fact]
  public void MissileSiloIsConsumedByItsFirstAttack()
  {
    UnitAbilityState state = AdvancedAbilityRules.RecordAttack(
      nameof(PieceType.MissileSilo), new UnitAbilityState(), null);

    Assert.True(state.Consumed);
    Assert.False(AdvancedAbilityRules.CanAttack(
      nameof(PieceType.MissileSilo), state, legacyHasAttacked: false));
  }



  [Fact]
  public void LinkedPortalLookupFindsItsPairedPortal()
  {
    AbilityEntity first = new(
      "first", AbilityEntityKind.Portal, NetworkTeam.Red, 1, 1, 0, "second");
    AbilityEntity second = new(
      "second", AbilityEntityKind.Portal, NetworkTeam.Red, 5, 5, 0, "first");

    Assert.Equal(second, AbilityEntityRules.GetLinkedPortal([first, second], first));
    Assert.Equal(first, AbilityEntityRules.GetLinkedPortal([first, second], second));
  }

  [Fact]
  public void CommandCentreUpgradeCanOnlyBeAppliedOnce()
  {
    UnitAbilityState upgraded = AdvancedAbilityRules.ApplyCommandCentreUpgrade(
      new UnitAbilityState(), "attack");
    UnitAbilityState second = AdvancedAbilityRules.ApplyCommandCentreUpgrade(
      upgraded, "move");

    Assert.True(upgraded.Upgraded);
    Assert.Equal(AdvancedAbilityRules.CommandCentreAttackBonus, upgraded.AttackBonus);
    Assert.Equal(upgraded, second);
  }



  [Fact]
  public void MimicUsesItsSwapAsMovementInsteadOfNormalMovement()
  {
    UnitAbilityState state = new();

    Assert.False(AdvancedAbilityRules.CanMove(
      nameof(PieceType.Mimic), state, legacyHasMoved: false));
    Assert.True(AdvancedAbilityRules.CanUseMovementAbility(
      state, legacyHasMoved: false));

    state = AdvancedAbilityRules.RecordMove(state);
    Assert.False(AdvancedAbilityRules.CanUseMovementAbility(
      state, legacyHasMoved: false));
  }

  [Fact]
  public void LandingAttackUnitsUseTheirCodexPushAndAttackConsumptionRules()
  {
    Assert.True(AdvancedAbilityRules.IsLandingAttackUnit(nameof(PieceType.Buffalo)));
    Assert.True(AdvancedAbilityRules.IsLandingAttackUnit(nameof(PieceType.ArmouredTruck)));
    Assert.Equal(1, AdvancedAbilityRules.GetLandingAttackPushDistance(nameof(PieceType.Buffalo)));
    Assert.Equal(2, AdvancedAbilityRules.GetLandingAttackPushDistance(nameof(PieceType.ArmouredTruck)));
    Assert.True(AdvancedAbilityRules.LandingAttackConsumesNormalAttack(nameof(PieceType.Buffalo)));
    Assert.False(AdvancedAbilityRules.LandingAttackConsumesNormalAttack(nameof(PieceType.ArmouredTruck)));
  }


}
