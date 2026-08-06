namespace MedivalChess.Shared;

/// <summary>Portable, rendering-free definition of a custom campaign level.</summary>
public sealed class CampaignLevelDefinition
{
  public int FormatVersion { get; set; } = CampaignLevelFormat.CurrentVersion;
  public CampaignLevelMetadata Metadata { get; set; } = new();
  public CampaignBoardDefinition Board { get; set; } = CampaignBoardDefinition.CreateRectangle(16, 12);
  public List<CampaignTerrainTileDefinition> Terrain { get; set; } = [];
  public List<CampaignRiverDefinition> Rivers { get; set; } = [];
  public List<CampaignBoardObjectDefinition> Objects { get; set; } = [];
  public List<CampaignUnitDefinition> Units { get; set; } = [];
  public List<CampaignTeamDefinition> Teams { get; set; } = [];
  public List<CampaignFormationDefinition> Formations { get; set; } = [];
  public CampaignScenarioDefinition Scenario { get; set; } = new();
  public CampaignRestrictionsDefinition Restrictions { get; set; } = new();
  public List<CampaignReinforcementDefinition> Reinforcements { get; set; } = [];
  public List<CampaignScriptedEventDefinition> ScriptedEvents { get; set; } = [];

  public static CampaignLevelDefinition CreateNew(int width = 16, int height = 12)
  {
    string[] purchasable = UnitRules.Purchasable.Select(rule => rule.Type).ToArray();
    return new CampaignLevelDefinition
    {
      Metadata = new CampaignLevelMetadata { Name = "Untitled Campaign Level" },
      Board = CampaignBoardDefinition.CreateRectangle(width, height),
      Teams =
      [
        new CampaignTeamDefinition
        {
          Team = NetworkTeam.Red,
          Controller = CampaignTeamController.Human,
          StartingMoney = Globals.StartingCash,
          AvailableUnitTypes = [.. purchasable]
        },
        new CampaignTeamDefinition
        {
          Team = NetworkTeam.Blue,
          Controller = CampaignTeamController.Cpu,
          StartingMoney = Globals.StartingCash,
          AvailableUnitTypes = [.. purchasable]
        }
      ],
      Scenario = new CampaignScenarioDefinition
      {
        FirstTeam = NetworkTeam.Red,
        VictoryConditions =
        [
          new CampaignObjectiveDefinition { Type = CampaignObjectiveType.DefeatEnemyRoyal }
        ]
      }
    };
  }
}

public static class CampaignLevelFormat
{
  public const int OldestSupportedVersion = 1;
  public const int CurrentVersion = 2;
  public const string Extension = ".mclvl";
  public const int MaximumFileBytes = 5 * 1024 * 1024;
  public const int MaximumBoardTiles = 4_096;
  public const int MaximumBoardDimension = 128;
}

public sealed class CampaignLevelMetadata
{
  public string Name { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string Author { get; set; } = string.Empty;
  public string Difficulty { get; set; } = "Normal";
  public string? CampaignDialogue { get; set; }
}

public enum CampaignBoardShape
{
  Rectangle,
  Custom
}

public sealed class CampaignBoardDefinition
{
  public CampaignBoardShape Shape { get; set; } = CampaignBoardShape.Rectangle;
  public int OriginX { get; set; }
  public int OriginY { get; set; }
  public int Width { get; set; }
  public int Height { get; set; }
  public List<CampaignCoordinate> Tiles { get; set; } = [];

  public static CampaignBoardDefinition CreateRectangle(int width, int height, int originX = 0, int originY = 0)
  {
    CampaignBoardDefinition board = new()
    {
      Shape = CampaignBoardShape.Rectangle,
      Width = width,
      Height = height,
      OriginX = originX,
      OriginY = originY
    };
    for (int y = 0; y < height; y++)
    for (int x = 0; x < width; x++)
    {
      board.Tiles.Add(new CampaignCoordinate(originX + x, originY + y));
    }

    return board;
  }

  public Board ToBoard() => new(Tiles.Select(tile => (tile.X, tile.Y)));
}

public sealed record CampaignCoordinate(int X, int Y);

public enum CampaignTerrainType
{
  Forest,
  Lake
}

public sealed class CampaignTerrainTileDefinition
{
  public CampaignTerrainType Type { get; set; }
  public CampaignCoordinate Position { get; set; } = new(0, 0);
}

public sealed class CampaignRiverDefinition
{
  public CampaignCoordinate First { get; set; } = new(0, 0);
  public CampaignCoordinate Second { get; set; } = new(0, 0);
}

public enum CampaignBoardObjectType
{
  Road,
  Barrier,
  Mine,
  Bridge,
  SpawnPoint,
  ObjectiveMarker,
  Treasure
}

public sealed class CampaignBoardObjectDefinition
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public CampaignBoardObjectType Type { get; set; }
  public CampaignCoordinate Position { get; set; } = new(0, 0);
  public NetworkTeam? Owner { get; set; }
  public int? Health { get; set; }
  public int Rotation { get; set; }
  public Dictionary<string, string> Properties { get; set; } = [];
}

