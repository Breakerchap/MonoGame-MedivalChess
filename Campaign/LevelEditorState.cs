#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.Shared;

namespace MedivalChess.Campaign;

public enum EditorTool
{
  Select,
  Tile,
  Unit,
  Move,
  Terrain,
  Object,
  Delete
}

public enum EditorSelectionKind
{
  None,
  Unit,
  Object,
  Terrain
}

public sealed record EditorSelection(EditorSelectionKind Kind, string? Id = null, CampaignCoordinate? Position = null)
{
  public static EditorSelection None { get; } = new(EditorSelectionKind.None);
}

public sealed record EditorHistoryEntry(string Description);

/// <summary>
/// Snapshot history for editor operations.  It owns only serialisable campaign data,
/// never MonoGame UI or runtime piece instances.
/// </summary>
public sealed class EditorHistory
{
  private readonly List<(EditorHistoryEntry entry, CampaignLevelDefinition snapshot)> _undo = [];
  private readonly List<(EditorHistoryEntry entry, CampaignLevelDefinition snapshot)> _redo = [];

  public int MaximumEntries { get; init; } = 100;
  public bool CanUndo => _undo.Count > 0;
  public bool CanRedo => _redo.Count > 0;
  public string? UndoDescription => CanUndo ? _undo[^1].entry.Description : null;
  public string? RedoDescription => CanRedo ? _redo[^1].entry.Description : null;

  public void Record(CampaignLevelDefinition current, string description)
  {
    _undo.Add((new EditorHistoryEntry(description), CampaignLevelCloner.Clone(current)));
    if (_undo.Count > MaximumEntries)
    {
      _undo.RemoveAt(0);
    }
    _redo.Clear();
  }

  public CampaignLevelDefinition? Undo(CampaignLevelDefinition current)
  {
    if (!CanUndo) return null;
    (EditorHistoryEntry entry, CampaignLevelDefinition snapshot) = _undo[^1];
    _undo.RemoveAt(_undo.Count - 1);
    _redo.Add((entry, CampaignLevelCloner.Clone(current)));
    return CampaignLevelCloner.Clone(snapshot);
  }

  public CampaignLevelDefinition? Redo(CampaignLevelDefinition current)
  {
    if (!CanRedo) return null;
    (EditorHistoryEntry entry, CampaignLevelDefinition snapshot) = _redo[^1];
    _redo.RemoveAt(_redo.Count - 1);
    _undo.Add((entry, CampaignLevelCloner.Clone(current)));
    return CampaignLevelCloner.Clone(snapshot);
  }

  public void Clear()
  {
    _undo.Clear();
    _redo.Clear();
  }
}

public static class CampaignLevelCloner
{
  public static CampaignLevelDefinition Clone(CampaignLevelDefinition level)
  {
    ArgumentNullException.ThrowIfNull(level);
    CampaignLevelLoadResult result = CampaignLevelSerializer.Deserialize(CampaignLevelSerializer.Serialize(level));
    return result.Level ?? throw new InvalidOperationException("Could not clone campaign level data.");
  }
}

public sealed class LevelEditorState
{
  public CampaignLevelDefinition Level { get; private set; }
  public EditorHistory History { get; } = new();
  public EditorTool ActiveTool { get; set; } = EditorTool.Select;
  public EditorSelection Selection { get; private set; } = EditorSelection.None;
  public string? SourcePath { get; private set; }
  public bool HasUnsavedChanges { get; private set; }

  public LevelEditorState(CampaignLevelDefinition level, string? sourcePath = null)
  {
    ArgumentNullException.ThrowIfNull(level);
    Level = CampaignLevelCloner.Clone(level);
    SourcePath = sourcePath;
  }

  public static LevelEditorState CreateNew(int width = 16, int height = 12) =>
    new(CampaignLevelDefinition.CreateNew(width, height));

  public CampaignValidationResult Validate() => CampaignLevelValidator.Validate(Level);

  public void SelectUnit(string? unitId) => Selection = string.IsNullOrWhiteSpace(unitId)
    ? EditorSelection.None
    : new EditorSelection(EditorSelectionKind.Unit, unitId);

