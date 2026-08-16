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

    Assert.Equal(2, pegasus.MinimumMoveRange);
    Assert.Equal(4, pegasus.MoveRange);
    Assert.False(UnitRules.CanMove(pegasus, 0, 0, 1, 0));
    Assert.True(UnitRules.CanMove(pegasus, 0, 0, 2, 0));
    Assert.True(UnitRules.CanMove(pegasus, 0, 0, 2, 2));
    Assert.False(UnitRules.CanMove(pegasus, 0, 0, 4, 1));

    Assert.Equal(4, sleipnir.MinimumMoveRange);
    Assert.Equal(6, sleipnir.MoveRange);
  }

  [Fact]
  public void CanonicalPacksAndNewUnitsAreRegistered()
  {
    Assert.Contains(Pack.Norse, PackRules.All);
    Assert.Contains(Pack.WildWest, PackRules.All);
    Assert.Contains(Pack.Medival, PackRules.All);
    Assert.Contains(PieceDefinitions.Swordsman, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Atlas, PieceDefinitions.Royals);
    Assert.Contains(PieceDefinitions.Stagecoach, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Viking, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Cowboy, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Vampire, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Artemis, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.President, PieceDefinitions.Royals);
    Assert.Contains(PieceDefinitions.Orc, PieceDefinitions.Purchasable);
    Assert.Contains(Pack.AngelsDemons, PackRules.All);
    Assert.Contains(Pack.Chess, PackRules.All);
    Assert.Contains(PieceDefinitions.Giant, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Cyclops, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.Herald, PieceDefinitions.Royals);
    Assert.Contains(PieceDefinitions.ChessKing, PieceDefinitions.Royals);
    Assert.Contains(UnitRules.Purchasable, rule => rule.Type == nameof(PieceType.Orc));
  }

  [Fact]
  public void WorkbookStatsAreUsedForRepresentativeUnits()
  {
    Assert.Equal(20, PieceDefinitions.Swordsman.Attack);
    Assert.Equal(30, PieceDefinitions.Swordsman.Health);
    Assert.Equal(35, PieceDefinitions.Swordsman.Cost);

    Assert.Equal(Shape.Circle, PieceDefinitions.Archer.Movement.Shape);
    Assert.Equal(new AttackRange(2, 3), PieceDefinitions.Archer.AttackRange);

    Assert.Equal(4, PieceDefinitions.Ox.Movement.Maximum);
    Assert.Equal((1, 1), PieceDefinitions.Ox.Size);
    Assert.Equal((2, 2), PieceDefinitions.Elephant.Size);
    Assert.Equal(2, PieceDefinitions.Emperor.Movement.Maximum);
    Assert.Equal(0, PieceDefinitions.TerracottaWarrior.Attack);
    Assert.Equal(0, PieceDefinitions.TerracottaWarrior.Movement.Maximum);

    Assert.Equal(110, PieceDefinitions.Chimera.Cost);
    Assert.Equal(new MovementDefinition(2, 4, Shape.Circle), PieceDefinitions.Pegasus.Movement);
    Assert.Equal(new AttackRange(2, 3), PieceDefinitions.Artemis.AttackRange);
    Assert.Equal(new AttackRange(3, 4), PieceDefinitions.Gunman.AttackRange);
    Assert.Equal(new AttackRange(2, 4), PieceDefinitions.Tank.AttackRange);

    Assert.Equal(10, Globals.FarmIncomePerTurn);
    Assert.Equal(25, AbilityRules.BombardSplashDamage);
    Assert.Equal(40, AbilityRules.EngineerBarrierHealth);
    Assert.Equal(25, AbilityRules.MercenaryPayroll);

    Assert.Equal(20, PieceDefinitions.Giant.Attack);
    Assert.Equal(70, PieceDefinitions.Giant.Health);
    Assert.Equal((2, 2), PieceDefinitions.Giant.Size);
    Assert.Equal(new AttackRange(1, 1), PieceDefinitions.Giant.AttackRange);
    Assert.Equal(115, PieceDefinitions.Giant.Cost);
    Assert.Equal(35, PieceDefinitions.Cyclops.Attack);
    Assert.Equal(85, PieceDefinitions.Cyclops.Health);
    Assert.Equal(125, PieceDefinitions.Cyclops.Cost);
    Assert.Equal(RuleShape.ChessKnight, UnitRules.GetRequired(nameof(PieceType.ChessKnight)).MovePattern);
    Assert.Equal(RuleShape.ForwardDiagonal, UnitRules.GetRequired(nameof(PieceType.Pawn)).AttackPattern);
  }

  [Fact]
  public void SharedAbilityHelpersMatchWorkbookRules()
  {
    Assert.Equal(3, AbilityRules.MaximumAttacksPerTurn(nameof(PieceType.Ninja)));
    Assert.Equal(1, AbilityRules.MaximumAttacksPerTurn(nameof(PieceType.Swordsman)));

    UnitRule berserker = UnitRules.GetRequired(nameof(PieceType.Beserker));
    Assert.Equal(20, AbilityRules.GetBaseAttack(berserker, 40));
    Assert.Equal(40, AbilityRules.GetBaseAttack(berserker, 20));

    UnitRule sleipnir = UnitRules.GetRequired(nameof(PieceType.Sleipnir));
    UnitRule artemis = UnitRules.GetRequired(nameof(PieceType.Artemis));
    Assert.True(AbilityRules.IsTerrainImmune(sleipnir));
    Assert.True(AbilityRules.CanTravelThroughUnits(sleipnir));
    Assert.True(AbilityRules.AttacksThroughForests(artemis));
  }
}