public enum CampaignUnitRotation
{
  Degrees0 = 0,
  Degrees90 = 90,
  Degrees180 = 180,
  Degrees270 = 270
}

public sealed class CampaignUnitDefinition
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  /// <summary>Stable UnitRules identifier; balance values remain owned by the game.</summary>
  public string UnitType { get; set; } = "Soldier";
  public NetworkTeam Team { get; set; }
  public CampaignCoordinate Position { get; set; } = new(0, 0);
  /// <summary>Null uses the unit's current default health from UnitRules.</summary>
  public int? Health { get; set; }
  public CampaignUnitRotation Rotation { get; set; }
}

public enum CampaignTeamController
{
  Human,
  Cpu
}

public sealed class CampaignCpuProfileDefinition
{
  public string Difficulty { get; set; } = "Medium";
  public string Personality { get; set; } = "Balanced";
}

public sealed class CampaignTeamDefinition
{
  public NetworkTeam Team { get; set; }
  public CampaignTeamController Controller { get; set; }
  public int StartingMoney { get; set; } = Globals.StartingCash;
  public int ActionsPerTurn { get; set; } = 2;
  public bool PurchasesEnabled { get; set; } = true;
  public string? ChosenRoyal { get; set; }
  public List<string> AvailableUnitTypes { get; set; } = [];
  public List<string> DisabledAbilityUnitTypes { get; set; } = [];
  public CampaignCpuProfileDefinition CpuProfile { get; set; } = new();
}

public sealed class CampaignFormationDefinition
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public string Name { get; set; } = string.Empty;
  public NetworkTeam Team { get; set; }
  public List<string> UnitIds { get; set; } = [];
}

public enum CampaignObjectiveType
{
  DefeatEnemyRoyal,
  Conquest,
  EscapeRoyal,
  Dominion,
  Plunder,
  EliminateEnemies,
  SurviveTurns,
  CaptureLocations,
  EscortUnit,
  GetUnitsToLocations,
  ProtectUnit,
  PreventEscape,
  Score,
  ReachCash
}

public sealed class CampaignUnitLocationTargetDefinition
{
  public string UnitId { get; set; } = string.Empty;
  public CampaignCoordinate Location { get; set; } = new(0, 0);
}

public sealed class CampaignObjectiveDefinition
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public CampaignObjectiveType Type { get; set; }
  public NetworkTeam? Team { get; set; }
  public string? TargetUnitId { get; set; }
  public List<CampaignCoordinate> Locations { get; set; } = [];
  /// <summary>Explicit unit-to-square mappings used by GetUnitsToLocations objectives.</summary>
  public List<CampaignUnitLocationTargetDefinition> UnitLocationTargets { get; set; } = [];
  public int RequiredAmount { get; set; } = 1;
  public string Description { get; set; } = string.Empty;
}

public sealed class CampaignScenarioDefinition
{
  public string GameMode { get; set; } = "Regicide";
  public NetworkTeam FirstTeam { get; set; } = NetworkTeam.Red;
  /// <summary>Null means no turn limit.</summary>
  public int? TurnLimit { get; set; }
  public List<CampaignObjectiveDefinition> VictoryConditions { get; set; } = [];
  public List<CampaignObjectiveDefinition> DefeatConditions { get; set; } = [];
}

public sealed class CampaignRestrictionsDefinition
{
  public bool PurchasesEnabled { get; set; } = true;
  public bool AbilitiesEnabled { get; set; } = true;
  /// <summary>Empty means use the team's available unit list.</summary>
  public List<string> AllowedUnitTypes { get; set; } = [];
  public List<string> DisabledUnitTypes { get; set; } = [];
  public List<string> DisabledAbilityUnitTypes { get; set; } = [];
}

public sealed class CampaignReinforcementDefinition
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public int ArrivesOnTurn { get; set; }
  public NetworkTeam Team { get; set; }
  public List<CampaignUnitDefinition> Units { get; set; } = [];
}

/// <summary>
/// Kept in the portable format for future campaign engines.  The current match
/// controller intentionally reports these as unsupported instead of pretending to run them.
/// </summary>
public sealed class CampaignScriptedEventDefinition
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public string Trigger { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public Dictionary<string, string> Parameters { get; set; } = [];
}
