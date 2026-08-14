using MedivalChess.Shared;

namespace MedivalChess.Server;

/// <summary>Authoritative board-state adapter for shared Chess landing-capture rules.</summary>
public sealed partial class MatchStore
{
  private static NetworkPiece? GetServerChessCaptureTarget(
    Match match,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) destination
  )
  {
    if (!ChessAbilityRules.IsLandingCaptureUnit(mover.Type)) return null;
    NetworkPiece? target = match.Pieces.FirstOrDefault(other =>
      other.Id != mover.Id && other.AttachedToId is null && other.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(
        other.X, other.Y, otherRule.Width, otherRule.Height,
        destination.x, destination.y, moverRule.Width, moverRule.Height));
    return target is not null && ChessAbilityRules.CanCaptureByLanding(
      moverRule,
      mover.Team,
      (mover.X, mover.Y),
      destination,
      target.Team)
      ? target
      : null;
  }

  private static bool CanServerChessCaptureLand(
    Match match,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) destination
  )
  {
    NetworkPiece? target = GetServerChessCaptureTarget(match, mover, moverRule, destination);
    if (target is null || !NetworkPieceRules.FootprintFitsBoard(
      match.Configuration, destination.x, destination.y, moverRule.Width, moverRule.Height)) return false;
    return OccupiedSquares(moverRule, destination).All(square =>
      !match.Terrain.IsLake(square) && !match.Barricades.ContainsKey(square));
  }

  private static bool CanContinueServerChessPath(
    Match match,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) position
  ) => GetServerChessCaptureTarget(match, mover, moverRule, position) is null;

  private static void AddServerPawnCapturePaths(
    Match match,
    NetworkPiece pawn,
    UnitRule pawnRule,
    IDictionary<(int x, int y), List<(int x, int y)>> paths
  )
  {
    foreach ((int x, int y) destination in ChessAbilityRules.GetAdditionalCaptureDestinations(
      pawnRule, pawn.Team, (pawn.X, pawn.Y)))
    {
      if (CanServerChessCaptureLand(match, pawn, pawnRule, destination))
      {
        paths[destination] = [destination];
      }
    }
  }
}
