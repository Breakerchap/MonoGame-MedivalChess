namespace MedivalChess.Shared;

/// <summary>Shared price and upkeep calculations used by local and authoritative matches.</summary>
public static class EconomyRules
{
  public static int GetUnitPrice(int baseCost, int pricePercent)
  {
    double amount = Math.Round(baseCost * (pricePercent / 100d), MidpointRounding.AwayFromZero);
    return (int)Math.Clamp(amount, int.MinValue, int.MaxValue);
  }

  public static int GetUnitMaintenance(int baseCost, int maintenancePercent)
  {
    if (maintenancePercent <= 0)
    {
      return 0;
    }

    int standardCost = Math.Max(0, baseCost);
    return (int)Math.Ceiling(standardCost * (maintenancePercent / 100d));
  }

  public static int GetInterest(int balance, int interestPercent)
  {
    long amount = (long)Math.Round(balance * (interestPercent / 100d), MidpointRounding.AwayFromZero);
    return (int)Math.Clamp(amount, int.MinValue, int.MaxValue);
  }
}
