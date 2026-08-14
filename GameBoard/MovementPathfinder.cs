namespace MedivalChess.GameBoard;

using System;
using System.Collections.Generic;
using System.Linq;
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

    if (piece.Definition.Type == PieceType.Sleipnir)
    {
      Func<(int x, int y), (int x, int y), bool> suppliedTravel = canTravelThrough;
      Func<(int x, int y), int> suppliedLandingCost = landingCost;
      Func<(int x, int y), (int x, int y), int> suppliedStepCost = stepCost;

      canTravelThrough = (from, destination) =>
        suppliedTravel(from, destination) || HasBlockingUnitAt(piece, destination);

      // Sleipnir ignores movement penalties from terrain. A road on open ground still keeps
      // its zero-cost benefit because Math.Min preserves the existing road cost of zero.
      landingCost = destination => Math.Min(1, suppliedLandingCost(destination));
      stepCost = suppliedStepCost is null
        ? null
        : (from, destination) => Math.Min(1, suppliedStepCost(from, destination));
      crossesRiver = (_, _) => false;
    }

    return MovementRules.FindPaths(
      rule, piece.Position, team, canLand, canTravelThrough, landingCost, crossesRiver,
      stepCost, movementRangeAt, maximumMovementRange
    );
  }

  private static bool HasBlockingUnitAt(Piece mover, (int x, int y) destination)
  {
    if (mover.OwnerSetup is null)
    {
      return false;
    }

    return mover.OwnerSetup.Pieces.Any(other =>
      other != mover &&
      other.AttachedTo is null &&
      other.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        destination.x, destination.y, mover.Definition.Size.x, mover.Definition.Size.y,
        other.Position.x, other.Position.y, other.Definition.Size.x, other.Definition.Size.y));
  }
}
