using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public class MovementPathfinderTests
{
  [Fact]
  public void StraightThree_UsesOrthogonalStepsAndCannotReturnToStart()
  {
    Piece soldier = new(PieceDefinitions.Soldier, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      soldier,
      _ => true,
      _ => true,
      _ => 1,
      (_, _) => false
    );

    Assert.True(paths.ContainsKey((2, 0)));
    Assert.Equal([(1, 0), (2, 0)], paths[(2, 0)]);
    Assert.False(paths.ContainsKey((0, 0)));
    Assert.True(paths.ContainsKey((3, 0)));
    Assert.True(paths.ContainsKey((1, 1)));
  }

  [Fact]
  public void BlockedOrForestStepPreventsReachingPastItWithinMovementBudget()
  {
    Piece soldier = new(PieceDefinitions.Soldier, (0, 0), TeamName.Red);

    var blockedPaths = MovementPathfinder.FindPaths(
      soldier,
      _ => true,
      position => position != (1, 0),
      _ => 1,
      (_, _) => false
    );
    var forestPaths = MovementPathfinder.FindPaths(
      soldier,
      _ => true,
      _ => true,
      position => position == (1, 0) ? 2 : 1,
      (_, _) => false
    );

    Assert.False(blockedPaths.ContainsKey((3, 0)));
    Assert.True(forestPaths.ContainsKey((1, 0)));
    Assert.True(forestPaths.ContainsKey((2, 0)));
    Assert.False(forestPaths.ContainsKey((3, 0)));
  }

  [Fact]
  public void OneMovementUnit_CanEnterAnAdjacentForest()
  {
    Piece peasant = new(PieceDefinitions.Peasant, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      peasant,
      _ => true,
      _ => true,
      position => position == (1, 0) ? 2 : 1,
      (_, _) => false
    );

    Assert.Equal([(1, 0)], paths[(1, 0)]);
    Assert.False(paths.ContainsKey((2, 0)));
  }

  [Fact]
  public void CrossingARiver_UsesTheRemainingMovementForThatPath()
  {
    Piece soldier = new(PieceDefinitions.Soldier, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      soldier,
      _ => true,
      _ => true,
      _ => 1,
      (from, to) => from == (0, 0) && to == (1, 0)
    );

    Assert.True(paths.ContainsKey((1, 0)));
    Assert.False(paths.ContainsKey((3, 0)));
  }

  [Fact]
  public void RepeatedRiverEdges_CannotBeUsedToMoveIndefinitely()
  {
    Piece soldier = new(PieceDefinitions.Soldier, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      soldier,
      _ => true,
      _ => true,
      _ => 1,
      (from, to) => from.y == 0 && to.y == 0
    );

    Assert.True(paths.ContainsKey((1, 0)));
    Assert.False(paths.ContainsKey((3, 0)));
  }

  [Fact]
  public void AnyMovement_PrefersOrthogonalPathWhenItCostsTheSame()
  {
    Piece archer = new(PieceDefinitions.Archer, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      archer,
      _ => true,
      _ => true,
      _ => 1,
      (_, _) => false
    );

    Assert.Equal([(1, 0), (2, 0)], paths[(2, 0)]);
  }

  [Fact]
  public void Elephant_AttacksByMovingOverAnEnemyInsteadOfUsingARightClickAttack()
  {
    Piece elephant = new(PieceDefinitions.Elephant, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      elephant,
      destination => destination != (0, 2),
      _ => true,
      _ => 1,
      (_, _) => false
    );

    Assert.Equal(Shape.None, elephant.Definition.AttackShape.shape);
    Assert.False(Actions.CanAttackSquare(elephant, (2, 0)));
    Assert.Equal([(0, 1), (0, 2), (0, 3)], paths[(0, 3)]);
  }

  [Fact]
  public void LargePieceMovementUsesItsListedRange()
  {
    Piece elephant = new(PieceDefinitions.Elephant, (0, 0), TeamName.Red);

    var paths = MovementPathfinder.FindPaths(
      elephant,
      _ => true,
      (_, _) => true,
      _ => 1,
      (_, _) => false
    );

    Assert.True(paths.ContainsKey((0, 3)));
    Assert.False(paths.ContainsKey((0, 4)));
  }

  [Fact]
  public void Terrain_TracksForestsLakesAndRiversByTileOrEdge()
  {
    BattlefieldTerrain terrain = new(
      forests: [(2, 2)],
      lakes: [(3, 3)],
      rivers: [TileEdge.Between((4, 4), (5, 4))]
    );

    Assert.True(terrain.IsForest((2, 2)));
    Assert.True(terrain.IsLake((3, 3)));
    Assert.True(terrain.HasRiverBetween((5, 4), (4, 4)));
    Assert.False(terrain.HasRiverBetween((4, 4), (4, 5)));
  }

  [Fact]
  public void GeneratedRivers_ConnectALakeShoreToTheBoardEdge()
  {
    Board board = new();
    for (int seed = 0; seed < 20; seed++)
    {
      BattlefieldTerrain terrain = BattlefieldTerrain.CreateRandom(board, seed);
      Assert.NotEmpty(terrain.Lakes);
      Assert.NotEmpty(terrain.Rivers);

      foreach (HashSet<TileEdge> river in GetRiverComponents(terrain.Rivers))
      {
        Assert.Contains(river, edge =>
          terrain.Lakes.Contains(edge.First) || terrain.Lakes.Contains(edge.Second)
        );
        Assert.Contains(river.SelectMany(GetSegmentEndpoints), endpoint =>
          IsBoardBoundaryVertex(board, endpoint)
        );
      }
    }
  }

  [Fact]
  public void GeneratedRivers_StayClearOfRoyalSpawnAreas()
  {
    Board board = new();
    (int x, int y)[] royalSpawns =
    [
      (board.MinX + board.BoardArray.GetLength(1) / 2, board.MinY),
      (board.MinX + board.BoardArray.GetLength(1) / 2, board.MinY + board.BoardArray.GetLength(0) - 1)
    ];

    for (int seed = 0; seed < 20; seed++)
    {
      BattlefieldTerrain terrain = BattlefieldTerrain.CreateRandom(board, seed);
      Assert.All(terrain.Rivers, edge =>
      {
        Assert.All(new[] { edge.First, edge.Second }, riverTile =>
          Assert.All(royalSpawns, spawn =>
            Assert.True(Math.Abs(riverTile.x - spawn.x) + Math.Abs(riverTile.y - spawn.y) > 6)
          )
        );
      });
    }
  }

  [Fact]
  public void GeneratedForests_AreWeightedTowardTheBoardInterior()
  {
    Board board = new();
    Dictionary<(int x, int y), int> edgeDistances = GetEdgeDistances(board);
    double boardAverageDistance = board.Cells.Average(cell => edgeDistances[cell]);
    List<int> forestDistances = [];

    for (int seed = 0; seed < 30; seed++)
    {
      BattlefieldTerrain terrain = BattlefieldTerrain.CreateRandom(board, seed);
      forestDistances.AddRange(terrain.Forests.Select(forest => edgeDistances[forest]));
    }

    Assert.NotEmpty(forestDistances);
    Assert.True(forestDistances.Average() > boardAverageDistance + 0.75);
  }

  [Fact]
  public void TerrainGenerationSettings_ControlForestEdgeClearance()
  {
    Board board = new();
    TerrainGenerationSettings settings = new()
    {
      ForestTilesPerGroup = 999,
      MinimumForestGroups = 1,
      MaximumForestGroups = 1,
      ForestEdgeClearance = 3
    };
    Dictionary<(int x, int y), int> edgeDistances = GetEdgeDistances(board);

    BattlefieldTerrain terrain = BattlefieldTerrain.CreateRandom(board, 41, settings);

    Assert.NotEmpty(terrain.Forests);
    Assert.All(terrain.Forests, forest => Assert.True(edgeDistances[forest] >= 3));
  }

  private static List<HashSet<TileEdge>> GetRiverComponents(IReadOnlySet<TileEdge> rivers)
  {
    HashSet<TileEdge> remaining = [.. rivers];
    List<HashSet<TileEdge>> components = [];
    while (remaining.Count > 0)
    {
      TileEdge first = remaining.First();
      HashSet<TileEdge> component = [first];
      Queue<TileEdge> frontier = [];
      frontier.Enqueue(first);
      remaining.Remove(first);

      while (frontier.TryDequeue(out TileEdge edge))
      {
        List<TileEdge> connected = remaining
          .Where(candidate => SharesEndpoint(edge, candidate))
          .ToList();
        foreach (TileEdge candidate in connected)
        {
          remaining.Remove(candidate);
          component.Add(candidate);
          frontier.Enqueue(candidate);
        }
      }

      components.Add(component);
    }

    return components;
  }

  private static bool SharesEndpoint(TileEdge first, TileEdge second)
  {
    foreach ((int x, int y) firstEndpoint in GetSegmentEndpoints(first))
    {
      if (GetSegmentEndpoints(second).Contains(firstEndpoint))
      {
        return true;
      }
    }

    return false;
  }

  private static IEnumerable<(int x, int y)> GetSegmentEndpoints(TileEdge edge)
  {
    if (edge.First.x != edge.Second.x)
    {
      int lineX = Math.Max(edge.First.x, edge.Second.x);
      yield return (lineX, edge.First.y);
      yield return (lineX, edge.First.y + 1);
      yield break;
    }

    int lineY = Math.Max(edge.First.y, edge.Second.y);
    yield return (edge.First.x, lineY);
    yield return (edge.First.x + 1, lineY);
  }

  private static bool IsBoardBoundaryVertex(Board board, (int x, int y) vertex)
  {
    return !board.ContainsCell((vertex.x - 1, vertex.y - 1)) ||
      !board.ContainsCell((vertex.x, vertex.y - 1)) ||
      !board.ContainsCell((vertex.x - 1, vertex.y)) ||
      !board.ContainsCell(vertex);
  }

  private static Dictionary<(int x, int y), int> GetEdgeDistances(Board board)
  {
    Dictionary<(int x, int y), int> distances = [];
    Queue<(int x, int y)> frontier = [];
    foreach ((int x, int y) cell in board.Cells)
    {
      if (GetOrthogonalNeighbours(cell).Any(neighbour => !board.ContainsCell(neighbour)))
      {
        distances[cell] = 0;
        frontier.Enqueue(cell);
      }
    }

    while (frontier.TryDequeue(out (int x, int y) current))
    {
      foreach ((int x, int y) neighbour in GetOrthogonalNeighbours(current))
      {
        if (board.ContainsCell(neighbour) && !distances.ContainsKey(neighbour))
        {
          distances[neighbour] = distances[current] + 1;
          frontier.Enqueue(neighbour);
        }
      }
    }

    return distances;
  }

  private static IEnumerable<(int x, int y)> GetOrthogonalNeighbours((int x, int y) position)
  {
    yield return (position.x + 1, position.y);
    yield return (position.x - 1, position.y);
    yield return (position.x, position.y + 1);
    yield return (position.x, position.y - 1);
  }
}
