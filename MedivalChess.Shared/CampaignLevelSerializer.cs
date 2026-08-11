namespace MedivalChess.Shared;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Security;
using System.Security.Cryptography;

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
    level.Uid = CalculateUid(level);
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
      string target = Path.GetFullPath(path);
      string? directory = Path.GetDirectoryName(target);
      if (!string.IsNullOrEmpty(directory))
      {
        Directory.CreateDirectory(directory);
      }

      // Write beside the destination and then replace it. This avoids partially written map
      // files if Windows Defender, OneDrive, or the game is interrupted during an export.
      string temporary = Path.Combine(
        directory ?? Directory.GetCurrentDirectory(),
        $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp"
      );
      try
      {
        using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
          writer.Write(Serialize(level));
        }
        File.Move(temporary, target, overwrite: true);
      }
      finally
      {
        // Move succeeds atomically on the same volume. A failed write leaves only this disposable
        // temporary file, never a corrupted destination.
        if (File.Exists(temporary)) File.Delete(temporary);
      }
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
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

      CampaignLevelMigrationResult migration = CampaignLevelMigrator.Migrate(level, sourceVersion);
      problems.AddRange(migration.Problems);
      if (!migration.IsSuccess)
      {
        return new CampaignLevelLoadResult { Problems = problems };
      }

      if (string.IsNullOrWhiteSpace(level.Uid))
      {
        problems.Add(CampaignValidationProblem.Error("uid.missing", "Level files must include a content UID."));
      }
      else if (!string.Equals(level.Uid, CalculateUid(level), StringComparison.OrdinalIgnoreCase))
      {
        problems.Add(CampaignValidationProblem.Error("uid.invalid", "The level content does not match its UID. Export it again from the editor."));
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

  /// <summary>Returns a deterministic identifier for all serialised level content except the UID itself.</summary>
  public static string CalculateUid(CampaignLevelDefinition level)
  {
    ArgumentNullException.ThrowIfNull(level);
    string? originalUid = level.Uid;
    try
    {
      level.Uid = null;
      string canonicalJson = JsonSerializer.Serialize(level, Options);
      byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
      return Convert.ToHexString(hash).ToLowerInvariant();
    }
    finally
    {
      level.Uid = originalUid;
    }
  }
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
    if (sourceVersion != CampaignLevelFormat.CurrentVersion)
    {
      problems.Add(CampaignValidationProblem.Error("migration.version", "Only the current .mclvl format is supported."));
      return new CampaignLevelMigrationResult { Problems = problems };
    }
    level.FormatVersion = CampaignLevelFormat.CurrentVersion;

    return new CampaignLevelMigrationResult { Problems = problems };
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
    level.UnitOverrides ??= [];
    foreach (CampaignUnitTemplateOverrideDefinition unitOverride in level.UnitOverrides)
    {
      unitOverride.StatOverrides ??= new CampaignUnitStatOverrides();
    }
    level.CustomUnits ??= [];
    foreach (CampaignCustomUnitDefinition customUnit in level.CustomUnits)
    {
      customUnit.StatOverrides ??= new CampaignUnitStatOverrides();
    }
    level.Teams ??= [];
    level.Formations ??= [];
    level.Scenario ??= new CampaignScenarioDefinition();
    level.Scenario.Territories ??= new CampaignTerritoriesDefinition();
    level.Scenario.Territories.NoMansLand ??= [];
    level.Scenario.Territories.TeamAreas ??= [];
    foreach (CampaignTeamAreaDefinition area in level.Scenario.Territories.TeamAreas)
    {
      area.Tiles ??= [];
    }
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
    foreach (CampaignTeamDefinition team in level.Teams)
    {
      team.CpuProfile ??= new CampaignCpuProfileDefinition();
    }
  }
}
