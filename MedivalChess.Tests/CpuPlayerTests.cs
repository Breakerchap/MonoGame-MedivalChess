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
  public void Cpu_FinishesADamagedEnemyBeforeSpreadingNonlethalDamage()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-knight", "Knight", NetworkTeam.Red, 0, 0, 30),
      new NetworkPiece("blue-damaged-soldier", "Soldier", NetworkTeam.Blue, 0, -1, 10),
      new NetworkPiece("blue-healthy-knight", "Knight", NetworkTeam.Blue, 1, 1, 30)
    );
    CpuProfile profile = new()
    {
      RandomSeed = 82,
      MistakeChance = 0f,
      Search = new CpuSearchSettings
      {
        BeamWidth = 8,
        CandidatesPerNode = 12,
        OpponentActionsToPredict = 0,
        MaxSearchNodes = 300,
        MaxSearchMilliseconds = 1_000,
        Randomness = 0f
      }
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
    AttackAction firstAttack = Assert.IsType<AttackAction>(plan.Actions.First());

    Assert.Equal("blue-damaged-soldier", firstAttack.TargetPieceId);
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

  [Fact]
  public void Cpu_DiscardsAnIllegalCandidateBeforeItCanBeSimulatedOrReturned()
  {
    CpuGameState state = CreateState(new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15));
    IllegalCandidateSelector selector = new();

    CpuTurnPlan plan = new CpuPlayer(candidateSelector: selector).ChooseTurn(
      state, NetworkTeam.Red, CpuProfile.Easy(85), CancellationToken.None);

    Assert.True(selector.Calls > 0);
    Assert.Equal(0, plan.Report.NodesGenerated);
    Assert.Empty(plan.Actions);
  }

  [Fact]
  public void Cpu_HandlesAStateWithNoAvailableAction()
  {
    CpuGameState state = CreateState(new NetworkPiece("red-farm", "Farm", NetworkTeam.Red, 0, 0, 30));
    CpuGameState noMoney = new(
      state.Configuration,
      state.Pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: state.Terrain
    );

    Assert.Empty(new CpuActionGenerator().GenerateLegalActions(noMoney, NetworkTeam.Red));
    Assert.Empty(new CpuPlayer().ChooseTurn(noMoney, NetworkTeam.Red, CpuProfile.Easy(83), CancellationToken.None).Actions);
  }

  [Fact]
  public void DifficultyProfiles_UseIncreasingBoundedSearchQuality()
  {
    CpuProfile easy = CpuProfile.Easy(84);
    CpuProfile normal = CpuProfile.Normal(84);
    CpuProfile hard = CpuProfile.Hard(84);

    Assert.True(easy.Search.BeamWidth < normal.Search.BeamWidth && normal.Search.BeamWidth < hard.Search.BeamWidth);
    Assert.True(easy.Search.CandidatesPerNode < normal.Search.CandidatesPerNode &&
      normal.Search.CandidatesPerNode < hard.Search.CandidatesPerNode);
    Assert.True(easy.Search.MaxSearchNodes < normal.Search.MaxSearchNodes &&
      normal.Search.MaxSearchNodes < hard.Search.MaxSearchNodes);
    Assert.Equal(0, easy.Search.OpponentActionsToPredict);
    Assert.Equal(1, normal.Search.OpponentActionsToPredict);
    Assert.Equal(MatchRules.ActionsPerTurn, hard.Search.OpponentActionsToPredict);
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

  private sealed class IllegalCandidateSelector : IActionCandidateSelector
  {
    public int Calls { get; private set; }

    public IReadOnlyList<ScoredAction> SelectCandidates(
      CpuGameState state,
      NetworkTeam team,
      IReadOnlyList<ICpuGameAction> legalActions,
      CpuSearchSettings settings,
      CpuPersonality? personality = null
    )
    {
      Calls++;
      return [new ScoredAction(new MoveAction(team, "missing-piece", 99, 99), 10_000f, "Injected illegal action")];
    }
  }
}
