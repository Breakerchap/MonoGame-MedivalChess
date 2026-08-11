#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

internal sealed class CampaignPlayableState
{
  internal required Board Board { get; init; }
  internal required BattlefieldTerrain Terrain { get; init; }
  internal required IReadOnlyList<Team> Teams { get; init; }
  internal required IReadOnlyList<Piece> Pieces { get; init; }
  internal required CampaignTerritoryMap Territories { get; init; }
  internal required NetworkTeam FirstTeam { get; init; }
  internal required string GameMode { get; init; }
  internal required IReadOnlySet<(int x, int y)> Roads { get; init; }
  internal required IReadOnlyDictionary<(int x, int y), int> Barricades { get; init; }
  internal required IReadOnlyDictionary<(int x, int y), TeamName> Mines { get; init; }
  internal required IReadOnlySet<TileEdge> RiverBridges { get; init; }
}

internal sealed class CampaignPlayableStateResult
{
  internal CampaignPlayableState? State { get; init; }
  internal IReadOnlyList<CampaignValidationProblem> Problems { get; init; } = [];
  internal bool IsSuccess => State is not null && Problems.All(problem => problem.Severity != CampaignValidationSeverity.Error);
}

/// <summary>Converts a validated editor definition into the desktop game's existing board, terrain, team, and piece models.</summary>
internal static class CampaignLevelConverter
{
  internal static CampaignPlayableStateResult CreatePlayableState(CampaignLevelDefinition level)
  {
    CampaignValidationResult validation = CampaignLevelValidator.Validate(level);
    if (!validation.IsValid)
    {
      return new CampaignPlayableStateResult { Problems = validation.Problems };
    }

    try
    {
      Board board = CampaignRuntimeFactory.CreateBoard(level.Board);
      BattlefieldTerrain terrain = new(
        level.Terrain.Where(tile => tile.Type == CampaignTerrainType.Forest).Select(tile => (tile.Position.X, tile.Position.Y)),
        level.Terrain.Where(tile => tile.Type == CampaignTerrainType.Lake).Select(tile => (tile.Position.X, tile.Position.Y)),
        level.Rivers.Select(river => TileEdge.Between(
          (river.First.X, river.First.Y),
          (river.Second.X, river.Second.Y)))
      );
      IReadOnlyList<Team> teams = level.Teams.Select(definition =>
      {
        PieceType? royal = !string.IsNullOrWhiteSpace(definition.ChosenRoyal) &&
          Enum.TryParse(definition.ChosenRoyal, ignoreCase: false, out PieceType parsedRoyal)
          ? parsedRoyal
          : null;
        return new Team(definition.Team.ToTeamName(), royal, definition.StartingMoney, definition.ActionsPerTurn);
      }).ToArray();
      IReadOnlyList<Piece> pieces = level.Units.Select(unit =>
      {
        if (!CampaignRuntimeFactory.TryCreatePiece(level, unit, out Piece? piece))
        {
          throw new InvalidOperationException($"Unknown unit type '{unit.UnitType}'.");
        }
        return piece!;
      }).ToArray();

      HashSet<(int x, int y)> roads = [];
      Dictionary<(int x, int y), int> barricades = [];
      Dictionary<(int x, int y), TeamName> mines = [];
      HashSet<TileEdge> bridges = [];
      foreach (CampaignBoardObjectDefinition boardObject in level.Objects)
      {
        (int x, int y) position = (boardObject.Position.X, boardObject.Position.Y);
        switch (boardObject.Type)
        {
          case CampaignBoardObjectType.Road:
            roads.Add(position);
            break;
          case CampaignBoardObjectType.Barrier:
            barricades[position] = boardObject.Health ?? 20;
            break;
          case CampaignBoardObjectType.Mine when boardObject.Owner is NetworkTeam owner:
            mines[position] = owner.ToTeamName();
            break;
          case CampaignBoardObjectType.Bridge:
            (int x, int y) second = string.Equals(
              (boardObject.Properties ?? []).GetValueOrDefault("direction"),
              "vertical",
              StringComparison.OrdinalIgnoreCase)
              ? (position.x, position.y + 1)
              : (position.x + 1, position.y);
            bridges.Add(TileEdge.Between(position, second));
            break;
        }
      }

      return new CampaignPlayableStateResult
      {
        State = new CampaignPlayableState
        {
          Board = board,
          Terrain = terrain,
          Teams = teams,
          Pieces = pieces,
          Territories = CampaignTerritoryRules.CreateMap(level.Scenario),
          FirstTeam = level.Scenario.FirstTeam,
          GameMode = level.Scenario.GameMode,
          Roads = roads,
          Barricades = barricades,
          Mines = mines,
          RiverBridges = bridges
        },
        Problems = validation.Problems
      };
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
      return new CampaignPlayableStateResult
      {
        Problems = [CampaignValidationProblem.Error("conversion", $"Could not create a playable state: {exception.Message}")]
      };
    }
  }
}
