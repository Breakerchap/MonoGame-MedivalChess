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

public readonly record struct UnitUpkeepRequest(string UnitId, string UnitType);

public readonly record struct UnitUpkeepDecision(
  string UnitId,
  string UnitType,
  int Cost,
  bool Paid,
  UnpaidUnitUpkeepEffect UnpaidEffect
);

public sealed record UnitUpkeepSequenceResult(
  int RemainingMoney,
  IReadOnlyList<UnitUpkeepDecision> Decisions
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
  /// Resolves the mandatory per-owner-turn upkeep attached to one unit. Ordinary units return a
  /// zero-cost paid result. The runtime applies the returned unpaid effect to its own state.
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

  /// <summary>
  /// Resolves all ability upkeep for a team in a deterministic order. Royal-preserving President
  /// upkeep is paid before Mercenaries, so an optional mercenary wage can never consume the money
  /// needed to keep the President alive. Every runtime must use this sequence rather than choosing
  /// its own payment order.
  /// </summary>
  public static UnitUpkeepSequenceResult ResolveAbilityUpkeepSequence(
    int currentMoney,
    IEnumerable<UnitUpkeepRequest> units
  )
  {
    UnitUpkeepRequest[] ordered = units
      .Where(unit => unit.UnitType is nameof(PieceType.President) or nameof(PieceType.Mercenary))
      .OrderBy(unit => unit.UnitType == nameof(PieceType.President) ? 0 : 1)
      .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
      .ToArray();

    int money = currentMoney;
    List<UnitUpkeepDecision> decisions = [];
    foreach (UnitUpkeepRequest unit in ordered)
    {
      UnitUpkeepResult result = ResolveAbilityUpkeep(unit.UnitType, money);
      decisions.Add(new(
        unit.UnitId,
        unit.UnitType,
        result.Cost,
        result.Paid,
        result.UnpaidEffect
      ));
      if (result.Paid)
      {
        money = result.RemainingMoney;
      }
      if (result.UnpaidEffect == UnpaidUnitUpkeepEffect.LoseMatch)
      {
        // Once a mandatory Royal upkeep cannot be paid the match is lost; later optional wages
        // are irrelevant and must not produce different state in different runtimes.
        break;
      }
    }

    return new UnitUpkeepSequenceResult(money, decisions);
  }
}
