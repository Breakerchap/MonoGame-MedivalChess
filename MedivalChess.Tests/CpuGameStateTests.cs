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

  private static NetworkMatchConfiguration CreateConfiguration() => new(
      "Small",
      "Light",
      "Light",
      "Regicide",
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
