using MedivalChess.GameBoard;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class TerrainGenerationTests
{
  [Fact]
  public void TerrainRespectsTheConfiguredRoyalSpawnClearance()
  {
    const int clearance = 5;
    Board board = new("board_medium.json");
    BattlefieldTerrain terrain = BattlefieldTerrain.CreateRandom(board, 12345, new TerrainGenerationSettings
    {
      RoyalSpawnTerrainClearance = clearance,
      MinimumForestGroups = 6,
      MaximumForestGroups = 6,
      AdditionalRiverChance = 1
    });
    List<(int x, int y)> royalFootprints = GetLargestRoyalSpawnFootprints(board);

    Assert.All(terrain.Forests, tile => AssertFarFromRoyalSpawn(tile, royalFootprints, clearance));
    Assert.All(terrain.Lakes, tile => AssertFarFromRoyalSpawn(tile, royalFootprints, clearance));
    Assert.All(terrain.Rivers, edge =>
    {
      AssertFarFromRoyalSpawn(edge.First, royalFootprints, clearance);
      AssertFarFromRoyalSpawn(edge.Second, royalFootprints, clearance);
    });
  }

  private static List<(int x, int y)> GetLargestRoyalSpawnFootprints(Board board)
  {
    int centreX = board.MinX + board.BoardArray.GetLength(1) / 2;
    int topY = board.MinY;
    int bottomY = board.MinY + board.BoardArray.GetLength(0) - 1;
    List<(int x, int y)> positions = [];
    for (int x = centreX - 1; x <= centreX + 1; x++)
    {
      positions.Add((x, topY));
      positions.Add((x, topY + 1));
      positions.Add((x, bottomY));
      positions.Add((x, bottomY - 1));
    }

    return positions.Where(board.ContainsCell).Distinct().ToList();
  }

  private static void AssertFarFromRoyalSpawn(
    (int x, int y) terrainPosition,
    IEnumerable<(int x, int y)> royalPositions,
    int clearance
  )
  {
    Assert.All(royalPositions, royalPosition =>
      Assert.True(
        Math.Abs(terrainPosition.x - royalPosition.x) + Math.Abs(terrainPosition.y - royalPosition.y) > clearance,
        $"Terrain at {terrainPosition} is too close to royal spawn square {royalPosition}."
      )
    );
  }
}
