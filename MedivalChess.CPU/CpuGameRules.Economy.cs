using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Currency, stable identifiers, and initial-purchase flow for CPU simulation.</summary>
public static partial class CpuGameRules
{
  private static int GetUnitPrice(NetworkMatchConfiguration configuration, UnitRule rule) => rule.Type == "Farm"
    ? rule.Cost
    : EconomyRules.GetUnitPrice(rule.Cost, configuration.UnitPricePercent);

  private static int GetUnitMaintenance(NetworkMatchConfiguration configuration, UnitRule rule) => rule.Type == "Farm"
    ? 0
    : EconomyRules.GetUnitMaintenance(rule.Cost, configuration.UnitMaintenancePercent);

  private static void SpendMoney(CpuMutableGameState state, NetworkTeam team, int amount) => AddMoney(state, team, -amount);

  private static void AddMoney(CpuMutableGameState state, NetworkTeam team, int amount)
  {
    if (state.Teams.TryGetValue(team, out CpuTeamState? existing))
    {
      state.Teams[team] = existing with { Money = ClampCurrency((long)existing.Money + amount) };
    }
  }

  private static int ClampCurrency(long amount) => (int)Math.Clamp(amount, int.MinValue, int.MaxValue);

  private static NetworkPiece? FindPiece(IEnumerable<NetworkPiece> pieces, string id) =>
    pieces.FirstOrDefault(piece => string.Equals(piece.Id, id, StringComparison.Ordinal));

  private static int FindPieceIndex(IReadOnlyList<NetworkPiece> pieces, string id)
  {
    for (int index = 0; index < pieces.Count; index++)
    {
      if (string.Equals(pieces[index].Id, id, StringComparison.Ordinal))
      {
        return index;
      }
    }
    return -1;
  }

  private static string CreatePieceId(CpuMutableGameState state, string type)
  {
    int suffix = 1;
    string id;
    do
    {
      id = $"cpu-{type.ToLowerInvariant()}-{state.TurnNumber}-{suffix++}";
    } while (state.Pieces.Any(piece => piece.Id == id));
    return id;
  }

  private static bool IsCurrentInitialBuyer(NetworkInitialBuyState initialBuy, NetworkTeam team) => initialBuy.CurrentTeam == team;

  private static void RecordInitialPurchase(CpuMutableGameState state, NetworkTeam team)
  {
    NetworkInitialBuyState current = state.InitialBuy!;
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records = GetInitialBuyRecords(current, state.Source.Configuration.PlayerCount);
    if (current.IsFarmPlacementPhase)
    {
      (int farmTurnsUsed, bool farmStopped, int farmCount) = records[team];
      records[team] = (farmTurnsUsed, farmStopped, farmCount + 1);
      bool farmsDone = records.Values.All(value => value.farmsPlaced >= 2);
      if (farmsDone)
      {
        state.InitialBuy = BuildInitialBuyState(state, records, TeamRules.GetFirstTeam(state.Source.Configuration.PlayerCount), 0, false, false);
        state.CurrentTurn = state.InitialBuy.CurrentTeam;
      }
      else
      {
        AdvanceInitialBuyer(state, records, 0, true, requireFarms: true);
      }
      return;
    }

    int purchases = current.PurchasesThisTurn + 1;
    if (purchases < current.PurchasesPerTurn)
    {
      state.InitialBuy = BuildInitialBuyState(state, records, team, purchases, false, false);
      state.CurrentTurn = team;
      return;
    }

    (int turnsUsed, bool stopped, int farmsPlaced) = records[team];
    records[team] = (turnsUsed + 1, stopped, farmsPlaced);
    AdvanceInitialBuyer(state, records, 0, false);
  }

  private static void AdvanceInitialBuyer(
    CpuMutableGameState state,
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records,
    int purchasesThisTurn,
    bool farmPlacement,
    bool requireFarms = false
  )
  {
    IReadOnlyList<NetworkTeam> teams = TeamRules.GetActiveTeams(state.Source.Configuration.PlayerCount);
    bool canContinue(NetworkTeam team) => requireFarms
      ? records[team].farmsPlaced < 2
      : !records[team].stopped && records[team].turnsUsed < state.InitialBuy!.BuyTurnsPerTeam;
    if (teams.All(team => !canContinue(team)))
    {
      state.InitialBuy = BuildInitialBuyState(state, records, TeamRules.GetFirstTeam(state.Source.Configuration.PlayerCount), 0, farmPlacement, true);
      state.CurrentTurn = state.InitialBuy.CurrentTeam;
      if (!farmPlacement)
      {
        state.InitialBuy = null;
        state.CurrentTurn = TeamRules.GetFirstTeam(state.Source.Configuration.PlayerCount);
        foreach (NetworkTeam activeTeam in teams)
        {
          state.Teams[activeTeam] = state.Teams[activeTeam] with { ActionsRemaining = state.Teams[activeTeam].ActionLimit };
          ResetTurnActions(state, activeTeam);
        }
        ApplyTurnEconomy(state, state.CurrentTurn);
      }
      return;
    }

    int currentIndex = Array.IndexOf(teams.ToArray(), state.InitialBuy!.CurrentTeam);
    for (int offset = 1; offset <= teams.Count; offset++)
    {
      NetworkTeam next = teams[(currentIndex + offset) % teams.Count];
      if (canContinue(next))
      {
        state.InitialBuy = BuildInitialBuyState(state, records, next, purchasesThisTurn, farmPlacement, false);
        state.CurrentTurn = next;
        return;
      }
    }
  }

  private static Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> GetInitialBuyRecords(
    NetworkInitialBuyState initialBuy,
    int playerCount
  )
  {
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> result = TeamRules.GetActiveTeams(playerCount)
      .ToDictionary(team => team, _ => (0, false, 0));
    if (initialBuy.TeamStates is not null)
    {
      foreach (NetworkInitialBuyTeamState entry in initialBuy.TeamStates)
      {
        result[entry.Team] = (entry.BuyTurnsUsed, entry.Stopped, entry.FarmsPlaced);
      }
    }
    else
    {
      result[NetworkTeam.Red] = (initialBuy.RedBuyTurnsUsed, initialBuy.RedStopped, 0);
      result[NetworkTeam.Blue] = (initialBuy.BlueBuyTurnsUsed, initialBuy.BlueStopped, 0);
    }
    return result;
  }

  private static NetworkInitialBuyState BuildInitialBuyState(
    CpuMutableGameState state,
    IReadOnlyDictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records,
    NetworkTeam currentTeam,
    int purchasesThisTurn,
    bool farmPlacement,
    bool complete
  ) => new(
    currentTeam,
    purchasesThisTurn,
    state.InitialBuy!.PurchasesPerTurn,
    records.GetValueOrDefault(NetworkTeam.Red).turnsUsed,
    records.GetValueOrDefault(NetworkTeam.Blue).turnsUsed,
    state.InitialBuy.BuyTurnsPerTeam,
    records.GetValueOrDefault(NetworkTeam.Red).stopped,
    records.GetValueOrDefault(NetworkTeam.Blue).stopped,
    complete,
    records.Select(pair => new NetworkInitialBuyTeamState(pair.Key, pair.Value.turnsUsed, pair.Value.stopped, pair.Value.farmsPlaced)).ToArray(),
    farmPlacement
  );
}
