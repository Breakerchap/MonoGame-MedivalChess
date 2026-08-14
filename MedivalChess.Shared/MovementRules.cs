namespace MedivalChess.Shared;

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
    int? maximumMovementRange = null,
    Func<(int x, int y), bool>? canContinueFrom = null
  )
  {
    Func<(int x, int y), int> callerMovementRangeAt = movementRangeAt ?? (_ => unit.MoveRange);
    movementRangeAt = destination =>
      callerMovementRangeAt(destination) + AbilityRules.GetMovementRangeBonus(unit, team, origin, destination);
    canContinueFrom ??= _ => true;
    int maximumRange = (maximumMovementRange ?? unit.MoveRange) + AbilityRules.GetMaximumMovementRangeBonus(unit);

    if (unit.MovePattern is RuleShape.Line or RuleShape.Diagonal or RuleShape.LineOrDiagonal or RuleShape.AbsoluteStraightOrDiagonal)
    {
      return FindRayPaths(unit, origin, team, canLand, canTravelThrough, landingCost, crossesRiver,
        stepCost, movementRangeAt, maximumRange, canContinueFrom);
    }

    if (unit.MovePattern == RuleShape.ChessKnight)
    {
      return FindKnightPaths(unit, origin, team, canLand, canTravelThrough, landingCost, movementRangeAt);
    }

    Dictionary<(int x, int y), int> bestCosts = new() { [origin] = 0 };
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    Queue<MovementState> frontier = new();
    frontier.Enqueue(new(origin, 0, []));

    while (frontier.TryDequeue(out MovementState? current) && current is not null)
    {
      if (current.Cost >= maximumRange) continue;

      foreach ((int x, int y) direction in ShapeGeometryRules.GetStepDirections(unit.MovePattern, team))
      {
        var next = (x: current.Position.x + direction.x, y: current.Position.y + direction.y);
        if (!canTravelThrough(current.Position, next)) continue;

        int nextCost = crossesRiver(current.Position, next)
          ? GetRiverCrossingCost(current.Cost, unit.MoveRange)
          : current.Cost + (stepCost?.Invoke(current.Position, next) ?? landingCost(next));
        int effectiveRange = movementRangeAt(next);
        bool isInitialStep = current.Path.Count == 0;
        bool exceedsMovementRange = nextCost > effectiveRange;
        if ((exceedsMovementRange && !isInitialStep) ||
            (bestCosts.TryGetValue(next, out int bestCost) && bestCost <= nextCost)) continue;

        List<(int x, int y)> nextPath = [.. current.Path, next];
        bestCosts[next] = nextCost;
        if (canLand(next) && UnitRules.CanMove(unit, origin.x, origin.y, next.x, next.y, effectiveRange)) paths[next] = nextPath;
        if (!exceedsMovementRange && canContinueFrom(next)) frontier.Enqueue(new MovementState(next, nextCost, nextPath));
      }
    }

    return paths;
  }

  private static Dictionary<(int x, int y), List<(int x, int y)>> FindRayPaths(
    UnitRule unit,
    (int x, int y) origin,
    NetworkTeam team,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), (int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), (int x, int y), bool> crossesRiver,
    Func<(int x, int y), (int x, int y), int>? stepCost,
    Func<(int x, int y), int> movementRangeAt,
    int maximumMovementRange,
    Func<(int x, int y), bool> canContinueFrom
  )
  {
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];

    foreach ((int x, int y) direction in ShapeGeometryRules.GetStepDirections(unit.MovePattern, team))
    {
      int cost = 0;
      (int x, int y) previous = origin;
      List<(int x, int y)> path = [];

      for (int distance = 1; distance <= maximumMovementRange; distance++)
      {
        var next = (x: origin.x + direction.x * distance, y: origin.y + direction.y * distance);
        if (!canTravelThrough(previous, next)) break;

        cost = crossesRiver(previous, next)
          ? GetRiverCrossingCost(cost, unit.MoveRange)
          : cost + (stepCost?.Invoke(previous, next) ?? landingCost(next));
        int effectiveRange = movementRangeAt(next);
        bool isInitialStep = path.Count == 0;
        if (cost > effectiveRange)
        {
          if (isInitialStep && canLand(next) && UnitRules.CanMove(unit, origin.x, origin.y, next.x, next.y, effectiveRange)) paths[next] = [next];
          break;
        }

        path.Add(next);
        if (canLand(next) && UnitRules.CanMove(unit, origin.x, origin.y, next.x, next.y, effectiveRange)) paths[next] = [.. path];
        if (!canContinueFrom(next)) break;
        previous = next;
      }
    }

    return paths;
  }

  private static Dictionary<(int x, int y), List<(int x, int y)>> FindKnightPaths(
    UnitRule unit,
    (int x, int y) origin,
    NetworkTeam team,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), (int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), int> movementRangeAt
  )
  {
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    foreach ((int x, int y) offset in ShapeGeometryRules.GetStepDirections(RuleShape.ChessKnight, team))
    {
      var destination = (x: origin.x + offset.x, y: origin.y + offset.y);
      // A knight jumps intervening units/terrain, but its landing square must itself be valid.
      if (!canTravelThrough(destination, destination) || !canLand(destination)) continue;
      int effectiveRange = movementRangeAt(destination);
      if (landingCost(destination) <= effectiveRange &&
          UnitRules.CanMove(unit, origin.x, origin.y, destination.x, destination.y, effectiveRange))
      {
        paths[destination] = [destination];
      }
    }
    return paths;
  }

  private static int GetRiverCrossingCost(int currentCost, int baseMovementRange) => Math.Max(currentCost + 1, baseMovementRange);

  private sealed record MovementState((int x, int y) Position, int Cost, List<(int x, int y)> Path);
}
