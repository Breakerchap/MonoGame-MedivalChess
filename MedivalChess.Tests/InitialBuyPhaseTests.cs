using MedivalChess.Player;
using Xunit;

namespace MedivalChess.Tests;

public class InitialBuyPhaseTests
{
  [Fact]
  public void TwoPurchases_AdvanceToTheOtherTeamsBuyTurn()
  {
    InitialBuyPhase phase = new(2, 4);

    phase.RecordPurchase();
    Assert.Equal(TeamName.Red, phase.CurrentTeam);
    Assert.Equal(1, phase.PurchasesThisTurn);

    phase.RecordPurchase();

    Assert.Equal(1, phase.GetBuyTurnsUsed(TeamName.Red));
    Assert.Equal(TeamName.Blue, phase.CurrentTeam);
    Assert.Equal(0, phase.PurchasesThisTurn);
  }

  [Fact]
  public void StoppedBuyer_LetsTheOtherTeamFinishItsRemainingBuyTurns()
  {
    InitialBuyPhase phase = new(2, 4);

    phase.StopCurrentBuyer();

    Assert.True(phase.HasStopped(TeamName.Red));
    Assert.Equal(TeamName.Blue, phase.CurrentTeam);

    for (int buyTurn = 0; buyTurn < 4; buyTurn++)
    {
      phase.RecordPurchase();
      phase.RecordPurchase();
    }

    Assert.True(phase.IsComplete);
    Assert.Equal(4, phase.GetBuyTurnsUsed(TeamName.Blue));
  }

  [Fact]
  public void BothBuyersCanStopBeforeUsingAllBuyTurns()
  {
    InitialBuyPhase phase = new(2, 4);

    phase.StopCurrentBuyer();
    phase.StopCurrentBuyer();

    Assert.True(phase.IsComplete);
  }

  [Fact]
  public void FarmOpeningRequiresTwoFreePlacementsPerTeamBeforeNormalBuying()
  {
    InitialBuyPhase phase = new(2, 4, farmsEnabled: true);

    Assert.True(phase.IsFarmPlacementPhase);
    Assert.False(phase.CanStopCurrentBuyer);
    phase.RecordPurchase();
    Assert.Equal(1, phase.GetFarmsPlaced(TeamName.Red));
    Assert.Equal(TeamName.Red, phase.CurrentTeam);
    phase.RecordPurchase();
    Assert.Equal(TeamName.Blue, phase.CurrentTeam);

    phase.RecordPurchase();
    phase.RecordPurchase();

    Assert.False(phase.IsFarmPlacementPhase);
    Assert.Equal(TeamName.Red, phase.CurrentTeam);
    Assert.Equal(0, phase.PurchasesThisTurn);
    Assert.True(phase.CanStopCurrentBuyer);
  }
}
