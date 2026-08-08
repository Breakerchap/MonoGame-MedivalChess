using MedivalChess.GameBoard;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class TerrainGenerationTests
{
  [Fact]
  public void MediumPresetTerrainLoadsAnAuthoredMapDeterministically()
  {
    Board board = new("board_medium.json");

    BattlefieldTerrain first = TerrainRules.Create(board, 8675309, "Heavy", "Heavy", terrainSource: "Preset", boardSize: "Medium");
    BattlefieldTerrain second = TerrainRules.Create(board, 8675309, "Light", "Light", terrainSource: "Preset", boardSize: "Medium");

    Assert.NotEmpty(first.Forests);
    Assert.NotEmpty(first.Lakes);
    Assert.NotEmpty(first.Rivers);
    Assert.Equal(first.Forests.Order(), second.Forests.Order());
    Assert.Equal(first.Lakes.Order(), second.Lakes.Order());
    Assert.Equal(first.Rivers.OrderBy(edge => edge.First).ThenBy(edge => edge.Second),
      second.Rivers.OrderBy(edge => edge.First).ThenBy(edge => edge.Second));
    Assert.All(first.Forests.Concat(first.Lakes), position => Assert.True(board.ContainsCell(position)));
  }

  [Fact]
  public void PresetSourceFallsBackToProceduralForBoardsWithoutPresetFiles()
  {
    BattlefieldTerrain terrain = TerrainRules.Create(
      new Board("board_small.json"), 42, "Standard", "Standard", terrainSource: "Preset", boardSize: "Small");

    Assert.NotEmpty(terrain.Forests);
    Assert.NotEmpty(terrain.Lakes);
  }

  [Fact]
  public void NoneTerrainSourceProducesAnOpenBattlefield()
  {
    BattlefieldTerrain terrain = TerrainRules.Create(
      new Board("board_medium.json"), 42, "Heavy", "Heavy", terrainSource: "None", boardSize: "Medium");

    Assert.Empty(terrain.Forests);
    Assert.Empty(terrain.Lakes);
    Assert.Empty(terrain.Rivers);
  }

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
      RiverCount = 2
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

  [Theory]
  [InlineData("Light", 1)]
  [InlineData("Standard", 2)]
  [InlineData("Heavy", 3)]
  public void WaterwayDensityCreatesTheRequestedNumberOfLakeFedRivers(string density, int expectedCount)
  {
    BattlefieldTerrain terrain = TerrainRules.Create(new Board("board_medium.json"), 12345, "Standard", density);

    Assert.Equal(expectedCount, CountOrthogonalGroups(terrain.Lakes));
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

  private static int CountOrthogonalGroups(IReadOnlySet<(int x, int y)> tiles)
  {
    HashSet<(int x, int y)> remaining = [.. tiles];
    int groups = 0;
    while (remaining.Count > 0)
    {
      groups++;
      Queue<(int x, int y)> frontier = new();
      frontier.Enqueue(remaining.First());
      while (frontier.Count > 0)
      {
        (int x, int y) position = frontier.Dequeue();
        if (!remaining.Remove(position)) continue;
        foreach ((int x, int y) neighbour in new[]
        {
          (position.x - 1, position.y), (position.x + 1, position.y),
          (position.x, position.y - 1), (position.x, position.y + 1)
        })
        {
          if (remaining.Contains(neighbour)) frontier.Enqueue(neighbour);
        }
      }
    }

    return groups;
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
