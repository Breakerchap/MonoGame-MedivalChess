namespace MedivalChess.Shared;

/// <summary>Shared, deterministic helpers for unit abilities used by client, CPU and server.</summary>
public static class AbilityRules
{
  public const int BombardSplashDamage = 20;
  public const int EngineerBarrierHealth = 40;
  public const int MercenaryPayroll = 20;
  public const int PresidentPayroll = 10;
  public const int DragonbornBurnDamage = 10;
  public const int VampireHealing = 20;
  public const int ZeusChainDamage = 10;
  public const int ArtemisForestBonus = 10;
  public const int ChimeraRearBonus = 20;
  public const int ShieldbearerReviveHealth = 20;
  public const int GhoulLifetimeTurns = 4;
  public const int TumbleweedLifetimeRounds = 3;

  public static bool IsTerrainImmune(UnitRule unit) => unit.Type is nameof(PieceType.Elephant) or nameof(PieceType.Sleipnir);
  public static bool CanTravelThroughUnits(UnitRule unit) => unit.Type is nameof(PieceType.Elephant) or nameof(PieceType.Sleipnir);
  public static bool IsTrampleAttacker(UnitRule unit) => unit.Type == nameof(PieceType.Elephant);

  /// <summary>Catapult ignores ordinary line-of-sight blockers. Princess ignores units naturally but forests still block it.</summary>
  public static bool AttacksOverObstacles(UnitRule unit) => unit.Type == nameof(PieceType.Catapult);
  public static bool AttacksThroughForests(UnitRule unit) => unit.Type == nameof(PieceType.Artemis);

  public static bool IsProjectileAttack(UnitRule attacker) => attacker.Type is
    nameof(PieceType.Archer) or nameof(PieceType.Crossbowman) or nameof(PieceType.Ninja) or
    nameof(PieceType.Cannon) or nameof(PieceType.Catapult) or nameof(PieceType.Bombard) or
    nameof(PieceType.Ballista) or nameof(PieceType.Artemis) or nameof(PieceType.Gunman) or
    nameof(PieceType.Sniper) or nameof(PieceType.Cowboy);

  public static int MaximumAttacksPerTurn(string unitType) =>
    unitType == nameof(PieceType.Ninja) ? 3 : 1;

  public static int GetBaseAttack(UnitRule attacker, int currentHealth) =>
    attacker.Type == nameof(PieceType.Berserker) && currentHealth <= 20 ? 40 : attacker.Attack;

  public static bool IsForestProtected(UnitRule target) => target.Category == RuleCategory.Ranged;

  public static bool IsBehind((int x, int y) facing, (int x, int y) attackerPosition, (int x, int y) targetPosition)
  {
    if (facing == (0, 0)) return false;
    int dx = targetPosition.x - attackerPosition.x;
    int dy = targetPosition.y - attackerPosition.y;
    return dx * facing.x + dy * facing.y < 0;
  }

  public static bool IsInFront((int x, int y) facing, (int x, int y) attackerPosition, (int x, int y) targetPosition)
  {
    if (facing == (0, 0)) return false;
    int dx = targetPosition.x - attackerPosition.x;
    int dy = targetPosition.y - attackerPosition.y;
    return dx * facing.x + dy * facing.y > 0;
  }

