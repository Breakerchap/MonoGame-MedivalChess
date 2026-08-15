namespace MedivalChess.Shared;

public static class CombatRules
{
  public const int BaronDamageBonus = 10;
  public const int AuraDamageReduction = 10;
  public const int ForestRangedDamageReduction = 10;

  public static int RoundCurrencyToNearestFive(float amount)
  {
    if (!float.IsFinite(amount)) return amount < 0 ? int.MinValue : int.MaxValue;
    double rounded = Math.Round(amount / 5d, MidpointRounding.AwayFromZero) * 5d;
    return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
  }

  public static int CalculateDamage(
    int baseDamage,
    bool hasBaronBonus,
    bool isSpyMarked,
    bool hasDamageReduction,
    bool isInForest,
    int forestDamageReduction
  )
  {
    int damage = baseDamage + (hasBaronBonus ? BaronDamageBonus : 0);
    if (isSpyMarked) damage *= 2;
    if (hasDamageReduction) damage = Math.Max(0, damage - AuraDamageReduction);
    return isInForest ? Math.Max(0, damage - forestDamageReduction) : damage;
  }
}

public static class LineOfSightRules
{
  public static bool HasClearAttackPath(
    UnitRule attacker,
    IEnumerable<(int x, int y)> origins,
    (int x, int y) target,
    Func<(int x, int y), bool> isForest,
    Func<(int x, int y), bool> isBarricade,
    Func<(int x, int y), bool> blocksDirectPath
  )
  {
    bool ranged = attacker.Category == RuleCategory.Ranged || attacker.Type is "Princess" or "Sorceress" or "Cannon" or "Ballista";

    foreach ((int x, int y) origin in origins)
    {
      int deltaX = target.x - origin.x;
      int deltaY = target.y - origin.y;
      if (!TeamRules.GetActiveTeams(4).Any(team => UnitRules.CanAttackOffset(
        attacker.AttackPattern, attacker.MinimumAttackRange, attacker.AttackRange,
        team, deltaX, deltaY))) continue;

      if (AbilityRules.AttacksOverObstacles(attacker)) return true;

      bool clear = true;
      foreach ((int x, int y) square in SquaresBetween(origin, target))
      {
        bool forestBlocks = ranged && !AbilityRules.AttacksThroughForests(attacker) && isForest(square);
        if (isBarricade(square) || forestBlocks ||
            (attacker.AttackPattern == RuleShape.Line && blocksDirectPath(square)))
        {
          clear = false;
          break;
        }
      }

      if (clear) return true;
    }

    return false;
  }

  public static IEnumerable<(int x, int y)> SquaresBetween((int x, int y) start, (int x, int y) end)
  {
    int x = start.x;
    int y = start.y;
    int deltaX = Math.Abs(end.x - start.x);
    int deltaY = Math.Abs(end.y - start.y);
    int stepX = Math.Sign(end.x - start.x);
    int stepY = Math.Sign(end.y - start.y);
    int error = deltaX - deltaY;
    while (x != end.x || y != end.y)
    {
      int doubledError = error * 2;
      if (doubledError > -deltaY) { error -= deltaY; x += stepX; }
      if (doubledError < deltaX) { error += deltaX; y += stepY; }
      if (x != end.x || y != end.y) yield return (x, y);
    }
  }
}
