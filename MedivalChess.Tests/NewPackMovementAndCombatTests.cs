using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class NewPackMovementAndCombatTests
{
  [Fact]
  public void RaiderGetsTwoExtraMovementOnlyWhenMovingForward()
  {
    Piece raider = new(PieceDefinitions.Raider, (0, 0), TeamName.Red);
    (int x, int y) forward = TeamRules.GetForwardDirection(NetworkTeam.Red);

    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementPathfinder.FindPaths(
      raider,
      _ => true,
      _ => true,
      _ => 1,
      (_, _) => false
    );

    var fourForward = (forward.x * 4, forward.y * 4);
    var threeBackward = (-forward.x * 3, -forward.y * 3);
    Assert.True(paths.ContainsKey(fourForward));
    Assert.False(paths.ContainsKey(threeBackward));
  }

  [Fact]
  public void SamuraiCannotBeTargetedOrDamagedByProjectileAttacks()
  {
    PieceSetup setup = new();
    Piece archer = new(PieceDefinitions.Archer, (0, 0), TeamName.Red);
    Piece samurai = new(PieceDefinitions.Samurai, (0, 2), TeamName.Blue);
    setup.AddPiece(archer);
    setup.AddPiece(samurai);

    Assert.False(Actions.CanAttackSquare(archer, samurai.Position));

    int startingHealth = samurai.CurrentHealth;
    Actions.Attack(archer, samurai);
    Assert.Equal(startingHealth, samurai.CurrentHealth);
  }

  [Fact]
  public void SamuraiCanStillBeDamagedByNonProjectileAttacks()
  {
    Piece knight = new(PieceDefinitions.Knight, (0, 0), TeamName.Red);
    Piece samurai = new(PieceDefinitions.Samurai, (0, 1), TeamName.Blue);

    Actions.Attack(knight, samurai);

    Assert.Equal(PieceDefinitions.Samurai.Health - PieceDefinitions.Knight.Attack, samurai.CurrentHealth);
  }
}
