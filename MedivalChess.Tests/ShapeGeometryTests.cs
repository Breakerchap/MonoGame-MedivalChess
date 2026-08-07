using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class ShapeGeometryTests
{
  [Fact]
  public void StraightRange_UsesTaxicabDistanceForMovementAndAttacks()
  {
    UnitRule straight = CreateRule(RuleShape.Straight, 3);
    Piece soldier = new(PieceDefinitions.Soldier, (0, 0), TeamName.Red);
    Piece bombard = new(PieceDefinitions.Bombard, (5, 5), TeamName.Red);

    Assert.True(UnitRules.CanMove(straight, 0, 0, 2, 1));
    Assert.False(UnitRules.CanMove(straight, 0, 0, 2, 2));
    Assert.True(UnitRules.CanAttackOffset(RuleShape.Straight, 1, 3, NetworkTeam.Red, 2, 1));
    Assert.False(UnitRules.CanAttackOffset(RuleShape.Straight, 1, 3, NetworkTeam.Red, 2, 2));
    Assert.True(Actions.IsValidMovementDestination(soldier, (2, 1)));
    Assert.False(Actions.IsValidMovementDestination(soldier, (2, 2)));
    Assert.True(Actions.CanAttackSquare(bombard, (6, 6)));
    Assert.False(Actions.CanAttackSquare(bombard, (7, 7)));
  }

  [Fact]
  public void AnyRange_UsesChessboardDistance()
  {
    UnitRule any = CreateRule(RuleShape.Any, 3);

    Assert.True(UnitRules.CanMove(any, 0, 0, 3, 3));
    Assert.False(UnitRules.CanMove(any, 0, 0, 4, 0));
    Assert.True(UnitRules.CanAttackOffset(RuleShape.Any, 1, 3, NetworkTeam.Red, 3, 3));
    Assert.False(UnitRules.CanAttackOffset(RuleShape.Any, 1, 3, NetworkTeam.Red, 4, 0));
  }

  [Fact]
  public void LineMovement_RemainsOnOneRayFromItsOrigin()
  {
    UnitRule line = CreateRule(RuleShape.Line, 3);

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      line,
      (0, 0),
      NetworkTeam.Red,
      _ => true,
      (_, _) => true,
      _ => 1,
      (_, _) => false
    );

    Assert.Equal([(1, 0), (2, 0), (3, 0)], paths[(3, 0)]);
    Assert.False(paths.ContainsKey((2, 1)));
    Assert.False(UnitRules.CanMove(line, 0, 0, 2, 1));
  }

  private static UnitRule CreateRule(RuleShape shape, int range) => new(
    "GeometryTest",
    RuleCategory.Melee,
    range,
    shape,
    1,
    1,
    1,
    1,
    range,
    shape,
    1
  );
}
