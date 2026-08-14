namespace MedivalChess.Shared;

/// <summary>
/// Single source of truth for shape geometry used by local play, CPU simulation and server rules.
/// </summary>
public static class ShapeGeometryRules
{
  private static readonly (int x, int y)[] OrthogonalDirections =
    [(1, 0), (-1, 0), (0, 1), (0, -1)];
  private static readonly (int x, int y)[] DiagonalDirections =
    [(1, 1), (1, -1), (-1, 1), (-1, -1)];
  private static readonly (int x, int y)[] EightDirections =
    [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
  private static readonly (int x, int y)[] KnightOffsets =
  [
    (1, 2), (2, 1), (-1, 2), (-2, 1),
    (1, -2), (2, -1), (-1, -2), (-2, -1)
  ];

  public static IReadOnlyList<(int x, int y)> GetStepDirections(RuleShape shape, NetworkTeam team) => shape switch
  {
    RuleShape.Straight or RuleShape.Line => OrthogonalDirections,
    RuleShape.Diagonal => DiagonalDirections,
    RuleShape.LineOrDiagonal or RuleShape.Any or RuleShape.Circle or RuleShape.AbsoluteStraightOrDiagonal => EightDirections,
    RuleShape.ChessKnight => KnightOffsets,
    RuleShape.Forward or RuleShape.ForwardLine => [TeamRules.GetForwardDirection(team)],
    RuleShape.ForwardOrForwardDiagonal => GetForwardAndDiagonalDirections(team),
    RuleShape.MoveOnEnemy => EightDirections,
    _ => Array.Empty<(int x, int y)>()
  };

  public static List<(int x, int y)> GetOffsets(
    RuleShape shape,
    int minimumRange,
    int maximumRange,
    NetworkTeam team
  )
  {
    if (shape == RuleShape.None || maximumRange < minimumRange || maximumRange < 0)
    {
      return [];
    }

    if (shape == RuleShape.MoveOnEnemy)
    {
      shape = RuleShape.Any;
    }

    List<(int x, int y)> offsets = [];
    for (int dx = -maximumRange; dx <= maximumRange; dx++)
    for (int dy = -maximumRange; dy <= maximumRange; dy++)
    {
      if (dx == 0 && dy == 0 && minimumRange > 0) continue;
      if (UnitRules.CanAttackOffset(shape, minimumRange, maximumRange, team, dx, dy))
      {
        offsets.Add((dx, dy));
      }
    }
    return offsets;
  }

  public static double Distance(RuleShape shape, (int x, int y) offset) => shape switch
  {
    RuleShape.Straight => Math.Abs(offset.x) + Math.Abs(offset.y),
    RuleShape.Circle => Math.Sqrt(offset.x * offset.x + offset.y * offset.y),
    _ => Math.Max(Math.Abs(offset.x), Math.Abs(offset.y))
  };

  private static IReadOnlyList<(int x, int y)> GetForwardAndDiagonalDirections(NetworkTeam team)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    return forward.x == 0
      ? [forward, (-1, forward.y), (1, forward.y)]
      : [forward, (forward.x, -1), (forward.x, 1)];
  }
}
