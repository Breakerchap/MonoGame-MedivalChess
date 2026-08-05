namespace MedivalChess.Shared;

public enum CampaignValidationSeverity
{
  Warning,
  Error
}

public sealed record CampaignValidationProblem(CampaignValidationSeverity Severity, string Code, string Message)
{
  public static CampaignValidationProblem Warning(string code, string message) => new(CampaignValidationSeverity.Warning, code, message);
  public static CampaignValidationProblem Error(string code, string message) => new(CampaignValidationSeverity.Error, code, message);
}

public sealed class CampaignValidationResult
{
  public IReadOnlyList<CampaignValidationProblem> Problems { get; init; } = [];
  public bool IsValid => Problems.All(problem => problem.Severity != CampaignValidationSeverity.Error);
}

/// <summary>Validates untrusted level data without instantiating rendering or gameplay objects.</summary>
public static class CampaignLevelValidator
{
  private static readonly HashSet<string> GameModes = new(StringComparer.Ordinal)
  {
    "Regicide", "Conquest", "Escort", "Dominion", "Plunder"
  };

  public static CampaignValidationResult Validate(CampaignLevelDefinition? level)
  {
    List<CampaignValidationProblem> problems = [];
    Validate(level, problems);
    return new CampaignValidationResult { Problems = problems };
  }

  internal static void Validate(CampaignLevelDefinition? level, ICollection<CampaignValidationProblem> problems)
  {
    if (level is null)
    {
      problems.Add(CampaignValidationProblem.Error("level.missing", "The level definition is missing."));
      return;
    }
    if (level.FormatVersion != CampaignLevelFormat.CurrentVersion)
    {
      problems.Add(CampaignValidationProblem.Error("format.version", $"Level format must be version {CampaignLevelFormat.CurrentVersion}."));
    }

    ValidateMetadata(level.Metadata, problems);
    HashSet<(int x, int y)> cells = ValidateBoard(level.Board, problems);
    HashSet<NetworkTeam> teams = ValidateTeams(level.Teams, problems);
    HashSet<string> unitIds = ValidateUnits(level.Units, teams, cells, level.Terrain, problems);
    ValidateTerrain(level.Terrain, cells, problems);
    ValidateRivers(level.Rivers, cells, problems);
    ValidateObjects(level.Objects, teams, cells, problems);
    ValidateFormations(level.Formations, level.Units, unitIds, teams, problems);
    ValidateScenario(level.Scenario, teams, unitIds, cells, problems);
    ValidateRestrictions(level.Restrictions, problems);
    ValidateReinforcements(level.Reinforcements, teams, cells, problems);
    ValidateScriptedEvents(level.ScriptedEvents, problems);
  }

  private static void ValidateMetadata(CampaignLevelMetadata? metadata, ICollection<CampaignValidationProblem> problems)
  {
    if (metadata is null)
    {
      problems.Add(CampaignValidationProblem.Error("metadata.missing", "Level metadata is required."));
      return;
    }
    if (string.IsNullOrWhiteSpace(metadata.Name))
    {
      problems.Add(CampaignValidationProblem.Error("metadata.name", "Enter a level name."));
    }
    else if (metadata.Name.Length > 120)
    {
      problems.Add(CampaignValidationProblem.Error("metadata.name.length", "The level name must be 120 characters or fewer."));
    }
  }

