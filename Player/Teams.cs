namespace MedivalChess.Player;

using MedivalChess.GameBoard;

public class Team
{
  public TeamName TeamName { get; }
  public PieceType? ChosenRoyal { get; }
  public int Money { get; set; }

  public Team(TeamName teamName, PieceType? chosenRoyal, int? money = null)
  {
    TeamName = teamName;
    ChosenRoyal = chosenRoyal;
    Money = money ?? Globals.startingCash;
  }

  public static TeamName currentTurn = TeamName.Red;

  public static void AdvanceTurn()
  {
    currentTurn = currentTurn == TeamName.Red ? TeamName.Blue : TeamName.Red;
  }
}

public enum TeamName
{
  Red, Blue
}