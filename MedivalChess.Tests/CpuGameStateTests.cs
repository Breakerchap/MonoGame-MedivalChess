using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuGameStateTests
{
  [Fact]
  public void SimulatedAttack_DoesNotMutateTheOriginalSnapshot()
  {
    CpuGameState original = CreateState(
      new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );
    AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-peasant", 0, -1);

    Assert.True(attack.IsLegal(original));
    CpuGameState simulated = attack.Apply(original);

    Assert.Contains(original.Pieces, piece => piece.Id == "blue-peasant" && piece.Health == 5);
    Assert.DoesNotContain(simulated.Pieces, piece => piece.Id == "blue-peasant");
    Assert.Equal(3, original.ActionsRemaining);
    Assert.Equal(3, simulated.ActionsRemaining);
  }

  [Fact]
  public void AttackingAFarmSquareDamagesTheUnitBeforeTheFarm()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-farm", "Farm", NetworkTeam.Blue, 0, -1, 30),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );
    AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-farm", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState afterAttack = attack.Apply(state);

    Assert.DoesNotContain(afterAttack.Pieces, piece => piece.Id == "blue-peasant");
    Assert.Equal(30, afterAttack.Pieces.Single(piece => piece.Id == "blue-farm").Health);
  }

  [Fact]
  public void AttackingAnUncoveredFarmDamagesTheFarm()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-farm", "Farm", NetworkTeam.Blue, 0, -1, 30)
    );
    AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-farm", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState afterAttack = attack.Apply(state);

    Assert.Equal(10, afterAttack.Pieces.Single(piece => piece.Id == "blue-farm").Health);
  }

  [Fact]
  public void CloneAndSimulatedMovement_LeaveTheAuthoritativeSnapshotUntouched()
  {
    CpuGameState original = CreateState(new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15));
    CpuGameState clone = original.Clone();
    MoveAction move = new(NetworkTeam.Red, "red-soldier", 0, -1);

    Assert.NotSame(original, clone);
    Assert.True(move.IsLegal(clone));
    CpuGameState simulated = move.Apply(clone);

    NetworkPiece originalPiece = Assert.Single(original.Pieces);
    NetworkPiece clonePiece = Assert.Single(clone.Pieces);
    NetworkPiece movedPiece = Assert.Single(simulated.Pieces);
    Assert.Equal((0, 0), (originalPiece.X, originalPiece.Y));
    Assert.Equal((0, 0), (clonePiece.X, clonePiece.Y));
    Assert.Equal((0, -1), (movedPiece.X, movedPiece.Y));
    Assert.Equal(MatchRules.ActionsPerTurn, original.ActionsRemaining);
    Assert.Equal(MatchRules.ActionsPerTurn, simulated.ActionsRemaining);
  }

  [Fact]
  public void CavalierAttack_AfterMoving_UnlocksOneTwoSquareStraightFollowUpMove()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-cavalier", "Cavalier", NetworkTeam.Red, 0, 0, 20, HasMovedThisTurn: true),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );
    AttackAction attack = new(NetworkTeam.Red, "red-cavalier", "blue-peasant", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState afterAttack = attack.Apply(state);
    NetworkPiece cavalier = afterAttack.Pieces.Single(piece => piece.Id == "red-cavalier");

    Assert.True(cavalier.HasAttackedThisTurn);
    Assert.True(cavalier.HasMovedThisTurn);
    Assert.True(cavalier.CavalierFollowUpMoveAvailable);
    Assert.True(new MoveAction(NetworkTeam.Red, cavalier.Id, 0, 1).IsLegal(afterAttack));
    Assert.False(new MoveAction(NetworkTeam.Red, cavalier.Id, 0, 3).IsLegal(afterAttack));

    CpuGameState afterMove = new MoveAction(NetworkTeam.Red, cavalier.Id, 0, 1).Apply(afterAttack);
    NetworkPiece movedCavalier = afterMove.Pieces.Single(piece => piece.Id == cavalier.Id);
    Assert.False(movedCavalier.CavalierFollowUpMoveAvailable);
    Assert.False(new MoveAction(NetworkTeam.Red, movedCavalier.Id, 1, 1).IsLegal(afterMove));
  }

  [Fact]
  public void AttackingAnOx_AlsoDamagesItsCarriedUnit()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-ox", "Ox", NetworkTeam.Blue, 0, -1, 25),
      new NetworkPiece(
        "blue-cargo", "Knight", NetworkTeam.Blue, 0, -1, 30,
        AttachedToId: "blue-ox", AttachmentKind: NetworkAttachmentKind.Carried
      )
    );
    AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-ox", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState afterAttack = attack.Apply(state);

    Assert.Equal(5, afterAttack.Pieces.Single(piece => piece.Id == "blue-ox").Health);
    Assert.Equal(10, afterAttack.Pieces.Single(piece => piece.Id == "blue-cargo").Health);
  }

  [Fact]
  public void Elephant_CanFinishItsMoveOnAnEnemyItTramples()
  {
    Board board = new([
      (0, 0), (1, 0), (2, 0), (3, 0), (4, 0),
      (0, 1), (1, 1), (2, 1), (3, 1), (4, 1)
    ]);
    CpuGameState state = new(
      CreateConfiguration(),
      [
        new NetworkPiece("red-elephant", "Elephant", NetworkTeam.Red, 0, 0, 60),
        new NetworkPiece("blue-soldier", "Swordsman", NetworkTeam.Blue, 2, 0, 60)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      board: board
    );
    MoveAction move = new(NetworkTeam.Red, "red-elephant", 1, 0);

    Assert.True(move.IsLegal(state));

    CpuGameState moved = move.Apply(state);
    NetworkPiece elephant = moved.Pieces.Single(piece => piece.Id == "red-elephant");
    NetworkPiece soldier = moved.Pieces.Single(piece => piece.Id == "blue-soldier");
    Assert.Equal((1, 0), (elephant.X, elephant.Y));
    Assert.Equal(30, soldier.Health);
    Assert.True(UnitRules.FootprintsOverlap(elephant.X, elephant.Y, 2, 2, soldier.X, soldier.Y, 1, 1));
  }

  [Fact]
  public void CampaignCpuSnapshotUsesTheSuppliedCustomBoardGeometry()
  {
    Board campaignBoard = new(new[] { (0, 0) });
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("campaign-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      board: campaignBoard
    );

    Assert.Same(campaignBoard, state.Board);
    Assert.False(new MoveAction(NetworkTeam.Red, "campaign-soldier", 0, 1).IsLegal(state));
    Assert.Same(campaignBoard, state.Clone().Board);
  }

  [Fact]
  public void LegalActionGenerator_ReturnsOnlyActionsThatApplyToTheCurrentTeam()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );

    IReadOnlyList<ICpuGameAction> actions = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red);

    Assert.NotEmpty(actions);
    Assert.All(actions, action =>
    {
      Assert.Equal(NetworkTeam.Red, action.Team);
      Assert.True(action.IsLegal(state), action.Describe());
    });
    Assert.Contains(actions, action => action is AttackAction { TargetPieceId: "blue-peasant" });
  }

  [Fact]
  public void EndTurn_IsAvailableImmediatelyWhenActionLimitsAreDisabled()
  {
    CpuGameState state = CreateState(new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15));
    MoveAction move = new(NetworkTeam.Red, "red-soldier", 0, 1);

    EndTurnAction endTurn = new(NetworkTeam.Red);

    Assert.True(move.IsLegal(state));
    CpuGameState afterMove = move.Apply(state);
    Assert.Equal(MatchRules.ActionsPerTurn, afterMove.ActionsRemaining);
    Assert.True(endTurn.IsLegal(state));
    CpuGameState afterEnd = endTurn.Apply(afterMove);

    Assert.Equal(NetworkTeam.Blue, afterEnd.CurrentTurn);
    Assert.Equal(MatchRules.ActionsPerTurn, afterEnd.ActionsRemaining);
    Assert.True(afterEnd.Pieces.Single(piece => piece.Id == "red-soldier").HasMovedThisTurn);
  }

  [Fact]
  public void PalaceRoyalMayEndTurnBeforeUsingAnAction()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [
        new NetworkPiece("red-palace", "Palace", NetworkTeam.Red, 0, 12, 150),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -12, 110)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, "Palace"),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, "King")
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    EndTurnAction endTurn = new(NetworkTeam.Red);

    Assert.True(endTurn.IsLegal(state));
    CpuGameState afterEnd = endTurn.Apply(state);

    Assert.Equal(NetworkTeam.Blue, afterEnd.CurrentTurn);
    Assert.Equal(MatchRules.ActionsPerTurn, afterEnd.ActionsRemaining);
  }

  [Fact]
  public void InitialBuyPurchase_UsesTheOpeningBuyerAndAdvancesToTheNextTeam()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    (int x, int y) blueSquare = BoardRules.GetBoard(configuration).Cells.First(position =>
      BoardRules.CanPlaceForTeam(configuration, NetworkTeam.Blue, position.x, position.y, 1, 1));
    NetworkInitialBuyState initialBuy = new(
      NetworkTeam.Blue,
      PurchasesThisTurn: 0,
      PurchasesPerTurn: 1,
      RedBuyTurnsUsed: 0,
      BlueBuyTurnsUsed: 0,
      BuyTurnsPerTeam: 1,
      RedStopped: false,
      BlueStopped: false,
      IsComplete: false,
      TeamStates:
      [
        new NetworkInitialBuyTeamState(NetworkTeam.Red, 0, false),
        new NetworkInitialBuyTeamState(NetworkTeam.Blue, 0, false)
      ]
    );
    CpuGameState state = new(
      configuration,
      [],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain(),
      initialBuy: initialBuy
    );
    PurchaseAction purchase = new(NetworkTeam.Blue, "Peasant", blueSquare.x, blueSquare.y);

    Assert.True(purchase.IsLegal(state));
    CpuGameState next = purchase.Apply(state);

    Assert.Equal(NetworkTeam.Red, next.CurrentTurn);
    Assert.NotNull(next.InitialBuy);
    Assert.Contains(next.Pieces, piece => piece.Team == NetworkTeam.Blue && piece.Type == "Peasant");
  }

  [Fact]
  public void PlunderMercenary_CanPickUpTreasureThroughTheSharedAbilityFlow()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration("Plunder");
    CpuGameState state = new(
      configuration,
      [new NetworkPiece("red-mercenary", "Mercenary", NetworkTeam.Red, 0, 1, 20)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      treasurePosition: (0, 0)
    );
    UseAbilityAction pickup = new(NetworkTeam.Red, "red-mercenary", "PickUpTreasure", null, 0, 0);

    Assert.True(pickup.IsLegal(state));
    Assert.Contains(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red), action => action.Equals(pickup));

    CpuGameState afterPickup = pickup.Apply(state);

    Assert.Equal("red-mercenary", afterPickup.TreasureCarrierId);
    Assert.Null(afterPickup.TreasurePosition);
    Assert.True(afterPickup.Pieces.Single(piece => piece.Id == "red-mercenary").HasAttackedThisTurn);
  }

  [Fact]
  public void TreasurePickup_RejectsAnOccupiedTreasureSquare()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration("Plunder");
    CpuGameState state = new(
      configuration,
      [
        new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 1, 15),
        new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, 0, 5)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      treasurePosition: (0, 0)
    );

    Assert.False(new UseAbilityAction(NetworkTeam.Red, "red-soldier", "PickUpTreasure", null, 0, 0).IsLegal(state));
  }

  [Fact]
  public void CrossingAMine_DamagesTheMoverEvenWhenItFinishesBeyondTheBlastRadius()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 2, 15)],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      mines: [KeyValuePair.Create((0, 1), NetworkTeam.Blue)]
    );
    MoveAction move = new(NetworkTeam.Red, "red-soldier", 0, -1);

    Assert.True(move.IsLegal(state));
    CpuGameState afterMove = move.Apply(state);

    Assert.DoesNotContain(afterMove.Pieces, piece => piece.Id == "red-soldier");
    Assert.Empty(afterMove.Mines);
  }

  [Fact]
  public void EngineerCannotDemolishAScenarioProvidedRiverBridge()
  {
    TileEdge bridge = TileEdge.Between((0, 0), (0, -1));
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("red-engineer", "Engineer", NetworkTeam.Red, 0, 0, 20)],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      riverBridges: [bridge]
    );
    UseAbilityAction demolish = new(NetworkTeam.Red, "red-engineer", "Demolish", null, 0, -1);

    Assert.False(demolish.IsLegal(state));
    Assert.DoesNotContain(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red), action => action.Equals(demolish));
  }

  [Fact]
  public void SearchPurchaseGeneration_AvoidsEveryExistingPieceFootprint()
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Regicide", 1234, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: true
    );
    UnitRule farmRule = UnitRules.GetRequired("Farm");
    (int x, int y) farmPosition = BoardRules.GetBoard(configuration).Cells.First(position =>
      BoardRules.CanPlaceForTeam(configuration, NetworkTeam.Red, position.x, position.y, farmRule.Width, farmRule.Height));
    NetworkPiece existingFarm = new("red-farm", "Farm", NetworkTeam.Red, farmPosition.x, farmPosition.y, farmRule.Health);
    CpuGameState state = new(
      configuration,
      [existingFarm],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );

    IReadOnlyList<PurchaseAction> purchases = new CpuActionGenerator().GenerateSearchActions(state, NetworkTeam.Red, 12)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType != "Mercenary")
      .ToArray();

    Assert.NotEmpty(purchases);
    Assert.All(purchases, action =>
    {
      UnitRule purchasedRule = UnitRules.GetRequired(action.UnitType);
      Assert.False(UnitRules.FootprintsOverlap(
        action.X, action.Y, purchasedRule.Width, purchasedRule.Height,
        existingFarm.X, existingFarm.Y, farmRule.Width, farmRule.Height), action.Describe());
    });
  }

  [Fact]
  public void MercenaryPurchase_RequiresACompletelyEmptyNoMansLandSquare()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    Board board = BoardRules.GetBoard(configuration);
    (int x, int y) noMansLand = board.Cells.First(position =>
      BoardRules.CanPlaceMercenary(board, configuration.GameMode, configuration.PlayerCount, position.x, position.y));
    CpuGameState state = new(
      configuration,
      [new NetworkPiece("blocking-farm", "Farm", NetworkTeam.Red, noMansLand.x, noMansLand.y, 30)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    PurchaseAction purchase = new(NetworkTeam.Red, "Mercenary", noMansLand.x, noMansLand.y);

    Assert.False(purchase.IsLegal(state));
    Assert.DoesNotContain(new CpuActionGenerator().GenerateSearchActions(state, NetworkTeam.Red, 24), action => action.Equals(purchase));
  }

  [Fact]
  public void CpuCanHireAFullHealthNeutralMercenaryForTheFixedHireCostButNotBuyOutARival()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    Board board = BoardRules.GetBoard(configuration);
    (int x, int y) position = board.Cells.First(square =>
      BoardRules.CanPlaceMercenary(board, configuration.GameMode, configuration.PlayerCount, square.x, square.y));
    CpuGameState neutralState = new(
      configuration,
      [new NetworkPiece("neutral-mercenary", "Mercenary", NetworkTeam.Neutral, position.x, position.y, 20)],
      [new CpuTeamState(NetworkTeam.Red, PieceDefinitions.NeutralMercenaryHireCost, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    PurchaseAction hire = new(NetworkTeam.Red, "Mercenary", position.x, position.y);

    Assert.True(hire.IsLegal(neutralState));
    CpuGameState hired = hire.Apply(neutralState);
    Assert.Equal(0, hired.Teams[NetworkTeam.Red].Money);
    Assert.Contains(hired.Pieces, piece => piece.Id == "neutral-mercenary" && piece.Team == NetworkTeam.Red &&
      piece.LastBid == PieceDefinitions.NeutralMercenaryHireCost);

    CpuGameState rivalState = new(
      configuration,
      [new NetworkPiece("blue-mercenary", "Mercenary", NetworkTeam.Blue, position.x, position.y, 20)],
      [new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    Assert.False(hire.IsLegal(rivalState));
  }

  [Fact]
  public void CpuCanFireItsMercenaryThroughTheSharedAbilityAction()
  {
    CpuGameState state = CreateState(new NetworkPiece("red-mercenary", "Mercenary", NetworkTeam.Red, 0, 0, 20));
    UseAbilityAction fire = new(NetworkTeam.Red, "red-mercenary", "Fire", null, 0, 0);

    Assert.True(fire.IsLegal(state));
    Assert.Contains(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red), action => action.Equals(fire));
    CpuGameState fired = fire.Apply(state);
    Assert.Contains(fired.Pieces, piece => piece.Id == "red-mercenary" && piece.Team == NetworkTeam.Neutral);
  }

  [Fact]
  public void AttackGeneration_TargetsTheReachableSquareOfALargePiece()
  {
    NetworkPiece soldier = new("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15);
    NetworkPiece farm = new("blue-farm", "Farm", NetworkTeam.Blue, -1, -3, 30);
    CpuGameState state = new(
      CreateConfiguration(),
      [soldier, farm],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    AttackAction reachable = new(NetworkTeam.Red, soldier.Id, farm.Id, 0, -1);
    AttackAction unreachableOrigin = new(NetworkTeam.Red, soldier.Id, farm.Id, farm.X, farm.Y);

    Assert.True(reachable.IsLegal(state));
    Assert.False(unreachableOrigin.IsLegal(state));
    Assert.Contains(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red), action => action.Equals(reachable));
  }

  [Fact]
  public void AttachedUnits_CannotAttackOrUseAnAbilityIndependently()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [
        new NetworkPiece("red-soldier", "Swordsman", NetworkTeam.Red, 0, 0, 15),
        new NetworkPiece("red-guard", "Guard", NetworkTeam.Red, 0, 0, 25,
          AttachedToId: "red-soldier", AttachmentKind: NetworkAttachmentKind.Guard),
        new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    AttackAction attack = new(NetworkTeam.Red, "red-guard", "blue-peasant", 0, -1);
    UseAbilityAction ability = new(NetworkTeam.Red, "red-guard", "Attach", "red-soldier", 0, 0);

    Assert.False(attack.IsLegal(state));
    Assert.False(ability.IsLegal(state));
    Assert.DoesNotContain(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red), action =>
      action is AttackAction { AttackerId: "red-guard" } or UseAbilityAction { ActorId: "red-guard" });
  }


  [Fact]
  public void HermesMayMoveTwiceButNotThreeTimesInOneOwnerTurn()
  {
    CpuGameState state = CreateState(new NetworkPiece("hermes", nameof(PieceType.Hermes), NetworkTeam.Red, 0, 0, 20));

    MoveAction first = new(NetworkTeam.Red, "hermes", 0, -1);
    Assert.True(first.IsLegal(state));
    state = first.Apply(state);

    MoveAction second = new(NetworkTeam.Red, "hermes", 0, -2);
    Assert.True(second.IsLegal(state));
    state = second.Apply(state);

    Assert.False(new MoveAction(NetworkTeam.Red, "hermes", 0, -3).IsLegal(state));
    Assert.Equal(2, state.Pieces.Single(piece => piece.Id == "hermes").AbilityState?.MovesThisTurn);
  }

  [Fact]
  public void SniperCooldownAdvancesOnCpuOwnerTurns()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("sniper", nameof(PieceType.Sniper), NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("target", nameof(PieceType.King), NetworkTeam.Blue, 0, -3, 110)
    );
    AttackAction shot = new(NetworkTeam.Red, "sniper", "target", 0, -3);

    Assert.True(shot.IsLegal(state));
    state = shot.Apply(state);
    Assert.False(shot.IsLegal(state));

    state = new EndTurnAction(NetworkTeam.Red).Apply(state);
    state = new EndTurnAction(NetworkTeam.Blue).Apply(state);
    Assert.Equal(NetworkTeam.Red, state.CurrentTurn);
    Assert.False(shot.IsLegal(state));

    state = new EndTurnAction(NetworkTeam.Red).Apply(state);
    state = new EndTurnAction(NetworkTeam.Blue).Apply(state);
    Assert.Equal(NetworkTeam.Red, state.CurrentTurn);
    Assert.True(shot.IsLegal(state));
  }

  [Fact]
  public void SeraphCpuAttacksThreeDistinctTargetsAndCannotRepeatOne()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("seraph", nameof(PieceType.Seraph), NetworkTeam.Red, 0, 0, 75),
      new NetworkPiece("a", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -1, 100),
      new NetworkPiece("b", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, 0, 100),
      new NetworkPiece("c", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, 2, 100),
      new NetworkPiece("d", nameof(PieceType.Swordsman), NetworkTeam.Blue, -1, 0, 100)
    );

    foreach ((string id, int x, int y) in new[] { ("a", 0, -1), ("b", 2, 0), ("c", 1, 2) })
    {
      AttackAction attack = new(NetworkTeam.Red, "seraph", id, x, y);
      Assert.True(attack.IsLegal(state));
      state = attack.Apply(state);
    }

    Assert.False(new AttackAction(NetworkTeam.Red, "seraph", "a", 0, -1).IsLegal(state));
    Assert.False(new AttackAction(NetworkTeam.Red, "seraph", "d", -1, 0).IsLegal(state));
    NetworkPiece seraph = state.Pieces.Single(piece => piece.Id == "seraph");
    Assert.Equal(3, seraph.AttacksThisTurn);
    Assert.True(seraph.HasAttackedThisTurn);
  }



  [Fact]
  public void HarvesterDestroysTerrainGainsGoldAndPreservesSourceSnapshot()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    BattlefieldTerrain terrain = new(forests: [(0, -1)]);
    CpuGameState original = new(
      configuration,
      [new NetworkPiece("harvester", nameof(PieceType.Harvester), NetworkTeam.Red, 0, 0, 30)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: terrain
    );
    UseAbilityAction harvest = new(NetworkTeam.Red, "harvester", "Harvest", null, 0, -1);

    Assert.True(harvest.IsLegal(original));
    Assert.Contains(new CpuActionGenerator().GenerateLegalActions(original, NetworkTeam.Red), action =>
      action is UseAbilityAction { ActorId: "harvester", Ability: "Harvest", TargetX: 0, TargetY: -1 });

    CpuGameState simulated = harvest.Apply(original);

    Assert.True(original.Terrain.IsForest((0, -1)));
    Assert.False(simulated.Terrain.IsForest((0, -1)));
    Assert.Equal(200, original.Teams[NetworkTeam.Red].Money);
    Assert.Equal(215, simulated.Teams[NetworkTeam.Red].Money);
    Assert.True(simulated.Pieces.Single(piece => piece.Id == "harvester").HasAttackedThisTurn);
    Assert.False(harvest.IsLegal(simulated));
  }


  [Fact]
  public void SumoPushesTwoTilesAndShortensBeforeALake()
  {
    CpuGameState clear = CreateState(
      new NetworkPiece("sumo", nameof(PieceType.Sumo), NetworkTeam.Red, 0, 0, 55),
      new NetworkPiece("target", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -1, 30)
    );
    AttackAction attack = new(NetworkTeam.Red, "sumo", "target", 0, -1);

    Assert.True(attack.IsLegal(clear));
    CpuGameState fullPush = attack.Apply(clear);
    NetworkPiece fullyPushed = fullPush.Pieces.Single(piece => piece.Id == "target");
    Assert.Equal((0, -3), (fullyPushed.X, fullyPushed.Y));
    Assert.False(fullyPushed.HasMovedThisTurn);

    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuGameState blocked = new(
      configuration,
      [
        new NetworkPiece("sumo", nameof(PieceType.Sumo), NetworkTeam.Red, 0, 0, 55),
        new NetworkPiece("target", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -1, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(lakes: [(0, -3)])
    );

    Assert.True(attack.IsLegal(blocked));
    CpuGameState shortPush = attack.Apply(blocked);
    NetworkPiece partlyPushed = shortPush.Pieces.Single(piece => piece.Id == "target");
    Assert.Equal((0, -2), (partlyPushed.X, partlyPushed.Y));
    Assert.False(partlyPushed.HasMovedThisTurn);
  }

  [Fact]
  public void MusketeerRetreatsWithoutSpendingItsMove()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("musketeer", nameof(PieceType.Musketeer), NetworkTeam.Red, 0, 0, 30),
      new NetworkPiece("target", nameof(PieceType.King), NetworkTeam.Blue, 0, -2, 190)
    );
    AttackAction attack = new(NetworkTeam.Red, "musketeer", "target", 0, -2);

    Assert.True(attack.IsLegal(state));
    CpuGameState result = attack.Apply(state);

    NetworkPiece musketeer = result.Pieces.Single(piece => piece.Id == "musketeer");
    Assert.Equal((0, 2), (musketeer.X, musketeer.Y));
    Assert.False(musketeer.HasMovedThisTurn);
  }

  [Fact]
  public void MusketeerStaysPutWhenFullRetreatIsBlocked()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuGameState state = new(
      configuration,
      [
        new NetworkPiece("musketeer", nameof(PieceType.Musketeer), NetworkTeam.Red, 0, 0, 30),
        new NetworkPiece("target", nameof(PieceType.King), NetworkTeam.Blue, 0, -2, 190)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(lakes: [(0, 2)])
    );
    AttackAction attack = new(NetworkTeam.Red, "musketeer", "target", 0, -2);

    Assert.True(attack.IsLegal(state));
    CpuGameState result = attack.Apply(state);

    NetworkPiece musketeer = result.Pieces.Single(piece => piece.Id == "musketeer");
    Assert.Equal((0, 0), (musketeer.X, musketeer.Y));
    Assert.False(musketeer.HasMovedThisTurn);
  }

  [Fact]
  public void BeelzebubCannotBePushedBySumo()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("sumo", nameof(PieceType.Sumo), NetworkTeam.Red, 0, 0, 55),
      new NetworkPiece("beelzebub", nameof(PieceType.Beelzebub), NetworkTeam.Blue, 0, -3, 120)
    );
    AttackAction attack = new(NetworkTeam.Red, "sumo", "beelzebub", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState result = attack.Apply(state);

    NetworkPiece beelzebub = result.Pieces.Single(piece => piece.Id == "beelzebub");
    Assert.Equal((0, -3), (beelzebub.X, beelzebub.Y));
  }

  [Fact]
  public void BeelzebubRetaliatesByPushingItsAttackerUsingFootprintCentre()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 0, 30),
      new NetworkPiece("beelzebub", nameof(PieceType.Beelzebub), NetworkTeam.Blue, 0, -3, 120)
    );
    AttackAction attack = new(NetworkTeam.Red, "attacker", "beelzebub", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState result = attack.Apply(state);

    NetworkPiece attacker = result.Pieces.Single(piece => piece.Id == "attacker");
    Assert.Equal((-2, 2), (attacker.X, attacker.Y));
    Assert.False(attacker.HasMovedThisTurn);
  }

  [Fact]
  public void ZeusChainsThroughDiagonalAndOrthogonalEnemiesButNotFriendlies()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("zeus", nameof(PieceType.Zeus), NetworkTeam.Red, 0, 0, 55),
      new NetworkPiece("target", nameof(PieceType.King), NetworkTeam.Blue, 0, -2, 190),
      new NetworkPiece("diagonal", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, -3, 30),
      new NetworkPiece("next", nameof(PieceType.Swordsman), NetworkTeam.Blue, 2, -3, 30),
      new NetworkPiece("friendly", nameof(PieceType.Swordsman), NetworkTeam.Red, 1, -2, 30)
    );
    AttackAction attack = new(NetworkTeam.Red, "zeus", "target", 0, -2);

    Assert.True(attack.IsLegal(state));
    CpuGameState result = attack.Apply(state);

    Assert.Equal(160, result.Pieces.Single(piece => piece.Id == "target").Health);
    Assert.Equal(10, result.Pieces.Single(piece => piece.Id == "diagonal").Health);
    Assert.Equal(10, result.Pieces.Single(piece => piece.Id == "next").Health);
    Assert.Equal(30, result.Pieces.Single(piece => piece.Id == "friendly").Health);
  }

  [Fact]
  public void PhantomPossessionMovesRoyalIdentityAndUnpossessLocksThePhantom()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("phantom", nameof(PieceType.Phantom), NetworkTeam.Red, 0, 0, 20),
      new NetworkPiece("host", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, -1, 30),
      new NetworkPiece("enemy-king", nameof(PieceType.King), NetworkTeam.Blue, 5, -5, 190)
    );
    UseAbilityAction possess = new(NetworkTeam.Red, "phantom", "Possess", "host", 0, -1);

    Assert.True(possess.IsLegal(state));
    state = possess.Apply(state);

    NetworkPiece possessedPhantom = state.Pieces.Single(piece => piece.Id == "phantom");
    NetworkPiece host = state.Pieces.Single(piece => piece.Id == "host");
    Assert.Equal("host", possessedPhantom.PossessedUnitId);
    Assert.True(host.IsRoyalProxy);
    Assert.False(RoyalAbilityRules.IsRoyal(
      possessedPhantom.Type, possessedPhantom.IsRoyalProxy, possessedPhantom.PossessedUnitId));
    Assert.True(RoyalAbilityRules.IsRoyal(host.Type, host.IsRoyalProxy, host.PossessedUnitId));

    UseAbilityAction unpossess = new(NetworkTeam.Red, "phantom", "Unpossess", "host", 0, -1);
    Assert.True(unpossess.IsLegal(state));
    state = unpossess.Apply(state);

    NetworkPiece releasedPhantom = state.Pieces.Single(piece => piece.Id == "phantom");
    NetworkPiece releasedHost = state.Pieces.Single(piece => piece.Id == "host");
    Assert.Null(releasedPhantom.PossessedUnitId);
    Assert.False(releasedHost.IsRoyalProxy);
    Assert.True(releasedPhantom.AbilityState?.CannotMoveThisTurn == true);
    Assert.True(releasedPhantom.AbilityState?.CannotActThisTurn == true);
    Assert.False(new MoveAction(NetworkTeam.Red, "phantom", 1, 0).IsLegal(state));
  }

  [Fact]
  public void KillingPossessedRoyalProxyAlsoKillsItsPhantom()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [
        new NetworkPiece(
          "phantom", nameof(PieceType.Phantom), NetworkTeam.Red, 0, 0, 20,
          PossessedUnitId: "host"),
        new NetworkPiece(
          "host", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, -1, 5,
          IsRoyalProxy: true),
        new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -2, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.Phantom)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );
    AttackAction attack = new(NetworkTeam.Blue, "attacker", "host", 0, -1);
    Assert.True(attack.IsLegal(state));

    CpuGameState result = attack.Apply(state);

    Assert.DoesNotContain(result.Pieces, piece => piece.Id == "host");
    Assert.DoesNotContain(result.Pieces, piece => piece.Id == "phantom");
    Assert.Equal(NetworkTeam.Blue, result.Winner);
  }

  [Fact]
  public void GoblinRoyaltyOnlyLosesWhenFinalGoblinDies()
  {
    CpuGameState twoGoblinState = new(
      CreateConfiguration(),
      [
        new NetworkPiece("goblin-a", nameof(PieceType.GoblinRoyalty), NetworkTeam.Red, 0, 0, 5),
        new NetworkPiece("goblin-b", nameof(PieceType.GoblinRoyalty), NetworkTeam.Red, 2, 0, 35),
        new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 1, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.GoblinRoyalty)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );
    AttackAction firstKill = new(NetworkTeam.Blue, "attacker", "goblin-a", 0, 0);
    Assert.True(firstKill.IsLegal(twoGoblinState));
    CpuGameState afterFirst = firstKill.Apply(twoGoblinState);
    Assert.Null(afterFirst.Winner);
    Assert.Contains(afterFirst.Pieces, piece => piece.Id == "goblin-b");

    CpuGameState finalGoblinState = new(
      CreateConfiguration(),
      [
        new NetworkPiece("goblin", nameof(PieceType.GoblinRoyalty), NetworkTeam.Red, 0, 0, 5),
        new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 1, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.GoblinRoyalty)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );
    AttackAction finalKill = new(NetworkTeam.Blue, "attacker", "goblin", 0, 0);
    Assert.True(finalKill.IsLegal(finalGoblinState));
    CpuGameState afterFinal = finalKill.Apply(finalGoblinState);
    Assert.Equal(NetworkTeam.Blue, afterFinal.Winner);
  }

  [Fact]
  public void PalaceGrantsIncomeAndOnlyAssistsMovementTowardIt()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    UnitRule swordsmanRule = UnitRules.GetRequired(nameof(PieceType.Swordsman));
    int assistedY = swordsmanRule.MoveRange + 1;
    int palaceY = assistedY + 2;
    CpuGameState movementState = new(
      configuration,
      [
        new NetworkPiece("palace", nameof(PieceType.Palace), NetworkTeam.Red, 0, palaceY, 230),
        new NetworkPiece("soldier", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 0, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.Palace)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(
        forests: [(0, 1), (0, 2), (0, 3), (0, assistedY)],
        lakes: [(0, -1)])
    );

    IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> paths =
      CpuGameRules.GetLegalMovementPaths(movementState, movementState.Pieces.Single(piece => piece.Id == "soldier"));
    Assert.Contains((0, assistedY), paths.Keys);
    Assert.DoesNotContain((0, -1), paths.Keys);

    CpuGameState economyState = new(
      configuration,
      [
        new NetworkPiece("palace", nameof(PieceType.Palace), NetworkTeam.Red, 0, 5, 230),
        new NetworkPiece("blue-king", nameof(PieceType.King), NetworkTeam.Blue, 0, -5, 190)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.Palace)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn - 1, nameof(PieceType.King))
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );

    CpuGameState redTurn = new EndTurnAction(NetworkTeam.Blue).Apply(economyState);
    Assert.Equal(NetworkTeam.Red, redTurn.CurrentTurn);
    Assert.Equal(210, redTurn.Teams[NetworkTeam.Red].Money);
  }

  [Fact]
  public void EmperorTransformsIntoTerracottaThenTerracottaDeathLosesRegicide()
  {
    CpuGameState emperorState = new(
      CreateConfiguration(),
      [
        new NetworkPiece("emperor", nameof(PieceType.Emperor), NetworkTeam.Red, 0, 0, 5),
        new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 1, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.Emperor)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );
    AttackAction killEmperor = new(NetworkTeam.Blue, "attacker", "emperor", 0, 0);
    Assert.True(killEmperor.IsLegal(emperorState));

    CpuGameState transformed = killEmperor.Apply(emperorState);
    NetworkPiece terracotta = transformed.Pieces.Single(piece => piece.Id == "emperor");
    Assert.Equal(nameof(PieceType.TerracottaWarrior), terracotta.Type);
    Assert.Equal(PieceDefinitions.TerracottaWarrior.Health, terracotta.Health);
    Assert.Null(transformed.Winner);

    CpuGameState terracottaState = new(
      CreateConfiguration(),
      [
        new NetworkPiece("terracotta", nameof(PieceType.TerracottaWarrior), NetworkTeam.Red, 0, 0, 5),
        new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 1, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.Emperor)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King))
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );
    CpuGameState defeated = new AttackAction(NetworkTeam.Blue, "attacker", "terracotta", 0, 0).Apply(terracottaState);
    Assert.Equal(NetworkTeam.Blue, defeated.Winner);
  }

  [Fact]
  public void GiantAndCyclopsCarryAndUseTheirDistinctThrowPatterns()
  {
    CpuGameState carryState = CreateState(
      new NetworkPiece("giant", nameof(PieceType.Giant), NetworkTeam.Red, 0, 0, 70),
      new NetworkPiece("cargo", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, -1, 30)
    );
    UseAbilityAction carry = new(NetworkTeam.Red, "giant", "Carry", "cargo", 0, -1);
    Assert.True(carry.IsLegal(carryState));
    CpuGameState carried = carry.Apply(carryState);
    NetworkPiece carriedCargo = carried.Pieces.Single(piece => piece.Id == "cargo");
    Assert.Equal("giant", carriedCargo.AttachedToId);
    Assert.Equal(NetworkAttachmentKind.Carried, carriedCargo.AttachmentKind);

    CpuGameState giantThrowState = CreateState(
      new NetworkPiece("giant", nameof(PieceType.Giant), NetworkTeam.Red, 0, 0, 70),
      new NetworkPiece(
        "cargo", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 0, 30,
        AttachedToId: "giant", AttachmentKind: NetworkAttachmentKind.Carried)
    );
    UseAbilityAction giantThrow = new(NetworkTeam.Red, "giant", "Throw", null, 3, -2);
    Assert.True(giantThrow.IsLegal(giantThrowState));
    CpuGameState giantThrown = giantThrow.Apply(giantThrowState);
    NetworkPiece giantCargo = giantThrown.Pieces.Single(piece => piece.Id == "cargo");
    Assert.Equal((3, -2), (giantCargo.X, giantCargo.Y));
    Assert.Null(giantCargo.AttachedToId);

    CpuGameState cyclopsThrowState = CreateState(
      new NetworkPiece("cyclops", nameof(PieceType.Cyclops), NetworkTeam.Red, 0, 0, 85),
      new NetworkPiece(
        "cargo", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 0, 30,
        AttachedToId: "cyclops", AttachmentKind: NetworkAttachmentKind.Carried)
    );
    Assert.False(new UseAbilityAction(
      NetworkTeam.Red, "cyclops", "Throw", null, 3, -2).IsLegal(cyclopsThrowState));
    UseAbilityAction cyclopsThrow = new(NetworkTeam.Red, "cyclops", "Throw", null, 3, -1);
    Assert.True(cyclopsThrow.IsLegal(cyclopsThrowState));
  }

  [Theory]
  [InlineData(PieceType.SummonedGolem, AdvancedAbilityRules.SummonedGolemUpkeep)]
  [InlineData(PieceType.HiredGun, AdvancedAbilityRules.HiredGunUpkeep)]
  public void UpkeepUnitsPayImmediatelyThenMayBeFired(PieceType type, int immediateUpkeep)
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    UnitRule rule = UnitRules.GetRequired(type.ToString());
    Board board = BoardRules.GetBoard(configuration);
    (int x, int y) position = board.Cells.First(square =>
      BoardRules.CanPlaceForTeam(
        board, configuration.GameMode, configuration.PlayerCount,
        NetworkTeam.Red, square.x, square.y, rule.Width, rule.Height));
    int purchasePrice = EconomyRules.GetUnitPrice(rule.Cost, configuration.UnitPricePercent);
    CpuGameState state = new(
      configuration,
      [],
      [
        new CpuTeamState(NetworkTeam.Red, purchasePrice + immediateUpkeep, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    PurchaseAction purchase = new(NetworkTeam.Red, rule.Type, position.x, position.y);

    Assert.True(purchase.IsLegal(state));
    CpuGameState purchased = purchase.Apply(state);
    Assert.Equal(0, purchased.Teams[NetworkTeam.Red].Money);
    NetworkPiece unit = purchased.Pieces.Single(piece => piece.Type == rule.Type);

    // Newly purchased normal-phase units cannot act until their next owner turn.
    CpuGameState ready = new(
      configuration,
      [unit with { HasMovedThisTurn = false, HasAttackedThisTurn = false, AttacksThisTurn = 0 }],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    UseAbilityAction fire = new(NetworkTeam.Red, unit.Id, "Fire", null, unit.X, unit.Y);
    Assert.True(fire.IsLegal(ready));
    Assert.Contains(new CpuActionGenerator().GenerateLegalActions(ready, NetworkTeam.Red), action => action.Equals(fire));
    CpuGameState fired = fire.Apply(ready);
    Assert.Equal(NetworkTeam.Neutral, fired.Pieces.Single(piece => piece.Id == unit.Id).Team);
  }

  [Theory]
  [InlineData(PieceType.SummonedGolem, AdvancedAbilityRules.SummonedGolemUpkeep)]
  [InlineData(PieceType.HiredGun, AdvancedAbilityRules.HiredGunUpkeep)]
  public void UpkeepUnitsBecomeNeutralWhenOwnerTurnPayrollCannotBePaid(PieceType type, int upkeep)
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    NetworkPiece unit = new("upkeep-unit", type.ToString(), NetworkTeam.Red, 0, 2, UnitRules.GetRequired(type.ToString()).Health);
    CpuGameState state = new(
      configuration,
      [
        unit,
        new NetworkPiece("blue-king", nameof(PieceType.King), NetworkTeam.Blue, 0, -5, 190)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, upkeep - 1, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn - 1)
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );

    CpuGameState redTurn = new EndTurnAction(NetworkTeam.Blue).Apply(state);

    Assert.Equal(NetworkTeam.Red, redTurn.CurrentTurn);
    NetworkPiece neutral = redTurn.Pieces.Single(piece => piece.Id == unit.Id);
    Assert.Equal(NetworkTeam.Neutral, neutral.Team);
    Assert.Equal(upkeep - 1, redTurn.Teams[NetworkTeam.Red].Money);
  }

  [Fact]
  public void FireDamagesMoverOnceAndIsConsumed()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("soldier", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 1, 30)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity("fire", AbilityEntityKind.Fire, NetworkTeam.Blue, 0, 0, 0)
      ]
    );
    MoveAction move = new(NetworkTeam.Red, "soldier", 0, -1);

    Assert.True(move.IsLegal(state));
    CpuGameState result = move.Apply(state);

    Assert.Equal(15, result.Pieces.Single(piece => piece.Id == "soldier").Health);
    Assert.DoesNotContain(result.AbilityEntities, entity => entity.Id == "fire");
  }

  [Fact]
  public void BrambleDamagesMoverAndItselfWhenCrossed()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("soldier", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 1, 30)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity("bramble", AbilityEntityKind.Bramble, NetworkTeam.Blue, 0, 0, 30)
      ]
    );
    MoveAction move = new(NetworkTeam.Red, "soldier", 0, -1);

    Assert.True(move.IsLegal(state));
    CpuGameState result = move.Apply(state);

    Assert.Equal(20, result.Pieces.Single(piece => piece.Id == "soldier").Health);
    Assert.Equal(20, result.AbilityEntities.Single(entity => entity.Id == "bramble").Health);
    Assert.False(new MoveAction(NetworkTeam.Red, "soldier", 0, 0).IsLegal(state));
  }

  [Fact]
  public void PhoenixMayCreateFireAfterAttackingAndPaysFiveHealth()
  {
    NetworkPiece phoenix = new(
      "phoenix", nameof(PieceType.Phoenix), NetworkTeam.Red, 0, 0, 60,
      HasAttackedThisTurn: true);
    CpuGameState state = CreateState(phoenix);
    UseAbilityAction fire = new(NetworkTeam.Red, "phoenix", "Fire", null, 0, -1);

    Assert.True(fire.IsLegal(state));
    CpuGameState result = fire.Apply(state);

    Assert.Equal(55, result.Pieces.Single(piece => piece.Id == "phoenix").Health);
    Assert.Contains(result.AbilityEntities, entity =>
      entity.Kind == AbilityEntityKind.Fire && entity.X == 0 && entity.Y == -1);
    Assert.False(fire.IsLegal(result));
  }

  [Fact]
  public void DragonCrossesFireWithoutDamageOrConsumingIt()
  {
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("dragon", nameof(PieceType.Dragon), NetworkTeam.Red, 0, 2, 120)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity("fire", AbilityEntityKind.Fire, NetworkTeam.Blue, 0, 0, 0)
      ]
    );
    MoveAction move = new(NetworkTeam.Red, "dragon", 0, -2);

    Assert.True(move.IsLegal(state));
    CpuGameState result = move.Apply(state);

    Assert.Equal(120, result.Pieces.Single(piece => piece.Id == "dragon").Health);
    Assert.Contains(result.AbilityEntities, entity => entity.Id == "fire");
  }

  [Fact]
  public void WitchPoisonCloudDamagesUnitsAtOwnerTurnStartAndDiesWithSource()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuGameState state = new(
      configuration,
      [
        new NetworkPiece("witch", nameof(PieceType.Witch), NetworkTeam.Red, 0, 0, 20),
        new NetworkPiece("victim", nameof(PieceType.Swordsman), NetworkTeam.Blue, 1, -1, 30),
        new NetworkPiece("blue-palace", nameof(PieceType.Palace), NetworkTeam.Blue, 5, -5, 230)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, nameof(PieceType.King)),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn, nameof(PieceType.Palace))
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    UseAbilityAction cloud = new(NetworkTeam.Red, "witch", "PoisonCloud", null, 0, -1);

    Assert.True(cloud.IsLegal(state));
    state = cloud.Apply(state);
    Assert.Single(state.AbilityEntities.Where(entity => entity.Kind == AbilityEntityKind.PoisonCloud));

    Assert.True(new EndTurnAction(NetworkTeam.Red).IsLegal(state));
    state = new EndTurnAction(NetworkTeam.Red).Apply(state);
    Assert.True(new EndTurnAction(NetworkTeam.Blue).IsLegal(state));
    state = new EndTurnAction(NetworkTeam.Blue).Apply(state);

    Assert.Equal(15, state.Pieces.Single(piece => piece.Id == "victim").Health);

    CpuGameState killSourceState = new(
      configuration,
      [
        new NetworkPiece("witch", nameof(PieceType.Witch), NetworkTeam.Red, 0, 0, 5),
        new NetworkPiece("attacker", nameof(PieceType.Swordsman), NetworkTeam.Blue, 0, 1, 30)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity(
          "cloud", AbilityEntityKind.PoisonCloud, NetworkTeam.Red, 0, -1, 0,
          SourcePieceId: "witch")
      ]
    );
    AttackAction kill = new(NetworkTeam.Blue, "attacker", "witch", 0, 0);
    Assert.True(kill.IsLegal(killSourceState));
    CpuGameState afterKill = kill.Apply(killSourceState);

    Assert.DoesNotContain(afterKill.Pieces, piece => piece.Id == "witch");
    Assert.DoesNotContain(afterKill.AbilityEntities, entity => entity.SourcePieceId == "witch");
  }

  [Fact]
  public void AbilityEntitiesBlockCpuMovementWithTeamAwareGates()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    NetworkPiece swordsman = new("red-swordsman", nameof(PieceType.Swordsman), NetworkTeam.Red, 0, 0, 30);
    CpuTeamState[] teams =
    [
      new(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
      new(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
    ];

    CpuGameState wallState = new(
      configuration,
      [swordsman],
      teams,
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity("wall", AbilityEntityKind.StoneWall, NetworkTeam.Red, 0, -1, 50)
      ]
    );
    Assert.False(new MoveAction(NetworkTeam.Red, swordsman.Id, 0, -2).IsLegal(wallState));

    CpuGameState friendlyGateState = new(
      configuration,
      [swordsman],
      teams,
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity("gate", AbilityEntityKind.Gatehouse, NetworkTeam.Red, 0, -1, 15)
      ]
    );
    Assert.True(new MoveAction(NetworkTeam.Red, swordsman.Id, 0, -2).IsLegal(friendlyGateState));

    CpuGameState enemyGateState = new(
      configuration,
      [swordsman],
      teams,
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      abilityEntities:
      [
        new AbilityEntity("gate", AbilityEntityKind.Gatehouse, NetworkTeam.Blue, 0, -1, 15)
      ]
    );
    Assert.False(new MoveAction(NetworkTeam.Red, swordsman.Id, 0, -2).IsLegal(enemyGateState));
  }

  private static CpuGameState CreateState(params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
  }

  private static NetworkMatchConfiguration CreateConfiguration(string gameMode = "Regicide") => new(
      "Small",
      "Light",
      "Light",
      gameMode,
      1234,
      200,
      0f,
      0f,
      2,
      1,
      15,
      FarmsEnabled: false
    );
}