  private static HashSet<(int x, int y)> ValidateBoard(
    CampaignBoardDefinition? board,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<(int x, int y)> cells = [];
    if (board is null)
    {
      problems.Add(CampaignValidationProblem.Error("board.missing", "Board layout is required."));
      return cells;
    }
    if (board.Width is < 1 or > CampaignLevelFormat.MaximumBoardDimension ||
        board.Height is < 1 or > CampaignLevelFormat.MaximumBoardDimension)
    {
      problems.Add(CampaignValidationProblem.Error("board.dimensions", $"Board width and height must be between 1 and {CampaignLevelFormat.MaximumBoardDimension}."));
    }

    IReadOnlyList<CampaignCoordinate> tiles = board.Tiles ?? [];
    if (tiles.Count == 0)
    {
      problems.Add(CampaignValidationProblem.Error("board.tiles", "Add at least one playable tile."));
      return cells;
    }
    if (tiles.Count > CampaignLevelFormat.MaximumBoardTiles)
    {
      problems.Add(CampaignValidationProblem.Error("board.tiles.maximum", $"A level may contain at most {CampaignLevelFormat.MaximumBoardTiles} tiles."));
    }

    foreach (CampaignCoordinate? tile in tiles)
    {
      if (tile is null)
      {
        problems.Add(CampaignValidationProblem.Error("board.tile.null", "Board tiles cannot be null."));
        continue;
      }
      if (!cells.Add((tile.X, tile.Y)))
      {
        problems.Add(CampaignValidationProblem.Error("board.tile.duplicate", $"Tile ({tile.X}, {tile.Y}) is listed more than once."));
      }
      if (tile.X < board.OriginX || tile.Y < board.OriginY ||
          tile.X >= board.OriginX + board.Width || tile.Y >= board.OriginY + board.Height)
      {
        problems.Add(CampaignValidationProblem.Error("board.tile.bounds", $"Tile ({tile.X}, {tile.Y}) lies outside the declared board bounds."));
      }
    }

    if (board.Shape == CampaignBoardShape.Rectangle && board.Width > 0 && board.Height > 0)
    {
      int expectedTileCount = board.Width * board.Height;
      if (cells.Count != expectedTileCount)
      {
        problems.Add(CampaignValidationProblem.Error("board.rectangle", "A rectangular board must contain every tile inside its declared bounds. Use Custom for a shaped board."));
      }
    }

    return cells;
  }

  private static HashSet<NetworkTeam> ValidateTeams(
    IReadOnlyList<CampaignTeamDefinition>? definitions,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<NetworkTeam> teams = [];
    IReadOnlyList<CampaignTeamDefinition> source = definitions ?? [];
    if (source.Count is < 2 or > 4)
    {
      problems.Add(CampaignValidationProblem.Error("teams.count", "A campaign level needs between two and four active teams."));
    }

    foreach (CampaignTeamDefinition? team in source)
    {
      if (team is null)
      {
        problems.Add(CampaignValidationProblem.Error("team.null", "Team entries cannot be null."));
        continue;
      }
      if (team.Team == NetworkTeam.Neutral)
      {
        problems.Add(CampaignValidationProblem.Error("team.neutral", "Neutral cannot be an active team."));
      }
      else if (!teams.Add(team.Team))
      {
        problems.Add(CampaignValidationProblem.Error("team.duplicate", $"{team.Team} is configured more than once."));
      }
      if (team.StartingMoney is < 0 or > 1_000_000)
      {
        problems.Add(CampaignValidationProblem.Error("team.money", $"{team.Team} starting money must be between 0 and 1,000,000."));
      }
      if (team.ActionsPerTurn is < 1 or > 100)
      {
        problems.Add(CampaignValidationProblem.Error("team.actions", $"{team.Team} actions per turn must be between 1 and 100."));
      }
      if (!string.IsNullOrEmpty(team.ChosenRoyal) && !UnitRules.Royals.Any(rule => rule.Type == team.ChosenRoyal))
      {
        problems.Add(CampaignValidationProblem.Error("team.royal", $"{team.ChosenRoyal} is not a recognised royal."));
      }
      ValidateUnitIdentifiers(team.AvailableUnitTypes, $"team.{team.Team}.availableUnits", problems);
      ValidateUnitIdentifiers(team.DisabledAbilityUnitTypes, $"team.{team.Team}.disabledAbilities", problems);
      if (team.CpuProfile is null || team.CpuProfile.Difficulty is not ("Easy" or "Normal" or "Hard"))
      {
        problems.Add(CampaignValidationProblem.Error("team.cpu.difficulty", $"{team.Team} CPU difficulty must be Easy, Normal, or Hard."));
      }
      if (team.CpuProfile is null || team.CpuProfile.Personality is not ("Balanced" or "Aggressive" or "Defensive" or "Greedy" or "Reckless" or "ObjectiveFocused" or "Swarmer"))
      {
        problems.Add(CampaignValidationProblem.Error("team.cpu.personality", $"{team.Team} CPU personality is not recognised."));
      }
    }

    return teams;
  }

