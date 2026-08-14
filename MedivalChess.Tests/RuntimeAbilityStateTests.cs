using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class RuntimeAbilityStateTests
{
  [Fact]
  public void NinjaCanAttackThreeTimesBeforeBeingSpent()
  {
    Piece ninja = new(PieceDefinitions.Ninja, (0, 0), TeamName.Red);

    ninja.HasAttackedThisTurn = true;
    Assert.False(ninja.HasAttackedThisTurn);
    Assert.Equal(1, ninja.AttacksThisTurn);

    ninja.HasAttackedThisTurn = true;
    Assert.False(ninja.HasAttackedThisTurn);
    Assert.Equal(2, ninja.AttacksThisTurn);

    ninja.HasAttackedThisTurn = true;
    Assert.True(ninja.HasAttackedThisTurn);
    Assert.Equal(3, ninja.AttacksThisTurn);

    ninja.HasAttackedThisTurn = false;
    Assert.False(ninja.HasAttackedThisTurn);
    Assert.Equal(0, ninja.AttacksThisTurn);
  }

  [Fact]
  public void ShieldbearerSurvivesFirstLethalHitAtTwentyHealth()
  {
    Piece shieldbearer = new(PieceDefinitions.Shieldbearer, (0, 0), TeamName.Red);

    shieldbearer.CurrentHealth = 0;
    Assert.Equal(AbilityRules.ShieldbearerReviveHealth, shieldbearer.CurrentHealth);
    Assert.True(shieldbearer.HasRevived);

    shieldbearer.CurrentHealth = 0;
    Assert.Equal(0, shieldbearer.CurrentHealth);
  }

  [Fact]
  public void EmperorBecomesTerracottaWarriorOnFirstDeath()
  {
    Piece emperor = new(PieceDefinitions.Emperor, (0, 0), TeamName.Red);

    emperor.CurrentHealth = 0;

    Assert.Same(PieceDefinitions.TerracottaWarrior, emperor.Definition);
    Assert.Equal(PieceDefinitions.TerracottaWarrior.Health, emperor.CurrentHealth);
    Assert.True(emperor.HasRevived);
  }

  [Fact]
  public void VampireHealsTwentyAfterAttackingWithoutExceedingMaximumHealth()
  {
    Piece vampire = new(PieceDefinitions.Vampire, (0, 0), TeamName.Red)
    {
      CurrentHealth = 15
    };

    vampire.HasAttackedThisTurn = true;
    Assert.Equal(35, vampire.CurrentHealth);

    vampire.HasAttackedThisTurn = false;
    vampire.CurrentHealth = 35;
    vampire.HasAttackedThisTurn = true;
    Assert.Equal(PieceDefinitions.Vampire.Health, vampire.CurrentHealth);
  }

  [Fact]
  public void ZombieTurnsIntoFleshAndFleshReturnsToZombieOnNextOwnerTurn()
  {
    PieceSetup setup = new();
    Piece zombie = new(PieceDefinitions.Zombie, (3, 4), TeamName.Blue);
    setup.AddPiece(zombie);

    zombie.CurrentHealth = 0;

    Assert.Same(PieceDefinitions.Flesh, zombie.Definition);
    Assert.Equal(PieceDefinitions.Flesh.Health, zombie.CurrentHealth);
    Assert.Equal((3, 4), zombie.Position);
    Assert.Equal(TeamName.Blue, zombie.Team);

    zombie.HasMovedThisTurn = false;
    Assert.Same(PieceDefinitions.Zombie, zombie.Definition);
    Assert.Equal(PieceDefinitions.Zombie.Health, zombie.CurrentHealth);
  }

  [Fact]
  public void GhoulExpiresAfterFourOwnerTurns()
  {
    PieceSetup setup = new();
    Piece ghoul = new(PieceDefinitions.Ghoul, (0, 0), TeamName.Red);
    setup.AddPiece(ghoul);

    for (int turn = 0; turn < AbilityRules.GhoulLifetimeTurns - 1; turn++)
    {
      ghoul.HasMovedThisTurn = false;
      Assert.Contains(ghoul, setup.Pieces);
    }

    ghoul.HasMovedThisTurn = false;
    Assert.DoesNotContain(ghoul, setup.Pieces);
  }

  [Fact]
  public void TumbleweedExpiresAfterThreeOwnerRounds()
  {
    PieceSetup setup = new();
    Piece tumbleweed = new(PieceDefinitions.Tumbleweed, (0, 0), TeamName.Red);
    setup.AddPiece(tumbleweed);

    tumbleweed.HasMovedThisTurn = false;
    tumbleweed.HasMovedThisTurn = false;
    Assert.Contains(tumbleweed, setup.Pieces);

    tumbleweed.HasMovedThisTurn = false;
    Assert.DoesNotContain(tumbleweed, setup.Pieces);
  }

  [Fact]
  public void BerserkerDoublesItsAttackAtTwentyHealthOrLess()
  {
    Piece berserker = new(PieceDefinitions.Berserker, (0, 0), TeamName.Red);

    Assert.Equal(20, berserker.Definition.Attack);
    berserker.CurrentHealth = 20;
    Assert.Equal(40, berserker.Definition.Attack);
    berserker.CurrentHealth = 30;
    Assert.Equal(20, berserker.Definition.Attack);
  }
}
