using MedivalChess.GameBoard;
using MedivalChess.Player;
using Xunit;

namespace MedivalChess.Tests;

public class PieceFootprintTests
{
  [Fact]
  public void Piece_OccupiesEverySquareInItsDefinedFootprint()
  {
    Piece catapult = new(PieceDefinitions.Catapult, (4, 7), TeamName.Red);

    Assert.True(catapult.Occupies((4, 7)));
    Assert.True(catapult.Occupies((5, 7)));
    Assert.True(catapult.Occupies((4, 8)));
    Assert.True(catapult.Occupies((5, 8)));
    Assert.False(catapult.Occupies((6, 8)));
  }

  [Fact]
  public void PieceSetup_UsesTheFullFootprintForSelectionAndPlacement()
  {
    PieceSetup setup = new();
    Piece cannon = new(PieceDefinitions.Cannon, (2, 3), TeamName.Blue);
    setup.AddPiece(cannon);

    Assert.Same(cannon, setup.GetPieceAt((2, 4)));
    Assert.False(setup.IsFootprintClear(PieceDefinitions.Soldier, (2, 4)));
    Assert.True(setup.IsFootprintClear(PieceDefinitions.Cannon, (2, 3), cannon));
  }

  [Fact]
  public void MultiSquarePiece_MovesByItsAnchorAndAttacksFromEveryOccupiedSquare()
  {
    Piece catapult = new(PieceDefinitions.Catapult, (5, 5), TeamName.Red);
    Piece cannon = new(PieceDefinitions.Cannon, (5, 5), TeamName.Blue);

    Assert.True(Actions.IsValidMovementDestination(catapult, (4, 4)));
    Assert.True(Actions.IsValidMovementDestination(catapult, (6, 5)));
    Assert.False(Actions.IsValidMovementDestination(catapult, (7, 5)));

    Assert.True(Actions.CanAttackSquare(cannon, (5, 10)));
    Assert.False(Actions.CanAttackSquare(cannon, (5, 11)));
  }
}