  private static HashSet<string> ValidateUnits(
    IReadOnlyList<CampaignUnitDefinition>? definitions,
    IReadOnlySet<NetworkTeam> teams,
    IReadOnlySet<(int x, int y)> cells,
    IReadOnlyList<CampaignTerrainTileDefinition>? terrain,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<string> ids = new(StringComparer.Ordinal);
    Dictionary<(int x, int y), CampaignUnitDefinition> occupied = [];
    HashSet<(int x, int y)> lakes = (terrain ?? [])
      .Where(tile => tile?.Type == CampaignTerrainType.Lake && tile.Position is not null)
      .Select(tile => (tile.Position.X, tile.Position.Y))
      .ToHashSet();

    foreach (CampaignUnitDefinition? unit in definitions ?? [])
    {
      if (unit is null)
      {
        problems.Add(CampaignValidationProblem.Error("unit.null", "Unit entries cannot be null."));
        continue;
      }
      if (string.IsNullOrWhiteSpace(unit.Id))
      {
        problems.Add(CampaignValidationProblem.Error("unit.id", "Every unit needs an ID."));
      }
      else if (!ids.Add(unit.Id))
      {
        problems.Add(CampaignValidationProblem.Error("unit.id.duplicate", $"Unit ID '{unit.Id}' is used more than once."));
      }
      if (!UnitRules.TryGet(unit.UnitType, out UnitRule rule))
      {
        problems.Add(CampaignValidationProblem.Error("unit.type", $"'{unit.UnitType}' is not a recognised unit type."));
        continue;
      }
      if (unit.Team == NetworkTeam.Neutral)
      {
        if (unit.UnitType != "Mercenary")
        {
          problems.Add(CampaignValidationProblem.Error("unit.team.neutral", "Only Mercenary units may start neutral."));
        }
      }
      else if (!teams.Contains(unit.Team))
      {
        problems.Add(CampaignValidationProblem.Error("unit.team", $"Unit '{unit.Id}' refers to team {unit.Team}, which is not configured."));
      }
      if (unit.Position is null)
      {
        problems.Add(CampaignValidationProblem.Error("unit.position", $"Unit '{unit.Id}' has no position."));
        continue;
      }
      if (unit.Health is < 1 or > int.MaxValue || unit.Health > rule.Health)
      {
        problems.Add(CampaignValidationProblem.Error("unit.health", $"Unit '{unit.Id}' health must be between 1 and {rule.Health}."));
      }
      if (!Enum.IsDefined(unit.Rotation))
      {
        problems.Add(CampaignValidationProblem.Error("unit.rotation", $"Unit '{unit.Id}' has an unsupported rotation."));
      }

      for (int y = 0; y < rule.Height; y++)
      for (int x = 0; x < rule.Width; x++)
      {
        (int x, int y) square = (unit.Position.X + x, unit.Position.Y + y);
        if (!cells.Contains(square))
        {
          problems.Add(CampaignValidationProblem.Error("unit.bounds", $"Unit '{unit.Id}' does not fit on a playable tile at ({square.x}, {square.y})."));
          continue;
        }
        if (lakes.Contains(square) && unit.UnitType != "Elephant")
        {
          problems.Add(CampaignValidationProblem.Error("unit.lake", $"Unit '{unit.Id}' cannot start on a lake tile."));
        }
        if (occupied.TryGetValue(square, out CampaignUnitDefinition? other) &&
            other.UnitType != "Farm" && unit.UnitType != "Farm")
        {
          problems.Add(CampaignValidationProblem.Error("unit.overlap", $"Units '{other.Id}' and '{unit.Id}' overlap at ({square.x}, {square.y})."));
        }
        else
        {
          occupied[square] = unit;
        }
      }
    }

    return ids;
  }

