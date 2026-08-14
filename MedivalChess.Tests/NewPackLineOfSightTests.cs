using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class NewPackLineOfSightTests
{
  [Fact]
  public void ArtemisCanAttackThroughForestButArcherCannot()
  {
    UnitRule artemis = UnitRules.GetRequired(nameof(PieceType.Artemis));
    UnitRule archer = UnitRules.GetRequired(nameof(PieceType.Archer));
    (int x, int y) forestSquare = (0, 1);

    bool artemisClear = LineOfSightRules.HasClearAttackPath(
      artemis,
      [(0, 0)],
      (0, 3),
      square => square == forestSquare,
      _ => false,
      _ => false
    );
    bool archerClear = LineOfSightRules.HasClearAttackPath(
      archer,
      [(0, 0)],
      (0, 3),
      square => square == forestSquare,
      _ => false,
      _ => false
    );

    Assert.True(artemisClear);
    Assert.False(archerClear);
  }

  [Fact]
  public void PrincessAttacksOverUnitsTerrainAndBarricades()
  {
    UnitRule princess = UnitRules.GetRequired(nameof(PieceType.Princess));

    bool clear = LineOfSightRules.HasClearAttackPath(
      princess,
      [(0, 0)],
      (0, 3),
      _ => true,
      _ => true,
      _ => true
    );

    Assert.True(clear);
  }
}
