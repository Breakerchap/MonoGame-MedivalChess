using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class EconomyRulesTests
{
  [Theory]
  [InlineData(60, 100, 60)]
  [InlineData(35, 150, 53)]
  [InlineData(20, -100, -20)]
  [InlineData(0, 500, 0)]
  public void UnitPrice_UsesTheConfiguredPercentage(int baseCost, int pricePercent, int expected)
  {
    Assert.Equal(expected, EconomyRules.GetUnitPrice(baseCost, pricePercent));
  }

  [Theory]
  [InlineData(35, 10, 4)]
  [InlineData(60, 10, 6)]
  [InlineData(60, 0, 0)]
  [InlineData(-60, 10, 0)]
  public void UnitMaintenance_IsRoundedUpFromTheNonNegativeStandardCost(
    int baseCost,
    int maintenancePercent,
    int expected
  )
  {
    Assert.Equal(expected, EconomyRules.GetUnitMaintenance(baseCost, maintenancePercent));
  }

  [Theory]
  [InlineData(100, -100, -100)]
  [InlineData(100, 0, 0)]
  [InlineData(100, 200, 200)]
  [InlineData(-100, -100, 100)]
  public void Interest_UsesTheConfiguredPercentage(int balance, int interestPercent, int expected)
  {
    Assert.Equal(expected, EconomyRules.GetInterest(balance, interestPercent));
  }

  [Fact]
  public void MatchConfiguration_UsesTheSharedEconomyDefaults()
  {
    NetworkMatchConfiguration configuration = new(
      "Medium", "Standard", "Standard", "Regicide", 1,
      Globals.StartingCash,
      Globals.KillerDeathRefundMultiplier,
      Globals.DefeatedTeamDeathRefundMultiplier,
      Globals.InitialBuysPerTurn,
      Globals.InitialBuyTurnsPerTeam,
      15
    );

    Assert.Equal(Globals.FarmsEnabled, configuration.FarmsEnabled);
    Assert.Equal(Globals.FarmIncomePerTurn, configuration.FarmIncomePerTurn);
    Assert.Equal(Globals.UnitMaintenanceEnabled, configuration.UnitMaintenanceEnabled);
    Assert.Equal(Globals.UnitMaintenancePercent, configuration.UnitMaintenancePercent);
    Assert.Equal(Globals.UnitPricePercent, configuration.UnitPricePercent);
    Assert.Equal(Globals.InterestEnabled, configuration.InterestEnabled);
    Assert.Equal(Globals.InterestPercent, configuration.InterestPercent);
    Assert.Equal(Globals.DefaultDominionWinScore, configuration.DominionWinScore);
    Assert.Equal(Globals.DefaultPlunderWinScore, configuration.PlunderWinScore);
    Assert.Equal(Globals.DefaultPlunderDeliveryScore, configuration.PlunderDeliveryScore);
  }
}
