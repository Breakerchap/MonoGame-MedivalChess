namespace MedivalChess.Shared;

/// <summary>Shared deterministic displacement helpers for push, recoil, and retreat abilities.</summary>
public static class DisplacementRules
{
  public static bool CanBePushed(string unitType) =>
    !string.Equals(unitType, nameof(PieceType.Beelzebub), StringComparison.Ordinal);

  /// <summary>
  /// Moves <paramref name="start"/> directly away from <paramref name="source"/> by up to
  /// <paramref name="maximumDistance"/> tiles. Movement stops immediately before the first
  /// illegal square, so a blocked two-tile push becomes a one-tile push and a blocked
  /// one-tile push leaves the unit in place.
  /// </summary>
  public static (int x, int y) GetFurthestLegalPositionAwayFrom(
    (int x, int y) source,
    (int x, int y) start,
    int maximumDistance,
    Func<(int x, int y), bool> isLegal
  ) => GetFurthestLegalPosition(
    start,
    Math.Sign(start.x - source.x),
    Math.Sign(start.y - source.y),
    maximumDistance,
    isLegal);

  public static (int x, int y) GetFurthestLegalPosition(
    (int x, int y) start,
    int directionX,
    int directionY,
    int maximumDistance,
    Func<(int x, int y), bool> isLegal,
    bool requireFullDistance = false
  )
  {
    ArgumentNullException.ThrowIfNull(isLegal);
    if (maximumDistance <= 0)
    {
      return start;
    }

    int stepX = Math.Sign(directionX);
    int stepY = Math.Sign(directionY);
    if (stepX == 0 && stepY == 0)
    {
      return start;
    }

    (int x, int y) result = start;
    for (int distance = 1; distance <= maximumDistance; distance++)
    {
      var candidate = (x: start.x + stepX * distance, y: start.y + stepY * distance);
      if (!isLegal(candidate))
      {
        break;
      }
      result = candidate;
    }

    if (requireFullDistance)
    {
      var required = (x: start.x + stepX * maximumDistance, y: start.y + stepY * maximumDistance);
      return result == required ? result : start;
    }

    return result;
  }
}
