namespace MedivalChess.Player;

using MedivalChess.GameBoard;
using System;

internal sealed class Team
{
  internal const int ActionsPerTurn = 3;

  internal TeamName TeamName { get; }
  internal PieceType? ChosenRoyal { get; private set; }
  internal int Money { get; set; }
  internal int ActionPoints { get; set; }

  internal Team(TeamName teamName, PieceType? chosenRoyal, int? money = null)
  {
    TeamName = teamName;
    ChosenRoyal = chosenRoyal;
    Money = money ?? Globals.StartingCash;
    ActionPoints = ActionsPerTurn;
  }

  internal static TeamName CurrentTurn { get; private set; } = TeamName.Red;

  internal static void AdvanceTurn()
  {
    CurrentTurn = CurrentTurn == TeamName.Red ? TeamName.Blue : TeamName.Red;
  }

  internal static void ResetTurn()
  {
    CurrentTurn = TeamName.Red;
  }

  internal static void SetCurrentTurn(TeamName teamName)
  {
    CurrentTurn = teamName;
  }

  internal void ChooseRoyal(PieceType royal)
  {
    ChosenRoyal = royal;
  }

  internal bool SpendAction()
  {
    ActionPoints--;

    if (ActionPoints > 0)
    {
      return false;
    }

    ActionPoints = ActionsPerTurn;
    return true;
  }

  internal static Piece BuyPiece(PieceDefinition piece, Team team, (int x, int y) position)
  {
    if (team.Money < piece.Cost) { Console.WriteLine("Cannot Afford Piece"); return null; }

    team.Money -= piece.Cost;
    return new Piece(piece, position, team.TeamName);
  }
}

internal enum TeamName
{
  Red, Blue, Neutral
}
