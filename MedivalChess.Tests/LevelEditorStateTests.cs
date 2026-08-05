using MedivalChess.Campaign;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class LevelEditorStateTests
{
  [Fact]
  public void UndoAndRedoRestoreMajorEditorOperations()
  {
    LevelEditorState editor = LevelEditorState.CreateNew(6, 6);
    CampaignUnitDefinition unit = new()
    {
      Id = "editor-soldier",
      UnitType = "Soldier",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0)
    };

    editor.PlaceUnit(unit);
    editor.MoveUnit(unit.Id, new CampaignCoordinate(1, 0));

    Assert.True(editor.Undo());
    Assert.Equal(new CampaignCoordinate(0, 0), Assert.Single(editor.Level.Units).Position);
    Assert.True(editor.Undo());
    Assert.Empty(editor.Level.Units);
    Assert.True(editor.Redo());
    Assert.Single(editor.Level.Units);
    Assert.True(editor.Redo());
    Assert.Equal(new CampaignCoordinate(1, 0), Assert.Single(editor.Level.Units).Position);
  }

  [Fact]
  public void TestPlaySnapshotCannotModifyTheEditorLevel()
  {
    LevelEditorState editor = LevelEditorState.CreateNew(6, 6);
    editor.PlaceUnit(new CampaignUnitDefinition
    {
      Id = "snapshot-soldier",
      UnitType = "Soldier",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0)
    });

    CampaignLevelLoadResult snapshot = editor.CreateTestPlaySnapshot();

    Assert.True(snapshot.IsSuccess);
    snapshot.Level!.Units[0].Health = 1;
    snapshot.Level.Units[0].Position = new CampaignCoordinate(3, 3);
    Assert.Null(editor.Level.Units[0].Health);
    Assert.Equal(new CampaignCoordinate(0, 0), editor.Level.Units[0].Position);
  }

  [Fact]
  public void ConverterCreatesTheExistingRuntimeBoardTeamsAndPieces()
  {
    CampaignLevelDefinition level = CampaignLevelDefinition.CreateNew(6, 6);
    level.Units.Add(new CampaignUnitDefinition
    {
      Id = "runtime-soldier",
      UnitType = "Soldier",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(0, 0),
      Health = 10
    });
    level.Objects.Add(new CampaignBoardObjectDefinition
    {
      Id = "runtime-road",
      Type = CampaignBoardObjectType.Road,
      Position = new CampaignCoordinate(1, 0)
    });

    CampaignPlayableStateResult result = CampaignLevelConverter.CreatePlayableState(level);

    Assert.True(result.IsSuccess);
    Assert.NotNull(result.State);
    Assert.True(result.State.Board.ContainsCell((5, 5)));
    Assert.Equal(2, result.State.Teams.Count);
    Assert.Equal(10, Assert.Single(result.State.Pieces).CurrentHealth);
    Assert.Contains((1, 0), result.State.Roads);
  }

  [Fact]
  public void ShippedBoardBaseIsCopiedIntoThePortableLevelDefinition()
  {
    LevelEditorState editor = LevelEditorState.CreateNew(4, 4);
    Board shipped = BoardRules.GetBoard("Small");

    editor.UseBoardBase(shipped);

    Assert.Equal(shipped.Cells.Count, editor.Level.Board.Tiles.Count);
    Assert.Equal(shipped.MinX, editor.Level.Board.OriginX);
    Assert.Equal(shipped.MinY, editor.Level.Board.OriginY);
    Assert.All(shipped.Cells, cell => Assert.Contains(new CampaignCoordinate(cell.x, cell.y), editor.Level.Board.Tiles));
  }

  [Fact]
  public void PaintingTerrainReplacesTheExistingTileType()
  {
    LevelEditorState editor = LevelEditorState.CreateNew(4, 4);
    CampaignCoordinate position = new(1, 1);

    editor.PaintTerrain(CampaignTerrainType.Forest, position);
    editor.PaintTerrain(CampaignTerrainType.Lake, position);

    CampaignTerrainTileDefinition terrain = Assert.Single(editor.Level.Terrain);
    Assert.Equal(CampaignTerrainType.Lake, terrain.Type);
    Assert.Equal(position, terrain.Position);
  }

  [Fact]
  public void UnitPlacementRejectsImpossibleFootprintsBeforeTheyReachValidation()
  {
    LevelEditorState editor = LevelEditorState.CreateNew(4, 4);
    CampaignUnitDefinition first = new()
    {
      Id = "first-soldier",
      UnitType = "Soldier",
      Team = NetworkTeam.Red,
      Position = new CampaignCoordinate(1, 1)
    };

    Assert.True(editor.TryPlaceUnit(first, out string firstReason), firstReason);
    Assert.False(editor.TryPlaceUnit(new CampaignUnitDefinition
    {
      UnitType = "Soldier",
      Team = NetworkTeam.Blue,
      Position = new CampaignCoordinate(1, 1)
    }, out string overlapReason));
    Assert.Contains("occupied", overlapReason, StringComparison.OrdinalIgnoreCase);
    Assert.False(editor.TryMoveUnit(first.Id, new CampaignCoordinate(9, 9), out string boundsReason));
    Assert.Contains("does not fit", boundsReason, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(new CampaignCoordinate(1, 1), Assert.Single(editor.Level.Units).Position);
  }
}
