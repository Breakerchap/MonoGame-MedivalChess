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
    Assert.All(actions.OfType<PurchaseAction>(), action => Assert.Equal("Farm", action.UnitType));
    PurchaseAction first = Assert.IsType<PurchaseAction>(actions[0]);
    Assert.Equal("Farm", first.UnitType);
    Assert.Equal(protectedPosition, (first.X, first.Y));
    Assert.True(stopwatch.ElapsedMilliseconds < 250);
  }

  [Fact]
  public void OpeningFarmCpu_RemainsFastAndFindsTheSecondFarmAfterOccupiedTopSquares()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration(farmsEnabled: true);
    NetworkInitialBuyState initialBuy = new(
      NetworkTeam.Red, 0, 1, 0, 0, 1, false, false, false,
      [
        new NetworkInitialBuyTeamState(NetworkTeam.Red, 0, false),
        new NetworkInitialBuyTeamState(NetworkTeam.Blue, 0, false)
      ],
      IsFarmPlacementPhase: true
    );
    CpuGameState state = CreateState([], configuration, new BattlefieldTerrain(), initialBuy);
    CpuPlayer player = new();
    CpuProfile profile = CpuProfile.Normal(91);

    for (int placement = 0; placement < 3; placement++)
    {
      NetworkTeam expectedTeam = placement == 1 ? NetworkTeam.Blue : NetworkTeam.Red;
      CpuTurnPlan plan = player.ChooseTurn(state, expectedTeam, profile, CancellationToken.None);
      PurchaseAction farm = Assert.Single(plan.Actions.OfType<PurchaseAction>());

      Assert.Equal(expectedTeam, farm.Team);
      Assert.Equal("Farm", farm.UnitType);
      Assert.True(farm.IsLegal(state), farm.Describe());
      Assert.True(plan.Report.SearchTime.TotalMilliseconds < 100, plan.Report.SearchTime.ToString());
      state = farm.Apply(state);
    }

    Assert.Equal(2, state.Pieces.Count(piece => piece.Team == NetworkTeam.Red && piece.Type == "Farm"));
    Assert.Equal(1, state.Pieces.Count(piece => piece.Team == NetworkTeam.Blue && piece.Type == "Farm"));
  }

  [Fact]
  public void CpuVsCpuOpening_CompletesBothFarmPlacementsAndTheInitialBuyPhase()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration(farmsEnabled: true);
    NetworkInitialBuyState initialBuy = new(
      NetworkTeam.Red, 0, 1, 0, 0, 1, false, false, false,
      [
        new NetworkInitialBuyTeamState(NetworkTeam.Red, 0, false),
        new NetworkInitialBuyTeamState(NetworkTeam.Blue, 0, false)
      ],
      IsFarmPlacementPhase: true
    );
    CpuGameState state = new(
      configuration,
      [],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      initialBuy: initialBuy
    );
    CpuPlayer player = new();
    CpuProfile profile = CpuProfile.Normal(122);
    List<string> simulatedActions = [];

    for (int decision = 0; decision < 8 && state.InitialBuy is not null; decision++)
    {
      NetworkTeam team = state.CurrentTurn;
      CpuTurnPlan plan = player.ChooseTurn(state, team, profile, CancellationToken.None);
      ICpuGameAction action = Assert.Single(plan.Actions);
      Assert.True(action.IsLegal(state), action.Describe());
      Assert.InRange(plan.Report.SearchTime.TotalMilliseconds, 0, 3_000);

      simulatedActions.Add(action.Describe());
      state = action.Apply(state);
    }

    Assert.Null(state.InitialBuy);
    Assert.Equal(NetworkTeam.Red, state.CurrentTurn);
    Assert.Equal(2, state.Pieces.Count(piece => piece.Team == NetworkTeam.Red && piece.Type == "Farm"));
    Assert.Equal(2, state.Pieces.Count(piece => piece.Team == NetworkTeam.Blue && piece.Type == "Farm"));
    Assert.Equal(6, simulatedActions.Count);
    Assert.Equal(4, simulatedActions.Count(action => action.StartsWith("Purchase Farm", StringComparison.Ordinal)));
    Assert.Equal(2, simulatedActions.Count(action => action == "Stop initial buying"));
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
  public void NormalProfile_UsesAnInteractiveBudgetAndSeededNearBestVariation()
  {
    CpuProfile profile = CpuProfile.Normal(123);

    Assert.Equal(CpuDifficultyLevel.Normal, profile.Difficulty);
    Assert.InRange(profile.Search.MaxSearchMilliseconds, 1, 3_000);
    Assert.InRange(profile.Search.BeamWidth, 1, 6);
    Assert.InRange(profile.Search.CandidatesPerNode, 1, 9);
    Assert.InRange(profile.Search.MaximumPurchasePlacementCandidates, 1, 12);
    Assert.Equal(3, profile.TopChoicesForRandomSelection);
    Assert.True(profile.MistakeChance > 0f);
    Assert.True(profile.Search.TopChoiceScoreWindow > 0f);
  }

  [Fact]
  public void Search_NodeBudgetBoundsWorkBeforeTheWallClockLimit()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -3, 15)
    );
    CpuProfile profile = new()
    {
      Search = new CpuSearchSettings
      {
        BeamWidth = 8,
        CandidatesPerNode = 12,
        OpponentActionsToPredict = 0,
        MaxSearchNodes = 1,
        MaxSearchMilliseconds = 1_000,
        Randomness = 0f
      },
      MistakeChance = 0f
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

    Assert.True(plan.Report.NodeBudgetReached, CpuDebugFormatter.FormatDecision(plan.Report));
    Assert.False(plan.Report.TimedOut);
    Assert.Equal(1, plan.Report.NodesGenerated);
    Assert.All(plan.Actions, action => Assert.True(action.IsLegal(state), action.Describe()));
  }

  [Fact]
  public void ReversalHistory_IsHashedAndPenalisedWithoutMakingMovesIllegal()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuMoveRecord forward = new(NetworkTeam.Red, "red-soldier", 0, 1, 0, 0, 2);
    CpuMoveRecord reversal = new(NetworkTeam.Red, "red-soldier", 0, 0, 0, 1, 3);
    CpuGameState state = new(
      configuration,
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 1, 15)],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      turnNumber: 3,
      terrain: new BattlefieldTerrain(),
      recentMoves: [forward, reversal]
    );
    CpuGameState withoutHistory = new(
      configuration,
      state.Pieces,
      state.Teams.Values,
      NetworkTeam.Red,
      turnNumber: 3,
      terrain: new BattlefieldTerrain()
    );

    EvaluationBreakdown evaluation = new StateEvaluator().EvaluateWithBreakdown(
      state, NetworkTeam.Red, new EvaluationContext(CpuProfile.Normal(1)));

    Assert.True(evaluation.Terms["Repetition"] < 0f);
    Assert.NotEqual(new GameStateHasher().ComputeSearchHash(state), new GameStateHasher().ComputeSearchHash(withoutHistory));
  }

  [Fact]
  public void StateHash_DistinguishesBridgesAndRoyalSelection()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    CpuGameState withoutBridge = new(
      configuration,
      [],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    CpuGameState withBridge = new(
      configuration,
      [],
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn, "King"),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      riverBridges: [TileEdge.Between((0, 0), (0, 1))]
    );
    GameStateHasher hasher = new();

    Assert.NotEqual(hasher.ComputeSearchHash(withoutBridge), hasher.ComputeSearchHash(withBridge));
  }

  [Fact]
  public void AbilityPersonality_ChangesAbilityCandidateAndAppliedEffectValue()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-spy", "Spy", NetworkTeam.Red, 0, 0, 10),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );
    UseAbilityAction mark = Assert.Single(
      new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red).OfType<UseAbilityAction>(),
      action => action.Ability == "Mark"
    );
    CpuPersonality lowAbility = new() { AbilityUsage = 0.2f };
    CpuPersonality highAbility = new() { AbilityUsage = 2f };
    CpuSearchSettings settings = new() { CandidatesPerNode = 1 };
    CpuActionCandidateSelector selector = new();

    float lowCandidateScore = Assert.Single(selector.SelectCandidates(
      state, NetworkTeam.Red, [mark], settings, lowAbility)).Score;
    float highCandidateScore = Assert.Single(selector.SelectCandidates(
      state, NetworkTeam.Red, [mark], settings, highAbility)).Score;
    CpuGameState marked = mark.Apply(state);
    float lowEffect = new StateEvaluator().EvaluateWithBreakdown(
      marked, NetworkTeam.Red, new EvaluationContext(new CpuProfile { Personality = lowAbility })).Terms["Ability"];
    float highEffect = new StateEvaluator().EvaluateWithBreakdown(
      marked, NetworkTeam.Red, new EvaluationContext(new CpuProfile { Personality = highAbility })).Terms["Ability"];

    Assert.True(highCandidateScore > lowCandidateScore);
    Assert.True(highEffect > lowEffect);
  }

  [Fact]
  public void LargeFourPlayerBoard_GeneratesAndSimulatesOnlyLegalActions()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration(boardSize: "Large", playerCount: 4);
    Board board = BoardRules.GetBoard(configuration);
    (int x, int y) redStart = board.Cells.First(position =>
      BoardRules.CanPlaceForTeam(configuration, NetworkTeam.Red, position.x, position.y, 1, 1));
    CpuGameState state = new(
      configuration,
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, redStart.x, redStart.y, 15)],
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Green, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Yellow, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Easy(18), CancellationToken.None);
    CpuGameState simulated = state;

    Assert.NotEmpty(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red));
    foreach (ICpuGameAction action in plan.Actions)
    {
      Assert.True(action.IsLegal(simulated), action.Describe());
      simulated = action.Apply(simulated);
    }
  }

  [Fact]
  public void BeamSearch_NormalisesCommutingMovesAndReportsDuplicateRemoval()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-left", "Soldier", NetworkTeam.Red, -1, 2, 15),
        new NetworkPiece("red-right", "Soldier", NetworkTeam.Red, 1, 2, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -3, 15)
      ],
      redMoney: 0
    );
    CpuProfile profile = new()
    {
      Search = new CpuSearchSettings
      {
        BeamWidth = 32,
        CandidatesPerNode = 32,
        OpponentActionsToPredict = 0,
        MaxSearchMilliseconds = 1_000,
        Randomness = 0f
      },
      MistakeChance = 0f
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

    Assert.True(plan.Report.DuplicateStatesRemoved > 0, CpuDebugFormatter.FormatDecision(plan.Report));
  }

  [Fact]
  public void CpuPlansOnlyLegalActionsAcrossEveryBuiltInMatchMode()
  {
    CpuProfile profile = new()
    {
      Search = new CpuSearchSettings
      {
        BeamWidth = 3,
        CandidatesPerNode = 5,
        OpponentActionsToPredict = 0,
        MaximumPurchasePlacementCandidates = 4,
        MaxSearchMilliseconds = 50,
        Randomness = 0f
      },
      MistakeChance = 0f
    };

    foreach (string mode in new[] { "Regicide", "Conquest", "Escort", "Dominion", "Plunder" })
    {
      CpuGameState state = CreateBuiltInModeState(mode);
      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
      CpuGameState simulated = state;

      Assert.False(state.IsFinished, mode);
      foreach (ICpuGameAction action in plan.Actions)
      {
        Assert.True(action.IsLegal(simulated), $"{mode}: {action.Describe()}");
        simulated = action.Apply(simulated);
      }
    }
  }

  [Fact]
  public void TerminalCampaignState_ReturnsNoCpuActions()
  {
    CpuScenarioDefinition scenario = new()
    {
      VictoryGoals = [new CaptureLocationsGoal([(0, 0)])]
    };
    CpuGameState state = CreateState(
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15)],
      scenario: scenario
    );

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Easy(3), CancellationToken.None);

    Assert.True(state.IsFinished);
    Assert.Empty(new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red));
    Assert.Empty(plan.Actions);
  }

  [Fact]
  public void CampaignGoals_ReportHoldEscortProtectionEscapeAndSurvivalProgress()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("escort", "Soldier", NetworkTeam.Red, 0, 0, 15),
        new NetworkPiece("protected", "Peasant", NetworkTeam.Red, 1, 0, 5),
        new NetworkPiece("runner", "Soldier", NetworkTeam.Blue, 0, -2, 15)
      ],
      scenario: new CpuScenarioDefinition()
    );
    HoldLocationsGoal hold = new([(0, 0)]);
    EscortUnitGoal escort = new("escort", (0, 0));
    ProtectUnitGoal protect = new("protected");
    PreventEscapeGoal prevent = new([(0, -3)]);
    SurviveTurnsGoal survive = new(3);

    Assert.Equal(CpuGoalStatus.Completed, hold.GetStatus(state, NetworkTeam.Red));
    Assert.Equal(CpuGoalStatus.Completed, escort.GetStatus(state, NetworkTeam.Red));
    Assert.Equal(CpuGoalStatus.InProgress, protect.GetStatus(state, NetworkTeam.Red));
    Assert.Equal(CpuGoalStatus.InProgress, prevent.GetStatus(state, NetworkTeam.Red));
    Assert.Equal(CpuGoalStatus.InProgress, survive.GetStatus(state, NetworkTeam.Red));
    Assert.Contains(escort.GenerateIntents(state, NetworkTeam.Red), intent => intent.Type == CpuIntentType.EscortUnit);
    Assert.Contains(prevent.GenerateIntents(state, NetworkTeam.Red), intent => intent.Type == CpuIntentType.BlockRoute);
  }

  [Fact]
  public void CampaignCaptureAndEscapeGoals_UseLocationsAndBoardEdgesWithoutSpecialCpuRules()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    Board board = BoardRules.GetBoard(configuration);
    (int x, int y) exit = board.Cells.First(position => position.y == board.MinY);
    CpuGameState state = CreateState(
      [
        new NetworkPiece("capturer", "Soldier", NetworkTeam.Red, 0, 0, 15),
        new NetworkPiece("runner", "Soldier", NetworkTeam.Red, exit.x, exit.y, 15)
      ],
      configuration: configuration,
      scenario: new CpuScenarioDefinition()
    );
    CaptureLocationsGoal capture = new([(0, 0)]);
    EscapeBoardGoal escape = new("runner");

    Assert.Equal(CpuGoalStatus.Completed, capture.GetStatus(state, NetworkTeam.Red));
    Assert.Equal(CpuGoalStatus.Completed, escape.GetStatus(state, NetworkTeam.Red));
    Assert.Contains(capture.GenerateIntents(state, NetworkTeam.Red), intent => intent.Type == CpuIntentType.CaptureLocation);
    Assert.Contains(escape.GenerateIntents(state, NetworkTeam.Red), intent => intent.Type == CpuIntentType.Escape && intent.PieceId == "runner");
  }

  [Fact]
  public void CampaignTurnLimitAndReinforcement_AreAppliedAtTheSharedTurnBoundary()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration();
    (int x, int y) reinforcementPosition = BoardRules.GetBoard(configuration).Cells
      .First(position => position != (0, 0));
    CpuScenarioDefinition scenario = new()
    {
      TurnLimit = 1,
      WinnerOnTurnLimit = NetworkTeam.Blue,
      ScriptedReinforcements =
      [
        new CpuScriptedReinforcement(1, NetworkTeam.Blue, "Peasant", reinforcementPosition.x, reinforcementPosition.y,
          Health: 3, PieceId: "blue-reinforcement")
      ]
    };
    CpuGameState state = new(
      configuration,
      [],
      [
        new CpuTeamState(NetworkTeam.Red, 0, 1),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      scenario: scenario
    );

    CpuGameState afterTurn = CpuGameRules.Apply(state, new EndTurnAction(NetworkTeam.Red));
    NetworkPiece reinforcement = Assert.Single(afterTurn.Pieces);

    Assert.Equal(1, afterTurn.TurnNumber);
    Assert.Equal(NetworkTeam.Blue, afterTurn.Winner);
    Assert.Equal("blue-reinforcement", reinforcement.Id);
    Assert.Equal(3, reinforcement.Health);
    Assert.True(afterTurn.IsFinished);
  }

  [Fact]
  public void HeadlessSimulator_RunsRepeatedTurnsWithinBoundsWithoutRendering()
  {
    CpuProfile fastProfile = new()
    {
      Name = "Fast Test CPU",
      Search = new CpuSearchSettings
      {
        BeamWidth = 2,
        CandidatesPerNode = 4,
        OpponentActionsToPredict = 0,
        MaximumPurchasePlacementCandidates = 4,
        MaxSearchMilliseconds = 10,
        Randomness = 0f
      },
      MistakeChance = 0f
    };
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -2, 15)
      ],
      redMoney: 0
    );

    CpuMatchSimulationReport report = new CpuMatchSimulator().Run(new CpuMatchSimulationRequest
    {
      InitialState = state,
      Profiles = new Dictionary<NetworkTeam, CpuProfile>
      {
        [NetworkTeam.Red] = fastProfile,
        [NetworkTeam.Blue] = fastProfile
      },
      MaximumTurns = 12
    });

    Assert.InRange(report.TurnCount, 1, 12);
    Assert.All(report.Turns, turn => Assert.NotNull(turn.Decision));
    Assert.NotEmpty(report.EndReason);
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

  private static CpuGameState CreateBuiltInModeState(string mode)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", mode, 1234, 0, 0f, 0f, 2, 1, 15,
      FarmsEnabled: false
    );
    NetworkPiece[] pieces = mode == "Regicide"
      ?
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 5, 110),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -5, 110),
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -2, 15)
      ]
      :
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -2, 15),
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 1, 5, 110),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, -1, -5, 110)
      ];
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      treasurePosition: mode == "Plunder" ? (0, 0) : null,
      scenario: CpuScenarioDefinition.ForMatch(configuration)
    );
  }

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

  private static NetworkMatchConfiguration CreateConfiguration(
    bool farmsEnabled = false,
    string boardSize = "Small",
    int playerCount = 2
  ) => new(
    boardSize, "Light", "Light", "Regicide", 1234, 200, 0f, 0f, 2, 1, 15,
    FarmsEnabled: farmsEnabled,
    PlayerCount: playerCount
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
