namespace MedivalChess.GameBoard;

using System;
using System.Collections.Generic;
using System.Linq;

internal readonly record struct TileEdge((int x, int y) First, (int x, int y) Second)
{
  internal static TileEdge Between((int x, int y) first, (int x, int y) second)
  {
    return Compare(first, second) <= 0
      ? new TileEdge(first, second)
      : new TileEdge(second, first);
  }

  private static int Compare((int x, int y) first, (int x, int y) second)
  {
    int xComparison = first.x.CompareTo(second.x);
    return xComparison != 0 ? xComparison : first.y.CompareTo(second.y);
  }
}

internal sealed class TerrainGenerationSettings
{
  // Forests
  internal int ForestTilesPerGroup { get; init; } = 90;
  internal int MinimumForestGroups { get; init; } = 4;
  internal int MaximumForestGroups { get; init; } = 6;
  internal int MinimumForestClusterSize { get; init; } = 3;
  internal int MaximumForestClusterSize { get; init; } = 6;
  internal int ForestEdgeClearance { get; init; } = 1;
  internal int ForestInteriorWeightExponent { get; init; } = 2;
  // Units standing in a forest take this much less damage (minimum damage remains 1).
  internal int ForestDamageReduction { get; init; } = 3;

  // Lakes
  internal int LakeSourceEdgeClearance { get; init; } = 2;
  internal int LakeEdgeClearance { get; init; } = 1;
  internal int MinimumLakeClusterSize { get; init; } = 3;
  internal int MaximumLakeClusterSize { get; init; } = 5;
  internal int MinimumLakeSourceSeparation { get; init; } = 7;

  // Rivers
  internal int LargeBoardCellCount { get; init; } = 300;
  internal double AdditionalRiverChance { get; init; } = 0.4;
  internal int MinimumRiverLength { get; init; } = 7;
  internal int MinimumRiverSeparation { get; init; } = 4;
  // Rivers favour side-to-side routes near the battlefield centre, away from royal spawn lanes.
  internal int RiverRoyalSpawnClearance { get; init; } = 6;
  internal int RiverMiddleBandHalfHeight { get; init; } = 5;
  internal int RiverMiddleWeightExponent { get; init; } = 4;
  internal double RiverFarEdgePreference { get; init; } = 0.8;
  internal int RiverFarEdgeTolerance { get; init; } = 2;
  internal int RiverMinimumDetours { get; init; } = 1;
  internal int RiverMaximumDetours { get; init; } = 2;
  internal int RiverMinimumDetourLength { get; init; } = 1;
  internal int RiverMaximumDetourLength { get; init; } = 2;
  internal int MaximumRiverTargetsPerOutlet { get; init; } = 12;
}

internal sealed class BattlefieldTerrain
{
  private readonly record struct RiverSegment(
    TileEdge Edge,
    (int x, int y) Start,
    (int x, int y) End
  );

  private readonly HashSet<(int x, int y)> _forests;
  private readonly HashSet<(int x, int y)> _lakes;
  private readonly HashSet<TileEdge> _rivers;

  internal IReadOnlySet<(int x, int y)> Forests => _forests;
  internal IReadOnlySet<(int x, int y)> Lakes => _lakes;
  internal IReadOnlySet<TileEdge> Rivers => _rivers;
  internal int ForestDamageReduction { get; }
  internal static TerrainGenerationSettings DefaultGenerationSettings { get; } = new();

  internal BattlefieldTerrain(
    IEnumerable<(int x, int y)> forests = null,
    IEnumerable<(int x, int y)> lakes = null,
    IEnumerable<TileEdge> rivers = null,
    int forestDamageReduction = 3
  )
  {
    _forests = forests == null ? [] : [.. forests];
    _lakes = lakes == null ? [] : [.. lakes];
    _rivers = rivers == null ? [] : [.. rivers];
    ForestDamageReduction = Math.Max(0, forestDamageReduction);
  }

  internal bool IsForest((int x, int y) position) => _forests.Contains(position);

  internal bool IsLake((int x, int y) position) => _lakes.Contains(position);

