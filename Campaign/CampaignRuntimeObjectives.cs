#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

/// <summary>Evaluates campaign-only objectives against the normal live piece and team instances.</summary>
internal static class CampaignRuntimeObjectives
{
  internal static NetworkTeam? FindWinner(
    CampaignLevelDefinition level,
    IReadOnlyList<Piece> pieces,
    IReadOnlyList<Team> teams,
    int completedRounds
  )
  {
    foreach (CampaignTeamDefinition teamDefinition in level.Teams)
    {
      NetworkTeam team = teamDefinition.Team;
      List<CampaignObjectiveDefinition> victories = level.Scenario.VictoryConditions
        .Where(objective => objective.Team == team)
        .Where(IsRuntimeCampaignObjective)
        .ToList();
      if (victories.Count > 0 && victories.All(objective => IsComplete(objective, team, pieces, teams, completedRounds)))
      {
        return team;
      }

      // A loss condition describes the team that causes the loss. If the CPU completes
      // one, it wins; this keeps editor wording natural for both player and CPU authors.
      List<CampaignObjectiveDefinition> defeats = level.Scenario.DefeatConditions
        .Where(objective => objective.Team == team)
        .Where(IsRuntimeCampaignObjective)
        .ToList();
      if (defeats.Count > 0 && defeats.Any(objective => IsComplete(objective, team, pieces, teams, completedRounds)))
      {
        return team;
      }
    }

    return null;
  }

  private static bool IsRuntimeCampaignObjective(CampaignObjectiveDefinition objective) => objective.Type is
    CampaignObjectiveType.GetUnitsToLocations or
    CampaignObjectiveType.EscortUnit or
    CampaignObjectiveType.ReachCash or
    CampaignObjectiveType.SurviveTurns or
    CampaignObjectiveType.EliminateEnemies;

  private static bool IsComplete(
    CampaignObjectiveDefinition objective,
    NetworkTeam team,
    IReadOnlyList<Piece> pieces,
    IReadOnlyList<Team> teams,
    int completedRounds
  )
  {
    return objective.Type switch
    {
      CampaignObjectiveType.GetUnitsToLocations => objective.UnitLocationTargets is { Count: > 0 } targets &&
        targets.All(target => pieces.Any(piece => piece.NetworkId == target.UnitId &&
          piece.Position == (target.Location.X, target.Location.Y))),
      CampaignObjectiveType.EscortUnit => !string.IsNullOrWhiteSpace(objective.TargetUnitId) &&
        pieces.Any(piece => piece.NetworkId == objective.TargetUnitId &&
          (objective.Locations ?? []).Any(location => piece.Occupies((location.X, location.Y)))),
      CampaignObjectiveType.ReachCash => teams.FirstOrDefault(candidate => candidate.TeamName.ToNetworkTeam() == team)?.Money >= objective.RequiredAmount,
      CampaignObjectiveType.SurviveTurns => completedRounds >= objective.RequiredAmount &&
        pieces.Any(piece => piece.Team.ToNetworkTeam() == team),
      CampaignObjectiveType.EliminateEnemies => pieces.Where(piece => piece.Team != TeamName.Neutral)
        .All(piece => piece.Team.ToNetworkTeam() == team),
      _ => false
    };
  }
}
