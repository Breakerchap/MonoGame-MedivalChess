namespace MedivalChess;

using System.Collections.Generic;
using MedivalChess.GameBoard;
using MedivalChess.Player;

internal sealed class PieceSetup
{
  private readonly List<Piece> _pieces = new();

  internal IReadOnlyList<Piece> Pieces => _pieces;

  internal void AddPieces()
  {
    Piece piece1 = new Piece(PieceDefinitions.Soldier, (0, 0), TeamName.Red);
    Piece piece2 = new Piece(PieceDefinitions.Archer, (2, 2), TeamName.Blue);

    _pieces.Add(piece1);
    _pieces.Add(piece2);
  }

  internal Piece GetPieceAt((int x, int y) position)
  {
    return _pieces.Find(piece => piece.Position == position);
  }

  internal bool RemovePiece(Piece piece)
  {
    return _pieces.Remove(piece);
  }

  internal void AddPiece(Piece piece)
  {
    _pieces.Add(piece);
  }

  internal List<Team> CreateTeams()
  {
    List<Team> teams = new();
    TeamName currentTeam = TeamName.Red;

    while (teams.Count < 2)
    {
      PieceType? chosenRoyal = null;

      foreach (Piece piece in _pieces)
      {
        if (piece.Definition.Category == PieceCategory.Royal && piece.Team == currentTeam)
        {
          chosenRoyal = piece.Definition.Type;
          break;
        }
      }
      teams.Add(new Team(currentTeam, chosenRoyal));
      currentTeam = TeamName.Blue;

    }
    return teams;
  }
}
