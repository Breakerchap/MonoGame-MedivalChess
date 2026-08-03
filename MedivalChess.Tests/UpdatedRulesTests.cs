using MedivalChess.GameBoard;
using MedivalChess.Player;
using Xunit;

namespace MedivalChess.Tests;

public class UpdatedRulesTests
{
  [Fact]
  public void WorkbookStats_AreAppliedToEveryAvailableUnit()
  {
    AssertDefinition(PieceDefinitions.Soldier, PieceCategory.Melee, 3, Shape.Straight, 10, 15, (1, 1), 1, Shape.Straight, 20);
    AssertDefinition(PieceDefinitions.Defender, PieceCategory.Melee, 2, Shape.Any, 5, 25, (1, 1), 1, Shape.Straight, 15);
    AssertDefinition(PieceDefinitions.Archer, PieceCategory.Ranged, 3, Shape.Straight, 10, 10, (1, 1), 3, Shape.Any, 30, 2);
    AssertDefinition(PieceDefinitions.Peasant, PieceCategory.Melee, 1, Shape.Any, 5, 5, (1, 1), 1, Shape.Straight, 10);
    AssertDefinition(PieceDefinitions.Knight, PieceCategory.Melee, 4, Shape.Any, 20, 30, (1, 1), 1, Shape.Any, 50);
    AssertDefinition(PieceDefinitions.Crossbowman, PieceCategory.Ranged, 2, Shape.Any, 20, 15, (1, 1), 3, Shape.Any, 50);
    AssertDefinition(PieceDefinitions.Chariot, PieceCategory.Melee, 5, Shape.Straight, 15, 25, (1, 1), 2, Shape.Straight, 40, 2);
    AssertDefinition(PieceDefinitions.Cannon, PieceCategory.Mechanical, 2, Shape.Straight, 30, 15, (1, 2), 4, Shape.Straight, 50, 2);
    AssertDefinition(PieceDefinitions.Spy, PieceCategory.Intelligence, 5, Shape.Any, 0, 15, (1, 1), 3, Shape.Straight, 35);
    AssertDefinition(PieceDefinitions.Catapult, PieceCategory.Mechanical, 1, Shape.Any, 20, 20, (1, 2), 5, Shape.Any, 55, 3);
    AssertDefinition(PieceDefinitions.Bombard, PieceCategory.Ranged, 2, Shape.Straight, 15, 20, (1, 1), 4, Shape.Straight, 55, 2);
    AssertDefinition(PieceDefinitions.Ox, PieceCategory.Transport, 4, Shape.Any, 5, 25, (1, 1), 1, Shape.Straight, 35);
    AssertDefinition(PieceDefinitions.Engineer, PieceCategory.Intelligence, 3, Shape.Any, 0, 20, (1, 1), 1, Shape.Any, 25);
    AssertDefinition(PieceDefinitions.Ballista, PieceCategory.Mechanical, 1, Shape.Straight, 25, 20, (1, 2), 5, Shape.Straight, 55, 2);
    AssertDefinition(PieceDefinitions.Elephant, PieceCategory.Melee, 4, Shape.Straight, 15, 60, (2, 2), 0, Shape.None, 55, 0);
    AssertDefinition(PieceDefinitions.Guard, PieceCategory.Melee, 3, Shape.Straight, 10, 25, (1, 1), 1, Shape.Straight, 35);
    AssertDefinition(PieceDefinitions.Mercenary, PieceCategory.Melee, 3, Shape.Any, 25, 20, (1, 1), 2, Shape.Any, 10);
    AssertDefinition(PieceDefinitions.Farm, PieceCategory.Structure, 0, Shape.None, 0, 30, (3, 3), 0, Shape.None, 60, 0);
    AssertDefinition(PieceDefinitions.King, PieceCategory.Royal, 1, Shape.Any, 15, 110, (1, 1), 1, Shape.Any, 0);
    AssertDefinition(PieceDefinitions.Princess, PieceCategory.Royal, 1, Shape.Any, 10, 80, (1, 1), 4, Shape.Any, 0, 1);
    AssertDefinition(PieceDefinitions.Palace, PieceCategory.Royal, 0, Shape.None, 0, 150, (3, 2), 0, Shape.None, 0, 0);
    AssertDefinition(PieceDefinitions.Baron, PieceCategory.Royal, 2, Shape.Straight, 10, 100, (1, 1), 1, Shape.Any, 0);
    AssertDefinition(PieceDefinitions.Emissary, PieceCategory.Royal, 4, Shape.Any, 5, 80, (1, 1), 1, Shape.Any, 0);
  }

  [Fact]
  public void RangedPatterns_RespectTheUpdatedMinimums()
  {
    Piece archer = new(PieceDefinitions.Archer, (5, 5), TeamName.Red);
    Piece catapult = new(PieceDefinitions.Catapult, (5, 5), TeamName.Red);
    Piece ballista = new(PieceDefinitions.Ballista, (5, 5), TeamName.Red);
    Piece bombard = new(PieceDefinitions.Bombard, (5, 5), TeamName.Red);

    Assert.False(Actions.CanAttackSquare(archer, (6, 5)));
    Assert.True(Actions.CanAttackSquare(archer, (7, 5)));
    Assert.False(Actions.CanAttackSquare(catapult, (7, 5)));
    Assert.True(Actions.CanAttackSquare(catapult, (8, 5)));
    Assert.False(Actions.CanAttackSquare(ballista, (5, 6)));
    Assert.True(Actions.CanAttackSquare(ballista, (5, 7)));
    Assert.False(Actions.CanAttackSquare(ballista, (7, 7)));
    Assert.False(Actions.CanAttackSquare(bombard, (6, 5)));
    Assert.True(Actions.CanAttackSquare(bombard, (7, 5)));
  }

