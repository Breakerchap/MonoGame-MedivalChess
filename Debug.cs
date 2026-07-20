namespace MedivalChess;

using System.Collections.Generic;
using MedivalChess.GameBoard;
using MedivalChess.Player;

public class PieceSetup
{
  public List<Piece> pieces = new();

  public void AddPieces()
  {
    Piece piece1 = new Piece(PieceDefinitions.Soldier, (0, 0), TeamName.Red);
    Piece piece2 = new Piece(PieceDefinitions.Archer, (2, 2), TeamName.Blue);

    pieces.Add(piece1);
    pieces.Add(piece2);
  }

  public Piece GetPieceAt((int x, int y) position)
  {
    return pieces.Find(piece => piece.Position == position);
  }

  public List<Team> CreateTeams()
  {
    List<Team> teams = new();
    TeamName currentTeam = TeamName.Red;

    while (teams.Count < 2)
    {
      PieceType? chosenRoyal = null;

      foreach (Piece piece in pieces)
      {
        if (piece.Definition.Category == PieceCategory.Royal && piece.Team == currentTeam)
        {
          chosenRoyal = piece.Definition.Type;
          break;
        }
      }
      currentTeam = TeamName.Blue;
      teams.Add(new Team(currentTeam, chosenRoyal));

    }
    return teams;
  }
}
