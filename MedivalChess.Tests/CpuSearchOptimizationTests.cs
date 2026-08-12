using System.Threading;
using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuSearchOptimizationTests
{
  [Fact]
  public void TacticalMacroAction_AppliesMoveThenAttackAsOrdinaryActions()
  {
    NetworkPiece soldier = Piece("soldier", "Soldier", NetworkTeam.Red, 0, 0);
    NetworkPiece peasant = Piece("peasant", "Peasant", NetworkTeam.Blue, 2, 0);
    CpuGameState state = CreateState(soldier, peasant);
    MoveAction move = new(NetworkTeam.Red, soldier.Id, 1, 0);
    AttackAction attack = new(NetworkTeam.Red, soldier.Id, peasant.Id, peasant.X, peasant.Y);
    TacticalMacroAction macro = new(NetworkTeam.Red, [move, attack]);

    Assert.True(move.IsLegal(state));
    Assert.True(macro.IsLegal(state));

    CpuGameState result = macro.Apply(state);

    Assert.DoesNotContain(result.Pieces, piece => piece.Id == peasant.Id);
    NetworkPiece moved = Assert.Single(result.Pieces.Where(piece => piece.Id == soldier.Id));
    Assert.Equal((1, 0), (moved.X, moved.Y));
    Assert.True(moved.HasMovedThisTurn);
    Assert.True(moved.HasAttackedThisTurn);
  }

  [Fact]
  public void CpuSearch_UsesIterativeDeepeningMacrosAndCandidateCache()
  {
    NetworkPiece soldier = Piece("soldier", "Soldier", NetworkTeam.Red, 0, 0);
    NetworkPiece peasant = Piece("peasant", "Peasant", NetworkTeam.Blue, 2, 0);
    CpuGameState state = CreateState(soldier, peasant);
    CpuProfile profile = new()
    {
      Name = "Optimisation test",
      Difficulty = CpuDifficultyLevel.Hard,
      Search = new CpuSearchSettings
      {
        BeamWidth = 8,
        CandidatesPerNode = 8,
        PromisingCandidatesPerNode = 8,
        OpponentBeamWidth = 1,
        OpponentActionsToPredict = 0,
        TacticalExtensionDepth = 0,
        MaxSearchNodes = 20_000,
        MaximumPurchasePlacementCandidates = 4,
        MaxSearchMilliseconds = 2_000,
        MaxParallelism = 1,
        Randomness = 0f
      },
      TopChoicesForRandomSelection = 1,
      MistakeChance = 0f,
      StrategyVariationChance = 0f
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

    Assert.True(plan.Report.IterativeDeepeningPasses >= 2, $"passes={plan.Report.IterativeDeepeningPasses}");
    Assert.True(plan.Report.CompletedSearchDepth >= 2, $"depth={plan.Report.CompletedSearchDepth}");
    Assert.True(plan.Report.CandidateCacheHits > 0, $"candidate cache hits={plan.Report.CandidateCacheHits}");
    Assert.True(plan.Report.TacticalMacrosGenerated > 0, $"macros={plan.Report.TacticalMacrosGenerated}");
    Assert.DoesNotContain(plan.Actions, action => action is TacticalMacroAction);
  }

  [Fact]
  public void EvaluationCache_SharesOneThreatMapAcrossParallelWorkers()
  {
    CpuGameState state = CreateState(
      Piece("red", "Soldier", NetworkTeam.Red, 0, 0),
      Piece("blue", "Peasant", NetworkTeam.Blue, 1, 0));
    CpuEvaluationCache cache = new();
    CountingThreatMapBuilder builder = new();

    Parallel.For(0, 24, _ =>
    {
      _ = cache.GetThreatMap(state, NetworkTeam.Red, builder);
    });

    Assert.Equal(1, builder.BuildCount);
  }

  private sealed class CountingThreatMapBuilder : ICpuThreatMapBuilder
  {
    private readonly CpuThreatMapBuilder _inner = new();
    private int _buildCount;
    public int BuildCount => _buildCount;

    public CpuThreatMap Build(CpuGameState state, NetworkTeam attackingTeam)
    {
      Interlocked.Increment(ref _buildCount);
      Thread.Sleep(5);
      return _inner.Build(state, attackingTeam);
    }
  }

  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return new NetworkPiece(id, type, team, x, y, rule.Health);
  }

  private static CpuGameState CreateState(params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 7812, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain());
  }
}
