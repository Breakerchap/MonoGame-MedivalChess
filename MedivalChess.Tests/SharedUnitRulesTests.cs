using MedivalChess.GameBoard;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class SharedUnitRulesTests
{
  [Fact]
  public void FarmsAreAvailableRegardlessOfTheSelectedUnitPack()
  {
    foreach (Pack pack in PackRules.All)
    {
      string[] selectedPack = [pack.ToString()];

      Assert.True(PackRules.IsAllowed(PieceDefinitions.Farm, selectedPack));
      Assert.True(PackRules.IsAllowed("Farm", selectedPack));
    }
  }

  [Fact]
  public void SharedMatchRules_PlaceDominionPointsAndTreasureOnTheBoardCentre()
  {
    Board board = new("board_medium.json");

    IReadOnlyList<(int x, int y)> points = MatchRules.GetDominionControlPoints(board);
    (int x, int y) treasure = MatchRules.GetTreasureSpawn(board);

    Assert.Equal(3, points.Count);
    Assert.Equal(3, points.Distinct().Count());
    Assert.All(points, point =>
    {
      Assert.True(board.ContainsCell(point));
      Assert.Null(MatchRules.GetSquareOwner(board, "Dominion", point));
      Assert.Null(MatchRules.GetSquareOwner(board, "Dominion", point, 4));
    });
    Assert.True(board.ContainsCell(treasure));
    Assert.Null(MatchRules.GetSquareOwner(board, "Plunder", treasure));
  }

  [Fact]
  public void PlunderNoMansLandExtendsTwoTilesBeyondTheDefault()
  {
    Assert.Equal(
      MatchRules.DefaultNoMansLandHalfHeight + MatchRules.PlunderNoMansLandExtraHalfHeight,
      MatchRules.GetNoMansLandHalfHeight("Plunder")
    );
  }

  [Fact]
  public void Teacher_IsNotPartOfTheSharedOrClientRoster()
  {
    Assert.False(UnitRules.TryGet("Teacher", out _));
    Assert.DoesNotContain(UnitRules.All, rule => rule.Type == "Teacher");
    Assert.DoesNotContain(PieceDefinitions.All, definition => definition.Type.ToString() == "Teacher");
    Assert.DoesNotContain(PieceDefinitions.Purchasable, definition => definition.Type.ToString() == "Teacher");
  }

  [Fact]
  public void ClientPieceDefinitionsMatchTheAuthoritativeOnlineRules()
  {
    foreach (PieceDefinition definition in PieceDefinitions.Encyclopedia)
    {
      UnitRule sharedRule = UnitRules.GetRequired(definition.Type.ToString());

      Assert.Equal(definition.Category.ToString(), sharedRule.Category.ToString());
      Assert.Equal(definition.Movement.range, sharedRule.MoveRange);
      Assert.Equal(definition.Movement.shape.ToString(), sharedRule.MovePattern.ToString());
      Assert.Equal(definition.Attack, sharedRule.Attack);
      Assert.Equal(definition.Health, sharedRule.Health);
      Assert.Equal(definition.Size.x, sharedRule.Width);
      Assert.Equal(definition.Size.y, sharedRule.Height);
      Assert.Equal(definition.AttackShape.range, sharedRule.AttackRange);
      Assert.Equal(definition.AttackShape.shape.ToString(), sharedRule.AttackPattern.ToString());
      Assert.Equal(definition.Cost, sharedRule.Cost);
      Assert.Equal(definition.MinimumAttackRange, sharedRule.MinimumAttackRange);
      Assert.Equal(definition.AbilityDescription, sharedRule.AbilityDescription);
    }

    Assert.Equal(
      PieceDefinitions.Encyclopedia.Select(definition => definition.Type.ToString()),
      UnitRules.All.Select(rule => rule.Type)
    );
  }

  [Fact]
  public void SharedRules_KeepLargeUnitFootprintsWithoutScalingMovement()
  {
    UnitRule cannon = UnitRules.GetRequired("Cannon");
    UnitRule elephant = UnitRules.GetRequired("Elephant");

    Assert.Equal((1, 2), (cannon.Width, cannon.Height));
    Assert.False(UnitRules.CanMove(cannon, 0, 0, 0, 4));
    Assert.True(UnitRules.CanMove(cannon, 0, 0, 2, 0));
    Assert.False(UnitRules.CanMove(cannon, 0, 0, 3, 0));
    Assert.Equal((2, 2), (elephant.Width, elephant.Height));
    Assert.True(UnitRules.CanMove(elephant, 0, 0, 3, 0));
    Assert.False(UnitRules.CanMove(elephant, 0, 0, 4, 0));
    Assert.True(UnitRules.FootprintsOverlap(0, 0, 2, 2, 1, 1, 1, 1));
    Assert.False(UnitRules.FootprintsOverlap(0, 0, 2, 2, 2, 0, 1, 1));
  }

  [Fact]
  public void SharedRules_ApplyTheSameAttackGeometryToLargeUnits()
  {
    UnitRule archer = UnitRules.GetRequired("Archer");
    UnitRule soldier = UnitRules.GetRequired("Soldier");
    UnitRule ballista = UnitRules.GetRequired("Ballista");

    Assert.False(UnitRules.CanAttack(archer, 0, 0, NetworkTeam.Red, soldier, 1, 0));
    Assert.True(UnitRules.CanAttack(archer, 0, 0, NetworkTeam.Red, soldier, 2, 0));
    Assert.True(UnitRules.CanAttack(ballista, 0, 0, NetworkTeam.Red, soldier, 0, 3));
    Assert.False(UnitRules.CanAttack(ballista, 0, 0, NetworkTeam.Red, soldier, 2, 2));
  }

  [Fact]
  public void SharedRules_DefineCatapultAndAbilityMetadata()
  {
    UnitRule catapult = UnitRules.GetRequired("Catapult");
    UnitRule bombard = UnitRules.GetRequired("Bombard");

    Assert.Equal(RuleShape.Circle, catapult.AttackPattern);
    Assert.Equal("Attacks over terrain and pieces.", UnitRules.GetAbilityDescription("Catapult"));
    Assert.Equal((2, 3), (bombard.MinimumAttackRange, bombard.AttackRange));
    Assert.Equal(new AttackRange(2, 3), bombard.AllowedAttackRange);
    Assert.Contains("barricade", UnitRules.GetAbilityDescription("Engineer"));
    Assert.Contains("Ignores terrain", UnitRules.GetAbilityDescription("Elephant"));
    Assert.True(AbilityRules.IsEngineerDemolition("Demolish"));
  }

  [Fact]
  public void SharedAbilityRules_KeepGuardAndOxAttachmentRestrictionsConsistent()
  {
    UnitRule guard = UnitRules.GetRequired("Guard");
    UnitRule soldier = UnitRules.GetRequired("Soldier");
    UnitRule king = UnitRules.GetRequired("King");
    UnitRule ox = UnitRules.GetRequired("Ox");
    UnitRule cannon = UnitRules.GetRequired("Cannon");

    Assert.True(AbilityRules.CanGuardAttach(guard, soldier, false, false));
    Assert.False(AbilityRules.CanGuardAttach(guard, king, false, false));
    Assert.False(AbilityRules.CanGuardAttach(guard, soldier, true, false));
    Assert.True(AbilityRules.CanOxAttach(ox, soldier, false, false));
    Assert.False(AbilityRules.CanOxAttach(ox, cannon, false, false));
    Assert.False(AbilityRules.CanOxAttach(ox, cannon, false, true));
  }
}
