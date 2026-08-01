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
    new("Soldier", RuleCategory.Melee, 2, RuleShape.Straight, 10, 15, 1, 1, 1, RuleShape.Straight, 20),
    new("Defender", RuleCategory.Melee, 2, RuleShape.Straight, 5, 25, 1, 1, 1, RuleShape.Straight, 20),
    new("Archer", RuleCategory.Ranged, 2, RuleShape.Any, 10, 10, 1, 1, 3, RuleShape.Any, 30, 2),
    new("Scout", RuleCategory.Intelligence, 4, RuleShape.Any, 5, 10, 1, 1, 1, RuleShape.Straight, 20),
    new("Spearman", RuleCategory.Melee, 2, RuleShape.Any, 15, 15, 1, 1, 1, RuleShape.ForwardOrForwardDiagonal, 25),
    new("Peasant", RuleCategory.Melee, 1, RuleShape.Straight, 5, 5, 1, 1, 1, RuleShape.ForwardOrForwardDiagonal, 10),
    new("Knight", RuleCategory.Melee, 3, RuleShape.Any, 20, 25, 1, 1, 1, RuleShape.Any, 50),
    new("Crossbowman", RuleCategory.Ranged, 2, RuleShape.Any, 20, 15, 1, 1, 3, RuleShape.Any, 45),
    new("Cavalier", RuleCategory.Melee, 4, RuleShape.Any, 15, 20, 1, 1, 1, RuleShape.Any, 50),
    new("Chariot", RuleCategory.Melee, 4, RuleShape.Straight, 15, 25, 1, 1, 1, RuleShape.Straight, 40),
    new("Cannon", RuleCategory.Mechanical, 2, RuleShape.Straight, 30, 25, 1, 2, 5, RuleShape.Straight, 50, 2),
    new("Spy", RuleCategory.Intelligence, 5, RuleShape.Any, 0, 10, 1, 1, 3, RuleShape.Any, 35, 1, "Marks an enemy; it takes double damage until attacked."),
    new("Catapult", RuleCategory.Mechanical, 1, RuleShape.Any, 20, 20, 1, 2, 6, RuleShape.Any, 55, 3, "Attacks one target at range."),
    new("Ox", RuleCategory.Transport, 4, RuleShape.Any, 5, 25, 1, 1, 1, RuleShape.Straight, 35, 1, "Carries one friendly unit or tows one Mechanical unit."),
    new("Engineer", RuleCategory.Intelligence, 3, RuleShape.Any, 0, 15, 1, 1, 1, RuleShape.Straight, 35, 1, "Builds a road or 20-health barricade on an adjacent empty square."),
    new("Ballista", RuleCategory.Mechanical, 1, RuleShape.Straight, 25, 20, 2, 2, 5, RuleShape.PierceStraight, 55, 2, "Its attack pierces enemies in a straight line."),
    new("Elephant", RuleCategory.Melee, 2, RuleShape.Straight, 15, 50, 2, 2, 0, RuleShape.None, 60, 0, "May move through enemies, damaging each crossed unit, but must land on empty squares."),
    new("Guard", RuleCategory.Melee, 3, RuleShape.Any, 10, 25, 1, 1, 1, RuleShape.Straight, 35, 1, "Attaches to a friendly unit and takes damage for it."),
    new("Mercenary", RuleCategory.Melee, 3, RuleShape.Any, 25, 20, 1, 1, 2, RuleShape.Any, 35, 1, "Place on a No-Man's-Land edge. An enemy can buy it in their territory for its last bid plus 10 gold."),
    new("Farm", RuleCategory.Structure, 0, RuleShape.None, 0, 40, 2, 2, 0, RuleShape.None, 60, 0, "Earns the configured gold amount at the start of each owner turn."),
    new("King", RuleCategory.Royal, 1, RuleShape.Any, 15, 120, 1, 1, 1, RuleShape.Any, 0, 1, "Adjacent allies take 5 less damage, to a minimum of 5."),
    new("Princess", RuleCategory.Royal, 1, RuleShape.Any, 15, 80, 1, 1, 3, RuleShape.Any, 0, 1, "May attack over friendly units."),
    new("Palace", RuleCategory.Royal, 0, RuleShape.None, 0, 160, 3, 2, 0, RuleShape.None, 0, 0, "If destroyed, its owner loses."),
    new("Baron", RuleCategory.Royal, 1, RuleShape.Any, 5, 100, 1, 1, 1, RuleShape.Any, 0, 1, "Adjacent allies deal 5 additional damage. Multiple bonuses do not stack."),
    new("Emissary", RuleCategory.Royal, 3, RuleShape.Any, 5, 80, 1, 1, 1, RuleShape.Any, 0, 1, "Moves directly adjacent friendly 1x1 allies with it.")
  ];

  private static readonly Dictionary<string, UnitRule> ByType = Rules.ToDictionary(rule => rule.Type, StringComparer.Ordinal);

  public static IReadOnlyList<UnitRule> All => Rules;
  public static IReadOnlyList<UnitRule> Purchasable { get; } = Rules.Where(rule =>
    rule.Category != RuleCategory.Royal && rule.Type is not "Scout" and not "Peasant"
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

    int maximumX = rule.MoveRange * rule.Width;
    int maximumY = rule.MoveRange * rule.Height;
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
      RuleShape.Forward => dx == 0 && dy == (team == NetworkTeam.Red ? -distance : distance),
      RuleShape.ForwardOrForwardDiagonal =>
        dy == (team == NetworkTeam.Red ? -distance : distance) && Math.Abs(dx) <= distance,
      _ => false
    };
  }
}
