namespace MedivalChess.Shared;

/// <summary>
/// Portable unit data and board geometry used by every game mode. This project deliberately has
/// no MonoGame dependency, so an authoritative server and the desktop client cannot silently
/// drift apart on unit statistics, sizes, or basic move/attack patterns.
/// </summary>
public enum RuleShape
{
  Any,
  Straight,
  Circle,
  Line,
  Diagonal,
  LineOrDiagonal,
  ChessKnight,
  Forward,
  ForwardDiagonal,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  ForwardLine,
  PierceStraight,
  MoveOnEnemy,
  None
}

public enum RuleCategory
{
  Melee,
  Ranged,
  Intelligence,
  Mechanical,
  Structure,
  Transport,
  Royal
}

public sealed record UnitRule(
  string Type,
  RuleCategory Category,
  int MoveRange,
  RuleShape MovePattern,
  int Attack,
  int Health,
  int Width,
  int Height,
  int AttackRange,
  RuleShape AttackPattern,
  int Cost,
  int MinimumAttackRange = 1,
  string AbilityDescription = "",
  int MinimumMoveRange = 1
)
{
  public AttackRange AllowedAttackRange => new(MinimumAttackRange, AttackRange);
}

public static class UnitRules
{
  private static readonly UnitRule[] Rules = PieceDefinitions.Encyclopedia
    .Select(FromPieceDefinition)
    .ToArray();

  private static readonly Dictionary<string, UnitRule> ByType = Rules.ToDictionary(rule => rule.Type, StringComparer.Ordinal);

  public static IReadOnlyList<UnitRule> All => Rules;
  public static IReadOnlyList<UnitRule> Purchasable { get; } = PieceDefinitions.Purchasable
    .Select(FromPieceDefinition)
    .ToArray();
  public static IReadOnlyList<UnitRule> Royals { get; } = PieceDefinitions.Royals
    .Select(FromPieceDefinition)
    .ToArray();

  public static bool TryGet(string type, out UnitRule rule) => ByType.TryGetValue(type, out rule!);
  public static UnitRule GetRequired(string type) => TryGet(type, out UnitRule rule)
    ? rule
    : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unit type.");

  public static string GetAbilityDescription(string type) => TryGet(type, out UnitRule rule)
    ? rule.AbilityDescription
    : string.Empty;

  public static UnitRule FromPieceDefinition(PieceDefinition definition)
  {
    ArgumentNullException.ThrowIfNull(definition);
    return new UnitRule(
      definition.Identifier,
      (RuleCategory)definition.Category,
      definition.Movement.range,
      ToRuleShape(definition.Movement.shape),
      definition.Attack,
      definition.Health,
      definition.Size.x,
      definition.Size.y,
      definition.AttackRange.Maximum,
      ToRuleShape(definition.AttackPattern),
      definition.Cost,
      definition.AttackRange.Minimum,
      definition.AbilityDescription,
      definition.Movement.Minimum
    );
  }

  public static RuleShape ToRuleShape(Shape shape) => shape switch
  {
    Shape.Any => RuleShape.Any,
    Shape.Straight => RuleShape.Straight,
    Shape.Circle => RuleShape.Circle,
    Shape.Line => RuleShape.Line,
    Shape.Diagonal => RuleShape.Diagonal,
    Shape.LineOrDiagonal => RuleShape.LineOrDiagonal,
    Shape.ChessKnight => RuleShape.ChessKnight,
    Shape.Forward => RuleShape.Forward,
    Shape.ForwardDiagonal => RuleShape.ForwardDiagonal,
    Shape.AbsoluteStraightOrDiagonal => RuleShape.AbsoluteStraightOrDiagonal,
    Shape.ForwardOrForwardDiagonal => RuleShape.ForwardOrForwardDiagonal,
    Shape.ForwardLine => RuleShape.ForwardLine,
    Shape.PierceStraight => RuleShape.PierceStraight,
    Shape.MoveOnEnemy => RuleShape.MoveOnEnemy,
    _ => RuleShape.None
  };

  public static bool FootprintsOverlap(
    int firstX, int firstY, int firstWidth, int firstHeight,
    int secondX, int secondY, int secondWidth, int secondHeight
  ) => firstX < secondX + secondWidth && firstX + firstWidth > secondX &&
       firstY < secondY + secondHeight && firstY + firstHeight > secondY;

