namespace MedivalChess.GameBoard;

using System;
using System.Collections.Generic;

internal static class MovementPathfinder
{
  private sealed class PathState
  {
    internal (int x, int y) Position { get; init; }
    internal int Cost { get; init; }
    internal List<(int x, int y)> Path { get; init; }
  }

  internal static Dictionary<(int x, int y), List<(int x, int y)>> FindPaths(
    Piece piece,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), (int x, int y), bool> crossesRiver
  )
  {
    int movementPoints = piece.Definition.Movement.range;
    Dictionary<(int x, int y), int> bestCosts = new() { [piece.Position] = 0 };
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    Queue<PathState> frontier = new();
    frontier.Enqueue(new PathState { Position = piece.Position, Cost = 0, Path = [] });

    while (frontier.Count > 0)
    {
      PathState current = frontier.Dequeue();
      if (current.Cost >= movementPoints)
      {
        continue;
      }

      foreach ((int x, int y) direction in Actions.ValidMovementStepDirections(piece))
      {
        var next = (
          x: current.Position.x + direction.x * piece.Definition.Size.x,
          y: current.Position.y + direction.y * piece.Definition.Size.y
        );

        if (!canTravelThrough(next))
        {
          continue;
        }

        int nextCost = crossesRiver(current.Position, next)
          ? movementPoints
          : current.Cost + landingCost(next);
        if (nextCost > movementPoints ||
            (bestCosts.TryGetValue(next, out int bestCost) && bestCost <= nextCost))
        {
          continue;
        }

        List<(int x, int y)> nextPath = [.. current.Path, next];
        bestCosts[next] = nextCost;
        if (canLand(next))
        {
          paths[next] = nextPath;
        }

        frontier.Enqueue(new PathState { Position = next, Cost = nextCost, Path = nextPath });
      }
    }

    return paths;
  }
}
