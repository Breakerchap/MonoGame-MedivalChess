namespace MedivalChess.Player;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class InitialBuyPhase
{
  private readonly Dictionary<TeamName, int> _buyTurnsUsed = [];
  private readonly Dictionary<TeamName, int> _farmsPlaced = [];
  private readonly HashSet<TeamName> _stoppedTeams = [];
  private readonly IReadOnlyList<TeamName> _teams;

  internal int PurchasesPerTurn { get; }
  internal int BuyTurnsPerTeam { get; }
  internal TeamName CurrentTeam { get; private set; } = TeamName.Red;
  internal int PurchasesThisTurn { get; private set; }
  internal bool IsComplete { get; private set; }
  internal bool IsFarmPlacementPhase { get; private set; }
  internal bool CanStopCurrentBuyer => !IsFarmPlacementPhase;

  internal InitialBuyPhase(
    int purchasesPerTurn,
    int buyTurnsPerTeam,
    IEnumerable<TeamName> teams = null,
    bool farmsEnabled = false
  )
  {
    PurchasesPerTurn = Math.Max(1, purchasesPerTurn);
    BuyTurnsPerTeam = Math.Max(1, buyTurnsPerTeam);
    _teams = (teams ?? Team.ActiveTeams).Distinct().ToArray();
    if (_teams.Count is < 2 or > 4)
    {
      throw new ArgumentOutOfRangeException(nameof(teams), "A buy phase needs two to four teams.");
    }

    foreach (TeamName team in _teams)
    {
      _buyTurnsUsed[team] = 0;
      _farmsPlaced[team] = 0;
    }
    CurrentTeam = _teams[0];
    IsFarmPlacementPhase = farmsEnabled;
  }

  internal InitialBuyPhase(
    int purchasesPerTurn,
    int buyTurnsPerTeam,
    TeamName currentTeam,
    int purchasesThisTurn,
    int redBuyTurnsUsed,
    int blueBuyTurnsUsed,
    bool redStopped,
    bool blueStopped,
    bool isComplete
  ) : this(purchasesPerTurn, buyTurnsPerTeam, [TeamName.Red, TeamName.Blue])
  {
    CurrentTeam = currentTeam;
    PurchasesThisTurn = purchasesThisTurn;
    _buyTurnsUsed[TeamName.Red] = redBuyTurnsUsed;
    _buyTurnsUsed[TeamName.Blue] = blueBuyTurnsUsed;
    if (redStopped) _stoppedTeams.Add(TeamName.Red);
    if (blueStopped) _stoppedTeams.Add(TeamName.Blue);
    IsComplete = isComplete;
  }

  internal InitialBuyPhase(
    int purchasesPerTurn,
    int buyTurnsPerTeam,
    TeamName currentTeam,
    int purchasesThisTurn,
    IReadOnlyDictionary<TeamName, (int buyTurnsUsed, bool stopped, int farmsPlaced)> teamStates,
    bool isComplete,
    bool isFarmPlacementPhase = false
  ) : this(purchasesPerTurn, buyTurnsPerTeam, teamStates.Keys)
  {
    CurrentTeam = currentTeam;
    PurchasesThisTurn = purchasesThisTurn;
    foreach ((TeamName team, (int buyTurnsUsed, bool stopped, int farmsPlaced) state) in teamStates)
    {
      _buyTurnsUsed[team] = state.buyTurnsUsed;
      _farmsPlaced[team] = state.farmsPlaced;
      if (state.stopped) _stoppedTeams.Add(team);
    }
    IsComplete = isComplete;
    IsFarmPlacementPhase = isFarmPlacementPhase;
  }

  internal int GetBuyTurnsUsed(TeamName team) => _buyTurnsUsed.TryGetValue(team, out int value) ? value : 0;

  internal bool HasStopped(TeamName team) => _stoppedTeams.Contains(team);
  internal int GetFarmsPlaced(TeamName team) => _farmsPlaced.TryGetValue(team, out int value) ? value : 0;

  internal void RecordPurchase()
  {
    if (IsComplete)
    {
      return;
    }

    if (IsFarmPlacementPhase)
    {
      _farmsPlaced[CurrentTeam]++;
      if (_farmsPlaced[CurrentTeam] >= 2)
      {
        FinishCurrentFarmPlacement();
      }
      return;
    }

    PurchasesThisTurn++;
    if (PurchasesThisTurn >= PurchasesPerTurn)
    {
      FinishCurrentBuyTurn(false);
    }
  }

  internal void StopCurrentBuyer()
  {
    if (!IsComplete && !IsFarmPlacementPhase)
    {
      FinishCurrentBuyTurn(true);
    }
  }

  private void FinishCurrentBuyTurn(bool stoppedBuying)
  {
    if (stoppedBuying)
    {
      _stoppedTeams.Add(CurrentTeam);
    }
    else
    {
      _buyTurnsUsed[CurrentTeam]++;
    }

    PurchasesThisTurn = 0;
    if (_teams.All(team => !CanKeepBuying(team)))
    {
      IsComplete = true;
      return;
    }

    int currentIndex = 0;
    for (int index = 0; index < _teams.Count; index++)
    {
      if (_teams[index] == CurrentTeam)
      {
        currentIndex = index;
        break;
      }
    }
    for (int offset = 1; offset <= _teams.Count; offset++)
    {
      TeamName nextTeam = _teams[(currentIndex + offset) % _teams.Count];
      if (CanKeepBuying(nextTeam))
      {
        CurrentTeam = nextTeam;
        return;
      }
    }
  }

  private void FinishCurrentFarmPlacement()
  {
    PurchasesThisTurn = 0;
    if (_teams.All(team => GetFarmsPlaced(team) >= 2))
    {
      IsFarmPlacementPhase = false;
      CurrentTeam = _teams[0];
      return;
    }

    int currentIndex = 0;
    for (int index = 0; index < _teams.Count; index++)
    {
      if (_teams[index] == CurrentTeam)
      {
        currentIndex = index;
        break;
      }
    }
    for (int offset = 1; offset <= _teams.Count; offset++)
    {
      TeamName nextTeam = _teams[(currentIndex + offset) % _teams.Count];
      if (GetFarmsPlaced(nextTeam) < 2)
      {
        CurrentTeam = nextTeam;
        return;
      }
    }
  }

  private bool CanKeepBuying(TeamName team)
  {
    return !_stoppedTeams.Contains(team) && _buyTurnsUsed.TryGetValue(team, out int turnsUsed) && turnsUsed < BuyTurnsPerTeam;
  }
}
