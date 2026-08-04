using System.Diagnostics;
using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuAdvancedTests
{
  [Fact]
  public void ThreatMap_ReportsImmediateLethalThreatAndThreatenedSquare()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );

    CpuThreatMap map = new CpuThreatMapBuilder().Build(state, NetworkTeam.Red);
    CpuPieceThreat threat = Assert.IsType<CpuPieceThreat>(map.GetThreat("blue-peasant"));

    Assert.True(threat.IsLethal);
    Assert.Equal("red-soldier", Assert.Single(threat.AttackerIds));
    Assert.Contains((0, -1), map.AttackedSquares);
  }

  [Fact]
  public void ScenarioRestrictions_BlockDisallowedPurchaseBeforeSimulation()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    (int x, int y) redSquare = BoardRules.GetBoard(configuration).Cells.First(position =>
      BoardRules.CanPlaceForTeam(configuration, NetworkTeam.Red, position.x, position.y, 1, 1));
    CpuScenarioDefinition scenario = new()
    {
      Restrictions = new CpuScenarioRestrictions
      {
        AllowedPurchases = new HashSet<string>(StringComparer.Ordinal) { "Peasant" }
      }
    };
    CpuGameState state = CreateState([], configuration, scenario: scenario);

    Assert.False(new PurchaseAction(NetworkTeam.Red, "Soldier", redSquare.x, redSquare.y).IsLegal(state));
    Assert.True(new PurchaseAction(NetworkTeam.Red, "Peasant", redSquare.x, redSquare.y).IsLegal(state));
  }

  [Fact]
  public void OpeningFarmSearch_IsBoundedAndPrioritisesForestCover()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration(farmsEnabled: true);
    Board board = BoardRules.GetBoard(configuration);
    UnitRule farm = UnitRules.GetRequired("Farm");
    (int x, int y) protectedPosition = board.Cells.First(position =>
      BoardRules.CanPlaceForTeam(configuration, NetworkTeam.Red, position.x, position.y, farm.Width, farm.Height));
    BattlefieldTerrain terrain = new(OccupiedSquares(protectedPosition, farm));
    NetworkInitialBuyState initialBuy = new(
      NetworkTeam.Red, 0, 1, 0, 0, 1, false, false, false,
      [
        new NetworkInitialBuyTeamState(NetworkTeam.Red, 0, false),
        new NetworkInitialBuyTeamState(NetworkTeam.Blue, 0, false)
      ],
      IsFarmPlacementPhase: true
    );
    CpuGameState state = CreateState([], configuration, terrain, initialBuy);

    Stopwatch stopwatch = Stopwatch.StartNew();
    IReadOnlyList<ICpuGameAction> actions = new CpuActionGenerator().GenerateSearchActions(state, NetworkTeam.Red, 4);
    stopwatch.Stop();

    Assert.InRange(actions.Count, 1, 4);
    Assert.All(actions, action => Assert.True(action.IsLegal(state), action.Describe()));
    PurchaseAction first = Assert.IsType<PurchaseAction>(actions[0]);
    Assert.Equal("Farm", first.UnitType);
    Assert.Equal(protectedPosition, (first.X, first.Y));
    Assert.True(stopwatch.ElapsedMilliseconds < 250);
  }

  [Fact]
  public void BeamSearch_FindsMoveThenAttackCombinationAgainstRoyalObjective()
  {
    CpuScenarioDefinition scenario = CpuScenarioDefinition.ForMatch(CreateConfiguration());
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 110),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -2, 5)
      ],
      scenario: scenario,
      redMoney: 0
    );
    CpuProfile profile = new()
    {
      Search = new CpuSearchSettings
      {
        BeamWidth = 16,
        CandidatesPerNode = 24,
        OpponentActionsToPredict = 0,
        MaxSearchMilliseconds = 2_000,
        Randomness = 0f
      },
      MistakeChance = 0f
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

    int moveIndex = plan.Actions.ToList().FindIndex(action => action is MoveAction { PieceId: "red-soldier" });
    int attackIndex = plan.Actions.ToList().FindIndex(action => action is AttackAction { TargetPieceId: "blue-king" });
    string planDescription = string.Join(" | ", plan.Actions.Select(action => action.Describe()));
    Assert.True(moveIndex >= 0, planDescription);
    Assert.True(attackIndex > moveIndex, planDescription);
  }

  [Fact]
  public void CancelledSearch_ReturnsDiagnosticWithoutIllegalAction()
  {
    CpuGameState state = CreateState(new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15));
    using CancellationTokenSource source = new();
    source.Cancel();

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Normal(22), source.Token);

    Assert.True(plan.Report.Cancelled);
    Assert.All(plan.Actions, action => Assert.True(action.IsLegal(state), action.Describe()));
  }

  [Fact]
  public void UnknownUnitType_DoesNotCauseGenerationOrSearchToThrow()
  {
    CpuGameState state = CreateState([new NetworkPiece("unknown", "FutureUnit", NetworkTeam.Red, 0, 0, 10)], redMoney: 0);

    IReadOnlyList<ICpuGameAction> legal = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red);
    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Easy(4), CancellationToken.None);

    Assert.Empty(legal);
    Assert.Empty(plan.Actions);
  }

  [Fact]
  public void HeadlessSimulator_RecordsBoundedCpuMatchMetricsAndCampaignProfile()
  {
    CpuProfile campaignProfile = new()
    {
      Name = "Campaign Test CPU",
      Search = new CpuSearchSettings
      {
        BeamWidth = 3,
        CandidatesPerNode = 5,
        OpponentActionsToPredict = 0,
        MaxSearchMilliseconds = 25
      }
    };
    CpuScenarioDefinition scenario = new()
    {
      Id = "headless-test",
      TeamProfiles = new Dictionary<NetworkTeam, CpuProfile> { [NetworkTeam.Red] = campaignProfile }
    };
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -3, 15)
      ],
      scenario: scenario
    );

    CpuMatchSimulationReport report = new CpuMatchSimulator().Run(new CpuMatchSimulationRequest
    {
      InitialState = state,
      MaximumTurns = 2
    });

    Assert.Equal("headless-test", report.ScenarioId);
    Assert.Equal("Campaign Test CPU", report.Profiles[NetworkTeam.Red]);
    Assert.InRange(report.TurnCount, 1, 2);
    Assert.Equal(report.TurnCount, report.Turns.Count);
    Assert.NotEmpty(report.EndReason);
  }

  [Fact]
  public void DebugFormatter_ExplainsTermsIntentionsAndChosenSequence()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Normal(9), CancellationToken.None);
    string text = CpuDebugFormatter.FormatDecision(plan.Report);

    Assert.Contains("searched", text, StringComparison.Ordinal);
    Assert.Contains("Personality:", text, StringComparison.Ordinal);
    Assert.Contains("Terms:", text, StringComparison.Ordinal);
    Assert.Contains("Threat=", text, StringComparison.Ordinal);
  }

  private static CpuGameState CreateState(params NetworkPiece[] pieces) => CreateState(pieces, null, null, null, null, 200);

  private static CpuGameState CreateState(
    NetworkPiece[] pieces,
    NetworkMatchConfiguration? configuration = null,
    BattlefieldTerrain? terrain = null,
    NetworkInitialBuyState? initialBuy = null,
    CpuScenarioDefinition? scenario = null,
    int redMoney = 200
  ) => new(
    configuration ?? CreateConfiguration(),
    pieces,
    [
      new CpuTeamState(NetworkTeam.Red, redMoney, MatchRules.ActionsPerTurn),
      new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
    ],
    initialBuy?.CurrentTeam ?? NetworkTeam.Red,
    terrain: terrain ?? new BattlefieldTerrain(),
    initialBuy: initialBuy,
    scenario: scenario
  );

  private static NetworkMatchConfiguration CreateConfiguration(bool farmsEnabled = false) => new(
    "Small", "Light", "Light", "Regicide", 1234, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: farmsEnabled
  );

  private static IEnumerable<(int x, int y)> OccupiedSquares((int x, int y) position, UnitRule rule)
  {
    for (int offsetY = 0; offsetY < rule.Height; offsetY++)
    for (int offsetX = 0; offsetX < rule.Width; offsetX++)
    {
      yield return (position.x + offsetX, position.y + offsetY);
    }
  }
}