  public void SelectObject(string? objectId) => Selection = string.IsNullOrWhiteSpace(objectId)
    ? EditorSelection.None
    : new EditorSelection(EditorSelectionKind.Object, objectId);

  public void SelectTerrain(CampaignCoordinate? position) => Selection = position is null
    ? EditorSelection.None
    : new EditorSelection(EditorSelectionKind.Terrain, Position: position);

  public void AddTile(CampaignCoordinate position)
  {
    if (Level.Board.Tiles.Any(tile => tile.X == position.X && tile.Y == position.Y)) return;
    Change("Add tile", level =>
    {
      level.Board.Shape = CampaignBoardShape.Custom;
      level.Board.Tiles.Add(position);
      RecalculateBoardBounds(level.Board);
    });
  }

  public void RemoveTile(CampaignCoordinate position)
  {
    if (!Level.Board.Tiles.Any(tile => tile.X == position.X && tile.Y == position.Y)) return;
    Change("Remove tile", level =>
    {
      level.Board.Shape = CampaignBoardShape.Custom;
      level.Board.Tiles.RemoveAll(tile => tile.X == position.X && tile.Y == position.Y);
      RecalculateBoardBounds(level.Board);
    });
  }

  public void SetBoardSize(int width, int height)
  {
    if (width is < 1 or > CampaignLevelFormat.MaximumBoardDimension ||
        height is < 1 or > CampaignLevelFormat.MaximumBoardDimension)
    {
      throw new ArgumentOutOfRangeException(nameof(width), "Board dimensions must be within the supported range.");
    }
    Change("Resize board", level => level.Board = CampaignBoardDefinition.CreateRectangle(
      width,
      height,
      level.Board.OriginX,
      level.Board.OriginY));
  }

  public void SetBoardShape(CampaignBoardShape shape)
  {
    if (Level.Board.Shape == shape) return;
    Change("Change board shape", level => level.Board.Shape = shape);
  }

  /// <summary>
  /// Uses one of the shipped game boards as a portable campaign board.  We copy its cells rather
  /// than retaining a file reference so exported levels remain self-contained.
  /// </summary>
  public void UseBoardBase(Board board)
  {
    ArgumentNullException.ThrowIfNull(board);
    Change("Use shipped board base", level =>
    {
      level.Board = new CampaignBoardDefinition
      {
        Shape = CampaignBoardShape.Custom,
        OriginX = board.MinX,
        OriginY = board.MinY,
        Width = board.BoardArray.GetLength(1),
        Height = board.BoardArray.GetLength(0),
        Tiles = board.Cells.Select(cell => new CampaignCoordinate(cell.x, cell.y)).ToList()
      };
    });
  }

  public void PlaceUnit(CampaignUnitDefinition unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Change("Place unit", level =>
    {
      CampaignUnitDefinition copy = new()
      {
        Id = unit.Id,
        UnitType = unit.UnitType,
        Team = unit.Team,
        Position = unit.Position,
        Health = unit.Health,
        Rotation = unit.Rotation
      };
      if (string.IsNullOrWhiteSpace(copy.Id) || level.Units.Any(existing => existing.Id == copy.Id))
      {
        copy.Id = Guid.NewGuid().ToString("N");
      }
      level.Units.Add(copy);
      Selection = new EditorSelection(EditorSelectionKind.Unit, copy.Id);
    });
  }

  /// <summary>
  /// Checks the same immediate placement constraints used by the runtime validator so the editor
  /// does not let the user accidentally build an impossible starting position.
  /// </summary>
  public bool CanPlaceUnit(CampaignUnitDefinition unit, out string reason, string? ignoredUnitId = null)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (!UnitRules.TryGet(unit.UnitType, out UnitRule rule))
    {
      reason = $"Unknown unit type: {unit.UnitType}.";
      return false;
    }
    if (unit.Team == NetworkTeam.Neutral && unit.UnitType != "Mercenary")
    {
      reason = "Only Mercenaries can start neutral.";
      return false;
    }
    if (unit.Team != NetworkTeam.Neutral && !Level.Teams.Any(team => team.Team == unit.Team))
    {
      reason = $"{unit.Team} is not configured for this level.";
      return false;
    }

