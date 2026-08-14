using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>CPU board-state adapter for shared Chess landing-capture rules.</summary>
public static partial class CpuGameRules
{
  private static NetworkPiece? GetChessCaptureTarget(
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) destination
  )
  {
    if (!ChessAbilityRules.IsLandingCaptureUnit(mover.Type)) return null;
    NetworkPiece? target = pieces.FirstOrDefault(other =>
      other.Id != mover.Id && other.AttachedToId is null && other.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, destination));
    return target is not null && ChessAbilityRules.CanCaptureByLanding(
      moverRule,
      mover.Team,
      (mover.X, mover.Y),
      destination,
      target.Team)
      ? target
      : null;
  }

  private static bool CanChessCaptureLand(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) destination
  )
  {
    NetworkPiece? target = GetChessCaptureTarget(pieces, mover, moverRule, destination);
    if (target is null || !BoardRules.FootprintFitsBoard(
      state.Board, destination.x, destination.y, moverRule.Width, moverRule.Height)) return false;
    return OccupiedSquares(moverRule, destination).All(square =>
      !state.Terrain.IsLake(square) && !state.Barricades.ContainsKey(square));
  }

  private static bool CanContinueChessPath(
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece mover,
    UnitRule moverRule,
    (int x, int y) position
  ) => GetChessCaptureTarget(pieces, mover, moverRule, position) is null;

  private static void AddPawnCapturePaths(
    CpuGameState state,
    IReadOnlyList<NetworkPiece> pieces,
    NetworkPiece pawn,
    UnitRule pawnRule,
    IDictionary<(int x, int y), List<(int x, int y)>> paths
  )
  {
    foreach ((int x, int y) destination in ChessAbilityRules.GetAdditionalCaptureDestinations(
      pawnRule, pawn.Team, (pawn.X, pawn.Y)))
    {
      if (CanChessCaptureLand(state, pieces, pawn, pawnRule, destination))
      {
        paths[destination] = [destination];
      }
    }
  }
}
