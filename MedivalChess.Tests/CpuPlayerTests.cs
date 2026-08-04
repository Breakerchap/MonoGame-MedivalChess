using System.Diagnostics;
using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuPlayerTests
{
  [Fact]
  public void Cpu_IncludesAnImmediateLethalAttackInItsTurn()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Normal(42), CancellationToken.None);

    AttackAction attack = Assert.Single(plan.Actions.OfType<AttackAction>());
    Assert.Equal("blue-peasant", attack.TargetPieceId);
    Assert.NotEmpty(plan.Report.TopChoices);
    Assert.True(plan.Report.NodesEvaluated > 0);
  }

  [Fact]
  public void Cpu_ReturnsOnlyActionsThatRemainLegalAsItsPlanIsApplied()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-archer", "Archer", NetworkTeam.Red, 1, 1, 10),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5),
      new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -3, 15)
    );

    Assert.NotEmpty(new CpuActionGenerator().GenerateSearchActions(state, NetworkTeam.Red, 48));

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Normal(73), CancellationToken.None);
    CpuGameState simulated = state;
    foreach (ICpuGameAction action in plan.Actions)
    {
      Assert.True(action.IsLegal(simulated), action.Describe());
      simulated = action.Apply(simulated);
    }
    Assert.InRange(plan.Actions.Count, 1, MatchRules.ActionsPerTurn);
  }

  [Fact]
  public void FixedSeed_ReproducesTheSameDecision()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );
    CpuProfile profile = CpuProfile.Easy(913);

    CpuTurnPlan first = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
    CpuTurnPlan second = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

    Assert.Equal(first.Actions.Select(action => action.Describe()), second.Actions.Select(action => action.Describe()));
  }

  [Fact]
  public void Search_ReportsAndRespectsAConservativeTimeLimit()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -3, 15)
    );
    CpuProfile profile = new()
    {
      Search = new CpuSearchSettings
      {
        BeamWidth = 4,
        CandidatesPerNode = 6,
        MaxSearchMilliseconds = 10,
        OpponentActionsToPredict = 0
      }
    };

    Stopwatch stopwatch = Stopwatch.StartNew();
    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
    stopwatch.Stop();

    Assert.True(plan.Report.TimedOut || plan.Report.SearchTime.TotalMilliseconds <= 100);
    Assert.True(stopwatch.ElapsedMilliseconds < 500);
  }

  [Fact]
  public void Cpu_DoesNotPlanActionsOutsideItsOwnTurn()
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Regicide", 1234, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false
    );
    CpuGameState state = new(
      configuration,
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15)],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Blue,
      terrain: new BattlefieldTerrain()
    );

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Easy(72), CancellationToken.None);

    Assert.Empty(plan.Actions);
  }

  private static CpuGameState CreateState(params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Regicide", 1234, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false
    );
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
  }
}
