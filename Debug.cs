namespace MedivalChess;

using System.Collections.Generic;
using MedivalChess.GameBoard;

public class PieceSetup{
  public List<Piece> pieces = new();
  public Piece piece1 = new Piece(PieceDefinitions.Soldier, (-3, -14));

  public void AddPieces()
  {
    pieces.Add(piece1);
  }
}
