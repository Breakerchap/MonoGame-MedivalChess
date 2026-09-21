using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class DisplacementRulesTests
{
  [Fact]
  public void PushUsesFullDistanceWhenEveryStepIsLegal()
  {
    var result = DisplacementRules.GetFurthestLegalPositionAwayFrom(
      (0, 0), (1, 0), 2, _ => true);

    Assert.Equal((3, 0), result);
  }

  [Fact]
  public void PushStopsOnLastLegalSquareBeforeBlockedDestination()
  {
    var result = DisplacementRules.GetFurthestLegalPositionAwayFrom(
      (0, 0), (1, 0), 2, position => position != (3, 0));

    Assert.Equal((2, 0), result);
  }

  [Fact]
  public void PushDoesNotMoveWhenFirstStepIsIllegal()
  {
    var result = DisplacementRules.GetFurthestLegalPositionAwayFrom(
      (0, 0), (1, 0), 2, _ => false);

    Assert.Equal((1, 0), result);
  }

  [Fact]
  public void PushPreservesDiagonalDirection()
  {
    var result = DisplacementRules.GetFurthestLegalPositionAwayFrom(
      (0, 0), (1, 1), 2, _ => true);

    Assert.Equal((3, 3), result);
  }

  [Fact]
  public void BeelzebubCannotBePushed()
  {
    Assert.False(DisplacementRules.CanBePushed(nameof(PieceType.Beelzebub)));
    Assert.True(DisplacementRules.CanBePushed(nameof(PieceType.Swordsman)));
  }
}
