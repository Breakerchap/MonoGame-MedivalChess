namespace MedivalChess.Shared;

/// <summary>
/// Shared, deterministic unit-ability rules. Runtime layers (local, CPU and server) should only
/// adapt their board state to these helpers; ability numbers and decisions belong here.
/// </summary>
public static class AbilityRules
{
  public const int BombardSplashDamage = 25;
  public const int ElephantTrampleDamage = 30;
  public const int EngineerBarrierHealth = 40;
  public const int EngineerMineDamage = 30;
  public const int EngineerBuildsPerTurn = 2;
  public const int MercenaryPayroll = 25;
  public const int PresidentPayroll = 5;
  public const int VampireHealing = 15;
  public const int ZeusChainDamage = 20;
  public const int ArtemisForestBonus = 10;
  public const int ChimeraRearBonus = 15;
  public const int SpartanReviveHealth = 20;
  public const int GhoulLifetimeTurns = 4;
  public const int NinjaAttacksPerTurn = 3;
  public const int RaiderForwardMovementBonus = 2;
  public const int OxHostMovementBonus = 2;
  public const int BerserkerEnrageHealth = 20;
  public const int BerserkerEnragedDamage = 40;
  public const int CavalierFollowUpMovement = 2;
  public const int SamuraiLongRangeDamageReduction = 15;

  public static bool IsTerrainImmune(UnitRule unit) =>
    unit.Type is nameof(PieceType.Elephant) or nameof(PieceType.Sleipnir);

  /// <summary>True when lakes and similar terrain restrictions do not block this unit.</summary>
  public static bool IgnoresImpassableTerrain(UnitRule unit) => IsTerrainImmune(unit);

  public static bool IgnoresRivers(UnitRule unit) => IsTerrainImmune(unit);

  /// <summary>
  /// Applies a unit's terrain-cost rule while preserving zero-cost roads. Elephant and Sleipnir
  /// pay at most one movement for a step; an owned open-ground road may still cost zero.
  /// </summary>
  public static int ApplyTerrainMovementCost(UnitRule unit, int ordinaryCost) =>
    IsTerrainImmune(unit) ? Math.Min(1, ordinaryCost) : ordinaryCost;

  public static bool CanTravelThroughUnits(UnitRule unit) =>
    unit.Type is nameof(PieceType.Elephant) or nameof(PieceType.Sleipnir);

  public static bool CanTravelThroughUnit(UnitRule mover, NetworkTeam moverTeam, NetworkTeam blockerTeam) =>
    mover.Type == nameof(PieceType.Sleipnir) ||
    (mover.Type == nameof(PieceType.Elephant) && blockerTeam != moverTeam);

  public static bool IsTrampleAttacker(UnitRule unit) => unit.Type == nameof(PieceType.Elephant);

  public static int GetMovementRangeBonus(
    UnitRule unit,
    NetworkTeam team,
    (int x, int y) origin,
    (int x, int y) destination
  ) => unit.Type == nameof(PieceType.Raider) && IsForwardDestination(team, origin, destination)
    ? RaiderForwardMovementBonus
    : 0;

  public static int GetMaximumMovementRangeBonus(UnitRule unit) =>
    unit.Type == nameof(PieceType.Raider) ? RaiderForwardMovementBonus : 0;

  /// <summary>Movement bonus granted to the host by one attached unit.</summary>
  public static int GetAttachmentMovementBonus(string attachmentType) =>
    attachmentType == nameof(PieceType.Ox) ? OxHostMovementBonus : 0;

  /// <summary>True when an attachment must take the same incoming damage as its host.</summary>
  public static bool SharesIncomingDamageWithHost(string attachmentType) =>
    attachmentType == nameof(PieceType.Ox);

  public static bool AttacksOverObstacles(UnitRule unit) =>
    unit.Type is nameof(PieceType.Catapult) or nameof(PieceType.Sorceress);

  public static bool AttacksThroughForests(UnitRule unit) =>
    unit.Type is nameof(PieceType.Artemis) or nameof(PieceType.Sorceress);

  public static bool IsProjectileAttack(UnitRule attacker) => attacker.Type is
    nameof(PieceType.Archer) or nameof(PieceType.Crossbowman) or nameof(PieceType.Ninja) or
    nameof(PieceType.Cannon) or nameof(PieceType.Catapult) or nameof(PieceType.Bombard) or
    nameof(PieceType.Ballista) or nameof(PieceType.Artemis) or nameof(PieceType.Gunman) or
    nameof(PieceType.Sniper) or nameof(PieceType.Cowboy);

  public static bool CanDamageTarget(UnitRule attacker, UnitRule target) =>
    true;

  public static int GetTargetDamageReduction(
    UnitRule attacker,
    UnitRule target,
    (int x, int y) attackerPosition,
    (int x, int y) targetPosition
  )
  {
    if (target.Type != nameof(PieceType.Samurai)) return 0;
    return IsWithinSquareRadius(attacker, attackerPosition, target, targetPosition, 1)
      ? 0
      : SamuraiLongRangeDamageReduction;
  }

  public static int MaximumAttacksPerTurn(string unitType) =>
    unitType == nameof(PieceType.Ninja) ? NinjaAttacksPerTurn :
    unitType == nameof(PieceType.Sherrif) ? 2 : 1;

  public static int GetBaseAttack(UnitRule attacker, int currentHealth) =>
    attacker.Type == nameof(PieceType.Beserker) && currentHealth <= BerserkerEnrageHealth
      ? BerserkerEnragedDamage
      : attacker.Attack;

