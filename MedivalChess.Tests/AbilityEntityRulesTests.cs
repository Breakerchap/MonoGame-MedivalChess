using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class AbilityEntityRulesTests
{
  [Fact]
  public void AuthoredEntityStatsAreSharedRatherThanUnitSpecific()
  {
    Assert.Equal(50, AbilityEntityRules.GetRequired(AbilityEntityKind.StoneWall).Health);
    Assert.Equal(15, AbilityEntityRules.GetRequired(AbilityEntityKind.Gatehouse).Health);
    Assert.Equal(30, AbilityEntityRules.GetRequired(AbilityEntityKind.Bramble).Health);
    Assert.Equal(15, AbilityEntityRules.GetRequired(AbilityEntityKind.PoisonCloud).StartOfOwnerTurnDamage);
    Assert.True(AbilityEntityRules.GetRequired(AbilityEntityKind.Seal).BlocksMovement);
  }
}
