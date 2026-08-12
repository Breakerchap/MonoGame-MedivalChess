namespace MedivalChess.Shared;

/// <summary>Terrain-aware movement search shared by the local client and authoritative server.</summary>
public static class MovementRules
{
  public static Dictionary<(int x, int y), List<(int x, int y)>> FindPaths(
    UnitRule unit,
    (int x, int y) origin,
    NetworkTeam team,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), (int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), (int x, int y), bool> crossesRiver,
    Func<(int x, int y), (int x, int y), int>? stepCost = null,
    Func<(int x, int y), int>? movementRangeAt = null,
    int? maximumMovementRange = null
  )
  {
    movementRangeAt ??= _ => unit.MoveRange;
    int maximumRange = maximumMovementRange ?? unit.MoveRange;

    if (unit.MovePattern == RuleShape.ChessKnight)
    {
      return FindChessKnightPaths(unit, origin, canLand, landingCost, movementRangeAt, maximumRange);
    }

    if (unit.MovePattern is RuleShape.Line or RuleShape.Diagonal or RuleShape.LineOrDiagonal)
    {
      return FindLinePaths(
        unit, origin, team, canLand, canTravelThrough, landingCost, crossesRiver,
        stepCost, movementRangeAt, maximumRange
      );
    }

    Dictionary<(int x, int y), int> bestCosts = new() { [origin] = 0 };
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    Queue<MovementState> frontier = new();
    frontier.Enqueue(new(origin, 0, []));

    while (frontier.TryDequeue(out MovementState? current) && current is not null)
    {
      if (current.Cost >= maximumRange) continue;

      foreach ((int x, int y) direction in GetStepDirections(unit.MovePattern, team))
      {
        int maximumStepDistance = GetMaximumStepDistance(unit, direction);
        for (int stepDistance = 1; stepDistance <= maximumStepDistance; stepDistance++)
        {
          var next = (
            x: current.Position.x + direction.x * stepDistance,
            y: current.Position.y + direction.y * stepDistance
          );
          if (!canTravelThrough(current.Position, next)) continue;

          int nextCost = crossesRiver(current.Position, next)
            ? GetRiverCrossingCost(current.Cost, unit.MoveRange)
            : current.Cost + (stepCost?.Invoke(current.Position, next) ?? landingCost(next));
          bool isInitialStep = current.Path.Count == 0;
          bool exceedsMovementRange = nextCost > movementRangeAt(next);
          if ((exceedsMovementRange && !isInitialStep) ||
              (bestCosts.TryGetValue(next, out int bestCost) && bestCost <= nextCost)) continue;

          List<(int x, int y)> nextPath = [.. current.Path, next];
          bestCosts[next] = nextCost;
          if (nextPath.Count >= unit.MinimumMoveRange && canLand(next)) paths[next] = nextPath;
          if (!exceedsMovementRange)
          {
            frontier.Enqueue(new MovementState(next, nextCost, nextPath));
          }
        }
      }
    }

    return paths;
  }

  private static Dictionary<(int x, int y), List<(int x, int y)>> FindChessKnightPaths(
    UnitRule unit,
    (int x, int y) origin,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), int>? movementRangeAt,
    int maximumMovementRange
  )
  {
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    (int x, int y)[] offsets =
    [
      (2, 1), (2, -1), (-2, 1), (-2, -1),
      (1, 2), (1, -2), (-1, 2), (-1, -2)
    ];
    foreach ((int x, int y) offset in offsets)
    {
      (int x, int y) next = (origin.x + offset.x, origin.y + offset.y);
      int availableRange = movementRangeAt?.Invoke(next) ?? unit.MoveRange;
      if (maximumMovementRange < 3 || availableRange < 3 || landingCost(next) > availableRange || !canLand(next))
      {
        continue;
      }
      paths[next] = [next];
    }
    return paths;
  }

  private static Dictionary<(int x, int y), List<(int x, int y)>> FindLinePaths(
    UnitRule unit,
    (int x, int y) origin,
    NetworkTeam team,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), (int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), (int x, int y), bool> crossesRiver,
    Func<(int x, int y), (int x, int y), int>? stepCost,
    Func<(int x, int y), int>? movementRangeAt,
    int maximumMovementRange
  )
  {
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    movementRangeAt ??= _ => unit.MoveRange;

    foreach ((int x, int y) direction in GetStepDirections(unit.MovePattern, team))
    {
      int cost = 0;
      (int x, int y) previous = origin;
      List<(int x, int y)> path = [];

      for (int distance = 1; distance <= maximumMovementRange; distance++)
      {
        var next = (
          x: origin.x + direction.x * distance,
          y: origin.y + direction.y * distance
        );
        if (!canTravelThrough(previous, next)) break;

        cost = crossesRiver(previous, next)
          ? GetRiverCrossingCost(cost, unit.MoveRange)
          : cost + (stepCost?.Invoke(previous, next) ?? landingCost(next));
        bool isInitialStep = path.Count == 0;
        if (cost > movementRangeAt(next))
        {
          if (isInitialStep && canLand(next)) paths[next] = [next];
          break;
        }

        path.Add(next);
        if (distance >= unit.MinimumMoveRange && canLand(next)) paths[next] = [.. path];
        previous = next;
      }
    }

    return paths;
  }

  /// <summary>
  /// Crossing a river consumes at least the unit's normal movement allowance, but it must never
  /// reduce movement already spent. This lets a genuine range bonus provide only its extra movement
  /// instead of allowing successive river crossings to keep resetting the path cost forever.
  /// </summary>
  private static int GetRiverCrossingCost(int currentCost, int baseMovementRange) =>
    Math.Max(currentCost + 1, baseMovementRange);

  public static IReadOnlyList<(int x, int y)> GetStepDirections(RuleShape shape, NetworkTeam team) => shape switch
  {
    RuleShape.Straight => [(1, 0), (-1, 0), (0, 1), (0, -1)],
    RuleShape.Line => [(1, 0), (-1, 0), (0, 1), (0, -1)],
    RuleShape.Diagonal => [(1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.LineOrDiagonal => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.Any => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.Forward or RuleShape.ForwardLine => [TeamRules.GetForwardDirection(team)],
    RuleShape.ForwardOrForwardDiagonal => GetForwardAndDiagonalDirections(team),
    RuleShape.AbsoluteStraightOrDiagonal => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    _ => []
  };

  private static IReadOnlyList<(int x, int y)> GetForwardAndDiagonalDirections(NetworkTeam team)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    return forward.x == 0
      ? [forward, (-1, forward.y), (1, forward.y)]
      : [forward, (forward.x, -1), (forward.x, 1)];
  }

  private static int GetMaximumStepDistance(UnitRule unit, (int x, int y) direction)
  {
    return 1;
  }

  private sealed record MovementState((int x, int y) Position, int Cost, List<(int x, int y)> Path);
}