    HashSet<CampaignCoordinate> boardCells = Level.Board.Tiles.ToHashSet();
    HashSet<CampaignCoordinate> lakes = Level.Terrain
      .Where(terrain => terrain.Type == CampaignTerrainType.Lake)
      .Select(terrain => terrain.Position)
      .ToHashSet();
    for (int y = 0; y < rule.Height; y++)
    for (int x = 0; x < rule.Width; x++)
    {
      CampaignCoordinate square = new(unit.Position.X + x, unit.Position.Y + y);
      if (!boardCells.Contains(square))
      {
        reason = $"{unit.UnitType} does not fit on the board at ({square.X}, {square.Y}).";
        return false;
      }
      if (unit.UnitType != "Elephant" && lakes.Contains(square))
      {
        reason = $"{unit.UnitType} cannot start on a lake.";
        return false;
      }
    }

    foreach (CampaignUnitDefinition other in Level.Units.Where(other => other.Id != ignoredUnitId))
    {
      if (!UnitRules.TryGet(other.UnitType, out UnitRule otherRule)) continue;
      if (unit.UnitType == "Farm" || other.UnitType == "Farm") continue;
      if (UnitRules.FootprintsOverlap(
        unit.Position.X, unit.Position.Y, rule.Width, rule.Height,
        other.Position.X, other.Position.Y, otherRule.Width, otherRule.Height))
      {
        reason = $"That space is occupied by {other.UnitType}.";
        return false;
      }
    }

