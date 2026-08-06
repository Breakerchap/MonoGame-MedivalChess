namespace MedivalChess.Shared;

/// <summary>
/// Resolves campaign-authored territory maps while preserving the normal match territory rules
/// for levels that have not opted in to custom areas.
/// </summary>
public static class CampaignTerritoryRules
{
  public static bool UsesCustomAreas(CampaignScenarioDefinition? scenario) =>
    scenario?.Territories?.UseCustomAreas == true;

  public static CampaignTerritoryMap CreateMap(CampaignScenarioDefinition? scenario) => new(scenario);

  public static NetworkTeam? GetSquareOwner(
    Board board,
    CampaignScenarioDefinition? scenario,
    (int x, int y) square,
    int playerCount
  )
  {
    return new CampaignTerritoryMap(scenario).GetSquareOwner(board, square, playerCount);
  }

  /// <summary>Builds an editable copy of the standard game zones for the current board.</summary>
  public static CampaignTerritoriesDefinition CreateDefaultAreas(Board board, string gameMode, int playerCount)
  {
    ArgumentNullException.ThrowIfNull(board);
    CampaignTerritoriesDefinition territories = new() { UseCustomAreas = true };
    Dictionary<NetworkTeam, CampaignTeamAreaDefinition> teamAreas = TeamRules.GetActiveTeams(playerCount)
      .ToDictionary(team => team, team => new CampaignTeamAreaDefinition { Team = team });

    foreach ((int x, int y) square in board.Cells.OrderBy(square => square.y).ThenBy(square => square.x))
    {
      NetworkTeam? owner = MatchRules.GetSquareOwner(board, gameMode, square, playerCount);
      CampaignCoordinate coordinate = new(square.x, square.y);
      if (owner is NetworkTeam team && teamAreas.TryGetValue(team, out CampaignTeamAreaDefinition? area))
      {
        area.Tiles.Add(coordinate);
      }
      else
      {
        territories.NoMansLand.Add(coordinate);
      }
    }

    territories.TeamAreas = teamAreas.Values.ToList();
    return territories;
  }

  public static string GetAreaLabel(NetworkTeam? team) => team is null or NetworkTeam.Neutral
    ? "NO-MAN'S-LAND"
    : $"{team} AREA";
}

/// <summary>
/// Fast lookup table for one campaign's territory configuration. Keep one alongside a running
/// campaign or editor frame instead of repeatedly scanning every painted tile.
/// </summary>
public sealed class CampaignTerritoryMap
{
  private readonly Dictionary<(int x, int y), NetworkTeam> _owners = [];
  private readonly bool _usesCustomAreas;
  private readonly string _gameMode;

  public CampaignTerritoryMap(CampaignScenarioDefinition? scenario)
  {
    _usesCustomAreas = CampaignTerritoryRules.UsesCustomAreas(scenario);
    _gameMode = scenario?.GameMode ?? "Regicide";
    if (!_usesCustomAreas) return;

    foreach (CampaignTeamAreaDefinition area in scenario!.Territories.TeamAreas ?? [])
    {
      foreach (CampaignCoordinate? tile in area.Tiles ?? [])
      {
        if (tile is not null) _owners[(tile.X, tile.Y)] = area.Team;
      }
    }
  }

  public NetworkTeam? GetSquareOwner(Board board, (int x, int y) square, int playerCount)
  {
    ArgumentNullException.ThrowIfNull(board);
    if (!board.ContainsCell(square)) return null;
    if (!_usesCustomAreas) return MatchRules.GetSquareOwner(board, _gameMode, square, playerCount);

    // The validator requires every playable tile to be in the explicit map. A missing entry is
    // treated as No-Man's-Land while the editor is still showing an invalid work in progress.
    return _owners.TryGetValue(square, out NetworkTeam owner) ? owner : null;
  }
}
