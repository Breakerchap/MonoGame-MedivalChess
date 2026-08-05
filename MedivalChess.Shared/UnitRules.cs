namespace MedivalChess.Shared;

/// <summary>
/// Portable unit data and board geometry used by every game mode.  This project deliberately has
/// no MonoGame dependency, so an authoritative server and the desktop client cannot silently
/// drift apart on unit statistics, sizes, or basic move/attack patterns.
/// </summary>
public enum RuleShape
{
  Any,
  Straight,
  Forward,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  PierceStraight,
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
  string AbilityDescription = ""
);

public static class UnitRules
{
  private static readonly UnitRule[] Rules =
  [
    new("Soldier", RuleCategory.Melee, 3, RuleShape.Straight, 10, 15, 1, 1, 1, RuleShape.Straight, 20),
    new("Defender", RuleCategory.Melee, 2, RuleShape.Any, 5, 25, 1, 1, 1, RuleShape.Straight, 15),
    new("Archer", RuleCategory.Ranged, 3, RuleShape.Straight, 10, 10, 1, 1, 3, RuleShape.Any, 30, 2),
    new("Peasant", RuleCategory.Melee, 1, RuleShape.Any, 5, 5, 1, 1, 1, RuleShape.Straight, 10),
    new("Knight", RuleCategory.Melee, 4, RuleShape.Any, 20, 30, 1, 1, 1, RuleShape.Any, 50),
    new("Crossbowman", RuleCategory.Ranged, 2, RuleShape.Any, 20, 15, 1, 1, 3, RuleShape.Any, 50, 1),
    new("Chariot", RuleCategory.Melee, 5, RuleShape.Straight, 15, 25, 1, 1, 2, RuleShape.Straight, 40, 2),
    new("Cannon", RuleCategory.Mechanical, 2, RuleShape.Straight, 30, 15, 1, 2, 4, RuleShape.Straight, 50, 2),
    new("Spy", RuleCategory.Intelligence, 5, RuleShape.Any, 0, 15, 1, 1, 3, RuleShape.Straight, 35, 1, "Marks an enemy; it takes double damage until attacked."),
    new("Catapult", RuleCategory.Mechanical, 1, RuleShape.Any, 20, 20, 1, 2, 5, RuleShape.Any, 55, 3, "Attacks over terrain and enemies."),
    new("Bombard", RuleCategory.Ranged, 2, RuleShape.Straight, 15, 20, 1, 1, 4, RuleShape.Straight, 55, 2, "The target and every adjacent unit take 10 damage, including friendly units. Large units take damage only once."),
    new("Ox", RuleCategory.Transport, 4, RuleShape.Any, 5, 25, 1, 1, 1, RuleShape.Straight, 35, 1, "Carries one friendly unit. Its movement becomes 3 Any while carrying a Mechanical unit."),
    new("Engineer", RuleCategory.Intelligence, 3, RuleShape.Any, 0, 20, 1, 1, 1, RuleShape.Any, 25, 1, "Builds up to two roads, 20-health barricades, or mines each turn. It may also demolish an adjacent Engineer structure without triggering mines."),
    new("Ballista", RuleCategory.Mechanical, 1, RuleShape.Straight, 25, 20, 1, 2, 5, RuleShape.Straight, 55, 2, "Its attack pierces enemies in a straight line."),
    new("Elephant", RuleCategory.Melee, 4, RuleShape.Straight, 15, 60, 2, 2, 0, RuleShape.None, 55, 0, "May move through enemies, damaging each crossed unit. Ignores terrain."),
    new("Guard", RuleCategory.Melee, 3, RuleShape.Straight, 10, 25, 1, 1, 1, RuleShape.Straight, 35, 1, "Attaches to a friendly non-royal unit and takes damage for it."),
    new("Mercenary", RuleCategory.Melee, 3, RuleShape.Any, 25, 20, 1, 1, 2, RuleShape.Any, 10, 1, "Place anywhere in No-Man's-Land. Costs 10 gold per owner turn; it is fired if you cannot pay. Fire it to leave it neutral for either player to hire or kill."),
    new("Farm", RuleCategory.Structure, 0, RuleShape.None, 0, 30, 3, 3, 0, RuleShape.None, 60, 0, "Earns the configured gold amount at the start of each owner turn (default 5). Units may move and attack over it."),
    new("King", RuleCategory.Royal, 1, RuleShape.Any, 15, 110, 1, 1, 1, RuleShape.Any, 0, 1, "Adjacent allies take 5 less damage, to a minimum of 5."),
    new("Princess", RuleCategory.Royal, 1, RuleShape.Any, 10, 80, 1, 1, 4, RuleShape.Any, 0, 1, "May attack over friendly units."),
    new("Palace", RuleCategory.Royal, 0, RuleShape.None, 0, 150, 3, 2, 0, RuleShape.None, 0, 0, "Earns 5 gold at the start of each owner turn."),
    new("Baron", RuleCategory.Royal, 2, RuleShape.Straight, 10, 100, 1, 1, 1, RuleShape.Any, 0, 1, "Adjacent allies deal 5 additional damage. Multiple bonuses do not stack."),
    new("Emissary", RuleCategory.Royal, 4, RuleShape.Any, 5, 80, 1, 1, 1, RuleShape.Any, 0, 1, "Moves directly adjacent friendly 1x1 allies with it.")
  ];

  private static readonly Dictionary<string, UnitRule> ByType = Rules.ToDictionary(rule => rule.Type, StringComparer.Ordinal);

  public static IReadOnlyList<UnitRule> All => Rules;
  public static IReadOnlyList<UnitRule> Purchasable { get; } = Rules.Where(rule =>
    rule.Category != RuleCategory.Royal
  ).ToArray();
  public static IReadOnlyList<UnitRule> Royals { get; } = Rules.Where(rule => rule.Category == RuleCategory.Royal).ToArray();

  public static bool TryGet(string type, out UnitRule rule) => ByType.TryGetValue(type, out rule!);
  public static UnitRule GetRequired(string type) => TryGet(type, out UnitRule rule)
    ? rule
    : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unit type.");

  public static string GetAbilityDescription(string type) => TryGet(type, out UnitRule rule)
    ? rule.AbilityDescription
    : string.Empty;

  public static bool FootprintsOverlap(
    int firstX, int firstY, int firstWidth, int firstHeight,
    int secondX, int secondY, int secondWidth, int secondHeight
  ) => firstX < secondX + secondWidth && firstX + firstWidth > secondX &&
       firstY < secondY + secondHeight && firstY + firstHeight > secondY;

  public static bool CanMove(UnitRule rule, int fromX, int fromY, int toX, int toY)
  {
    int dx = Math.Abs(toX - fromX);
    int dy = Math.Abs(toY - fromY);
    if (dx == 0 && dy == 0) return false;

    int maximumX = rule.MoveRange;
    int maximumY = rule.MoveRange;
    return rule.MovePattern switch
    {
      RuleShape.Straight => (dx == 0 || dy == 0) && dx <= maximumX && dy <= maximumY,
      RuleShape.Any => dx <= maximumX && dy <= maximumY,
      RuleShape.None => false,
      _ => dx <= maximumX && dy <= maximumY
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
    if (attacker.Attack <= 0 || attacker.AttackPattern == RuleShape.None) return false;
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
    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
    if (distance < minimumRange || distance > range) return false;

    return pattern switch
    {
      RuleShape.Any => true,
      RuleShape.Straight or RuleShape.PierceStraight => dx == 0 || dy == 0,
      RuleShape.Forward => IsForwardOffset(team, dx, dy, distance),
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
}
