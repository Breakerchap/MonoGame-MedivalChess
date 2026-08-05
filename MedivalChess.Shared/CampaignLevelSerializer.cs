namespace MedivalChess.Shared;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class CampaignLevelLoadResult
{
  public CampaignLevelDefinition? Level { get; init; }
  public IReadOnlyList<CampaignValidationProblem> Problems { get; init; } = [];
  public bool IsSuccess => Level is not null && Problems.All(problem => problem.Severity != CampaignValidationSeverity.Error);
}

public sealed class CampaignLevelSaveResult
{
  public IReadOnlyList<CampaignValidationProblem> Problems { get; init; } = [];
  public bool IsSuccess => Problems.All(problem => problem.Severity != CampaignValidationSeverity.Error);
}

/// <summary>Reads and writes the portable, versioned .mclvl campaign format.</summary>
public static class CampaignLevelSerializer
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
  };

  public static string LocalLevelDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CrownAndSiege",
    "Levels"
  );

  public static string Serialize(CampaignLevelDefinition level)
  {
    ArgumentNullException.ThrowIfNull(level);
    level.FormatVersion = CampaignLevelFormat.CurrentVersion;
    return JsonSerializer.Serialize(level, Options);
  }

  public static CampaignLevelSaveResult Save(string path, CampaignLevelDefinition level)
  {
    List<CampaignValidationProblem> problems = [];
    if (!HasExpectedExtension(path))
    {
      problems.Add(CampaignValidationProblem.Error("file.extension", $"Level files must use the {CampaignLevelFormat.Extension} extension."));
      return new CampaignLevelSaveResult { Problems = problems };
    }

    CampaignValidationResult validation = CampaignLevelValidator.Validate(level);
    problems.AddRange(validation.Problems);
    if (!validation.IsValid)
    {
      return new CampaignLevelSaveResult { Problems = problems };
    }

    try
    {
      string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
      if (!string.IsNullOrEmpty(directory))
      {
        Directory.CreateDirectory(directory);
      }

      File.WriteAllText(path, Serialize(level));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
      problems.Add(CampaignValidationProblem.Error("file.write", $"Could not save level: {exception.Message}"));
    }

    return new CampaignLevelSaveResult { Problems = problems };
  }

  public static CampaignLevelLoadResult Load(string path)
  {
    List<CampaignValidationProblem> problems = [];
    if (!HasExpectedExtension(path))
    {
      problems.Add(CampaignValidationProblem.Error("file.extension", $"Choose a {CampaignLevelFormat.Extension} level file."));
      return new CampaignLevelLoadResult { Problems = problems };
    }

    try
    {
      FileInfo file = new(path);
      if (!file.Exists)
      {
        problems.Add(CampaignValidationProblem.Error("file.missing", "The selected level file no longer exists."));
        return new CampaignLevelLoadResult { Problems = problems };
      }
      if (file.Length > CampaignLevelFormat.MaximumFileBytes)
      {
        problems.Add(CampaignValidationProblem.Error("file.size", "The level file is too large to import safely."));
        return new CampaignLevelLoadResult { Problems = problems };
      }

      return Deserialize(File.ReadAllText(path));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
      problems.Add(CampaignValidationProblem.Error("file.read", $"Could not read level: {exception.Message}"));
      return new CampaignLevelLoadResult { Problems = problems };
    }
  }

  /// <summary>Useful for previews and tests; uses exactly the same untrusted-input path as file import.</summary>
  public static CampaignLevelLoadResult Deserialize(string json)
  {
    List<CampaignValidationProblem> problems = [];
    if (string.IsNullOrWhiteSpace(json))
    {
      problems.Add(CampaignValidationProblem.Error("format.empty", "The level file is empty."));
      return new CampaignLevelLoadResult { Problems = problems };
    }

    if (json.Length > CampaignLevelFormat.MaximumFileBytes)
    {
      problems.Add(CampaignValidationProblem.Error("file.size", "The level file is too large to import safely."));
      return new CampaignLevelLoadResult { Problems = problems };
    }

    try
    {
      using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
      {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
      });
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object ||
          !root.TryGetProperty("formatVersion", out JsonElement versionElement) ||
          !versionElement.TryGetInt32(out int sourceVersion))
      {
        problems.Add(CampaignValidationProblem.Error("format.version", "The level file must contain an integer formatVersion."));
        return new CampaignLevelLoadResult { Problems = problems };
      }

      if (sourceVersion > CampaignLevelFormat.CurrentVersion)
      {
        problems.Add(CampaignValidationProblem.Error("format.future", $"This level requires format version {sourceVersion}, but this game supports up to version {CampaignLevelFormat.CurrentVersion}."));
        return new CampaignLevelLoadResult { Problems = problems };
      }
      if (sourceVersion < CampaignLevelFormat.OldestSupportedVersion)
      {
        problems.Add(CampaignValidationProblem.Error("format.old", $"Format version {sourceVersion} is no longer supported."));
        return new CampaignLevelLoadResult { Problems = problems };
      }

      CampaignLevelDefinition? level = JsonSerializer.Deserialize<CampaignLevelDefinition>(json, Options);
      if (level is null)
      {
        problems.Add(CampaignValidationProblem.Error("format.level", "The level file did not contain a level definition."));
        return new CampaignLevelLoadResult { Problems = problems };
      }

      CampaignLevelMigrator.ApplyLegacyBoardCells(level, root, sourceVersion);
      CampaignLevelMigrationResult migration = CampaignLevelMigrator.Migrate(level, sourceVersion);
      problems.AddRange(migration.Problems);
      if (!migration.IsSuccess)
      {
        return new CampaignLevelLoadResult { Problems = problems };
      }

      CampaignLevelValidator.Validate(level, problems);
      return new CampaignLevelLoadResult { Level = level, Problems = problems };
    }
    catch (JsonException exception)
    {
      problems.Add(CampaignValidationProblem.Error("format.json", $"The level file is not valid JSON: {exception.Message}"));
    }
    catch (NotSupportedException exception)
    {
      problems.Add(CampaignValidationProblem.Error("format.content", $"The level contains unsupported data: {exception.Message}"));
    }

    return new CampaignLevelLoadResult { Problems = problems };
  }

  public static bool HasExpectedExtension(string? path) => !string.IsNullOrWhiteSpace(path) &&
    string.Equals(Path.GetExtension(path), CampaignLevelFormat.Extension, StringComparison.OrdinalIgnoreCase);
}