  public static (int x, int y) DirectionToward((int x, int y) from, (int x, int y) to)
  {
    int dx = to.x - from.x;
    int dy = to.y - from.y;
    if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0) return (Math.Sign(dx), 0);
    if (dy != 0) return (0, Math.Sign(dy));
    return (0, 0);
  }

  public static bool IsForwardDestination(NetworkTeam team, (int x, int y) from, (int x, int y) to)
  {
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    int dx = to.x - from.x;
    int dy = to.y - from.y;
    return dx * forward.x + dy * forward.y > 0;
  }

  public static bool IsEmissaryCompanion(UnitRule unit, (int x, int y) emissaryPosition, (int x, int y) unitPosition) =>
    Math.Max(Math.Abs(unitPosition.x - emissaryPosition.x), Math.Abs(unitPosition.y - emissaryPosition.y)) == 1;

  public static bool MovesTowardPalace(
    UnitRule movingUnit,
    (int x, int y) from,
    (int x, int y) to,
    UnitRule palace,
    (int x, int y) palacePosition
  )
  {
    int movementSteps = Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.y - from.y));
    return movementSteps > 1 && FootprintDistance(movingUnit, to, palace, palacePosition) <
      FootprintDistance(movingUnit, from, palace, palacePosition);
  }

  private static int FootprintDistance(
    UnitRule first,
    (int x, int y) firstPosition,
    UnitRule second,
    (int x, int y) secondPosition
  )
  {
    int horizontalGap = Math.Max(0, Math.Max(
      secondPosition.x - (firstPosition.x + first.Width - 1),
      firstPosition.x - (secondPosition.x + second.Width - 1)));
    int verticalGap = Math.Max(0, Math.Max(
      secondPosition.y - (firstPosition.y + first.Height - 1),
      firstPosition.y - (secondPosition.y + second.Height - 1)));
    return horizontalGap + verticalGap;
  }

  public static bool GrantsCavalierFollowUpMove(string unitType, bool hasMovedThisTurn) =>
    hasMovedThisTurn && unitType == nameof(PieceType.Cavalier);

  public static bool CanUseCavalierFollowUpMove(string unitType, bool isAvailable) =>
    isAvailable && unitType == nameof(PieceType.Cavalier);

  public static bool SharesDamageWithCargo(string unitType) => unitType == nameof(PieceType.Ox);

  public static bool CanGuardAttach(UnitRule guard, UnitRule target, bool guardIsAttached, bool targetAlreadyHasGuard) =>
    guard.Type == nameof(PieceType.Guard) && target.Category != RuleCategory.Royal && !guardIsAttached && !targetAlreadyHasGuard;

  public static bool CanOxAttach(UnitRule ox, UnitRule target, bool targetIsAttached, bool oxAlreadyHasCargo) =>
    ox.Type == nameof(PieceType.Ox) && !targetIsAttached && !oxAlreadyHasCargo &&
    ((target.Width == 1 && target.Height == 1) || target.Category == RuleCategory.Mechanical);

  public static bool IsEngineerBuild(string ability) =>
    string.Equals(ability, "Road", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(ability, "Barrier", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(ability, "Mine", StringComparison.OrdinalIgnoreCase);

  public static bool IsEngineerDemolition(string ability) => string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase);

  public static bool PathOverlapsUnit(
    UnitRule movingUnit,
    IEnumerable<(int x, int y)> path,
    UnitRule otherUnit,
    int otherX,
    int otherY
  ) => path.Any(step => UnitRules.FootprintsOverlap(
    step.x, step.y, movingUnit.Width, movingUnit.Height,
    otherX, otherY, otherUnit.Width, otherUnit.Height));

  public static IReadOnlyList<(int x, int y)> GetPiercingRay(
    UnitRule ballista,
    int attackerX,
    int attackerY,
    int targetX,
    int targetY
  )
  {
    for (int originY = 0; originY < ballista.Height; originY++)
    for (int originX = 0; originX < ballista.Width; originX++)
    {
      int startX = attackerX + originX;
      int startY = attackerY + originY;
      int deltaX = targetX - startX;
      int deltaY = targetY - startY;
      if ((deltaX == 0 && deltaY == 0) || (deltaX != 0 && deltaY != 0)) continue;
      int distance = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
      if (distance < ballista.MinimumAttackRange || distance > ballista.AttackRange) continue;
      int stepX = Math.Sign(deltaX);
      int stepY = Math.Sign(deltaY);
      return Enumerable.Range(1, ballista.AttackRange)
        .Select(step => (startX + stepX * step, startY + stepY * step))
        .ToArray();
    }

    return [];
  }
}
