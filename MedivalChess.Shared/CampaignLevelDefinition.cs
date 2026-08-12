using System.Text.Json.Serialization;

namespace MedivalChess.Shared;

/// <summary>Portable, rendering-free definition of a custom campaign level.</summary>
public sealed class CampaignLevelDefinition
{
  public int FormatVersion { get; set; } = CampaignLevelFormat.CurrentVersion;
  /// <summary>A deterministic SHA-256 identity of this level's content, excluding this field.</summary>
  public string? Uid { get; set; }
  public CampaignLevelMetadata Metadata { get; set; } = new();
  public CampaignBoardDefinition Board { get; set; } = CampaignBoardDefinition.CreateRectangle(16, 12);
  public List<CampaignTerrainTileDefinition> Terrain { get; set; } = [];
  public List<CampaignRiverDefinition> Rivers { get; set; } = [];
  public List<CampaignBoardObjectDefinition> Objects { get; set; } = [];
  public List<CampaignUnitDefinition> Units { get; set; } = [];
  /// <summary>Level-local edits to built-in units. They keep native identifiers so existing placements and rules stay simple.</summary>
  public List<CampaignUnitTemplateOverrideDefinition> UnitOverrides { get; set; } = [];
  /// <summary>Author-defined unit templates that can be placed and optionally added to team buy lists.</summary>
  public List<CampaignCustomUnitDefinition> CustomUnits { get; set; } = [];
  public List<CampaignTeamDefinition> Teams { get; set; } = [];
  public List<CampaignFormationDefinition> Formations { get; set; } = [];
  public CampaignScenarioDefinition Scenario { get; set; } = new();
  public CampaignRestrictionsDefinition Restrictions { get; set; } = new();
  public List<CampaignReinforcementDefinition> Reinforcements { get; set; } = [];
  public List<CampaignScriptedEventDefinition> ScriptedEvents { get; set; } = [];

  /// <summary>Creates a level on the same medium battlefield used by a normal match.</summary>
  public static CampaignLevelDefinition CreateNew() => CreateNew(BoardRules.GetBoard("Medium"));

  /// <summary>Creates an explicit rectangular test/custom board when dimensions are requested.</summary>
  public static CampaignLevelDefinition CreateNew(int width, int height) =>
    CreateNew(CampaignBoardDefinition.CreateRectangle(width, height));

  private static CampaignLevelDefinition CreateNew(Board board) => CreateNew(new CampaignBoardDefinition
  {
    Shape = CampaignBoardShape.Custom,
    OriginX = board.MinX,
    OriginY = board.MinY,
    Width = board.BoardArray.GetLength(1),
    Height = board.BoardArray.GetLength(0),
    Tiles = board.Cells.Select(cell => new CampaignCoordinate(cell.x, cell.y)).ToList()
  });