public sealed class CampaignLevelMigrationResult
{
  public IReadOnlyList<CampaignValidationProblem> Problems { get; init; } = [];
  public bool IsSuccess => Problems.All(problem => problem.Severity != CampaignValidationSeverity.Error);
}

/// <summary>Applies explicit, one-way migrations before a level reaches the editor or game state converter.</summary>
public static class CampaignLevelMigrator
{
  public static CampaignLevelMigrationResult Migrate(CampaignLevelDefinition level, int sourceVersion)
  {
    ArgumentNullException.ThrowIfNull(level);
    List<CampaignValidationProblem> problems = [];
    if (sourceVersion < CampaignLevelFormat.OldestSupportedVersion || sourceVersion > CampaignLevelFormat.CurrentVersion)
    {
      problems.Add(CampaignValidationProblem.Error("migration.version", $"No migration path exists for format version {sourceVersion}."));
      return new CampaignLevelMigrationResult { Problems = problems };
    }

    NormaliseCollections(level);
    if (sourceVersion == 1)
    {
      // Version 2 made per-team action limits and restrictions explicit.  V1 used
      // default match behaviour, so adding the defaults is a lossless migration.
      foreach (CampaignTeamDefinition team in level.Teams)
      {
        team.ActionsPerTurn = team.ActionsPerTurn <= 0 ? 2 : team.ActionsPerTurn;
        team.AvailableUnitTypes ??= [];
        team.DisabledAbilityUnitTypes ??= [];
        team.CpuProfile ??= new CampaignCpuProfileDefinition();
      }
      level.Restrictions ??= new CampaignRestrictionsDefinition();
      level.FormatVersion = CampaignLevelFormat.CurrentVersion;
      problems.Add(CampaignValidationProblem.Warning("migration.v1", "Migrated level format version 1 to version 2."));
    }
    else
    {
      level.FormatVersion = CampaignLevelFormat.CurrentVersion;
    }

    return new CampaignLevelMigrationResult { Problems = problems };
  }

  internal static void ApplyLegacyBoardCells(CampaignLevelDefinition level, JsonElement root, int sourceVersion)
  {
    if (sourceVersion != 1 || !root.TryGetProperty("board", out JsonElement boardElement) ||
        boardElement.ValueKind != JsonValueKind.Object || !boardElement.TryGetProperty("cells", out JsonElement cells) ||
        cells.ValueKind != JsonValueKind.Array || level.Board.Tiles.Count > 0)
    {
      return;
    }

    foreach (JsonElement cell in cells.EnumerateArray())
    {
      if (cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() == 2 &&
          cell[0].TryGetInt32(out int x) && cell[1].TryGetInt32(out int y))
      {
        level.Board.Tiles.Add(new CampaignCoordinate(x, y));
      }
    }

    if (level.Board.Tiles.Count > 0)
    {
      level.Board.Shape = CampaignBoardShape.Custom;
      level.Board.OriginX = level.Board.Tiles.Min(tile => tile.X);
      level.Board.OriginY = level.Board.Tiles.Min(tile => tile.Y);
      level.Board.Width = level.Board.Tiles.Max(tile => tile.X) - level.Board.OriginX + 1;
      level.Board.Height = level.Board.Tiles.Max(tile => tile.Y) - level.Board.OriginY + 1;
    }
  }

  private static void NormaliseCollections(CampaignLevelDefinition level)
  {
    level.Metadata ??= new CampaignLevelMetadata();
    level.Board ??= new CampaignBoardDefinition();
    level.Board.Tiles ??= [];
    level.Terrain ??= [];
    level.Rivers ??= [];
    level.Objects ??= [];
    level.Units ??= [];
    level.Teams ??= [];
    level.Formations ??= [];
    level.Scenario ??= new CampaignScenarioDefinition();
    level.Scenario.VictoryConditions ??= [];
    level.Scenario.DefeatConditions ??= [];
    foreach (CampaignObjectiveDefinition objective in level.Scenario.VictoryConditions.Concat(level.Scenario.DefeatConditions))
    {
      objective.Locations ??= [];
      objective.UnitLocationTargets ??= [];
    }
    level.Restrictions ??= new CampaignRestrictionsDefinition();
    level.Restrictions.AllowedUnitTypes ??= [];
    level.Restrictions.DisabledUnitTypes ??= [];
    level.Restrictions.DisabledAbilityUnitTypes ??= [];
    level.Reinforcements ??= [];
    level.ScriptedEvents ??= [];
  }
}
