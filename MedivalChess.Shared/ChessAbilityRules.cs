namespace MedivalChess.Shared;

/// <summary>
/// Shared rules for Chess-pack pieces. Chess captures are made by moving onto an enemy rather than
/// by using the ordinary ranged/melee attack action. Runtime layers only apply the returned result.
/// </summary>
public static class ChessAbilityRules
{
  public static bool IsLandingCaptureUnit(string unitType) => unitType is
    nameof(PieceType.Pawn) or nameof(PieceType.ChessKnight) or nameof(PieceType.Bishop) or
    nameof(PieceType.Rook) or nameof(PieceType.Queen) or nameof(PieceType.ChessKing);

  public static bool IsChessKing(string unitType) => unitType == nameof(PieceType.ChessKing);

  public static bool CanCaptureByLanding(
    UnitRule mover,
    NetworkTeam moverTeam,
    (int x, int y) origin,
    (int x, int y) destination,
    NetworkTeam targetTeam
  )
  {
    if (!IsLandingCaptureUnit(mover.Type) || targetTeam == moverTeam)
    {
      return false;
    }

    int dx = destination.x - origin.x;
    int dy = destination.y - origin.y;
    int absX = Math.Abs(dx);
    int absY = Math.Abs(dy);
    int distance = Math.Max(absX, absY);

    return mover.Type switch
    {
      nameof(PieceType.Pawn) => IsPawnCapture(moverTeam, dx, dy),
      nameof(PieceType.ChessKnight) => (absX == 1 && absY == 2) || (absX == 2 && absY == 1),
      nameof(PieceType.Bishop) => absX == absY && distance is > 0 && distance <= mover.MoveRange,
      nameof(PieceType.Rook) => (dx == 0 || dy == 0) && distance is > 0 && distance <= mover.MoveRange,
      nameof(PieceType.Queen) => (dx == 0 || dy == 0 || absX == absY) && distance is > 0 && distance <= mover.MoveRange,
      nameof(PieceType.ChessKing) => distance == 1,
      _ => false
    };
  }

  public static IReadOnlyList<(int x, int y)> GetAdditionalCaptureDestinations(
    UnitRule mover,
    NetworkTeam moverTeam,
    (int x, int y) origin
  )
  {
    if (mover.Type != nameof(PieceType.Pawn))
    {
      return Array.Empty<(int x, int y)>();
    }

    (int x, int y) forward = TeamRules.GetForwardDirection(moverTeam);
    return forward.x == 0
      ? [(origin.x - 1, origin.y + forward.y), (origin.x + 1, origin.y + forward.y)]
      : [(origin.x + forward.x, origin.y - 1), (origin.x + forward.x, origin.y + 1)];
  }

  public static bool CanContinueAfterEnteringOccupiedSquare(string unitType) =>
    !IsLandingCaptureUnit(unitType);

  public static (int x, int y) GetFailedCaptureFallback(
    (int x, int y) origin,
    IReadOnlyList<(int x, int y)> path
  )
  {
    if (path.Count <= 1)
    {
      return origin;
    }
    return path[^2];
  }

  public static bool IsCheckmated(
    (int x, int y) kingPosition,
    IEnumerable<(int x, int y)> candidateEscapes,
    Func<(int x, int y), bool> canOccupy,
    Func<(int x, int y), bool> isThreatened
  ) => isThreatened(kingPosition) &&
       !candidateEscapes.Any(square => canOccupy(square) && !isThreatened(square));

  /// <summary>A Chess King ignores lethal damage unless it is checkmated.</summary>
  public static bool CanChessKingDie(bool isCheckmated) => isCheckmated;

  private static bool IsPawnCapture(NetworkTeam team, int dx, int dy)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    if (forward.x == 0)
    {
      return dy == forward.y && Math.Abs(dx) == 1;
    }
    return dx == forward.x && Math.Abs(dy) == 1;
  }
}
