namespace MedivalChess.Shared;

/// <summary>Shared geometry for the units whose attacks or movement have special resolution.</summary>
public static class AbilityRules
{
  public static bool IsTerrainImmune(UnitRule unit) => unit.Type == "Elephant";

  /// <summary>Whether the unit's attack ignores terrain, barricades, and intervening pieces.</summary>
  public static bool AttacksOverObstacles(UnitRule unit) => unit.Type == nameof(PieceType.Princess);

  /// <summary>Whether a friendly one-square unit qualifies to move alongside an Emissary.</summary>
  public static bool IsEmissaryCompanion(UnitRule unit, (int x, int y) emissaryPosition, (int x, int y) unitPosition) =>
    unit.Width == 1 && unit.Height == 1 &&
    Math.Max(Math.Abs(unitPosition.x - emissaryPosition.x), Math.Abs(unitPosition.y - emissaryPosition.y)) == 1;

  /// <summary>
  /// Determines whether a move closes the taxicab gap between a unit's footprint and a Palace's
  /// footprint. Palace-supported moves gain one range and ignore terrain.
  /// </summary>
  public static bool MovesTowardPalace(
    UnitRule movingUnit,
    (int x, int y) from,
    (int x, int y) to,
    UnitRule palace,
    (int x, int y) palacePosition
  ) => FootprintDistance(movingUnit, to, palace, palacePosition) <
       FootprintDistance(movingUnit, from, palace, palacePosition);

  private static int FootprintDistance(
    UnitRule first,
    (int x, int y) firstPosition,
    UnitRule second,
    (int x, int y) secondPosition
  )
  {
    int horizontalGap = Math.Max(
      0,
      Math.Max(secondPosition.x - (firstPosition.x + first.Width - 1),
        firstPosition.x - (secondPosition.x + second.Width - 1))
    );
    int verticalGap = Math.Max(
      0,
      Math.Max(secondPosition.y - (firstPosition.y + first.Height - 1),
        firstPosition.y - (secondPosition.y + second.Height - 1))
    );
    return horizontalGap + verticalGap;
  }

  /// <summary>Whether an attack unlocks the Cavalier's one two-square straight follow-up move.</summary>
  public static bool GrantsCavalierFollowUpMove(string unitType, bool hasMovedThisTurn) =>
    hasMovedThisTurn && string.Equals(unitType, nameof(PieceType.Cavalier), StringComparison.Ordinal);

  public static bool CanUseCavalierFollowUpMove(string unitType, bool isAvailable) =>
    isAvailable && string.Equals(unitType, nameof(PieceType.Cavalier), StringComparison.Ordinal);

  /// <summary>Whether damage dealt to this unit is also dealt to its carried unit.</summary>
  public static bool SharesDamageWithCargo(string unitType) =>
    string.Equals(unitType, nameof(PieceType.Ox), StringComparison.Ordinal);

  public static bool CanGuardAttach(UnitRule guard, UnitRule target, bool guardIsAttached, bool targetAlreadyHasGuard)
  {
    return guard.Type == "Guard" && target.Category != RuleCategory.Royal &&
      !guardIsAttached && !targetAlreadyHasGuard;
  }

  public static bool CanOxAttach(
    UnitRule ox,
    UnitRule target,
    bool targetIsAttached,
    bool oxAlreadyHasCargo
  )
  {
    return ox.Type == "Ox" && !targetIsAttached && !oxAlreadyHasCargo &&
      target.Width == 1 && target.Height == 1;
  }

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
    otherX, otherY, otherUnit.Width, otherUnit.Height
  ));

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