  internal bool HasRiverBetween((int x, int y) first, (int x, int y) second)
  {
    return _rivers.Contains(TileEdge.Between(first, second));
  }

  internal static BattlefieldTerrain CreateRandom(
    Board board,
    int seed,
    TerrainGenerationSettings settings = null
  )
  {
    settings ??= DefaultGenerationSettings;
    Random random = new(seed);
    List<(int x, int y)> cells = [.. board.Cells];
    if (cells.Count == 0)
    {
      return new BattlefieldTerrain();
    }

    Dictionary<(int x, int y), int> edgeDistances = GetEdgeDistances(board);
    List<(int x, int y)> protectedInterior = cells
      .Where(position => edgeDistances[position] >= settings.ForestEdgeClearance)
      .ToList();
    List<(int x, int y)> deepInterior = cells
      .Where(position => edgeDistances[position] >= settings.LakeSourceEdgeClearance)
      .ToList();
    if (protectedInterior.Count == 0)
    {
      return new BattlefieldTerrain();
    }

    HashSet<(int x, int y)> forests = [];
    int forestGroupCount = Math.Clamp(
      cells.Count / settings.ForestTilesPerGroup,
      settings.MinimumForestGroups,
      settings.MaximumForestGroups
    );
    for (int group = 0; group < forestGroupCount; group++)
    {
      (int x, int y) origin = PickWeighted(
        random,
        protectedInterior,
        position => GetInteriorWeight(edgeDistances[position], settings)
      );
      AddForestCluster(board, random, forests, origin, edgeDistances, settings);
    }

    List<(int x, int y)> royalSpawns = GetRoyalSpawnPositions(board);
    List<(int x, int y)> lakeCandidates = (deepInterior.Count > 0 ? deepInterior : protectedInterior)
      .Where(position =>
        !forests.Contains(position) &&
        !IsNearRoyalSpawn(position, royalSpawns, settings.RiverRoyalSpawnClearance)
      )
      .ToList();
    int desiredRiverCount =
      cells.Count >= settings.LargeBoardCellCount && random.NextDouble() < settings.AdditionalRiverChance
        ? 2
        : 1;
    List<HashSet<(int x, int y)>> lakeGroups = [];
    for (int index = 0; index < desiredRiverCount; index++)
    {
      List<(int x, int y)> distantCandidates = lakeCandidates
        .Where(candidate => lakeGroups.All(lake =>
          lake.All(existing =>
            ManhattanDistance(candidate, existing) >= settings.MinimumLakeSourceSeparation
          )
        ))
        .ToList();
      if (distantCandidates.Count == 0)
      {
        break;
      }

      (int x, int y) origin = PickWeighted(
        random,
        distantCandidates,
        position => GetInteriorWeight(edgeDistances[position], settings)
      );
      HashSet<(int x, int y)> lake = BuildLakeCluster(
        board,
        random,
        origin,
        forests,
        edgeDistances,
        settings
      );
      if (lake.Count == 0)
      {
        continue;
      }

      lakeGroups.Add(lake);
      lakeCandidates.RemoveAll(position => lake.Contains(position));
    }

    HashSet<(int x, int y)> lakes = [.. lakeGroups.SelectMany(group => group)];
    HashSet<TileEdge> rivers = [];
    foreach (HashSet<(int x, int y)> lake in lakeGroups)
    {
      TryAddRiverFromLake(board, random, lake, lakes, rivers, royalSpawns, settings);
    }

    forests.ExceptWith(lakes);
    return new BattlefieldTerrain(forests, lakes, rivers, settings.ForestDamageReduction);
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
      int nextDistance = distances[current] + 1;
      foreach ((int x, int y) neighbour in GetOrthogonalNeighbours(current))
      {
        if (board.ContainsCell(neighbour) && !distances.ContainsKey(neighbour))
        {
          distances[neighbour] = nextDistance;
          frontier.Enqueue(neighbour);
        }
      }
    }

