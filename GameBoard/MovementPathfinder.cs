namespace MedivalChess.GameBoard;

using System;
using System.Collections.Generic;
using MedivalChess.Player;
using MedivalChess.Shared;

internal static class MovementPathfinder
{
  internal static Dictionary<(int x, int y), List<(int x, int y)>> FindPaths(
    Piece piece,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), (int x, int y), bool> crossesRiver
  )
  {
    return FindPaths(
      piece,
      canLand,
      (_, destination) => canTravelThrough(destination),
      landingCost,
      crossesRiver
    );
  }

  internal static Dictionary<(int x, int y), List<(int x, int y)>> FindPaths(
    Piece piece,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), (int x, int y), bool> canTravelThrough,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), (int x, int y), bool> crossesRiver,
    UnitRule movementRule = null,
    Func<(int x, int y), (int x, int y), int> stepCost = null,
    Func<(int x, int y), int> movementRangeAt = null,
    int? maximumMovementRange = null
  )
  {
    UnitRule rule = movementRule ?? UnitRules.FromPieceDefinition(piece.Definition);
    NetworkTeam team = piece.Team.ToNetworkTeam();

    if (piece.Definition.Type == PieceType.Raider)
    {
      int normalRange = rule.MoveRange;
      rule = rule with { MoveRange = normalRange + 2 };
      Func<(int x, int y), int> suppliedRange = movementRangeAt;
      movementRangeAt = destination =>
      {
        int range = suppliedRange?.Invoke(destination) ?? normalRange;
        return AbilityRules.IsForwardDestination(team, piece.Position, destination)
          ? range + 2
          : range;
      };
      maximumMovementRange = Math.Max(maximumMovementRange ?? 0, normalRange + 2);
    }

    return MovementRules.FindPaths(
      rule, piece.Position, team, canLand, canTravelThrough, landingCost, crossesRiver,
      stepCost, movementRangeAt, maximumMovementRange
    );
  }
}