  public static bool CanMove(UnitRule rule, int fromX, int fromY, int toX, int toY, int? maximumRangeOverride = null)
  {
    int dx = Math.Abs(toX - fromX);
    int dy = Math.Abs(toY - fromY);
    if (dx == 0 && dy == 0) return false;
    int maximumRange = maximumRangeOverride ?? rule.MoveRange;

    if (rule.MovePattern == RuleShape.Circle)
    {
      int squaredDistance = dx * dx + dy * dy;
      return squaredDistance >= rule.MinimumMoveRange * rule.MinimumMoveRange &&
             squaredDistance <= maximumRange * maximumRange;
    }

    int chessboardDistance = Math.Max(dx, dy);
    int taxicabDistance = dx + dy;
    int distance = rule.MovePattern == RuleShape.Straight ? taxicabDistance : chessboardDistance;
    if (distance < rule.MinimumMoveRange || distance > maximumRange) return false;

    return rule.MovePattern switch
    {
      RuleShape.Straight => true,
      RuleShape.Line => dx == 0 || dy == 0,
      RuleShape.Diagonal => dx == dy,
      RuleShape.LineOrDiagonal or RuleShape.AbsoluteStraightOrDiagonal => dx == 0 || dy == 0 || dx == dy,
      RuleShape.ChessKnight => (dx == 1 && dy == 2) || (dx == 2 && dy == 1),
      RuleShape.Any => true,
      RuleShape.None => false,
      _ => true
    };
  }

  public static bool CanAttack(
    UnitRule attacker,
    int attackerX,
    int attackerY,
    NetworkTeam attackerTeam,
    UnitRule target,
    int targetX,
    int targetY
  )
  {
    if (attacker.Attack <= 0 || attacker.AttackPattern == RuleShape.None ||
        !AbilityRules.CanDamageTarget(attacker, target)) return false;
    for (int sourceY = 0; sourceY < attacker.Height; sourceY++)
    for (int sourceX = 0; sourceX < attacker.Width; sourceX++)
    for (int victimY = 0; victimY < target.Height; victimY++)
    for (int victimX = 0; victimX < target.Width; victimX++)
    {
      if (CanAttackOffset(
        attacker.AttackPattern,
        attacker.MinimumAttackRange,
        attacker.AttackRange,
        attackerTeam,
        targetX + victimX - (attackerX + sourceX),
        targetY + victimY - (attackerY + sourceY))) return true;
    }

    return false;
  }

  public static bool CanAttackOffset(
    RuleShape pattern,
    int minimumRange,
    int range,
    NetworkTeam team,
    int dx,
    int dy
  )
  {
    int absX = Math.Abs(dx);
    int absY = Math.Abs(dy);

    if (pattern == RuleShape.Circle)
    {
      int squaredDistance = absX * absX + absY * absY;
      return squaredDistance >= minimumRange * minimumRange &&
             squaredDistance <= range * range;
    }

    int chessboardDistance = Math.Max(absX, absY);
    int taxicabDistance = absX + absY;
    int distance = pattern == RuleShape.Straight ? taxicabDistance : chessboardDistance;
    if (distance < minimumRange || distance > range) return false;

    return pattern switch
    {
      RuleShape.Any => true,
      RuleShape.Straight => true,
      RuleShape.Line or RuleShape.PierceStraight => dx == 0 || dy == 0,
      RuleShape.Diagonal => absX == absY,
      RuleShape.LineOrDiagonal or RuleShape.AbsoluteStraightOrDiagonal => dx == 0 || dy == 0 || absX == absY,
      RuleShape.ChessKnight => (absX == 1 && absY == 2) || (absX == 2 && absY == 1),
      RuleShape.Forward => IsForwardOffset(team, dx, dy, distance),
      RuleShape.ForwardDiagonal => IsForwardDiagonalOffset(team, dx, dy, distance),
      RuleShape.ForwardLine => IsForwardOffset(team, dx, dy, distance),
      RuleShape.ForwardOrForwardDiagonal => IsForwardOrDiagonalOffset(team, dx, dy, distance),
      _ => false
    };
  }

  private static bool IsForwardOffset(NetworkTeam team, int dx, int dy, int distance)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    return dx == forward.x * distance && dy == forward.y * distance;
  }

  private static bool IsForwardOrDiagonalOffset(NetworkTeam team, int dx, int dy, int distance)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    return forward.x == 0
      ? dy == forward.y * distance && Math.Abs(dx) <= distance
      : dx == forward.x * distance && Math.Abs(dy) <= distance;
  }

  private static bool IsForwardDiagonalOffset(NetworkTeam team, int dx, int dy, int distance)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    return forward.x == 0
      ? dy == forward.y * distance && Math.Abs(dx) == distance
      : dx == forward.x * distance && Math.Abs(dy) == distance;
  }
}
