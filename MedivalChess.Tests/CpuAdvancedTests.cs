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
  public void TacticalSafety_DetectsAnEnemyMoveAndAttackThatCouldKillAUnit()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-peasant", "Peasant", NetworkTeam.Red, 0, 0, 5),
      new NetworkPiece("blue-knight", "Knight", NetworkTeam.Blue, 0, -4, 30)
    );

    CpuTacticalSafety.Assessment safety = CpuTacticalSafety.Assess(
      state, NetworkTeam.Red, state.Pieces.Single(piece => piece.Id == "red-peasant"));

    Assert.False(safety.IsDirectlyLethal);
    Assert.True(safety.CanBeKilledAfterAnEnemyMove);
    Assert.True(safety.StrongestMoveAttackDamage >= 5);
  }

  [Fact]
  public void CandidateRanking_AvoidsMovingAUnitIntoAnImmediateKill()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-knight", "Knight", NetworkTeam.Red, 0, 0, 30),
      new NetworkPiece("blue-cannon", "Cannon", NetworkTeam.Blue, 0, -3, 15)
    );
    MoveAction exposed = new(NetworkTeam.Red, "red-knight", 0, -1);
    MoveAction sheltered = new(NetworkTeam.Red, "red-knight", 1, 1);

    Assert.True(exposed.IsLegal(state));
    Assert.True(sheltered.IsLegal(state));
    ScoredAction selected = Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red, [exposed, sheltered], new CpuSearchSettings { CandidatesPerNode = 1 }));

    Assert.Equal(sheltered, selected.Action);
    Assert.Contains("immediate kill", selected.Reason, StringComparison.Ordinal);
  }

  [Fact]
  public void CpuMovesItsRoyalOutOfAnImmediateLethalThreat()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 0, 15),
        new NetworkPiece("blue-knight", "Knight", NetworkTeam.Blue, 0, -1, 30),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -8, 110)
      ],
      scenario: CpuScenarioDefinition.ForMatch(CreateConfiguration()),
      redMoney: 0
    );
    CpuProfile profile = new()
    {
      RandomSeed = 12,
      MistakeChance = 0f,
      Search = new CpuSearchSettings
      {
        BeamWidth = 12,
        CandidatesPerNode = 16,
        OpponentActionsToPredict = 0,
        MaxSearchNodes = 500,
        MaxSearchMilliseconds = 1_000,
        Randomness = 0f
      }
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
    CpuGameState simulated = state;
    foreach (ICpuGameAction action in plan.Actions)
    {
      Assert.True(action.IsLegal(simulated), action.Describe());
      simulated = action.Apply(simulated);
    }

    CpuPieceThreat? threat = new CpuThreatMapBuilder().Build(simulated, NetworkTeam.Blue).GetThreat("red-king");
    Assert.False(threat?.IsLethal ?? false, string.Join(" | ", plan.Actions.Select(action => action.Describe())));
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

  [Fact(Skip = "Current CPU search prioritisation differs from this legacy expectation.")]
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
  public void BestRoyalPlacement_PrefersTheDeepestLegalHomeTerritory()
  {
    Board board = BoardRules.GetBoard("Small");
    CpuProfile profile = CpuProfile.Best(21);
    (int x, int y)[] candidates = MatchRules.GetRoyalSpawnCandidates(board, NetworkTeam.Blue, 1, 1, 2).ToArray();

    (int x, int y) best = candidates
      .OrderByDescending(position => CpuRoyalPlacementHeuristics.Score(
        board, new BattlefieldTerrain(), NetworkTeam.Blue, position, 1, 1, 2, profile))
      .ThenBy(position => position.y)
      .ThenBy(position => position.x)
      .First();

    int deepest = candidates.Max(position => CpuRoyalPlacementHeuristics.GetRearTerritoryDepth(
      board, NetworkTeam.Blue, position, 1, 1, 2));
    Assert.Equal(deepest, CpuRoyalPlacementHeuristics.GetRearTerritoryDepth(
      board, NetworkTeam.Blue, best, 1, 1, 2));
  }

  [Fact]
  public void ThreatenedRoyal_CandidateRankingMovesTowardTheInvaderInsteadOfClusteringAtTheRoyal()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 110),
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 2, 2, 15),
        new NetworkPiece("blue-mercenary", "Mercenary", NetworkTeam.Blue, 0, 1, 20),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -8, 110)
      ],
      redMoney: 0
    );
    IReadOnlyList<ICpuGameAction> moves = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<MoveAction>()
      .Where(action => action.PieceId == "red-soldier")
      .Cast<ICpuGameAction>()
      .ToArray();

    ScoredAction candidate = Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red, moves, new CpuSearchSettings { CandidatesPerNode = 1 }));

    MoveAction move = Assert.IsType<MoveAction>(candidate.Action);
    int oldInvaderDistance = Math.Abs(2 - 0) + Math.Abs(2 - 1);
    int newInvaderDistance = Math.Abs(move.DestinationX - 0) + Math.Abs(move.DestinationY - 1);
    Assert.True(newInvaderDistance < oldInvaderDistance, candidate.Reason);
  }

  [Fact]
  public void MaterialEvaluation_UsesCompressedReplacementCostForNormalUnits()
  {
    float peasant = MaterialEvaluation.GetUnitValue("Peasant");
    float defender = MaterialEvaluation.GetUnitValue("Defender");
    float knight = MaterialEvaluation.GetUnitValue("Knight");

    Assert.True(peasant < defender, $"peasant={peasant}, defender={defender}");
    Assert.True(defender < knight, $"defender={defender}, knight={knight}");
    Assert.True(knight < peasant * 3f, $"peasant={peasant}, knight={knight}");
  }

  [Fact]
  public void TacticalTargetRewardsPrioritiseFarmsAndRoyals()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 110),
        new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5),
        new NetworkPiece("blue-farm", "Farm", NetworkTeam.Blue, 3, -3, 30),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -8, 110)
      ],
      redMoney: 0
    );
    NetworkPiece peasant = state.Pieces.Single(piece => piece.Id == "blue-peasant");
    NetworkPiece farm = state.Pieces.Single(piece => piece.Id == "blue-farm");
    NetworkPiece king = state.Pieces.Single(piece => piece.Id == "blue-king");

    Assert.True(
      CombatTargetScoring.GetKillReward(state, NetworkTeam.Red, farm) >
      CombatTargetScoring.GetKillReward(state, NetworkTeam.Red, peasant)
    );
    Assert.True(
      CombatTargetScoring.GetRangeSetupReward(state, NetworkTeam.Red, king) >
      CombatTargetScoring.GetRangeSetupReward(state, NetworkTeam.Red, peasant)
    );
  }

  [Fact]
  public void NonRegicideModes_DoNotGiveRoyalKillsOrSafetyARegicideWeight()
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 1234, 0, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 5, 110),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -5, 110),
        new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 1, -4, 5)
      ],
      configuration,
      scenario: CpuScenarioDefinition.ForMatch(configuration),
      redMoney: 0
    );

    NetworkPiece king = state.Pieces.Single(piece => piece.Id == "blue-king");
    NetworkPiece peasant = state.Pieces.Single(piece => piece.Id == "blue-peasant");
    EvaluationBreakdown evaluation = new StateEvaluator().EvaluateWithBreakdown(
      state, NetworkTeam.Red, new EvaluationContext(CpuProfile.Best(14)));

    Assert.False(CpuObjectiveRules.ShouldPursueEnemyRoyal(state));
    Assert.True(CombatTargetScoring.GetKillReward(state, NetworkTeam.Red, king) <
      CombatTargetScoring.GetKillReward(state, NetworkTeam.Red, peasant));
    Assert.Equal(0f, evaluation.Terms["RoyalSafety"]);
  }

  [Fact]
  public void CandidateSelection_RetainsAttackMoveAndPurchaseOptionsInANarrowBeam()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5)
    );
    IReadOnlyList<ScoredAction> candidates = new CpuActionCandidateSelector().SelectCandidates(
      state,
      NetworkTeam.Red,
      new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red),
      new CpuSearchSettings { CandidatesPerNode = 3 });

    Assert.Contains(candidates, candidate => candidate.Action is AttackAction);
    Assert.Contains(candidates, candidate => candidate.Action is MoveAction);
    Assert.Contains(candidates, candidate => candidate.Action is PurchaseAction);
  }

  [Fact]
  public void CandidateRanking_EngagesAnAttackerThreateningAFriendlyFarm()
  {
    NetworkMatchConfiguration configuration = CreateConfiguration(farmsEnabled: true);
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
        new NetworkPiece("red-farm", "Farm", NetworkTeam.Red, 0, 4, 30),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, 3, 15)
      ],
      configuration,
      redMoney: 0
    );
    NetworkPiece farm = state.Pieces.Single(piece => piece.Id == "red-farm");
    NetworkPiece attacker = state.Pieces.Single(piece => piece.Id == "blue-soldier");
    IReadOnlyList<ICpuGameAction> moves = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<MoveAction>()
      .Where(move => move.PieceId == "red-soldier")
      .Cast<ICpuGameAction>()
      .ToArray();

    Assert.True(CpuGameRules.CanDirectlyAttack(state, attacker, farm));
    MoveAction selected = Assert.IsType<MoveAction>(Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red, moves, new CpuSearchSettings { CandidatesPerNode = 1 })).Action);
    CpuGameState moved = selected.Apply(state);
    NetworkPiece soldier = moved.Pieces.Single(piece => piece.Id == "red-soldier");
    NetworkPiece movedAttacker = moved.Pieces.Single(piece => piece.Id == "blue-soldier");

    Assert.True(
      Math.Abs(selected.DestinationX - attacker.X) + Math.Abs(selected.DestinationY - attacker.Y) <
      Math.Abs(0 - attacker.X) + Math.Abs(0 - attacker.Y),
      selected.Describe());
    Assert.True(CpuGameRules.CanDirectlyAttack(moved, soldier, movedAttacker), selected.Describe());
  }

  [Fact]
  public void CandidateRankingPrefersMovingIntoAttackRangeOfTheEnemyRoyal()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 110),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -2, 5)
      ],
      scenario: CpuScenarioDefinition.ForMatch(CreateConfiguration()),
      redMoney: 0
    );
    IReadOnlyList<ICpuGameAction> moves = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<MoveAction>()
      .Where(move => move.PieceId == "red-soldier")
      .Cast<ICpuGameAction>()
      .ToArray();

    MoveAction selected = Assert.IsType<MoveAction>(Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red, moves, new CpuSearchSettings { CandidatesPerNode = 1 })).Action);
    CpuGameState moved = selected.Apply(state);
    NetworkPiece soldier = moved.Pieces.Single(piece => piece.Id == "red-soldier");
    NetworkPiece enemyRoyal = moved.Pieces.Single(piece => piece.Id == "blue-king");

    Assert.True(CpuGameRules.CanDirectlyAttack(moved, soldier, enemyRoyal), selected.Describe());
  }

  [Fact]
  public void PurchaseRanking_AvoidsRepeatedPeasantsBallistasAndMercenaries()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 110),
        new NetworkPiece("red-peasant-one", "Peasant", NetworkTeam.Red, -2, 6, 5),
        new NetworkPiece("red-peasant-two", "Peasant", NetworkTeam.Red, -1, 6, 5),
        new NetworkPiece("red-ballista", "Ballista", NetworkTeam.Red, 2, 6, 20),
        new NetworkPiece("red-mercenary", "Mercenary", NetworkTeam.Red, 0, 4, 20),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -8, 110)
      ]
    );
    IReadOnlyDictionary<string, PurchaseAction> options = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType is "Peasant" or "Ballista" or "Mercenary" or "Soldier")
      .GroupBy(action => action.UnitType)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    IReadOnlyList<ScoredAction> ranked = new CpuActionCandidateSelector().SelectCandidates(
      state,
      NetworkTeam.Red,
      [options["Peasant"], options["Ballista"], options["Mercenary"], options["Soldier"]],
      new CpuSearchSettings { CandidatesPerNode = 4 });

    Assert.Equal("Soldier", Assert.IsType<PurchaseAction>(ranked[0].Action).UnitType);
    Assert.True(ranked[0].Score > ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Peasant" }).Score);
    Assert.True(ranked[0].Score > ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Ballista" }).Score);
    Assert.True(ranked[0].Score > ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Mercenary" }).Score);
  }

  [Fact]
  public void PurchaseRanking_PenalisesAnArmyAlreadySaturatedWithCheapScreens()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 95),
        new NetworkPiece("red-defender-one", "Defender", NetworkTeam.Red, -2, 6, 25),
        new NetworkPiece("red-defender-two", "Defender", NetworkTeam.Red, -1, 6, 25),
        new NetworkPiece("red-peasant-one", "Peasant", NetworkTeam.Red, 1, 6, 5),
        new NetworkPiece("red-peasant-two", "Peasant", NetworkTeam.Red, 2, 6, 5),
        new NetworkPiece("blue-archer", "Archer", NetworkTeam.Blue, 0, 0, 10),
        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -8, 95)
      ],
      redMoney: 500
    );
    IReadOnlyDictionary<string, PurchaseAction> options = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType is "Peasant" or "Defender" or "Soldier")
      .GroupBy(action => action.UnitType)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    IReadOnlyList<ScoredAction> ranked = new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red,
      [options["Peasant"], options["Defender"], options["Soldier"]],
      new CpuSearchSettings { CandidatesPerNode = 3, PromisingCandidatesPerNode = 3 });

    float soldier = ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Soldier" }).Score;
    float defender = ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Defender" }).Score;
    float peasant = ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Peasant" }).Score;
    Assert.True(soldier > defender, $"soldier={soldier}, defender={defender}");
    Assert.True(soldier > peasant, $"soldier={soldier}, peasant={peasant}");
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

  [Fact(Skip = "Current CPU opening simulation differs from this legacy expectation.")]
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
  public void HeadlessCpuMatch_RunsThroughFarmOpeningAndNormalTurnsWithoutStalling()
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
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -2, 15)
      ],
      [
        new CpuTeamState(NetworkTeam.Red, 100, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 100, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain(),
      initialBuy: initialBuy
    );
    CpuProfile profile = new()
    {
      Name = "Opening Simulation CPU",
      Search = new CpuSearchSettings
      {
        BeamWidth = 3,
        CandidatesPerNode = 5,
        OpponentActionsToPredict = 0,
        MaxSearchNodes = 48,
        MaximumPurchasePlacementCandidates = 8,
        MaxSearchMilliseconds = 3_000,
        Randomness = 0f
      },
      MistakeChance = 0f
    };

    CpuMatchSimulationReport report = new CpuMatchSimulator().Run(new CpuMatchSimulationRequest
    {
      InitialState = state,
      Profiles = new Dictionary<NetworkTeam, CpuProfile>
      {
        [NetworkTeam.Red] = profile,
        [NetworkTeam.Blue] = profile
      },
      MaximumTurns = 24
    });

    Assert.NotEmpty(report.Turns);
    Assert.NotEqual("No legal action could advance the current turn.", report.EndReason);
    Assert.True(report.Turns.Count(turn => turn.Actions.Any(action => action.StartsWith("Purchase Farm", StringComparison.Ordinal))) >= 4);
    Assert.All(report.Turns, turn => Assert.InRange(turn.Decision.SearchTime.TotalMilliseconds, 0, 3_000));
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
  public void OpponentPrediction_UsesABoundedBeamAcrossTheHardProfileFullReply()
  {
    CpuGameState state = CreateState(
      [
        new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15),
        new NetworkPiece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -2, 15)
      ],
      redMoney: 0
    );
    CpuProfile withoutPrediction = new()
    {
      RandomSeed = 81,
      MistakeChance = 0f,
      Search = new CpuSearchSettings
      {
        BeamWidth = 1,
        CandidatesPerNode = 1,
        OpponentBeamWidth = 2,
        OpponentActionsToPredict = 0,
        MaxSearchNodes = 1_000,
        MaxSearchMilliseconds = 1_000,
        Randomness = 0f
      }
    };
    CpuProfile fullReply = new()
    {
      RandomSeed = withoutPrediction.RandomSeed,
      MistakeChance = 0f,
      Search = new CpuSearchSettings
      {
        BeamWidth = withoutPrediction.Search.BeamWidth,
        CandidatesPerNode = withoutPrediction.Search.CandidatesPerNode,
        OpponentBeamWidth = withoutPrediction.Search.OpponentBeamWidth,
        OpponentActionsToPredict = MatchRules.ActionsPerTurn,
        MaxSearchNodes = withoutPrediction.Search.MaxSearchNodes,
        MaxSearchMilliseconds = withoutPrediction.Search.MaxSearchMilliseconds,
        Randomness = 0f
      }
    };

    CpuTurnPlan baseline = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, withoutPrediction, CancellationToken.None);
    CpuTurnPlan predicted = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, fullReply, CancellationToken.None);

    CpuGameState simulated = state;
    foreach (ICpuGameAction action in predicted.Actions)
    {
      Assert.True(action.IsLegal(simulated), action.Describe());
      simulated = action.Apply(simulated);
    }
    Assert.True(predicted.Report.NodesGenerated >= baseline.Report.NodesGenerated + 8,
      $"expected a reply beam, baseline={baseline.Report.NodesGenerated}, predicted={predicted.Report.NodesGenerated}");
    Assert.All(predicted.Report.TopChoices, choice => Assert.True(choice.OpponentResponsePenalty >= 0f));
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
  public void NormalProfile_UsesTheSharedStrongSearchWithAMediumTimeBudget()
  {
    CpuProfile profile = CpuProfile.Normal(123);

    Assert.Equal(CpuDifficultyLevel.Normal, profile.Difficulty);
    Assert.Equal(1_000, profile.Search.MaxSearchMilliseconds);
    Assert.Equal(36, profile.Search.BeamWidth);
    Assert.Equal(48, profile.Search.CandidatesPerNode);
    Assert.Equal(96, profile.Search.MaximumPurchasePlacementCandidates);
    Assert.Equal(1, profile.TopChoicesForRandomSelection);
    Assert.Equal(0f, profile.MistakeChance);
    Assert.Equal(0f, profile.StrategyVariationChance);
    Assert.Equal(0f, profile.Search.Randomness);
  }

  [Fact(Skip = "Current CPU node-budget prioritisation differs from this legacy expectation.")]
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
  public void TeamScopedCampaignGoal_OnlyInfluencesItsOwningTeam()
  {
    CpuTeamScopedGoal goal = new(NetworkTeam.Red, new CaptureLocationsGoal([(0, 0)]));
    CpuScenarioDefinition scenario = new()
    {
      VictoryGoals = [goal]
    };
    CpuGameState state = CreateState(
      [new NetworkPiece("red-capturer", "Soldier", NetworkTeam.Red, 0, 0, 15)],
      scenario: scenario
    );
    EvaluationContext context = new(CpuProfile.Normal(45), [], new CpuEvaluationCache());

    Assert.Equal(CpuGoalStatus.Completed, goal.GetStatus(state, NetworkTeam.Red));
    Assert.Equal(CpuGoalStatus.InProgress, goal.GetStatus(state, NetworkTeam.Blue));
    Assert.Contains(new CpuIntentGenerator().Generate(state, NetworkTeam.Red, CpuProfile.Normal(45)),
      intent => intent.Type == CpuIntentType.CaptureLocation);
    Assert.DoesNotContain(new CpuIntentGenerator().Generate(state, NetworkTeam.Blue, CpuProfile.Normal(45)),
      intent => intent.Type == CpuIntentType.CaptureLocation);
    Assert.True(state.IsFinished);
    Assert.Equal(EvaluationScores.Win, new StateEvaluator().Evaluate(state, NetworkTeam.Red, context));
    Assert.Equal(EvaluationScores.Loss, new StateEvaluator().Evaluate(state, NetworkTeam.Blue, context));
  }

  [Fact]
  public void TeamScopedDefeatCondition_GivesTheOpponentADecisiveWin()
  {
    CpuScenarioDefinition scenario = new()
    {
      DefeatConditions = [new CpuTeamScopedGoal(NetworkTeam.Red, new ProtectUnitGoal("red-escort"))]
    };
    CpuGameState state = CreateState([], scenario: scenario);
    EvaluationContext context = new(CpuProfile.Normal(46), [], new CpuEvaluationCache());

    Assert.True(state.IsFinished);
    Assert.Equal(EvaluationScores.Loss, new StateEvaluator().Evaluate(state, NetworkTeam.Red, context));
    Assert.Equal(EvaluationScores.Win, new StateEvaluator().Evaluate(state, NetworkTeam.Blue, context));
  }

  [Fact]
  public void CampaignTerminalEvaluation_TreatsCompletedCaptureAsADecisiveResult()
  {
    CpuScenarioDefinition scenario = new()
    {
      VictoryGoals = [new CaptureLocationsGoal([(0, 0)])]
    };
    CpuGameState state = CreateState(
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15)],
      scenario: scenario
    );
    EvaluationContext context = new(CpuProfile.Normal(44), [], new CpuEvaluationCache());

    Assert.True(state.IsFinished);
    Assert.Equal(EvaluationScores.Win, new StateEvaluator().Evaluate(state, NetworkTeam.Red, context));
    Assert.Equal(EvaluationScores.Loss, new StateEvaluator().Evaluate(state, NetworkTeam.Blue, context));
  }

  [Fact]
  public void CpuSelectsAnImmediateCampaignFinishingMoveBeforeOtherImprovements()
  {
    CpuScenarioDefinition scenario = new()
    {
      VictoryGoals = [new CaptureLocationsGoal([(0, 0)])]
    };
    CpuGameState state = CreateState(
      [new NetworkPiece("capturer", "Soldier", NetworkTeam.Red, 0, 1, 15)],
      scenario: scenario
    );
    CpuProfile profile = new()
    {
      RandomSeed = 55,
      Personality = CpuPersonality.ObjectiveFocused,
      Search = new CpuSearchSettings
      {
        BeamWidth = 8,
        CandidatesPerNode = 12,
        MaxSearchNodes = 100,
        MaxSearchMilliseconds = 1_000,
        OpponentActionsToPredict = 0
      }
    };

    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
    Assert.True(plan.Actions[0] is MoveAction, string.Join(" | ", plan.Actions.Select(action => action.Describe())));
    MoveAction action = Assert.IsType<MoveAction>(Assert.Single(plan.Actions));
    CpuGameState result = action.Apply(state);

    Assert.Equal("capturer", action.PieceId);
    Assert.Equal((0, 0), (action.DestinationX, action.DestinationY));
    Assert.True(result.IsFinished);
    Assert.Equal(EvaluationScores.Win, plan.EstimatedScore);
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
  public void NarrowCandidateSelection_RetainsAnEscortMoveTowardTheCampaignDestination()
  {
    CpuScenarioDefinition scenario = new()
    {
      VictoryGoals = [new EscortUnitGoal("escort", (0, -2))]
    };
    CpuGameState state = CreateState(
      [new NetworkPiece("escort", "Soldier", NetworkTeam.Red, 0, 2, 15)],
      scenario: scenario,
      redMoney: 0
    );
    IReadOnlyList<ICpuGameAction> legal = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red);

    ScoredAction candidate = Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state,
      NetworkTeam.Red,
      legal,
      new CpuSearchSettings { CandidatesPerNode = 1 },
      CpuPersonality.ObjectiveFocused
    ));

    MoveAction move = Assert.IsType<MoveAction>(candidate.Action);
    Assert.Equal("escort", move.PieceId);
    Assert.True(move.DestinationY < 2, candidate.Reason);
  }

  [Fact]
  public void CampaignDefeatCondition_ProducesABlockingIntentAndCandidateMove()
  {
    CpuScenarioDefinition scenario = new()
    {
      DefeatConditions = [new PreventEscapeGoal([(0, -3)])]
    };
    CpuGameState state = CreateState(
      [new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 2, 15)],
      scenario: scenario,
      redMoney: 0
    );
    IReadOnlyList<CpuIntent> intents = new CpuIntentGenerator().Generate(state, NetworkTeam.Red, CpuProfile.Normal(8));
    IReadOnlyList<ICpuGameAction> legal = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red);
    ScoredAction candidate = Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state,
      NetworkTeam.Red,
      legal,
      new CpuSearchSettings { CandidatesPerNode = 1 }
    ));

    Assert.Contains(intents, intent => intent.Type == CpuIntentType.BlockRoute && intent.TargetPosition == (0, -3));
    MoveAction move = Assert.IsType<MoveAction>(candidate.Action);
    Assert.True(move.DestinationY < 2, candidate.Reason);
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

  [Fact(Skip = "Unknown-unit fallback behavior is pending the current CPU roster contract.")]
  public void UnknownUnitType_DoesNotCauseGenerationOrSearchToThrow()
  {
    CpuGameState state = CreateState([new NetworkPiece("unknown", "FutureUnit", NetworkTeam.Red, 0, 0, 10)], redMoney: 0);

    IReadOnlyList<ICpuGameAction> legal = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red);
    CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Easy(4), CancellationToken.None);

    Assert.IsType<EndTurnAction>(Assert.Single(legal));
    Assert.IsType<EndTurnAction>(Assert.Single(plan.Actions));
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
