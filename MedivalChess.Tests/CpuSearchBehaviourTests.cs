using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuSearchBehaviourTests
{
  [Fact]
  public void Search_AllowsAStrongerMoveToCompeteWithAnAvailableAttack()
  {
    CpuGameState state = CreateState(
      "Regicide",
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-knight", "Knight", NetworkTeam.Red, 3, 1, 30),
      new NetworkPiece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, -1, 5),
      new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -6, 110)
    );
    PreferMoveOverAttackSelector selector = new();
    CpuProfile profile = TestProfile(maxNodes: 2);

    CpuTurnPlan plan = new CpuPlayer(candidateSelector: selector).ChooseTurn(
      state, NetworkTeam.Red, profile, CancellationToken.None);

    ICpuGameAction firstBoardAction = Assert.Single(plan.Actions, action => action is not EndTurnAction);
    Assert.IsType<MoveAction>(firstBoardAction);
  }

  [Fact]
  public void Verifier_DoesNotAppendUnsearchedMovesOrAttacks()
  {
    CpuGameState state = CreateState(
      "Regicide",
      new NetworkPiece("red-soldier-a", "Soldier", NetworkTeam.Red, -2, 1, 15),
      new NetworkPiece("red-soldier-b", "Soldier", NetworkTeam.Red, 2, 1, 15),
      new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -6, 110)
    );
    SingleMoveSelector selector = new();
    CpuProfile profile = TestProfile(maxNodes: 1);

    CpuTurnPlan plan = new CpuPlayer(candidateSelector: selector).ChooseTurn(
      state, NetworkTeam.Red, profile, CancellationToken.None);

    Assert.Single(plan.Actions.OfType<MoveAction>());
    Assert.Empty(plan.Actions.OfType<AttackAction>());
    Assert.True(plan.Actions.Count is 1 or 2);
    if (plan.Actions.Count == 2)
    {
      Assert.IsType<EndTurnAction>(plan.Actions[1]);
    }
  }

  [Fact]
  public void NonRegicideSearch_DoesNotLetRoyalQuickPriorityCrowdOutOrdinaryLines()
  {
    CpuGameState state = CreateState(
      "Conquest",
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-knight", "Knight", NetworkTeam.Red, 3, 1, 30),
      new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -1, 110)
    );
    RoyalCrowdingSelector selector = new();
    CpuProfile profile = TestProfile(maxNodes: 2);

    CpuTurnPlan plan = new CpuPlayer(candidateSelector: selector).ChooseTurn(
      state, NetworkTeam.Red, profile, CancellationToken.None);

    Assert.True(selector.SawMixedRoyalAndOrdinaryActions);
    Assert.True(selector.SawOrdinaryOnlyActions);
    Assert.IsType<MoveAction>(plan.Actions.First(action => action is not EndTurnAction));
  }

  private static CpuProfile TestProfile(int maxNodes) => new()
  {
    Search = new CpuSearchSettings
    {
      BeamWidth = 4,
      CandidatesPerNode = 4,
      PromisingCandidatesPerNode = 4,
      OpponentActionsToPredict = 0,
      TacticalExtensionDepth = 0,
      MaxSearchNodes = maxNodes,
      MaxSearchMilliseconds = 1_000,
      MaximumPurchasePlacementCandidates = 4,
      Randomness = 0f,
      MaxParallelism = 1
    },
    MistakeChance = 0f,
    StrategyVariationChance = 0f,
    TopChoicesForRandomSelection = 1
  };

  private static CpuGameState CreateState(string gameMode, params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", gameMode, 1234, 0, 0f, 0f, 2, 1, 15, FarmsEnabled: false
    );
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
  }

  private sealed class PreferMoveOverAttackSelector : IActionCandidateSelector
  {
    public IReadOnlyList<ScoredAction> SelectCandidates(
      CpuGameState state,
      NetworkTeam team,
      IReadOnlyList<ICpuGameAction> legalActions,
      CpuSearchSettings settings,
      CpuPersonality? personality = null)
    {
      AttackAction? attack = legalActions.OfType<AttackAction>().FirstOrDefault(action => action.TargetPieceId is not null);
      MoveAction? move = legalActions.OfType<MoveAction>().FirstOrDefault(action => action.PieceId == "red-knight");
      List<ScoredAction> result = [];
      if (move is not null) result.Add(new ScoredAction(move, 100_000f, "Test strategic move"));
      if (attack is not null) result.Add(new ScoredAction(attack, 0f, "Test ordinary attack"));
      return result;
    }
  }

  private sealed class SingleMoveSelector : IActionCandidateSelector
  {
    public IReadOnlyList<ScoredAction> SelectCandidates(
      CpuGameState state,
      NetworkTeam team,
      IReadOnlyList<ICpuGameAction> legalActions,
      CpuSearchSettings settings,
      CpuPersonality? personality = null)
    {
      MoveAction? move = legalActions.OfType<MoveAction>().FirstOrDefault();
      return move is null ? [] : [new ScoredAction(move, 100f, "Single searched move")];
    }
  }

  private sealed class RoyalCrowdingSelector : IActionCandidateSelector
  {
    public bool SawMixedRoyalAndOrdinaryActions { get; private set; }
    public bool SawOrdinaryOnlyActions { get; private set; }

    public IReadOnlyList<ScoredAction> SelectCandidates(
      CpuGameState state,
      NetworkTeam team,
      IReadOnlyList<ICpuGameAction> legalActions,
      CpuSearchSettings settings,
      CpuPersonality? personality = null)
    {
      AttackAction? royalAttack = legalActions.OfType<AttackAction>().FirstOrDefault(attack =>
        attack.TargetPieceId is not null &&
        state.Pieces.FirstOrDefault(piece => piece.Id == attack.TargetPieceId) is NetworkPiece target &&
        UnitRules.TryGet(target.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal);
      MoveAction? move = legalActions.OfType<MoveAction>().FirstOrDefault(action => action.PieceId == "red-knight");
      if (royalAttack is not null && move is not null) SawMixedRoyalAndOrdinaryActions = true;
      if (royalAttack is null && move is not null) SawOrdinaryOnlyActions = true;

      // Model the old cheap-pruning failure: whenever a royal attack shares a shortlist with
      // ordinary actions, only the royal survives. When the search asks for an objective-aware
      // ordinary shortlist separately, the strong move survives and can compete in the beam.
      if (royalAttack is not null) return [new ScoredAction(royalAttack, 0f, "Royal quick-priority candidate")];
      return move is null ? [] : [new ScoredAction(move, 100_000f, "Objective-relevant ordinary candidate")];
    }
  }
}
