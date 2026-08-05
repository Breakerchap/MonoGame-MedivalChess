namespace MedivalChess.Player;

using MedivalChess.GameBoard;
using MedivalChess.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class Team
{
  internal const int ActionsPerTurn = MatchRules.ActionsPerTurn;

  internal TeamName TeamName { get; }
  internal PieceType? ChosenRoyal { get; private set; }
  internal int Money { get; set; }
  internal int ActionPoints { get; set; }
  internal int ActionLimit { get; }

  internal Team(TeamName teamName, PieceType? chosenRoyal, int? money = null, int? actionLimit = null)
  {
    TeamName = teamName;
    ChosenRoyal = chosenRoyal;
    Money = money ?? Globals.StartingCash;
    ActionLimit = Math.Max(1, actionLimit ?? ActionsPerTurn);
    ActionPoints = ActionLimit;
  }

  private static IReadOnlyList<TeamName> _turnOrder = [TeamName.Red, TeamName.Blue];

  internal static TeamName CurrentTurn { get; private set; } = TeamName.Red;
  internal static IReadOnlyList<TeamName> ActiveTeams => _turnOrder;

  internal static void ConfigureTurnOrder(IEnumerable<TeamName> teams)
  {
    TeamName[] order = teams.Distinct().Where(team => team != TeamName.Neutral).ToArray();
    if (order.Length is < 2 or > 4)
    {
      throw new ArgumentOutOfRangeException(nameof(teams), "A match needs two to four active teams.");
    }

    _turnOrder = order;
    CurrentTurn = _turnOrder[0];
  }

  internal static void AdvanceTurn()
  {
    int index = 0;
    for (int candidate = 0; candidate < _turnOrder.Count; candidate++)
    {
      if (_turnOrder[candidate] == CurrentTurn)
      {
        index = candidate;
        break;
      }
    }
    CurrentTurn = _turnOrder[(Math.Max(0, index) + 1) % _turnOrder.Count];
  }

  internal static void ResetTurn()
  {
    CurrentTurn = _turnOrder[0];
  }

  internal static void SetCurrentTurn(TeamName teamName)
  {
    CurrentTurn = teamName;
  }

  internal void ChooseRoyal(PieceType royal)
  {
    ChosenRoyal = royal;
  }

  internal void ClearRoyal()
  {
    ChosenRoyal = null;
  }

  internal bool SpendAction()
  {
    ActionPoints--;

    if (ActionPoints > 0)
    {
      return false;
    }

    ActionPoints = ActionLimit;
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
  Red, Blue, Green, Yellow, Neutral
}

internal static class TeamNameExtensions
{
  internal static NetworkTeam ToNetworkTeam(this TeamName team) => team switch
  {
    TeamName.Red => NetworkTeam.Red,
    TeamName.Blue => NetworkTeam.Blue,
    TeamName.Green => NetworkTeam.Green,
    TeamName.Yellow => NetworkTeam.Yellow,
    TeamName.Neutral => NetworkTeam.Neutral,
    _ => throw new ArgumentOutOfRangeException(nameof(team))
  };

  internal static TeamName ToTeamName(this NetworkTeam team) => team switch
  {
    NetworkTeam.Red => TeamName.Red,
    NetworkTeam.Blue => TeamName.Blue,
    NetworkTeam.Green => TeamName.Green,
    NetworkTeam.Yellow => TeamName.Yellow,
    NetworkTeam.Neutral => TeamName.Neutral,
    _ => throw new ArgumentOutOfRangeException(nameof(team))
  };
}
