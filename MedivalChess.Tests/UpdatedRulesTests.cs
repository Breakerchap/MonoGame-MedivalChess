using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public class UpdatedRulesTests
{
  [Fact]
  public void GuardAndTransportAttachments_MoveWithTheirHostAndDetachOnDeath()
  {
    PieceSetup setup = new();
    Piece soldier = new(PieceDefinitions.Soldier, (4, 4), TeamName.Red);
    Piece guard = new(PieceDefinitions.Guard, (4, 3), TeamName.Red);
    Piece secondGuard = new(PieceDefinitions.Guard, (3, 4), TeamName.Red);
    Piece ox = new(PieceDefinitions.Ox, (8, 4), TeamName.Red);
    Piece cannon = new(PieceDefinitions.Cannon, (8, 5), TeamName.Red);
    setup.AddPiece(soldier);
    setup.AddPiece(guard);
    setup.AddPiece(secondGuard);
    setup.AddPiece(ox);
    setup.AddPiece(cannon);

    Assert.True(setup.Attach(guard, soldier, AttachmentKind.Guard));
    Assert.False(setup.Attach(secondGuard, soldier, AttachmentKind.Guard));
    Assert.False(setup.Attach(secondGuard, secondGuard, AttachmentKind.Guard));
    Assert.True(setup.Attach(cannon, ox, AttachmentKind.Carried));
    setup.MovePiece(soldier, (5, 4));
    setup.MovePiece(ox, (9, 4));

    Assert.Equal((5, 4), guard.Position);
    Assert.Same(soldier, setup.GetPieceAt((5, 4)));
    Assert.Equal((9, 4), cannon.Position);

    setup.RemovePiece(guard);
    Assert.Null(guard.AttachedTo);
    Assert.Same(soldier, setup.GetPieceAt((5, 4)));
  }

  [Fact]
  public void Ox_CanHaveOnlyOneCarriedUnit()
  {
    PieceSetup setup = new();
    Piece ox = new(PieceDefinitions.Ox, (4, 4), TeamName.Red);
    Piece soldier = new(PieceDefinitions.Soldier, (4, 3), TeamName.Red);
    Piece knight = new(PieceDefinitions.Knight, (3, 4), TeamName.Red);
    setup.AddPiece(ox);
    setup.AddPiece(soldier);
    setup.AddPiece(knight);

    Assert.True(setup.Attach(soldier, ox, AttachmentKind.Carried));
    Assert.False(setup.Attach(knight, ox, AttachmentKind.Carried));
    Assert.Same(ox, soldier.AttachedTo);
    Assert.Null(knight.AttachedTo);

    setup.Detach(soldier);
    Assert.True(setup.Attach(knight, ox, AttachmentKind.Carried));
  }
}
