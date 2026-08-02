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
  public void MatchConfiguration_EnablesFiveGoldFarmsAndDisablesInterestByDefault()
  {
    NetworkMatchConfiguration configuration = new(
      "Medium", "Standard", "Standard", "Regicide", 1, 300, 0.5f, 0f, 2, 4, 15
    );

    Assert.True(configuration.FarmsEnabled);
    Assert.Equal(5, configuration.FarmIncomePerTurn);
    Assert.False(configuration.InterestEnabled);
    Assert.Equal(0, configuration.InterestPercent);
  }
}
