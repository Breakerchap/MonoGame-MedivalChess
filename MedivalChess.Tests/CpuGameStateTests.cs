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
    Assert.Equal(2, simulated.ActionsRemaining);
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
  public void EndTurn_AppliesTheExistingThreeActionTurnAccounting()
  {
    CpuGameState state = CreateState(new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15));
    MoveAction move = new(NetworkTeam.Red, "red-soldier", 0, 1);

    Assert.True(move.IsLegal(state));
    CpuGameState afterMove = move.Apply(state);
    EndTurnAction endTurn = new(NetworkTeam.Red);

    Assert.Equal(2, afterMove.ActionsRemaining);
    Assert.True(endTurn.IsLegal(afterMove));
    CpuGameState afterEnd = endTurn.Apply(afterMove);

    Assert.Equal(NetworkTeam.Blue, afterEnd.CurrentTurn);
    Assert.Equal(MatchRules.ActionsPerTurn, afterEnd.ActionsRemaining);
    Assert.True(afterEnd.Pieces.Single(piece => piece.Id == "red-soldier").HasMovedThisTurn);
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
