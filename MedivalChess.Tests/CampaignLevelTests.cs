using MedivalChess.Campaign;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CampaignLevelTests
{
  [Fact]
  public void SerializingAndLoadingPreservesCampaignData()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Metadata.Author = "The Cartographer";
    level.Metadata.Description = "Secure the northern crossing.";
    level.Terrain.Add(new CampaignTerrainTileDefinition
    {
      Type = CampaignTerrainType.Forest,
      Position = new CampaignCoordinate(2, 2)
    });
    level.Objects.Add(new CampaignBoardObjectDefinition
    {
      Id = "road-a",
      Type = CampaignBoardObjectType.Road,
      Position = new CampaignCoordinate(1, 1)
    });
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "red-soldier",
      UnitType = "Swordsman",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0),
      Health = 12
    });

    CampaignLevelLoadResult result = CampaignLevelSerializer.Deserialize(CampaignLevelSerializer.Serialize(level));

    Assert.True(result.IsSuccess, FormatProblems(result.Problems));
    Assert.NotNull(result.Level);
    Assert.Equal("The Cartographer", result.Level.Metadata.Author);
    Assert.Equal("Secure the northern crossing.", result.Level.Metadata.Description);
    Assert.Single(result.Level.Terrain);
    Assert.Single(result.Level.Objects);
    CampaignUnitDefinition unit = Assert.Single(result.Level.Units);
    Assert.Equal("Swordsman", unit.UnitType);
    Assert.Equal(12, unit.Health);
  }

  [Fact]
  public void RejectsMalformedAndUnknownFutureFiles()
  {
    CampaignLevelLoadResult malformed = CampaignLevelSerializer.Deserialize("{ not json }");
    CampaignLevelLoadResult future = CampaignLevelSerializer.Deserialize("{\"formatVersion\": 99}");

    Assert.False(malformed.IsSuccess);
    Assert.Contains(malformed.Problems, problem => problem.Code == "format.json");
    Assert.False(future.IsSuccess);
    Assert.Contains(future.Problems, problem => problem.Code == "format.future");
  }

  [Fact]
  public void CpuDifficultyAndPersonalityAreStoredAsIndependentValidatedChoices()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    CampaignTeamDefinition cpu = level.Teams.Single(team => team.Team == NetworkTeam.Blue);
    cpu.Controller = CampaignTeamController.Cpu;
    cpu.CpuProfile.Difficulty = "Hard";
    cpu.CpuProfile.Personality = "Defensive";

    CampaignLevelLoadResult valid = CampaignLevelSerializer.Deserialize(CampaignLevelSerializer.Serialize(level));

    Assert.True(valid.IsSuccess, FormatProblems(valid.Problems));
    Assert.Equal("Hard", valid.Level!.Teams.Single(team => team.Team == NetworkTeam.Blue).CpuProfile.Difficulty);
    Assert.Equal("Defensive", valid.Level.Teams.Single(team => team.Team == NetworkTeam.Blue).CpuProfile.Personality);

    cpu.CpuProfile.Personality = "Unknown";
    CampaignValidationResult invalid = CampaignLevelValidator.Validate(level);
    Assert.Contains(invalid.Problems, problem => problem.Code == "team.cpu.personality");
  }

  [Fact]
  public void RejectsOlderFormatsBecauseTheCompactRangeFormatIsRequired()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    string versionOne = CampaignLevelSerializer.Serialize(level)
      .Replace($"\"formatVersion\": {CampaignLevelFormat.CurrentVersion}", "\"formatVersion\": 1", StringComparison.Ordinal);

    CampaignLevelLoadResult result = CampaignLevelSerializer.Deserialize(versionOne);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Problems, problem => problem.Code == "format.old");
  }

  [Fact]
  public void ValidatorReportsInvalidUnitPositionMissingObjectivesDuplicateObjectsAndUnknownRestrictions()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Scenario.VictoryConditions.Clear();
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "outside",
      UnitType = "Swordsman",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(99, 99)
    });
    level.Objects.Add(new CampaignBoardObjectDefinition
    {
      Id = "twice",
      Type = CampaignBoardObjectType.Road,
      Position = new CampaignCoordinate(1, 1)
    });
    level.Objects.Add(new CampaignBoardObjectDefinition
    {
      Id = "twice",
      Type = CampaignBoardObjectType.Road,
      Position = new CampaignCoordinate(2, 1)
    });
    level.Restrictions.DisabledUnitTypes.Add("AncientDragon");

    CampaignValidationResult result = CampaignLevelValidator.Validate(level);

    Assert.False(result.IsValid);
    Assert.Contains(result.Problems, problem => problem.Code == "unit.bounds");
    Assert.Contains(result.Problems, problem => problem.Code == "scenario.objectives");
    Assert.Contains(result.Problems, problem => problem.Code == "object.id.duplicate");
    Assert.Contains(result.Problems, problem => problem.Code == "restriction.disabledUnits");
  }

  [Fact]
  public void ValidatorRejectsImpossibleFormation()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "blue-soldier",
      UnitType = "Swordsman",
      Team = NetworkTeam.Blue,
      Position = new CampaignCoordinate(5, 5)
    });
    level.Formations.Add(new CampaignFormationDefinition
    {
      Id = "red-line",
      Team = NetworkTeam.Red,
      UnitIds = ["blue-soldier", "missing-unit"]
    });

    CampaignValidationResult result = CampaignLevelValidator.Validate(level);

    Assert.False(result.IsValid);
    Assert.Contains(result.Problems, problem => problem.Code == "formation.unit.team");
    Assert.Contains(result.Problems, problem => problem.Code == "formation.unit");
  }

  [Fact]
  public void ValidatorRequiresTeamsAndMappedUnitsForLocationObjectives()
  {
    CampaignLevelDefinition missingTeams = CreateValidLevel();
    missingTeams.Teams.Clear();
    CampaignValidationResult missingTeamResult = CampaignLevelValidator.Validate(missingTeams);

    CampaignLevelDefinition level = CreateValidLevel();
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "runner",
      UnitType = "Swordsman",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0)
    });
    level.Scenario.VictoryConditions =
    [
      new CampaignObjectiveDefinition { Id = "reach", Type = CampaignObjectiveType.GetUnitsToLocations, Team = NetworkTeam.Red }
    ];
    CampaignValidationResult noTargets = CampaignLevelValidator.Validate(level);
    level.Scenario.VictoryConditions[0].UnitLocationTargets.Add(new CampaignUnitLocationTargetDefinition
    {
      UnitId = "runner",
      Location = new CampaignCoordinate(4, 4)
    });
    CampaignValidationResult validTarget = CampaignLevelValidator.Validate(level);

    Assert.Contains(missingTeamResult.Problems, problem => problem.Code == "teams.count");
    Assert.Contains(noTargets.Problems, problem => problem.Code == "objective.victory.unitLocations");
    Assert.True(validTarget.IsValid, FormatProblems(validTarget.Problems));
  }

  [Fact]
  public void LevelFilesUseStableUnitIdentifiersRatherThanBalanceStatistics()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "stable-unit",
      UnitType = "Swordsman",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0)
    });

    string json = CampaignLevelSerializer.Serialize(level);
    CampaignLevelLoadResult loaded = CampaignLevelSerializer.Deserialize(json);

    Assert.DoesNotContain("\"attack\"", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("\"health\"", json, StringComparison.OrdinalIgnoreCase);
    Assert.True(loaded.IsSuccess, FormatProblems(loaded.Problems));
    Assert.Null(Assert.Single(loaded.Level!.Units).Health);
    Assert.Equal("Swordsman", loaded.Level.Units[0].UnitType);
  }

  [Fact]
  public void CompactBoardRangesAndContentUidRoundTrip()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Board = CampaignBoardDefinition.CreateRectangle(32, 12);

    string json = CampaignLevelSerializer.Serialize(level);
    CampaignLevelLoadResult loaded = CampaignLevelSerializer.Deserialize(json);

    Assert.Contains("\"tileRanges\"", json, StringComparison.Ordinal);
    Assert.DoesNotContain("\"tiles\"", json, StringComparison.Ordinal);
    Assert.True(loaded.IsSuccess, FormatProblems(loaded.Problems));
    Assert.Equal(level.Board.Tiles, loaded.Level!.Board.Tiles);
    Assert.False(string.IsNullOrWhiteSpace(loaded.Level.Uid));
    Assert.Equal(CampaignLevelSerializer.CalculateUid(loaded.Level), loaded.Level.Uid);

    string firstUid = loaded.Level.Uid!;
    loaded.Level.Metadata.Name = "Changed";
    string changedJson = CampaignLevelSerializer.Serialize(loaded.Level);
    Assert.NotEqual(firstUid, loaded.Level.Uid);
    Assert.Contains(loaded.Level.Uid!, changedJson, StringComparison.Ordinal);
  }

  [Fact]
  public void CustomUnitsCanOverrideStatsAndCopyAnotherUnitsAbility()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.CustomUnits.Add(new CampaignCustomUnitDefinition
    {
      Id = "siege-scout",
      Name = "Siege Scout",
      BaseUnitType = "Swordsman",
      AbilitySourceUnitType = "Ballista",
      StatOverrides = new CampaignUnitStatOverrides { MoveRange = 5, Attack = 7, Cost = 42 }
    });
    level.Teams[0].AvailableUnitTypes.Add("siege-scout");
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "custom-unit",
      UnitType = "siege-scout",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0)
    });

    CampaignLevelLoadResult result = CampaignLevelSerializer.Deserialize(CampaignLevelSerializer.Serialize(level));

    Assert.True(result.IsSuccess, FormatProblems(result.Problems));
    Assert.True(CampaignUnitResolver.TryResolve(result.Level!, "siege-scout", null, out PieceDefinition definition));
    Assert.Equal(PieceType.Ballista, definition.Type);
    Assert.Equal(5, definition.Movement.range);
    Assert.Equal(7, definition.Attack);
    Assert.Equal(42, definition.Cost);
  }

  [Fact]
  public void CustomUnitCannotReplaceABuiltInUnitIdentifier()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.CustomUnits.Add(new CampaignCustomUnitDefinition
    {
      Id = "Swordsman",
      Name = "Not a Soldier",
      BaseUnitType = "Swordsman",
      AbilitySourceUnitType = "Swordsman"
    });

    CampaignValidationResult result = CampaignLevelValidator.Validate(level);

    Assert.Contains(result.Problems, problem => problem.Code == "customUnit.id");
  }

  [Fact]
  public void BuiltInUnitCatalogueOverridesAreStoredAndDriveRuntimeAndBuying()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.UnitOverrides.Add(new CampaignUnitTemplateOverrideDefinition
    {
      UnitType = "Archer",
      Name = "Longbow",
      Abbreviation = "LB",
      AbilitySourceUnitType = "None",
      Purchasable = false,
      StatOverrides = new CampaignUnitStatOverrides
      {
        MoveRange = 4,
        Health = 12,
        Attack = 17,
        MinimumAttackRange = 3,
        MaximumAttackRange = 6,
        AttackPattern = Shape.Line,
        Cost = 33
      }
    });

    CampaignLevelLoadResult result = CampaignLevelSerializer.Deserialize(CampaignLevelSerializer.Serialize(level));

    Assert.True(result.IsSuccess, FormatProblems(result.Problems));
    Assert.True(CampaignUnitResolver.TryResolve(result.Level!, "Archer", null, out PieceDefinition archer));
    Assert.Equal("Longbow", archer.DisplayName);
    Assert.Equal("LB", archer.Abbreviation);
    Assert.Equal(4, archer.Movement.range);
    Assert.Equal(17, archer.Attack);
    Assert.Equal(12, archer.Health);
    Assert.Equal(new AttackRange(3, 6), archer.AttackRange);
    Assert.Equal(Shape.Line, archer.AttackPattern);
    Assert.Equal(33, archer.Cost);
    Assert.DoesNotContain("Archer", CampaignUnitResolver.GetPurchasableIdentifiers(result.Level!));
  }

  [Fact]
  public void SaveAndLoadUsePortableMclvlFiles()
  {
    string path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}{CampaignLevelFormat.Extension}");
    try
    {
      CampaignLevelSaveResult save = CampaignLevelSerializer.Save(path, CreateValidLevel());
      CampaignLevelLoadResult load = CampaignLevelSerializer.Load(path);

      Assert.True(save.IsSuccess, FormatProblems(save.Problems));
      Assert.True(load.IsSuccess, FormatProblems(load.Problems));
      Assert.Equal("Bridgehead", load.Level!.Metadata.Name);
    }
    finally
    {
      if (File.Exists(path)) File.Delete(path);
    }
  }

  [Fact]
  public void SavingAnExistingLevelReplacesItWithoutLeavingAPartialFile()
  {
    string path = Path.Combine(Path.GetTempPath(), $"campaign-overwrite-{Guid.NewGuid():N}{CampaignLevelFormat.Extension}");
    try
    {
      CampaignLevelDefinition first = CreateValidLevel();
      CampaignLevelDefinition second = CreateValidLevel();
      second.Metadata.Name = "Replaced Export";

      Assert.True(CampaignLevelSerializer.Save(path, first).IsSuccess);
      Assert.True(CampaignLevelSerializer.Save(path, second).IsSuccess);
      CampaignLevelLoadResult loaded = CampaignLevelSerializer.Load(path);

      Assert.True(loaded.IsSuccess, FormatProblems(loaded.Problems));
      Assert.Equal("Replaced Export", loaded.Level!.Metadata.Name);
      Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"));
    }
    finally
    {
      if (File.Exists(path)) File.Delete(path);
    }
  }

  [Fact]
  public void ExportNamesAreSafeForWindowsReservedAndInvalidFileNames()
  {
    Assert.Equal("_CON.mclvl", LevelFilePicker.CreateSafeLevelFileName("CON"));
    Assert.Equal("Untitled.mclvl", LevelFilePicker.CreateSafeLevelFileName("..."));
  }

  [Fact]
  public void CustomTerritoriesRoundTripAndReplaceTheAutomaticZoneRules()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Board = CampaignBoardDefinition.CreateRectangle(12, 12);
    Board board = level.Board.ToBoard();
    level.Scenario.Territories = CampaignTerritoryRules.CreateDefaultAreas(board, level.Scenario.GameMode, playerCount: 2);
    CampaignCoordinate movedToNoMansLand = new(0, 0);
    level.Scenario.Territories.TeamAreas.Single(area => area.Team == NetworkTeam.Blue).Tiles.Remove(movedToNoMansLand);
    level.Scenario.Territories.NoMansLand.Add(movedToNoMansLand);

    CampaignLevelLoadResult result = CampaignLevelSerializer.Deserialize(CampaignLevelSerializer.Serialize(level));

    Assert.True(result.IsSuccess, FormatProblems(result.Problems));
    Assert.True(result.Level!.Scenario.Territories.UseCustomAreas);
    Assert.Null(CampaignTerritoryRules.GetSquareOwner(board, result.Level.Scenario, (0, 0), 2));
    Assert.Equal(NetworkTeam.Red, CampaignTerritoryRules.GetSquareOwner(board, result.Level.Scenario, (0, 11), 2));
  }

  [Fact]
  public void CustomTerritoriesMustAssignEveryPlayableTileExactlyOnce()
  {
    CampaignLevelDefinition level = CreateValidLevel();
    level.Scenario.Territories.UseCustomAreas = true;

    CampaignValidationResult result = CampaignLevelValidator.Validate(level);

    Assert.Contains(result.Problems, problem => problem.Code == "territory.area.missing");
    Assert.Contains(result.Problems, problem => problem.Code == "territory.coverage");
  }

  private static CampaignLevelDefinition CreateValidLevel()
  {
    CampaignLevelDefinition level = CampaignLevelDefinition.CreateNew(6, 6);
    level.Metadata.Name = "Bridgehead";
    level.Scenario.VictoryConditions =
    [
      new CampaignObjectiveDefinition
      {
        Id = "defeat-royal",
        Type = CampaignObjectiveType.DefeatEnemyRoyal
      }
    ];
    return level;
  }

  private static string FormatProblems(IEnumerable<CampaignValidationProblem> problems) =>
    string.Join(Environment.NewLine, problems.Select(problem => $"{problem.Code}: {problem.Message}"));
}
