using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuPlanningEfficiencyTests
{
  [Fact]
  public void UnlimitedTurn_ReplansAfterCommittingAShortSegment()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0),
        Piece("red-knight", "Knight", NetworkTeam.Red, 0, 2),
        Piece("blue-soldier", "Soldier", NetworkTeam.Blue, 6, 0),
        Piece("blue-defender", "Defender", NetworkTeam.Blue, 6, 2));
      CpuProfile profile = new()
      {
        Name = "Receding horizon test",
        Difficulty = CpuDifficultyLevel.Hard,
        Search = new CpuSearchSettings
        {
          BeamWidth = 6,
          CandidatesPerNode = 8,
          PromisingCandidatesPerNode = 8,
          OpponentBeamWidth = 2,
          OpponentActionsToPredict = 0,
          TacticalExtensionDepth = 2,
          MaxSearchNodes = 50_000,
          MaximumPurchasePlacementCandidates = 12,
          MaxSearchMilliseconds = 1_200,
          MaxParallelism = 1,
          Randomness = 0f
        },
        TopChoicesForRandomSelection = 1,
        MistakeChance = 0f,
        StrategyVariationChance = 0f
      };

      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

      Assert.True(plan.Report.RecedingHorizonReplans >= 1, $"replans={plan.Report.RecedingHorizonReplans}");
      Assert.True(plan.Report.RecedingHorizonActionsCommitted >= 1,
        $"committed={plan.Report.RecedingHorizonActionsCommitted}");
      CpuGameState current = state;
      foreach (ICpuGameAction action in plan.Actions)
      {
        Assert.True(action.IsLegal(current), action.Describe());
        current = action.Apply(current);
        if (current.IsFinished) break;
      }
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void SearchPurchases_ClusterManyLegalSquaresIntoFewRepresentatives()
  {
    CpuGameState state = CreateState(
      money: 500,
      Piece("red", "Soldier", NetworkTeam.Red, 0, 0),
      Piece("blue", "Soldier", NetworkTeam.Blue, 8, 8));
    CpuActionGenerator generator = new();

    PurchaseAction[] exhaustive = generator.GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType == "Soldier")
      .ToArray();
    PurchaseAction[] search = generator.GenerateSearchActions(state, NetworkTeam.Red, 96)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType == "Soldier")
      .ToArray();

    Assert.True(exhaustive.Length > search.Length,
      $"exhaustive={exhaustive.Length}, search={search.Length}");
    Assert.InRange(search.Length, 1, 12);
    Assert.Equal(search.Length, search.Select(action => (action.X, action.Y)).Distinct().Count());
  }

  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return new NetworkPiece(id, type, team, x, y, rule.Health);
  }

  private static CpuGameState CreateState(int money, params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 11821, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, money, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, money, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain());
  }
}