  private static CampaignLevelDefinition CreateNew(CampaignBoardDefinition board)
  {
    string[] purchasable = UnitRules.Purchasable.Select(rule => rule.Type).ToArray();
    return new CampaignLevelDefinition
    {
      Metadata = new CampaignLevelMetadata { Name = "Untitled Campaign Level" },
      Board = board,
      Teams =
      [
        new CampaignTeamDefinition
        {
          Team = NetworkTeam.Red,
          Controller = CampaignTeamController.Human,
          StartingMoney = Globals.StartingCash,
          ActionsPerTurn = MatchRules.ActionsPerTurn,
          AvailableUnitTypes = [.. purchasable]
        },
        new CampaignTeamDefinition
        {
          Team = NetworkTeam.Blue,
          Controller = CampaignTeamController.Cpu,
          StartingMoney = Globals.StartingCash,
          ActionsPerTurn = MatchRules.ActionsPerTurn,
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
  public const int OldestSupportedVersion = 5;
  public const int CurrentVersion = 5;
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
  /// <summary>Expanded cells used by editor and game code. Files store these as compact row ranges.</summary>
  [JsonIgnore]
  public List<CampaignCoordinate> Tiles { get; set; } = [];

  /// <summary>Run-length encoded playable squares, one horizontal range per row segment.</summary>
  [JsonPropertyName("tileRanges")]
  public List<CampaignTileRange> TileRanges
  {
    get => CampaignTileRangeCodec.Encode(Tiles);
    set => Tiles = CampaignTileRangeCodec.Decode(value);
  }

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

/// <summary>An inclusive horizontal run of playable squares in a board row.</summary>
public sealed class CampaignTileRange
{
  public int X { get; set; }
  public int Y { get; set; }
  public int Length { get; set; }
}

internal static class CampaignTileRangeCodec
{
  internal static List<CampaignTileRange> Encode(IEnumerable<CampaignCoordinate>? tiles)
  {
    List<CampaignTileRange> ranges = [];
    foreach (IGrouping<int, CampaignCoordinate> row in (tiles ?? []).Distinct().OrderBy(tile => tile.Y).ThenBy(tile => tile.X).GroupBy(tile => tile.Y))
    {
      CampaignCoordinate? start = null;
      CampaignCoordinate? previous = null;
      foreach (CampaignCoordinate tile in row)
      {
        if (start is null || previous is null || tile.X != previous.X + 1)
        {
          if (start is not null && previous is not null)
          {
            ranges.Add(new CampaignTileRange { X = start.X, Y = start.Y, Length = previous.X - start.X + 1 });
          }
          start = tile;
        }
        previous = tile;
      }
      if (start is not null && previous is not null)
      {
        ranges.Add(new CampaignTileRange { X = start.X, Y = start.Y, Length = previous.X - start.X + 1 });
      }
    }
    return ranges;
  }

  internal static List<CampaignCoordinate> Decode(IEnumerable<CampaignTileRange>? ranges)
  {
    List<CampaignCoordinate> tiles = [];
    int remaining = CampaignLevelFormat.MaximumBoardTiles + 1;
    foreach (CampaignTileRange range in ranges ?? [])
    {
      // Validation reports malformed values after deserialisation; avoid allocating absurd input
      // before that boundary has a chance to run.
      int length = Math.Clamp(range.Length, 0, Math.Max(0, remaining));
      for (int offset = 0; offset < length; offset++)
      {
        tiles.Add(new CampaignCoordinate(range.X + offset, range.Y));
      }
      remaining -= length;
      if (remaining == 0) break;
    }
    return tiles;
  }
}

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
  /// <summary>Optional stat switches for this placed unit. Null leaves the source unit stat unchanged.</summary>
  public CampaignUnitStatOverrides? StatOverrides { get; set; }
}

/// <summary>Optional overrides applied to a standard or custom unit. Each nullable field is an on/off stat switch.</summary>
public sealed class CampaignUnitStatOverrides
{
  public int? MinimumMoveRange { get; set; }
  public int? MoveRange { get; set; }
  public Shape? MovePattern { get; set; }
  public int? Attack { get; set; }
  public int? Health { get; set; }
  public int? Width { get; set; }
  public int? Height { get; set; }
  public int? MinimumAttackRange { get; set; }
  public int? MaximumAttackRange { get; set; }
  public Shape? AttackPattern { get; set; }
  public int? Cost { get; set; }
}

/// <summary>Reusable campaign unit template. Its selected ability source also supplies special runtime behaviour.</summary>
public sealed class CampaignCustomUnitDefinition
{
  public string Id { get; set; } = "custom-unit";
  public string Name { get; set; } = "Custom Unit";
  public string Abbreviation { get; set; } = "CU";
  /// <summary>The standard unit whose category and baseline stats are used before applying overrides.</summary>
  public string BaseUnitType { get; set; } = "Soldier";
  /// <summary>The standard unit whose special ability behaviour is copied.</summary>
  public string AbilitySourceUnitType { get; set; } = "Soldier";
  public CampaignUnitStatOverrides StatOverrides { get; set; } = new();
  public bool Purchasable { get; set; } = true;
}

/// <summary>Editable level-local version of a built-in unit. Its ID is always a native unit identifier.</summary>
public sealed class CampaignUnitTemplateOverrideDefinition
{
  public string UnitType { get; set; } = "Soldier";
  public string Name { get; set; } = "Soldier";
  public string Abbreviation { get; set; } = "So";
  /// <summary>A built-in unit whose special behaviour is borrowed, or <c>None</c> for no special ability.</summary>
  public string AbilitySourceUnitType { get; set; } = "Soldier";
  public CampaignUnitStatOverrides StatOverrides { get; set; } = new();
  public bool Purchasable { get; set; } = true;
}

public enum CampaignTeamController
{
  Human,
  Cpu
}

/// <summary>Editor-friendly preset for a team's purchasable-unit list.</summary>
public enum CampaignPurchaseListMode
{
  All,
  Custom,
  None
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
  public int ActionsPerTurn { get; set; } = MatchRules.ActionsPerTurn;
  public bool PurchasesEnabled { get; set; } = true;
  public CampaignPurchaseListMode PurchaseListMode { get; set; } = CampaignPurchaseListMode.All;
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
  /// <summary>
  /// Optional authored ownership map. When disabled, the normal match territory rules are used.
  /// Enabling it lets a level author paint every team's deployment area and No-Man's-Land.
  /// </summary>
  public CampaignTerritoriesDefinition Territories { get; set; } = new();
  /// <summary>Null means no turn limit.</summary>
  public int? TurnLimit { get; set; }
  public List<CampaignObjectiveDefinition> VictoryConditions { get; set; } = [];
  public List<CampaignObjectiveDefinition> DefeatConditions { get; set; } = [];
}

/// <summary>
/// A complete, explicit territory map for a campaign board. A tile belongs to exactly one team
/// area or to No-Man's-Land when <see cref="UseCustomAreas"/> is enabled.
/// </summary>
public sealed class CampaignTerritoriesDefinition
{
  public bool UseCustomAreas { get; set; }
  public List<CampaignCoordinate> NoMansLand { get; set; } = [];
  public List<CampaignTeamAreaDefinition> TeamAreas { get; set; } = [];
}

public sealed class CampaignTeamAreaDefinition
{
  public NetworkTeam Team { get; set; }
  public List<CampaignCoordinate> Tiles { get; set; } = [];
}

public sealed class CampaignRestrictionsDefinition
{
  public bool PurchasesEnabled { get; set; } = true;
  public bool AbilitiesEnabled { get; set; } = true;
  public List<string> AllowedPacks { get; set; } = [.. PackRules.AllNames];
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