  [Fact]
  public void GuardAndTransportAttachments_MoveWithTheirHostAndDetachOnDeath()
  {
    PieceSetup setup = new();
    Piece soldier = new(PieceDefinitions.Soldier, (4, 4), TeamName.Red);
    Piece guard = new(PieceDefinitions.Guard, (4, 3), TeamName.Red);
    Piece secondGuard = new(PieceDefinitions.Guard, (3, 4), TeamName.Red);
    Piece ox = new(PieceDefinitions.Ox, (8, 4), TeamName.Red);
    Piece cannon = new(PieceDefinitions.Cannon, (8, 5), TeamName.Red);
    setup.AddPiece(soldier);
    setup.AddPiece(guard);
    setup.AddPiece(secondGuard);
    setup.AddPiece(ox);
    setup.AddPiece(cannon);

    Assert.True(setup.Attach(guard, soldier, AttachmentKind.Guard));
    Assert.False(setup.Attach(secondGuard, soldier, AttachmentKind.Guard));
    Assert.False(setup.Attach(secondGuard, secondGuard, AttachmentKind.Guard));
    Assert.True(setup.Attach(cannon, ox, AttachmentKind.Carried));
    setup.MovePiece(soldier, (5, 4));
    setup.MovePiece(ox, (9, 4));

    Assert.Equal((5, 4), guard.Position);
    Assert.Same(soldier, setup.GetPieceAt((5, 4)));
    Assert.Equal((9, 4), cannon.Position);

    setup.RemovePiece(guard);
    Assert.Null(guard.AttachedTo);
    Assert.Same(soldier, setup.GetPieceAt((5, 4)));
  }

  [Fact]
  public void Ox_CanHaveOnlyOneCarriedUnit()
  {
    PieceSetup setup = new();
    Piece ox = new(PieceDefinitions.Ox, (4, 4), TeamName.Red);
    Piece soldier = new(PieceDefinitions.Soldier, (4, 3), TeamName.Red);
    Piece knight = new(PieceDefinitions.Knight, (3, 4), TeamName.Red);
    setup.AddPiece(ox);
    setup.AddPiece(soldier);
    setup.AddPiece(knight);

    Assert.True(setup.Attach(soldier, ox, AttachmentKind.Carried));
    Assert.False(setup.Attach(knight, ox, AttachmentKind.Carried));
    Assert.Same(ox, soldier.AttachedTo);
    Assert.Null(knight.AttachedTo);

    setup.Detach(soldier);
    Assert.True(setup.Attach(knight, ox, AttachmentKind.Carried));
  }

  [Fact]
  public void UpdatedEconomy_UsesThreeActionTurnsAndRoundsRewardsToFive()
  {
    Team attacker = new(TeamName.Red, null, 0);
    Team defeated = new(TeamName.Blue, null, 0);
    Piece soldier = new(PieceDefinitions.Soldier, (0, 0), TeamName.Blue) { CurrentHealth = 0 };

    Assert.True(Actions.HandlePieceDeath(soldier, attacker, defeated, 0.4f, 0f));
    Assert.Equal(10, attacker.Money);
    Assert.Equal(0, defeated.Money);

    Piece defender = new(PieceDefinitions.Defender, (1, 0), TeamName.Blue) { CurrentHealth = 0 };
    Assert.True(Actions.HandlePieceDeath(defender, attacker, defeated, 0.5f, 0f));
    Assert.Equal(20, attacker.Money);

    Assert.Equal(200, Globals.StartingCash);
    Assert.Equal(3, Team.ActionsPerTurn);

    attacker.ActionPoints = 3;
    Assert.False(attacker.SpendAction());
    Assert.Equal(2, attacker.ActionPoints);
    Assert.False(attacker.SpendAction());
    Assert.True(attacker.SpendAction());
    Assert.Equal(3, attacker.ActionPoints);
  }

  [Fact]
  public void Mercenary_IsPurchasableAndTracksItsLastBid()
  {
    Piece mercenary = new(PieceDefinitions.Mercenary, (0, 0), TeamName.Red);

    Assert.Contains(PieceDefinitions.Mercenary, PieceDefinitions.Purchasable);
    Assert.DoesNotContain(PieceDefinitions.Mercenary, PieceDefinitions.Royals);
    Assert.Equal(10, mercenary.LastBid);
    Assert.Equal(20, mercenary.NextMercenaryBid);
    Assert.Equal(23, PieceDefinitions.All.Length);
  }

  private static void AssertDefinition(
    PieceDefinition definition,
    PieceCategory category,
    int moveRange,
    Shape moveShape,
    int attack,
    int health,
    (int x, int y) size,
    int attackRange,
    Shape attackShape,
    int cost,
    int minimumAttackRange = 1
  )
  {
    Assert.Equal(category, definition.Category);
    Assert.Equal((moveRange, moveShape), definition.Movement);
    Assert.Equal(attack, definition.Attack);
    Assert.Equal(health, definition.Health);
    Assert.Equal(size, definition.Size);
    Assert.Equal((attackRange, attackShape), definition.AttackShape);
    Assert.Equal(cost, definition.Cost);
    Assert.Equal(minimumAttackRange, definition.MinimumAttackRange);
  }
}
