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
  [Fact]
  public void FirePersistsUntilTriggeredAndBrambleIsTraversableButCannotBeLandedOn()
  {
    AbilityEntityDefinition fire = AbilityEntityRules.GetRequired(AbilityEntityKind.Fire);
    AbilityEntityDefinition bramble = AbilityEntityRules.GetRequired(AbilityEntityKind.Bramble);

    Assert.Equal(0, fire.LifetimeOwnerTurns);
    Assert.Equal(15, fire.EnterDamage);
    Assert.False(bramble.BlocksMovement);
    Assert.True(bramble.BlocksLanding);
  }

  [Fact]
  public void GatehouseLetsItsOwnerThroughButBlocksEnemies()
  {
    AbilityEntity gate = new("gate", AbilityEntityKind.Gatehouse, NetworkTeam.Red, 4, 4, 15);
    Assert.False(AbilityEntityRules.BlocksMovementFor(gate, NetworkTeam.Red));
    Assert.True(AbilityEntityRules.BlocksMovementFor(gate, NetworkTeam.Blue));
  }

  [Fact]
  public void SameRuneTypeDoesNotStack()
  {
    NetworkPiece unit = new("u", nameof(PieceType.Swordsman), NetworkTeam.Red, 5, 5, 50);
    AbilityEntity[] runes =
    [
      new("a", AbilityEntityKind.RuneAttack, NetworkTeam.Red, 4, 5, 5),
      new("b", AbilityEntityKind.RuneAttack, NetworkTeam.Red, 6, 5, 5)
    ];
    Assert.Equal(AdvancedAbilityRules.RuneAttackBonus, AbilityEntityRules.GetAttackBonus(runes, unit));
  }


}
