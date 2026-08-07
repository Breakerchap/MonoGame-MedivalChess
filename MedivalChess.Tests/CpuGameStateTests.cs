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
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
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
  public void CloneAndSimulatedMovement_LeaveTheAuthoritativeSnapshotUntouched()
  {
    CpuGameState original = CreateState(new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15));
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
  public void CavalierAttack_RefreshesMovementForTheRestOfItsTurn()
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
    Assert.False(cavalier.HasMovedThisTurn);
    Assert.True(new MoveAction(NetworkTeam.Red, cavalier.Id, 0, 1).IsLegal(afterAttack));
  }

  [Fact]
  public void AttackingAnOx_AlsoDamagesItsCarriedUnit()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-ox", "Ox", NetworkTeam.Blue, 0, -1, 25),
      new NetworkPiece(
        "blue-cargo", "Knight", NetworkTeam.Blue, 0, -1, 30,
        AttachedToId: "blue-ox", AttachmentKind: NetworkAttachmentKind.Carried
      )
    );
    AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-ox", 0, -1);

    Assert.True(attack.IsLegal(state));
    CpuGameState afterAttack = attack.Apply(state);

    Assert.Equal(15, afterAttack.Pieces.Single(piece => piece.Id == "blue-ox").Health);
    Assert.Equal(20, afterAttack.Pieces.Single(piece => piece.Id == "blue-cargo").Health);
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
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 2, 0, 30)
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
    Assert.Equal(15, soldier.Health);
    Assert.True(UnitRules.FootprintsOverlap(elephant.X, elephant.Y, 2, 2, soldier.X, soldier.Y, 1, 1));
  }

  [Fact]
  public void CampaignCpuSnapshotUsesTheSuppliedCustomBoardGeometry()
  {
    Board campaignBoard = new(new[] { (0, 0) });
    CpuGameState state = new(
      CreateConfiguration(),
      [new NetworkPiece("campaign-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15)],
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
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
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
    CpuGameState state = CreateState(new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15));
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
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 1, 15),
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
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15)],
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
  public void EngineerCanDemolishAScenarioProvidedRiverBridge()
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

    Assert.True(demolish.IsLegal(state));
    Assert.Contains(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red), action => action.Equals(demolish));

    CpuGameState afterDemolish = demolish.Apply(state);

    Assert.DoesNotContain(bridge, afterDemolish.RiverBridges);
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
    NetworkPiece soldier = new("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15);
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
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
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
