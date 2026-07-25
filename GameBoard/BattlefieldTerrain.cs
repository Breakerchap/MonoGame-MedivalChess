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
  internal int MaximumRiverTargetsPerOutlet { get; init; } = 12;
  internal int MinimumMeanderLegLength { get; init; } = 5;
  internal int MinimumMeanderDetour { get; init; } = 1;
  internal int MaximumMeanderDetour { get; init; } = 2;
  internal int MaximumMeanderWaypointAttempts { get; init; } = 10;
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

    List<(int x, int y)> lakeCandidates = (deepInterior.Count > 0 ? deepInterior : protectedInterior)
      .Where(position => !forests.Contains(position))
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
      TryAddRiverFromLake(board, random, lake, lakes, rivers, settings);
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
    TerrainGenerationSettings settings
  )
  {
    List<RiverSegment> segments = GetRiverSegments(board);
    List<RiverSegment> outlets = segments
      .Where(segment =>
        sourceLake.Contains(segment.Edge.First) != sourceLake.Contains(segment.Edge.Second) &&
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
            ManhattanDistance(start, vertex) >= settings.MinimumRiverLength - 1
          )
          .ToList();
        Shuffle(random, targets);

        foreach ((int x, int y) target in targets.Take(settings.MaximumRiverTargetsPerOutlet))
        {
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
    int directDistance = ManhattanDistance(start, target);
    List<(int x, int y)> waypoints = graph.Keys
      .Where(vertex =>
      {
        int startDistance = ManhattanDistance(start, vertex);
        int targetDistance = ManhattanDistance(vertex, target);
        int detour = startDistance + targetDistance - directDistance;
        return startDistance >= settings.MinimumMeanderLegLength &&
          targetDistance >= settings.MinimumMeanderLegLength &&
          detour >= settings.MinimumMeanderDetour &&
          detour <= settings.MaximumMeanderDetour;
      })
      .ToList();
    Shuffle(random, waypoints);

    foreach ((int x, int y) waypoint in waypoints.Take(settings.MaximumMeanderWaypointAttempts))
    {
      List<RiverSegment> firstLeg = FindShortestRoute(graph, random, start, waypoint);
      if (firstLeg.Count == 0)
      {
        continue;
      }

      HashSet<TileEdge> firstLegEdges = [.. firstLeg.Select(segment => segment.Edge)];
      List<RiverSegment> secondLeg = FindShortestRoute(
        graph,
        random,
        waypoint,
        target,
        firstLegEdges
      );
      if (secondLeg.Count == 0)
      {
        continue;
      }

      firstLeg.AddRange(secondLeg);
      return firstLeg;
    }

    return FindShortestRoute(graph, random, start, target);
  }

  private static List<RiverSegment> FindShortestRoute(
    IReadOnlyDictionary<(int x, int y), List<RiverSegment>> graph,
    Random random,
    (int x, int y) start,
    (int x, int y) target,
    IReadOnlySet<TileEdge> excludedEdges = null
  )
  {
    Queue<(int x, int y)> frontier = [];
    Dictionary<(int x, int y), ((int x, int y) Previous, RiverSegment Segment)> previous = [];
    HashSet<(int x, int y)> visited = [start];
    frontier.Enqueue(start);

    while (frontier.TryDequeue(out (int x, int y) current))
    {
      if (current == target)
      {
        break;
      }

      List<RiverSegment> options = [.. graph[current]];
      Shuffle(random, options);
      foreach (RiverSegment segment in options)
      {
        if (excludedEdges?.Contains(segment.Edge) == true)
        {
          continue;
        }

        (int x, int y) next = segment.Start == current ? segment.End : segment.Start;
        if (!visited.Add(next))
        {
          continue;
        }

        previous[next] = (current, segment);
        frontier.Enqueue(next);
      }
    }

    if (!previous.ContainsKey(target))
    {
      return [];
    }

    List<RiverSegment> route = [];
    var cursor = target;
    while (cursor != start)
    {
      ((int x, int y) previousVertex, RiverSegment segment) = previous[cursor];
      route.Add(segment);
      cursor = previousVertex;
    }

    route.Reverse();
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
