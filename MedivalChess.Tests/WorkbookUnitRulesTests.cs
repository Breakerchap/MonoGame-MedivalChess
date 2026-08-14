using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class WorkbookUnitRulesTests
{
  [Fact]
  public void CircleUnitsUseEuclideanGeometryAndMinimumRanges()
  {
    UnitRule archer = UnitRules.GetRequired(nameof(PieceType.Archer));
    UnitRule pegasus = UnitRules.GetRequired(nameof(PieceType.Pegasus));
    UnitRule sleipnir = UnitRules.GetRequired(nameof(PieceType.Sleipnir));

    Assert.Equal(RuleShape.Circle, archer.MovePattern);
    Assert.Equal(RuleShape.Circle, archer.AttackPattern);
    Assert.Equal(2, archer.MinimumAttackRange);
    Assert.Equal(3, archer.AttackRange);

    Assert.Equal(4, pegasus.MinimumMoveRange);
    Assert.Equal(6, pegasus.MoveRange);
    Assert.True(UnitRules.CanMove(pegasus, 0, 0, 4, 4));
    Assert.False(UnitRules.CanMove(pegasus, 0, 0, 5, 4));

    Assert.Equal(4, sleipnir.MinimumMoveRange);
    Assert.Equal(6, sleipnir.MoveRange);
  }

  [Fact]
  public void NonChessPacksAndNewUnitsAreRegistered()
  {
    Assert.Contains(Pack.Norse, PackRules.All);
    Assert.Contains(Pack.WildWest, PackRules.All);
    Assert.Contains(PieceDefinitions.Viking, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Cowboy, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Vampire, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Artemis, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.President, PieceDefinitions.Royals);
    Assert.DoesNotContain(PieceDefinitions.Orc, PieceDefinitions.Purchasable);
  }

  [Fact]
  public void WorkbookStatsAreUsedForRepresentativeUnits()
  {
    Assert.Equal(20, PieceDefinitions.Soldier.Attack);
    Assert.Equal(30, PieceDefinitions.Soldier.Health);
    Assert.Equal(40, PieceDefinitions.Soldier.Cost);

    Assert.Equal(20, PieceDefinitions.Archer.Attack);
    Assert.Equal(20, PieceDefinitions.Archer.Health);
    Assert.Equal(50, PieceDefinitions.Archer.Cost);

    Assert.Equal(10, Globals.FarmIncomePerTurn);
    Assert.Equal(20, AbilityRules.BombardSplashDamage);
    Assert.Equal(40, AbilityRules.EngineerBarrierHealth);
    Assert.Equal(20, AbilityRules.MercenaryPayroll);
  }

  [Fact]
  public void ChessDefinitionsRemainAtTheirExistingValues()
  {
    Assert.Equal(60, PieceDefinitions.Pawn.Attack);
    Assert.Equal(5, PieceDefinitions.Pawn.Health);
    Assert.Equal(Shape.Forward, PieceDefinitions.Pawn.Movement.Shape);
    Assert.Equal(Shape.MoveOnEnemy, PieceDefinitions.Pawn.AttackPattern);
  }

  [Fact]
  public void SharedAbilityHelpersMatchWorkbookRules()
  {
    Assert.Equal(3, AbilityRules.MaximumAttacksPerTurn(nameof(PieceType.Ninja)));
    Assert.Equal(1, AbilityRules.MaximumAttacksPerTurn(nameof(PieceType.Soldier)));

    UnitRule berserker = UnitRules.GetRequired(nameof(PieceType.Berserker));
    Assert.Equal(20, AbilityRules.GetBaseAttack(berserker, 40));
    Assert.Equal(40, AbilityRules.GetBaseAttack(berserker, 20));

    UnitRule sleipnir = UnitRules.GetRequired(nameof(PieceType.Sleipnir));
    UnitRule artemis = UnitRules.GetRequired(nameof(PieceType.Artemis));
    Assert.True(AbilityRules.IsTerrainImmune(sleipnir));
    Assert.True(AbilityRules.CanTravelThroughUnits(sleipnir));
    Assert.True(AbilityRules.AttacksThroughForests(artemis));
  }
}