    return distances;
  }

  private static List<(int x, int y)> GetRoyalSpawnPositions(Board board)
  {
    int centreX = board.MinX + board.BoardArray.GetLength(1) / 2;
    return
    [
      (centreX, board.MinY),
      (centreX, board.MinY + board.BoardArray.GetLength(0) - 1)
    ];
  }

  private static (int minX, int maxX, int minY, int maxY) GetBoardBounds(Board board)
  {
    return (
      board.MinX,
      board.MinX + board.BoardArray.GetLength(1) - 1,
      board.MinY,
      board.MinY + board.BoardArray.GetLength(0) - 1
    );
  }

  private static bool IsNearRoyalSpawn(
    (int x, int y) position,
    IReadOnlyList<(int x, int y)> royalSpawns,
    int clearance
  )
  {
    return royalSpawns.Any(spawn => ManhattanDistance(position, spawn) <= clearance);
  }

  private static bool IsNearRoyalSpawn(
    RiverSegment segment,
    IReadOnlyList<(int x, int y)> royalSpawns,
    int clearance
  )
  {
    return IsNearRoyalSpawn(segment.Edge.First, royalSpawns, clearance) ||
      IsNearRoyalSpawn(segment.Edge.Second, royalSpawns, clearance);
  }

  private static bool IsSideToSideRiverTarget(
    (int x, int y) vertex,
    (int minX, int maxX, int minY, int maxY) bounds,
    TerrainGenerationSettings settings
  )
  {
    bool reachesLeftOrRight = vertex.x <= bounds.minX + 1 || vertex.x >= bounds.maxX;
    int centreY = (bounds.minY + bounds.maxY) / 2;
    return reachesLeftOrRight &&
      Math.Abs(vertex.y - centreY) <= settings.RiverMiddleBandHalfHeight;
  }

  private static int GetRiverTargetWeight(
    (int x, int y) vertex,
    (int x, int y) start,
    (int minX, int maxX, int minY, int maxY) bounds,
    TerrainGenerationSettings settings
  )
  {
    int centreY = (bounds.minY + bounds.maxY) / 2;
    int closeness = settings.RiverMiddleBandHalfHeight - Math.Abs(vertex.y - centreY) + 1;
    int weight = 1;
    for (int exponent = 0; exponent < settings.RiverMiddleWeightExponent; exponent++)
    {
      weight *= Math.Max(1, closeness);
    }

    return weight * Math.Max(1, Math.Abs(vertex.x - start.x));
  }

  private static void AddForestCluster(
    Board board,
    Random random,
    HashSet<(int x, int y)> forests,
    (int x, int y) origin,
    IReadOnlyDictionary<(int x, int y), int> edgeDistances,
    TerrainGenerationSettings settings
  )
  {
    HashSet<(int x, int y)> cluster = [origin];
    int targetSize = random.Next(settings.MinimumForestClusterSize, settings.MaximumForestClusterSize + 1);
    while (cluster.Count < targetSize)
    {
      List<(int x, int y)> candidates = cluster
        .SelectMany(GetOrthogonalNeighbours)
        .Where(position =>
          board.ContainsCell(position) &&
          edgeDistances[position] >= settings.ForestEdgeClearance &&
          !cluster.Contains(position)
        )
        .Distinct()
        .ToList();
      if (candidates.Count == 0)
      {
        break;
      }

      cluster.Add(PickWeighted(
        random,
        candidates,
        position => GetInteriorWeight(edgeDistances[position], settings)
      ));
    }

    forests.UnionWith(cluster);
  }

  private static HashSet<(int x, int y)> BuildLakeCluster(
    Board board,
    Random random,
    (int x, int y) origin,
    IReadOnlySet<(int x, int y)> forests,
    IReadOnlyDictionary<(int x, int y), int> edgeDistances,
    TerrainGenerationSettings settings
  )
  {
    HashSet<(int x, int y)> lake = [origin];
    int targetSize = random.Next(settings.MinimumLakeClusterSize, settings.MaximumLakeClusterSize + 1);
    while (lake.Count < targetSize)
    {
      List<(int x, int y)> candidates = lake
        .SelectMany(GetOrthogonalNeighbours)
        .Where(position =>
          board.ContainsCell(position) &&
          edgeDistances[position] >= settings.LakeEdgeClearance &&
          !forests.Contains(position) &&
          !lake.Contains(position)
        )
        .Distinct()
        .ToList();
      if (candidates.Count == 0)
      {
        break;
      }

      lake.Add(candidates[random.Next(candidates.Count)]);
    }

    return lake;
  }

  private static bool TryAddRiverFromLake(
    Board board,
    Random random,
    IReadOnlySet<(int x, int y)> sourceLake,
    IReadOnlySet<(int x, int y)> allLakes,
    HashSet<TileEdge> rivers,
    IReadOnlyList<(int x, int y)> royalSpawns,
    TerrainGenerationSettings settings
  )
  {
    (int minX, int maxX, int minY, int maxY) bounds = GetBoardBounds(board);
    List<RiverSegment> segments = GetRiverSegments(board);
    List<RiverSegment> outlets = segments
      .Where(segment =>
        sourceLake.Contains(segment.Edge.First) != sourceLake.Contains(segment.Edge.Second) &&
        !IsNearRoyalSpawn(segment, royalSpawns, settings.RiverRoyalSpawnClearance) &&
        IsFarEnoughFromExistingRivers(segment.Edge, rivers, settings)
      )
      .ToList();
    Shuffle(random, outlets);

    foreach (RiverSegment outlet in outlets)
    {
      List<RiverSegment> usableSegments = segments
        .Where(segment =>
          segment.Edge != outlet.Edge &&
          !allLakes.Contains(segment.Edge.First) &&
          !allLakes.Contains(segment.Edge.Second) &&
          !IsNearRoyalSpawn(segment, royalSpawns, settings.RiverRoyalSpawnClearance) &&
          IsFarEnoughFromExistingRivers(segment.Edge, rivers, settings)
        )
        .ToList();
      Dictionary<(int x, int y), List<RiverSegment>> graph = BuildRiverGraph(usableSegments);

      foreach ((int x, int y) start in new[] { outlet.Start, outlet.End })
      {
        if (!graph.ContainsKey(start))
        {
          continue;
        }

        List<(int x, int y)> targets = graph.Keys
          .Where(vertex =>
            IsBoardBoundaryVertex(board, vertex) &&
            IsSideToSideRiverTarget(vertex, bounds, settings) &&
            !IsNearRoyalSpawn(vertex, royalSpawns, settings.RiverRoyalSpawnClearance) &&
            (settings.RiverMinimumDetours == 0 ||
              Math.Abs(vertex.y - start.y) >= settings.RiverMinimumDetourLength) &&
            Math.Abs(vertex.y - start.y) <= settings.RiverMaximumDetourLength &&
            ManhattanDistance(start, vertex) >= settings.MinimumRiverLength - 1
          )
          .ToList();

        for (int targetAttempt = 0;
             targetAttempt < settings.MaximumRiverTargetsPerOutlet && targets.Count > 0;
             targetAttempt++)
        {
          int farthestDistance = targets.Max(vertex => Math.Abs(vertex.x - start.x));
          List<(int x, int y)> preferredTargets = random.NextDouble() < settings.RiverFarEdgePreference
            ? targets.Where(vertex =>
              Math.Abs(vertex.x - start.x) >= farthestDistance - settings.RiverFarEdgeTolerance
            ).ToList()
            : targets;
          (int x, int y) target = PickWeighted(
            random,
            preferredTargets,
            vertex => GetRiverTargetWeight(vertex, start, bounds, settings)
          );
          targets.Remove(target);
          List<RiverSegment> route = FindMeanderingRoute(graph, random, start, target, settings);
          if (route.Count + 1 < settings.MinimumRiverLength ||
              !route.All(segment => IsFarEnoughFromExistingRivers(segment.Edge, rivers, settings)))
          {
            continue;
          }

          rivers.Add(outlet.Edge);
          foreach (RiverSegment segment in route)
          {
            rivers.Add(segment.Edge);
          }

          return true;
        }
      }
    }

    return false;
  }

  private static List<RiverSegment> FindMeanderingRoute(
    IReadOnlyDictionary<(int x, int y), List<RiverSegment>> graph,
    Random random,
    (int x, int y) start,
    (int x, int y) target,
    TerrainGenerationSettings settings
  )
  {
    int minimumDetours = Math.Max(0, settings.RiverMinimumDetours);
    int maximumDetours = Math.Max(minimumDetours, settings.RiverMaximumDetours);
    int detourCount = random.Next(minimumDetours, maximumDetours + 1);
    if (detourCount > 0)
    {
      List<(int x, int y)> waypointTargets = [];
      if (detourCount == 1)
      {
        waypointTargets.Add(((start.x + target.x) / 2, target.y));
      }
      else
      {
        int travelDirection = Math.Sign(target.x - start.x);
        int verticalDirection = target.y == start.y
          ? (random.Next(2) == 0 ? -1 : 1)
          : Math.Sign(target.y - start.y);
        int detourLength = random.Next(
          settings.RiverMinimumDetourLength,
          settings.RiverMaximumDetourLength + 1
        );
        int firstY = target.y == start.y
          ? start.y + verticalDirection * detourLength
          : start.y + verticalDirection * Math.Min(detourLength, Math.Abs(target.y - start.y));
        waypointTargets.Add((
          start.x + travelDirection * Math.Max(1, Math.Abs(target.x - start.x) / 3),
          firstY
        ));
        waypointTargets.Add((
          start.x + travelDirection * Math.Max(2, Math.Abs(target.x - start.x) * 2 / 3),
          target.y
        ));
      }

      List<RiverSegment> route = [];
      HashSet<TileEdge> usedEdges = [];
      (int x, int y) cursor = start;
      bool routeSucceeded = true;
      foreach ((int x, int y) waypointTarget in waypointTargets.Append(target))
      {
        (int x, int y)? waypoint = graph.Keys
          .Where(vertex => Math.Abs(vertex.x - waypointTarget.x) <= 1 && vertex.y == waypointTarget.y)
          .OrderBy(vertex => Math.Abs(vertex.x - waypointTarget.x))
          .Select(vertex => ((int x, int y)?)vertex)
          .FirstOrDefault();
        if (!waypoint.HasValue)
        {
          routeSucceeded = false;
          break;
        }

        List<RiverSegment> leg = FindFlowingLeg(graph, cursor, waypoint.Value, usedEdges);
        if (leg.Count == 0)
        {
          routeSucceeded = false;
          break;
        }

        route.AddRange(leg);
        usedEdges.UnionWith(leg.Select(segment => segment.Edge));
        cursor = waypoint.Value;
      }

      if (routeSucceeded && cursor == target)
      {
        return route;
      }
    }

    return FindFlowingLeg(graph, start, target);
  }

  private static List<RiverSegment> FindFlowingLeg(
    IReadOnlyDictionary<(int x, int y), List<RiverSegment>> graph,
    (int x, int y) start,
    (int x, int y) target,
    IReadOnlySet<TileEdge> excludedEdges = null
  )
  {
    List<RiverSegment> route = [];
    HashSet<(int x, int y)> visited = [start];
    (int x, int y) current = start;
    bool preferHorizontal = Math.Abs(target.x - start.x) >= Math.Abs(target.y - start.y);

    while (current != target)
    {
      List<(RiverSegment segment, (int x, int y) next)> options = graph[current]
        .Where(segment => excludedEdges?.Contains(segment.Edge) != true)
        .Select(segment => (segment, segment.Start == current ? segment.End : segment.Start))
        .Where(option => !visited.Contains(option.Item2))
        .Where(option => ManhattanDistance(option.Item2, target) < ManhattanDistance(current, target))
        .OrderBy(option => preferHorizontal
          ? Math.Abs(option.Item2.x - target.x)
          : Math.Abs(option.Item2.y - target.y))
        .ThenBy(option => preferHorizontal
          ? Math.Abs(option.Item2.y - target.y)
          : Math.Abs(option.Item2.x - target.x))
        .ToList();
      if (options.Count == 0)
      {
        return [];
      }

      (RiverSegment segment, (int x, int y) next) option = options[0];
      route.Add(option.segment);
      visited.Add(option.next);
      current = option.next;
    }

    return route;
  }

  private static Dictionary<(int x, int y), List<RiverSegment>> BuildRiverGraph(
    IEnumerable<RiverSegment> segments
  )
  {
    Dictionary<(int x, int y), List<RiverSegment>> graph = [];
    foreach (RiverSegment segment in segments)
    {
      if (!graph.TryGetValue(segment.Start, out List<RiverSegment> startSegments))
      {
        startSegments = [];
        graph[segment.Start] = startSegments;
      }

      if (!graph.TryGetValue(segment.End, out List<RiverSegment> endSegments))
      {
        endSegments = [];
        graph[segment.End] = endSegments;
      }

      startSegments.Add(segment);
      endSegments.Add(segment);
    }

    return graph;
  }

  private static List<RiverSegment> GetRiverSegments(Board board)
  {
    List<RiverSegment> segments = [];
    foreach ((int x, int y) cell in board.Cells)
    {
      (int x, int y) right = (cell.x + 1, cell.y);
      if (board.ContainsCell(right))
      {
        segments.Add(new RiverSegment(
          TileEdge.Between(cell, right),
          (cell.x + 1, cell.y),
          (cell.x + 1, cell.y + 1)
        ));
      }

      (int x, int y) below = (cell.x, cell.y + 1);
      if (board.ContainsCell(below))
      {
        segments.Add(new RiverSegment(
          TileEdge.Between(cell, below),
          (cell.x, cell.y + 1),
          (cell.x + 1, cell.y + 1)
        ));
      }
    }

    return segments;
  }

  private static bool IsBoardBoundaryVertex(Board board, (int x, int y) vertex)
  {
    return !board.ContainsCell((vertex.x - 1, vertex.y - 1)) ||
      !board.ContainsCell((vertex.x, vertex.y - 1)) ||
      !board.ContainsCell((vertex.x - 1, vertex.y)) ||
      !board.ContainsCell(vertex);
  }

  private static bool IsFarEnoughFromExistingRivers(
    TileEdge candidate,
    IReadOnlyCollection<TileEdge> rivers,
    TerrainGenerationSettings settings
  )
  {
    return rivers.All(existing =>
      GetMinimumTileDistance(candidate, existing) >= settings.MinimumRiverSeparation
    );
  }

  private static int GetMinimumTileDistance(TileEdge first, TileEdge second)
  {
    return new[]
    {
      ManhattanDistance(first.First, second.First),
      ManhattanDistance(first.First, second.Second),
      ManhattanDistance(first.Second, second.First),
      ManhattanDistance(first.Second, second.Second)
    }.Min();
  }

  private static int ManhattanDistance((int x, int y) first, (int x, int y) second)
  {
    return Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);
  }

  private static int GetInteriorWeight(int edgeDistance, TerrainGenerationSettings settings)
  {
    int weight = 1;
    for (int exponent = 0; exponent < settings.ForestInteriorWeightExponent; exponent++)
    {
      weight *= edgeDistance + 1;
    }

    return weight;
  }

  private static T PickWeighted<T>(Random random, IReadOnlyList<T> choices, Func<T, int> weight)
  {
    int totalWeight = choices.Sum(choice => Math.Max(1, weight(choice)));
    int choice = random.Next(totalWeight);
    foreach (T candidate in choices)
    {
      choice -= Math.Max(1, weight(candidate));
      if (choice < 0)
      {
        return candidate;
      }
    }

    return choices[^1];
  }

  private static void Shuffle<T>(Random random, IList<T> values)
  {
    for (int index = values.Count - 1; index > 0; index--)
    {
      int otherIndex = random.Next(index + 1);
      (values[index], values[otherIndex]) = (values[otherIndex], values[index]);
    }
  }

  private static IEnumerable<(int x, int y)> GetOrthogonalNeighbours((int x, int y) position)
  {
    yield return (position.x + 1, position.y);
    yield return (position.x - 1, position.y);
    yield return (position.x, position.y + 1);
    yield return (position.x, position.y - 1);
  }
}