    reason = string.Empty;
    return true;
  }

  public bool TryPlaceUnit(CampaignUnitDefinition unit, out string reason)
  {
    if (!CanPlaceUnit(unit, out reason)) return false;
    PlaceUnit(unit);
    return true;
  }

  public void MoveUnit(string unitId, CampaignCoordinate position)
  {
    CampaignUnitDefinition? unit = Level.Units.FirstOrDefault(candidate => candidate.Id == unitId);
    if (unit is null || unit.Position == position) return;
    Change("Move unit", level => level.Units.First(candidate => candidate.Id == unitId).Position = position);
  }

  public bool TryMoveUnit(string unitId, CampaignCoordinate position, out string reason)
  {
    CampaignUnitDefinition? unit = Level.Units.FirstOrDefault(candidate => candidate.Id == unitId);
    if (unit is null)
    {
      reason = "The selected unit no longer exists.";
      return false;
    }
    CampaignUnitDefinition candidate = new()
    {
      Id = unit.Id,
      UnitType = unit.UnitType,
      Team = unit.Team,
      Position = position,
      Health = unit.Health,
      Rotation = unit.Rotation
    };
    if (!CanPlaceUnit(candidate, out reason, unitId)) return false;
    MoveUnit(unitId, position);
    return true;
  }

  public void RotateUnit(string unitId)
  {
    if (!Level.Units.Any(unit => unit.Id == unitId)) return;
    Change("Rotate unit", level =>
    {
      CampaignUnitDefinition unit = level.Units.First(candidate => candidate.Id == unitId);
      unit.Rotation = unit.Rotation switch
      {
        CampaignUnitRotation.Degrees0 => CampaignUnitRotation.Degrees90,
        CampaignUnitRotation.Degrees90 => CampaignUnitRotation.Degrees180,
        CampaignUnitRotation.Degrees180 => CampaignUnitRotation.Degrees270,
        _ => CampaignUnitRotation.Degrees0
      };
    });
  }

  public void DeleteUnit(string unitId)
  {
    if (!Level.Units.Any(unit => unit.Id == unitId)) return;
    Change("Delete unit", level => level.Units.RemoveAll(unit => unit.Id == unitId));
    if (Selection.Kind == EditorSelectionKind.Unit && Selection.Id == unitId) Selection = EditorSelection.None;
  }

  public void AddTerrain(CampaignTerrainTileDefinition terrain)
  {
    ArgumentNullException.ThrowIfNull(terrain);
    Change("Add terrain", level => level.Terrain.Add(new CampaignTerrainTileDefinition
    {
      Type = terrain.Type,
      Position = terrain.Position
    }));
  }

  /// <summary>Paints terrain in a single operation, replacing a prior terrain tile when needed.</summary>
  public void PaintTerrain(CampaignTerrainType type, CampaignCoordinate position)
  {
    CampaignTerrainTileDefinition? existing = Level.Terrain.FirstOrDefault(terrain => terrain.Position == position);
    if (existing?.Type == type) return;
    Change("Paint terrain", level =>
    {
      CampaignTerrainTileDefinition? target = level.Terrain.FirstOrDefault(terrain => terrain.Position == position);
      if (target is null)
      {
        level.Terrain.Add(new CampaignTerrainTileDefinition { Type = type, Position = position });
      }
      else
      {
        target.Type = type;
      }
    });
  }

  public void DeleteTerrain(CampaignCoordinate position)
  {
    if (!Level.Terrain.Any(terrain => terrain.Position == position)) return;
    Change("Delete terrain", level => level.Terrain.RemoveAll(terrain => terrain.Position == position));
    if (Selection.Kind == EditorSelectionKind.Terrain && Selection.Position == position) Selection = EditorSelection.None;
  }

  public void UpdateUnit(string unitId, Action<CampaignUnitDefinition> update)
  {
    ArgumentNullException.ThrowIfNull(update);
    if (!Level.Units.Any(unit => unit.Id == unitId)) return;
    Change("Update unit", level => update(level.Units.First(unit => unit.Id == unitId)));
  }

  public void AddObject(CampaignBoardObjectDefinition boardObject)
  {
    ArgumentNullException.ThrowIfNull(boardObject);
    Change("Add board object", level =>
    {
      CampaignBoardObjectDefinition copy = new()
      {
        Id = string.IsNullOrWhiteSpace(boardObject.Id) || level.Objects.Any(existing => existing.Id == boardObject.Id)
          ? Guid.NewGuid().ToString("N")
          : boardObject.Id,
        Type = boardObject.Type,
        Position = boardObject.Position,
        Owner = boardObject.Owner,
        Health = boardObject.Health,
        Rotation = boardObject.Rotation,
        Properties = new Dictionary<string, string>(boardObject.Properties ?? [])
      };
      level.Objects.Add(copy);
      Selection = new EditorSelection(EditorSelectionKind.Object, copy.Id);
    });
  }

  public void DeleteObject(string objectId)
  {
    if (!Level.Objects.Any(boardObject => boardObject.Id == objectId)) return;
    Change("Delete board object", level => level.Objects.RemoveAll(boardObject => boardObject.Id == objectId));
    if (Selection.Kind == EditorSelectionKind.Object && Selection.Id == objectId) Selection = EditorSelection.None;
  }

  public void UpdateTeam(NetworkTeam team, Action<CampaignTeamDefinition> update)
  {
    ArgumentNullException.ThrowIfNull(update);
    if (!Level.Teams.Any(candidate => candidate.Team == team)) return;
    Change("Update team", level => update(level.Teams.First(candidate => candidate.Team == team)));
  }

  public void UpdateScenario(Action<CampaignScenarioDefinition> update)
  {
    ArgumentNullException.ThrowIfNull(update);
    Change("Update scenario", level => update(level.Scenario));
  }

  public CampaignObjectiveDefinition AddObjective(bool defeatCondition, CampaignObjectiveType type, NetworkTeam? team)
  {
    CampaignObjectiveDefinition objective = new()
    {
      Type = type,
      Team = team,
      RequiredAmount = type is CampaignObjectiveType.SurviveTurns or CampaignObjectiveType.ReachCash ? 10 : 1
    };
    Change(defeatCondition ? "Add defeat condition" : "Add victory condition", level =>
    {
      (defeatCondition ? level.Scenario.DefeatConditions : level.Scenario.VictoryConditions).Add(objective);
    });
    return objective;
  }

  public void RemoveObjective(bool defeatCondition, string objectiveId)
  {
    Change(defeatCondition ? "Remove defeat condition" : "Remove victory condition", level =>
    {
      (defeatCondition ? level.Scenario.DefeatConditions : level.Scenario.VictoryConditions)
        .RemoveAll(objective => objective.Id == objectiveId);
    });
  }

  public void UpdateObjective(bool defeatCondition, string objectiveId, Action<CampaignObjectiveDefinition> update)
  {
    ArgumentNullException.ThrowIfNull(update);
    List<CampaignObjectiveDefinition> objectives = defeatCondition ? Level.Scenario.DefeatConditions : Level.Scenario.VictoryConditions;
    if (!objectives.Any(objective => objective.Id == objectiveId)) return;
    Change(defeatCondition ? "Update defeat condition" : "Update victory condition", level =>
    {
      CampaignObjectiveDefinition objective = (defeatCondition ? level.Scenario.DefeatConditions : level.Scenario.VictoryConditions)
        .First(candidate => candidate.Id == objectiveId);
      update(objective);
    });
  }

  /// <summary>Text-entry changes are intentionally grouped outside snapshot history; board edits remain undoable individually.</summary>
  public void EditMetadata(Action<CampaignLevelMetadata> update)
  {
    ArgumentNullException.ThrowIfNull(update);
    update(Level.Metadata);
    HasUnsavedChanges = true;
  }

  public bool Undo()
  {
    CampaignLevelDefinition? previous = History.Undo(Level);
    if (previous is null) return false;
    Level = previous;
    Selection = EditorSelection.None;
    HasUnsavedChanges = true;
    return true;
  }

  public bool Redo()
  {
    CampaignLevelDefinition? next = History.Redo(Level);
    if (next is null) return false;
    Level = next;
    Selection = EditorSelection.None;
    HasUnsavedChanges = true;
    return true;
  }

  public CampaignLevelSaveResult Save(string? path = null)
  {
    string? target = path ?? SourcePath;
    if (string.IsNullOrWhiteSpace(target))
    {
      return new CampaignLevelSaveResult
      {
        Problems = [CampaignValidationProblem.Error("file.path", "Choose a file location before saving this new level.")]
      };
    }

    CampaignLevelSaveResult result = CampaignLevelSerializer.Save(target, Level);
    if (result.IsSuccess)
    {
      SourcePath = target;
      HasUnsavedChanges = false;
    }
    return result;
  }

  public CampaignLevelLoadResult Import(string path)
  {
    CampaignLevelLoadResult result = CampaignLevelSerializer.Load(path);
    if (result.IsSuccess && result.Level is not null)
    {
      Level = CampaignLevelCloner.Clone(result.Level);
      SourcePath = path;
      History.Clear();
      Selection = EditorSelection.None;
      HasUnsavedChanges = false;
    }
    return result;
  }

  /// <summary>Returns an isolated definition for test play; editor data remains untouched.</summary>
  public CampaignLevelLoadResult CreateTestPlaySnapshot()
  {
    CampaignValidationResult validation = Validate();
    return new CampaignLevelLoadResult
    {
      Level = validation.IsValid ? CampaignLevelCloner.Clone(Level) : null,
      Problems = validation.Problems
    };
  }

  private void Change(string description, Action<CampaignLevelDefinition> change)
  {
    History.Record(Level, description);
    change(Level);
    HasUnsavedChanges = true;
  }

  private static void RecalculateBoardBounds(CampaignBoardDefinition board)
  {
    if (board.Tiles.Count == 0)
    {
      board.Width = 0;
      board.Height = 0;
      return;
    }
    board.OriginX = board.Tiles.Min(tile => tile.X);
    board.OriginY = board.Tiles.Min(tile => tile.Y);
    board.Width = board.Tiles.Max(tile => tile.X) - board.OriginX + 1;
    board.Height = board.Tiles.Max(tile => tile.Y) - board.OriginY + 1;
  }
}