  private static void ValidateTerrain(
    IReadOnlyList<CampaignTerrainTileDefinition>? terrain,
    IReadOnlySet<(int x, int y)> cells,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<(int x, int y)> forests = [];
    HashSet<(int x, int y)> lakes = [];
    foreach (CampaignTerrainTileDefinition? tile in terrain ?? [])
    {
      if (tile?.Position is null)
      {
        problems.Add(CampaignValidationProblem.Error("terrain.position", "Terrain must have a position."));
        continue;
      }
      (int x, int y) position = (tile.Position.X, tile.Position.Y);
      if (!cells.Contains(position))
      {
        problems.Add(CampaignValidationProblem.Error("terrain.bounds", $"Terrain at ({position.x}, {position.y}) is outside the board."));
      }
      HashSet<(int x, int y)> bucket = tile.Type == CampaignTerrainType.Forest ? forests : lakes;
      if (!bucket.Add(position))
      {
        problems.Add(CampaignValidationProblem.Error("terrain.duplicate", $"{tile.Type} is placed more than once at ({position.x}, {position.y})."));
      }
      if (forests.Contains(position) && lakes.Contains(position))
      {
        problems.Add(CampaignValidationProblem.Error("terrain.conflict", $"Forest and lake terrain cannot share ({position.x}, {position.y})."));
      }
    }
  }

