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
}
