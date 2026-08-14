namespace MedivalChess.Shared;

public enum UnpaidUnitUpkeepEffect
{
  None,
  FireUnit,
  LoseMatch
}

public readonly record struct UnitUpkeepResult(
  int Cost,
  int RemainingMoney,
  bool Paid,
  UnpaidUnitUpkeepEffect UnpaidEffect
);

/// <summary>Shared price and upkeep calculations used by local, CPU and authoritative matches.</summary>
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

  /// <summary>
  /// Resolves the mandatory per-owner-turn upkeep attached to a unit ability. Ordinary units
  /// return a zero-cost paid result. The runtime applies the returned unpaid effect to its own state.
  /// </summary>
  public static UnitUpkeepResult ResolveAbilityUpkeep(string unitType, int currentMoney)
  {
    int cost;
    UnpaidUnitUpkeepEffect unpaidEffect;
    if (unitType == nameof(PieceType.Mercenary))
    {
      cost = AbilityRules.MercenaryPayroll;
      unpaidEffect = UnpaidUnitUpkeepEffect.FireUnit;
    }
    else if (unitType == nameof(PieceType.President))
    {
      cost = AbilityRules.PresidentPayroll;
      unpaidEffect = UnpaidUnitUpkeepEffect.LoseMatch;
    }
    else
    {
      return new UnitUpkeepResult(0, currentMoney, true, UnpaidUnitUpkeepEffect.None);
    }

    if (currentMoney < cost)
    {
      return new UnitUpkeepResult(cost, currentMoney, false, unpaidEffect);
    }

    return new UnitUpkeepResult(cost, (int)Math.Clamp((long)currentMoney - cost, int.MinValue, int.MaxValue), true, UnpaidUnitUpkeepEffect.None);
  }
}