  public static int GetAttackAbilityBonus(
    UnitRule attacker,
    UnitRule target,
    bool targetIsInForest,
    (int x, int y) targetFacing,
    (int x, int y) attackerPosition,
    (int x, int y) targetPosition
  )
  {
    int bonus = 0;
    if (attacker.Type == nameof(PieceType.Artemis) && targetIsInForest)
    {
      bonus += ArtemisForestBonus;
    }
    if (target.Type == nameof(PieceType.Chimera) && IsBehind(targetFacing, targetPosition, attackerPosition))
    {
      bonus += ChimeraRearBonus;
    }
    return bonus;
  }

  public static bool IsForestProtected(UnitRule target) => target.Category == RuleCategory.Ranged;

  public static bool IsBehind((int x, int y) facing, (int x, int y) subjectPosition, (int x, int y) otherPosition)
  {
    if (facing == (0, 0)) return false;
    int dx = otherPosition.x - subjectPosition.x;
    int dy = otherPosition.y - subjectPosition.y;
    return dx * facing.x + dy * facing.y < 0;
  }

  public static bool IsInFront((int x, int y) facing, (int x, int y) subjectPosition, (int x, int y) otherPosition)
  {
    if (facing == (0, 0)) return false;
    int dx = otherPosition.x - subjectPosition.x;
    int dy = otherPosition.y - subjectPosition.y;
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

  public static bool AreAdjacent(
    UnitRule first,
    (int x, int y) firstPosition,
    UnitRule second,
    (int x, int y) secondPosition,
    bool includeDiagonal = false
  )
  {
    int horizontalGap = Math.Max(0, Math.Max(
      secondPosition.x - (firstPosition.x + first.Width - 1),
      firstPosition.x - (secondPosition.x + second.Width - 1)));
    int verticalGap = Math.Max(0, Math.Max(
      secondPosition.y - (firstPosition.y + first.Height - 1),
      firstPosition.y - (secondPosition.y + second.Height - 1)));

    return includeDiagonal
      ? Math.Max(horizontalGap, verticalGap) == 1
      : horizontalGap + verticalGap == 1;
  }

  public static bool IsWithinSquareRadius(
    UnitRule centre,
    (int x, int y) centrePosition,
    UnitRule candidate,
    (int x, int y) candidatePosition,
    int radius
  )
  {
    int horizontalGap = Math.Max(0, Math.Max(
      candidatePosition.x - (centrePosition.x + centre.Width - 1),
      centrePosition.x - (candidatePosition.x + candidate.Width - 1)));
    int verticalGap = Math.Max(0, Math.Max(
      candidatePosition.y - (centrePosition.y + centre.Height - 1),
      centrePosition.y - (candidatePosition.y + candidate.Height - 1)));
    return Math.Max(horizontalGap, verticalGap) <= radius;
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

  public static bool CanGuardAttach(UnitRule guard, UnitRule target, bool guardIsAttached, bool targetAlreadyHasGuard) =>
    guard.Type == nameof(PieceType.Guard) && target.Category != RuleCategory.Royal && !guardIsAttached && !targetAlreadyHasGuard;

  public static bool CanOxAttach(UnitRule ox, UnitRule target, bool oxIsAlreadyAttached, bool targetAlreadyHasOx) =>
    ox.Type == nameof(PieceType.Ox) && !oxIsAlreadyAttached && !targetAlreadyHasOx &&
    target.Width == 1 && target.Height == 1;

  public static bool IsCarryThrowUnit(string unitType) =>
    unitType is nameof(PieceType.Giant) or nameof(PieceType.Cyclops);

  public static RuleShape GetCarryThrowPattern(string unitType) =>
    unitType == nameof(PieceType.Cyclops) ? RuleShape.Straight : RuleShape.Circle;

  public static bool CanCarry(
    UnitRule carrier,
    UnitRule target,
    bool carrierIsAttached,
    bool targetIsAttached,
    bool carrierAlreadyHasCargo
  ) => IsCarryThrowUnit(carrier.Type) && !carrierIsAttached && !targetIsAttached &&
       !carrierAlreadyHasCargo && target.Width == 1 && target.Height == 1;

  public static bool CanCarry(
    UnitRule carrier,
    (int x, int y) carrierPosition,
    UnitRule target,
    (int x, int y) targetPosition,
    bool carrierIsAttached,
    bool targetIsAttached,
    bool carrierAlreadyHasCargo
  ) => CanCarry(carrier, target, carrierIsAttached, targetIsAttached, carrierAlreadyHasCargo) &&
       AreAdjacent(carrier, carrierPosition, target, targetPosition);

  public static bool CanThrow(
    UnitRule carrier,
    UnitRule cargo,
    (int x, int y) carrierPosition,
    (int x, int y) destination
  )
  {
    if (!IsCarryThrowUnit(carrier.Type) || cargo.Width != 1 || cargo.Height != 1)
    {
      return false;
    }

    for (int sourceY = 0; sourceY < carrier.Height; sourceY++)
    for (int sourceX = 0; sourceX < carrier.Width; sourceX++)
    {
      if (UnitRules.CanAttackOffset(
        GetCarryThrowPattern(carrier.Type), 2, 3, NetworkTeam.Red,
        destination.x - (carrierPosition.x + sourceX),
        destination.y - (carrierPosition.y + sourceY)))
      {
        return true;
      }
    }

    return false;
  }

  public static bool IsHeraldCompanion(
    UnitRule unit,
    (int x, int y) heraldPosition,
    (int x, int y) unitPosition
  ) => unit.Width == 1 && unit.Height == 1 &&
       Math.Max(Math.Abs(unitPosition.x - heraldPosition.x), Math.Abs(unitPosition.y - heraldPosition.y)) == 1;

  public static bool IsEngineerBuild(string ability) =>
    string.Equals(ability, "Road", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(ability, "Barrier", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(ability, "Mine", StringComparison.OrdinalIgnoreCase);

  public static bool IsEngineerDemolition(string ability) =>
    string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase);

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
