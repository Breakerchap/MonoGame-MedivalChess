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
  }

  internal Piece GetPieceAt((int x, int y) position)
  {
    return _pieces.Find(piece => piece.Occupies(position));
  }

  internal bool IsFootprintClear(
    PieceDefinition definition,
    (int x, int y) position,
    Piece ignoredPiece = null
  )
  {
    for (int y = 0; y < definition.Size.y; y++)
    {
      for (int x = 0; x < definition.Size.x; x++)
      {
        Piece occupiedPiece = GetPieceAt((position.x + x, position.y + y));
        if (occupiedPiece != null && occupiedPiece != ignoredPiece)
        {
          return false;
        }
      }
    }

    return true;
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
