using System.Collections.Generic;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess;

/// <summary>Local board-state adapter for shared Chess landing-capture rules.</summary>
internal sealed partial class Game1
{
  private Piece GetLocalChessCaptureTarget(Piece mover, UnitRule moverRule, (int x, int y) destination)
  {
    if (!ChessAbilityRules.IsLandingCaptureUnit(mover.Definition.Type.ToString())) return null;
    Piece target = pieceSetup.Pieces.FirstOrDefault(other =>
      other != mover && other.AttachedTo is null && other.Definition.Type != PieceType.Farm &&
      FootprintsOverlap(mover.Definition, destination, other.Definition, other.Position));
    return target is not null && ChessAbilityRules.CanCaptureByLanding(
      moverRule,
      mover.Team.ToNetworkTeam(),
      mover.Position,
      destination,
      target.Team.ToNetworkTeam())
      ? target
      : null;
  }

  private bool CanLocalChessCaptureLand(Piece mover, UnitRule moverRule, (int x, int y) destination)
  {
    Piece target = GetLocalChessCaptureTarget(mover, moverRule, destination);
    if (target is null || !IsFootprintOnBoard(mover.Definition, destination)) return false;
    return OccupiedSquares(mover.Definition, destination).All(square =>
      !_terrain.IsLake(square) && !_barricades.ContainsKey(square));
  }

  private bool CanContinueLocalChessPath(Piece mover, UnitRule moverRule, (int x, int y) position) =>
    GetLocalChessCaptureTarget(mover, moverRule, position) is null;

  private void AddLocalPawnCapturePaths(
    Piece pawn,
    UnitRule pawnRule,
    IDictionary<(int x, int y), List<(int x, int y)>> paths
  )
  {
    foreach ((int x, int y) destination in ChessAbilityRules.GetAdditionalCaptureDestinations(
      pawnRule, pawn.Team.ToNetworkTeam(), pawn.Position))
    {
      if (CanLocalChessCaptureLand(pawn, pawnRule, destination))
      {
        paths[destination] = [destination];
      }
    }
  }

  private (int x, int y) ResolveLocalChessLandingCapture(
    Piece mover,
    IReadOnlyList<(int x, int y)> path,
    (int x, int y) requestedDestination
  )
  {
    UnitRule moverRule = GetEffectiveMovementRule(mover);
    Piece target = GetLocalChessCaptureTarget(mover, moverRule, requestedDestination);
    if (target is null) return requestedDestination;

    ResolveDamage(mover, target);
    mover.HasAttackedThisTurn = true;
    return pieceSetup.Pieces.Contains(target)
      ? ChessAbilityRules.GetFailedCaptureFallback(mover.Position, path)
      : requestedDestination;
  }
}
