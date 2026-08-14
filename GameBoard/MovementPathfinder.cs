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

    if (AbilityRules.IsTerrainImmune(rule))
    {
      Func<(int x, int y), (int x, int y), bool> suppliedTravel = canTravelThrough;
      Func<(int x, int y), int> suppliedLandingCost = landingCost;
      Func<(int x, int y), (int x, int y), int> suppliedStepCost = stepCost;

      canTravelThrough = (from, destination) =>
        suppliedTravel(from, destination) ||
        (HasBlockingUnitAt(piece, destination, out Piece blocker) &&
          AbilityRules.CanTravelThroughUnit(rule, team, blocker.Team.ToNetworkTeam()));

      landingCost = destination => AbilityRules.ApplyTerrainMovementCost(rule, suppliedLandingCost(destination));
      stepCost = suppliedStepCost is null
        ? null
        : (from, destination) => AbilityRules.ApplyTerrainMovementCost(rule, suppliedStepCost(from, destination));
      if (AbilityRules.IgnoresRivers(rule))
      {
        crossesRiver = (_, _) => false;
      }
    }

    return MovementRules.FindPaths(
      rule, piece.Position, team, canLand, canTravelThrough, landingCost, crossesRiver,
      stepCost, movementRangeAt, maximumMovementRange
    );
  }

  private static bool HasBlockingUnitAt(Piece mover, (int x, int y) destination, out Piece blocker)
  {
    blocker = null;
    if (mover.OwnerSetup is null)
    {
      return false;
    }

    blocker = mover.OwnerSetup.Pieces.FirstOrDefault(other =>
      other != mover &&
      other.AttachedTo is null &&
      other.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        destination.x, destination.y, mover.Definition.Size.x, mover.Definition.Size.y,
        other.Position.x, other.Position.y, other.Definition.Size.x, other.Definition.Size.y));
    return blocker is not null;
  }
}
