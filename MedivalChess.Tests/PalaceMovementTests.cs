using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class PalaceMovementTests
{
  [Fact]
  public void PalaceBonus_QualifiesWholeDestinationsButNotSinglePathSteps()
  {
    UnitRule unit = CreateRule("Swordsman", 2, RuleShape.Any);
    UnitRule palace = CreateRule("Palace", 0, RuleShape.Straight, 3, 2);

    Assert.False(AbilityRules.MovesTowardPalace(unit, (0, 0), (0, -1), palace, (0, -5)));
    Assert.True(AbilityRules.MovesTowardPalace(unit, (0, 0), (0, -2), palace, (0, -5)));
    Assert.False(AbilityRules.MovesTowardPalace(unit, (0, 0), (0, 2), palace, (0, -5)));
  }

  [Fact]
  public void PalaceBonus_DoesNotPermitTravelThroughLake()
  {
    UnitRule unit = CreateRule("Swordsman", 1, RuleShape.Straight);
    UnitRule palace = CreateRule("Palace", 0, RuleShape.Straight, 3, 2);
    (int x, int y) origin = (0, 0);
    (int x, int y) palacePosition = (0, -5);
    (int x, int y) lake = (0, -1);

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      unit,
      origin,
      NetworkTeam.Red,
      _ => true,
      (from, to) => to != lake || AbilityRules.MovesTowardPalace(unit, from, to, palace, palacePosition),
      _ => 1,
      (_, _) => false,
      movementRangeAt: destination => unit.MoveRange +
        (AbilityRules.MovesTowardPalace(unit, origin, destination, palace, palacePosition) ? 1 : 0),
      maximumMovementRange: unit.MoveRange + 1
    );

    Assert.False(paths.ContainsKey(lake));
    Assert.False(paths.ContainsKey((0, -2)));
  }

  [Fact]
  public void PalaceBonus_DoesNotAllowRiverCrossingsToResetMovementForever()
  {
    UnitRule unit = CreateRule("Swordsman", 2, RuleShape.Straight);
    UnitRule palace = CreateRule("Palace", 0, RuleShape.Straight, 3, 2);
    (int x, int y) origin = (0, 0);
    (int x, int y) palacePosition = (0, -5);

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      unit,
      origin,
      NetworkTeam.Red,
      _ => true,
      (_, to) => to.y == 0 && to.x >= 0 && to.x <= 4,
      _ => 1,
      (from, to) => from.y == 0 && to.y == 0 && Math.Abs(to.x - from.x) == 1,
      movementRangeAt: destination => unit.MoveRange +
        (AbilityRules.MovesTowardPalace(unit, origin, destination, palace, palacePosition) ? 1 : 0),
      maximumMovementRange: unit.MoveRange + 1
    );

    Assert.True(paths.ContainsKey((1, 0)));
    Assert.False(paths.ContainsKey((2, 0)));
  }

  [Fact]
  public void PalaceBonus_StillProvidesOneExtraStepAfterCrossingRiverTowardPalace()
  {
    UnitRule unit = CreateRule("Swordsman", 2, RuleShape.Straight);
    UnitRule palace = CreateRule("Palace", 0, RuleShape.Straight, 3, 2);
    (int x, int y) origin = (0, 0);
    (int x, int y) palacePosition = (0, -5);

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      unit,
      origin,
      NetworkTeam.Red,
      _ => true,
      (_, to) => to.x == 0 && to.y <= 0 && to.y >= -3,
      _ => 1,
      (from, to) => from == origin && to == (0, -1),
      movementRangeAt: destination => unit.MoveRange +
        (AbilityRules.MovesTowardPalace(unit, origin, destination, palace, palacePosition) ? 1 : 0),
      maximumMovementRange: unit.MoveRange + 1
    );

    Assert.True(paths.ContainsKey((0, -1)));
    Assert.True(paths.ContainsKey((0, -2)));
    Assert.False(paths.ContainsKey((0, -3)));
  }

  private static UnitRule CreateRule(
    string type,
    int range,
    RuleShape shape,
    int width = 1,
    int height = 1
  ) => new(
    type,
    type == "Palace" ? RuleCategory.Royal : RuleCategory.Melee,
    range,
    shape,
    1,
    1,
    width,
    height,
    range,
    shape,
    1
  );
}
