namespace MedivalChess.Player;

using System;
using System.Collections.Generic;

internal sealed class InitialBuyPhase
{
  private readonly Dictionary<TeamName, int> _buyTurnsUsed = new()
  {
    [TeamName.Red] = 0,
    [TeamName.Blue] = 0
  };
  private readonly HashSet<TeamName> _stoppedTeams = [];

  internal int PurchasesPerTurn { get; }
  internal int BuyTurnsPerTeam { get; }
  internal TeamName CurrentTeam { get; private set; } = TeamName.Red;
  internal int PurchasesThisTurn { get; private set; }
  internal bool IsComplete { get; private set; }

  internal InitialBuyPhase(int purchasesPerTurn, int buyTurnsPerTeam)
  {
    PurchasesPerTurn = Math.Max(1, purchasesPerTurn);
    BuyTurnsPerTeam = Math.Max(1, buyTurnsPerTeam);
  }

  internal int GetBuyTurnsUsed(TeamName team) => _buyTurnsUsed[team];

  internal bool HasStopped(TeamName team) => _stoppedTeams.Contains(team);

  internal void RecordPurchase()
  {
    if (IsComplete)
    {
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
    if (!IsComplete)
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
    if (!CanKeepBuying(TeamName.Red) && !CanKeepBuying(TeamName.Blue))
    {
      IsComplete = true;
      return;
    }

    TeamName otherTeam = CurrentTeam == TeamName.Red ? TeamName.Blue : TeamName.Red;
    if (CanKeepBuying(otherTeam))
    {
      CurrentTeam = otherTeam;
    }
  }

  private bool CanKeepBuying(TeamName team)
  {
    return !_stoppedTeams.Contains(team) && _buyTurnsUsed[team] < BuyTurnsPerTeam;
  }
}