  private static void ValidateRivers(
    IReadOnlyList<CampaignRiverDefinition>? rivers,
    IReadOnlySet<(int x, int y)> cells,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<TileEdge> seen = [];
    foreach (CampaignRiverDefinition? river in rivers ?? [])
    {
      if (river?.First is null || river.Second is null)
      {
        problems.Add(CampaignValidationProblem.Error("river.position", "Each river must define both adjacent tiles."));
        continue;
      }
      (int x, int y) first = (river.First.X, river.First.Y);
      (int x, int y) second = (river.Second.X, river.Second.Y);
      if (!cells.Contains(first) || !cells.Contains(second) || Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y) != 1)
      {
        problems.Add(CampaignValidationProblem.Error("river.edge", "River segments must lie between two adjacent playable tiles."));
        continue;
      }
      if (!seen.Add(TileEdge.Between(first, second)))
      {
        problems.Add(CampaignValidationProblem.Error("river.duplicate", $"The river edge between ({first.x}, {first.y}) and ({second.x}, {second.y}) is duplicated."));
      }
    }
  }

  private static void ValidateObjects(
    IReadOnlyList<CampaignBoardObjectDefinition>? objects,
    IReadOnlySet<NetworkTeam> teams,
    IReadOnlySet<(int x, int y)> cells,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<string> ids = new(StringComparer.Ordinal);
    int treasures = 0;
    foreach (CampaignBoardObjectDefinition? boardObject in objects ?? [])
    {
      if (boardObject is null || boardObject.Position is null)
      {
        problems.Add(CampaignValidationProblem.Error("object.position", "Board objects must have a position."));
        continue;
      }
      if (string.IsNullOrWhiteSpace(boardObject.Id))
      {
        problems.Add(CampaignValidationProblem.Error("object.id", "Every board object needs an ID."));
      }
      else if (!ids.Add(boardObject.Id))
      {
        problems.Add(CampaignValidationProblem.Error("object.id.duplicate", $"Board object ID '{boardObject.Id}' is used more than once."));
      }
      (int x, int y) position = (boardObject.Position.X, boardObject.Position.Y);
      if (!cells.Contains(position))
      {
        problems.Add(CampaignValidationProblem.Error("object.bounds", $"Object '{boardObject.Id}' is outside the board."));
      }
      if (boardObject.Owner is NetworkTeam owner && owner != NetworkTeam.Neutral && !teams.Contains(owner))
      {
        problems.Add(CampaignValidationProblem.Error("object.owner", $"Object '{boardObject.Id}' references team {owner}, which is not configured."));
      }
      if (boardObject.Type == CampaignBoardObjectType.Mine && boardObject.Owner is null)
      {
        problems.Add(CampaignValidationProblem.Error("object.mine.owner", $"Mine '{boardObject.Id}' must have an owner."));
      }
      if (boardObject.Type == CampaignBoardObjectType.Barrier &&
          (!boardObject.Health.HasValue || boardObject.Health.Value < 1 || boardObject.Health.Value > 100))
      {
        problems.Add(CampaignValidationProblem.Error("object.barrier.health", $"Barrier '{boardObject.Id}' health must be between 1 and 100."));
      }
      if (boardObject.Type == CampaignBoardObjectType.Treasure && ++treasures > 1)
      {
        problems.Add(CampaignValidationProblem.Error("object.treasure.duplicate", "A level can contain only one treasure."));
      }
    }
  }

  private static void ValidateFormations(
    IReadOnlyList<CampaignFormationDefinition>? formations,
    IReadOnlyList<CampaignUnitDefinition>? units,
    IReadOnlySet<string> unitIds,
    IReadOnlySet<NetworkTeam> teams,
    ICollection<CampaignValidationProblem> problems
  )
  {
    Dictionary<string, CampaignUnitDefinition> unitsById = (units ?? [])
      .Where(unit => unit is not null && !string.IsNullOrWhiteSpace(unit.Id))
      .GroupBy(unit => unit.Id, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    HashSet<string> formationIds = new(StringComparer.Ordinal);
    foreach (CampaignFormationDefinition? formation in formations ?? [])
    {
      if (formation is null)
      {
        problems.Add(CampaignValidationProblem.Error("formation.null", "Formation entries cannot be null."));
        continue;
      }
      if (string.IsNullOrWhiteSpace(formation.Id) || !formationIds.Add(formation.Id))
      {
        problems.Add(CampaignValidationProblem.Error("formation.id", "Each formation needs a unique ID."));
      }
      if (!teams.Contains(formation.Team))
      {
        problems.Add(CampaignValidationProblem.Error("formation.team", $"Formation '{formation.Id}' refers to a missing team."));
      }
      HashSet<string> members = new(StringComparer.Ordinal);
      foreach (string? unitId in formation.UnitIds ?? [])
      {
        if (string.IsNullOrWhiteSpace(unitId) || !unitIds.Contains(unitId))
        {
          problems.Add(CampaignValidationProblem.Error("formation.unit", $"Formation '{formation.Id}' references a unit that does not exist."));
        }
        else if (!members.Add(unitId))
        {
          problems.Add(CampaignValidationProblem.Error("formation.unit.duplicate", $"Formation '{formation.Id}' contains unit '{unitId}' more than once."));
        }
        else if (unitsById.TryGetValue(unitId, out CampaignUnitDefinition? unit) && unit.Team != formation.Team)
        {
          problems.Add(CampaignValidationProblem.Error("formation.unit.team", $"Formation '{formation.Id}' includes a unit from another team."));
        }
      }
    }
  }

  private static void ValidateScenario(
    CampaignScenarioDefinition? scenario,
    IReadOnlySet<NetworkTeam> teams,
    IReadOnlySet<string> unitIds,
    IReadOnlySet<(int x, int y)> cells,
    ICollection<CampaignValidationProblem> problems
  )
  {
    if (scenario is null)
    {
      problems.Add(CampaignValidationProblem.Error("scenario.missing", "Scenario settings are required."));
      return;
    }
    if (!GameModes.Contains(scenario.GameMode))
    {
      problems.Add(CampaignValidationProblem.Error("scenario.mode", $"'{scenario.GameMode}' is not a supported game mode."));
    }
    if (!teams.Contains(scenario.FirstTeam))
    {
      problems.Add(CampaignValidationProblem.Error("scenario.firstTeam", "The first-moving team must be configured."));
    }
    if (scenario.TurnLimit is < 1 or > 10_000)
    {
      problems.Add(CampaignValidationProblem.Error("scenario.turnLimit", "Turn limit must be between 1 and 10,000 when set."));
    }
    IReadOnlyList<CampaignObjectiveDefinition> victory = scenario.VictoryConditions ?? [];
    if (victory.Count == 0)
    {
      problems.Add(CampaignValidationProblem.Error("scenario.objectives", "Add at least one victory condition."));
    }
    ValidateObjectives(victory, "victory", teams, unitIds, cells, problems);
    ValidateObjectives(scenario.DefeatConditions ?? [], "defeat", teams, unitIds, cells, problems);
  }

  private static void ValidateObjectives(
    IEnumerable<CampaignObjectiveDefinition> objectives,
    string kind,
    IReadOnlySet<NetworkTeam> teams,
    IReadOnlySet<string> unitIds,
    IReadOnlySet<(int x, int y)> cells,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<string> ids = new(StringComparer.Ordinal);
    foreach (CampaignObjectiveDefinition? objective in objectives)
    {
      if (objective is null)
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.null", "Objective entries cannot be null."));
        continue;
      }
      if (string.IsNullOrWhiteSpace(objective.Id) || !ids.Add(objective.Id))
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.id", "Each objective needs a unique ID."));
      }
      if (objective.Team is NetworkTeam team && !teams.Contains(team))
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.team", $"Objective '{objective.Id}' references a missing team."));
      }
      if (!string.IsNullOrEmpty(objective.TargetUnitId) && !unitIds.Contains(objective.TargetUnitId))
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.unit", $"Objective '{objective.Id}' references a unit that does not exist."));
      }
      if (objective.RequiredAmount is < 1 or > 10_000)
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.amount", $"Objective '{objective.Id}' required amount must be between 1 and 10,000."));
      }
      foreach (CampaignCoordinate? location in objective.Locations ?? [])
      {
        if (location is null || !cells.Contains((location.X, location.Y)))
        {
          problems.Add(CampaignValidationProblem.Error($"objective.{kind}.location", $"Objective '{objective.Id}' references a location outside the board."));
        }
      }
      if (objective.Type == CampaignObjectiveType.GetUnitsToLocations)
      {
        if ((objective.UnitLocationTargets ?? []).Count == 0)
        {
          problems.Add(CampaignValidationProblem.Error($"objective.{kind}.unitLocations", $"Objective '{objective.Id}' needs at least one unit-to-location target."));
        }
        HashSet<string> targetUnits = new(StringComparer.Ordinal);
        foreach (CampaignUnitLocationTargetDefinition? target in objective.UnitLocationTargets ?? [])
        {
          if (target is null || string.IsNullOrWhiteSpace(target.UnitId) || !unitIds.Contains(target.UnitId))
          {
            problems.Add(CampaignValidationProblem.Error($"objective.{kind}.unitLocations", $"Objective '{objective.Id}' references a unit that does not exist."));
          }
          else if (!targetUnits.Add(target.UnitId))
          {
            problems.Add(CampaignValidationProblem.Error($"objective.{kind}.unitLocations.duplicate", $"Objective '{objective.Id}' maps unit '{target.UnitId}' more than once."));
          }
          if (target?.Location is null || !cells.Contains((target.Location.X, target.Location.Y)))
          {
            problems.Add(CampaignValidationProblem.Error($"objective.{kind}.unitLocations.bounds", $"Objective '{objective.Id}' contains a location outside the board."));
          }
        }
      }
      if ((objective.Type is CampaignObjectiveType.EscortUnit or CampaignObjectiveType.ProtectUnit) && string.IsNullOrWhiteSpace(objective.TargetUnitId))
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.target", $"Objective '{objective.Id}' needs a target unit."));
      }
      if (objective.Type == CampaignObjectiveType.EscortUnit && (objective.Locations ?? []).Count == 0)
      {
        problems.Add(CampaignValidationProblem.Error($"objective.{kind}.destination", $"Escort objective '{objective.Id}' needs a destination."));
      }
    }
  }

  private static void ValidateRestrictions(CampaignRestrictionsDefinition? restrictions, ICollection<CampaignValidationProblem> problems)
  {
    if (restrictions is null)
    {
      problems.Add(CampaignValidationProblem.Error("restrictions.missing", "Restrictions settings are required."));
      return;
    }
    ValidateUnitIdentifiers(restrictions.AllowedUnitTypes, "restriction.allowedUnits", problems);
    ValidateUnitIdentifiers(restrictions.DisabledUnitTypes, "restriction.disabledUnits", problems);
    ValidateUnitIdentifiers(restrictions.DisabledAbilityUnitTypes, "restriction.disabledAbilities", problems);
  }

  private static void ValidateUnitIdentifiers(
    IEnumerable<string>? identifiers,
    string code,
    ICollection<CampaignValidationProblem> problems
  )
  {
    HashSet<string> seen = new(StringComparer.Ordinal);
    foreach (string? identifier in identifiers ?? [])
    {
      if (string.IsNullOrWhiteSpace(identifier) || !UnitRules.TryGet(identifier, out _))
      {
        problems.Add(CampaignValidationProblem.Error(code, $"'{identifier ?? "(missing)"}' is not a recognised unit type."));
      }
      else if (!seen.Add(identifier))
      {
        problems.Add(CampaignValidationProblem.Error($"{code}.duplicate", $"Unit type '{identifier}' is listed more than once."));
      }
    }
  }

  private static void ValidateReinforcements(
    IReadOnlyList<CampaignReinforcementDefinition>? reinforcements,
    IReadOnlySet<NetworkTeam> teams,
    IReadOnlySet<(int x, int y)> cells,
    ICollection<CampaignValidationProblem> problems
  )
  {
    foreach (CampaignReinforcementDefinition? reinforcement in reinforcements ?? [])
    {
      if (reinforcement is null)
      {
        problems.Add(CampaignValidationProblem.Error("reinforcement.null", "Reinforcement entries cannot be null."));
        continue;
      }
      if (reinforcement.ArrivesOnTurn is < 1 or > 10_000 || !teams.Contains(reinforcement.Team))
      {
        problems.Add(CampaignValidationProblem.Error("reinforcement.settings", $"Reinforcement '{reinforcement.Id}' has an invalid turn or team."));
      }
      foreach (CampaignUnitDefinition? unit in reinforcement.Units ?? [])
      {
        if (unit is null || !UnitRules.TryGet(unit.UnitType, out UnitRule rule) || unit.Position is null ||
            !Enumerable.Range(0, rule.Width).SelectMany(x => Enumerable.Range(0, rule.Height).Select(y => (unit.Position.X + x, unit.Position.Y + y))).All(cells.Contains))
        {
          problems.Add(CampaignValidationProblem.Error("reinforcement.unit", $"Reinforcement '{reinforcement.Id}' contains an invalid unit placement."));
        }
      }
      problems.Add(CampaignValidationProblem.Warning("reinforcement.runtime", "Reinforcements are stored in the format but are not yet run by the current match controller."));
    }
  }

  private static void ValidateScriptedEvents(
    IReadOnlyList<CampaignScriptedEventDefinition>? events,
    ICollection<CampaignValidationProblem> problems
  )
  {
    foreach (CampaignScriptedEventDefinition? scriptedEvent in events ?? [])
    {
      if (scriptedEvent is null || string.IsNullOrWhiteSpace(scriptedEvent.Id) ||
          string.IsNullOrWhiteSpace(scriptedEvent.Trigger) || string.IsNullOrWhiteSpace(scriptedEvent.Action))
      {
        problems.Add(CampaignValidationProblem.Error("event.definition", "Every scripted event needs an ID, trigger, and action."));
      }
      problems.Add(CampaignValidationProblem.Warning("event.runtime", "Scripted events are stored for future campaign support and are not run by the current match controller."));
    }
  }
}
