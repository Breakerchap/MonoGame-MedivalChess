using System.Diagnostics;
using MedivalChess.Shared;

namespace MedivalChess.CPU;

public interface ICpuPlayer
{
  CpuTurnPlan ChooseTurn(CpuGameState state, NetworkTeam team, CpuProfile profile, CancellationToken cancellationToken);
}

public sealed record CpuTurnPlan(
  IReadOnlyList<ICpuGameAction> Actions,
  float EstimatedScore,
  CpuDecisionReport Report
);

public sealed record SearchNode(
  CpuGameState State,
  IReadOnlyList<ICpuGameAction> Actions,
  float Score,
  EvaluationBreakdown Breakdown
);

/// <summary>One independently simulated branch, kept in candidate order for deterministic merging.</summary>
internal sealed record PendingSearchExpansion(SearchNode Node, ScoredAction Candidate);

internal sealed record EvaluatedSearchExpansion(
  PendingSearchExpansion Pending,
  CpuGameState Result,
  EvaluationBreakdown Breakdown
);

public sealed class CpuDecisionReport
{
  public string ProfileName { get; init; } = string.Empty;
  public CpuDifficultyLevel Difficulty { get; init; }
  public CpuPersonality Personality { get; init; } = CpuPersonality.Balanced;
  public ulong InitialStateHash { get; init; }
  public int RootLegalActionCount { get; init; }
  public IReadOnlyList<CpuIntent> Intentions { get; init; } = [];
  public TimeSpan SearchTime { get; init; }
  public int NodesGenerated { get; init; }
  public int NodesEvaluated { get; init; }
  public int DuplicateStatesRemoved { get; init; }
  public int EvaluationCacheHits { get; init; }
  public bool TimedOut { get; init; }
  public bool NodeBudgetReached { get; init; }
  public bool Cancelled { get; init; }
  public IReadOnlyList<CpuChoiceReport> TopChoices { get; init; } = [];
}

public sealed class CpuChoiceReport
{
  public IReadOnlyList<string> Actions { get; init; } = [];
  public float FinalScore { get; init; }
  public IReadOnlyDictionary<string, float> EvaluationTerms { get; init; } = new Dictionary<string, float>();
  public float OpponentResponsePenalty { get; init; }
  public string Reason { get; init; } = string.Empty;
}

/// <summary>Bounded deterministic beam search over the current team's remaining turn actions.</summary>
public sealed class CpuPlayer : ICpuPlayer
{
  private readonly CpuActionGenerator _actionGenerator;
  private readonly IActionCandidateSelector _candidateSelector;
  private readonly StateEvaluator _evaluator;
  private readonly GameStateHasher _hasher;
  private readonly ICpuIntentGenerator _intentGenerator;
  private readonly bool _canParallelizeEvaluation;

  public CpuPlayer(
    CpuActionGenerator? actionGenerator = null,
    IActionCandidateSelector? candidateSelector = null,
    StateEvaluator? evaluator = null,
    GameStateHasher? hasher = null,
    ICpuIntentGenerator? intentGenerator = null
  )
  {
    _actionGenerator = actionGenerator ?? new CpuActionGenerator();
    _candidateSelector = candidateSelector ?? new CpuActionCandidateSelector();
    _evaluator = evaluator ?? new StateEvaluator();
    _hasher = hasher ?? new GameStateHasher();
    _intentGenerator = intentGenerator ?? new CpuIntentGenerator();
    // Evaluation extensions are allowed to maintain their own mutable diagnostic state. Only the
    // built-in evaluator is promised to be safely callable by several branch workers at once.
    _canParallelizeEvaluation = evaluator is null;
  }

  public CpuTurnPlan ChooseTurn(CpuGameState state, NetworkTeam team, CpuProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(profile);
    // The advertised budget covers the whole decision: intent generation, evaluation, action
    // ranking, and branch search. Starting here keeps a 1.4-second campaign turn responsive
    // even on a first-use/JIT-heavy board.
    Stopwatch stopwatch = Stopwatch.StartNew();
    CpuSearchSettings settings = profile.Search;
    ulong initialStateHash = _hasher.ComputeSearchHash(state);
    if (state.InitialBuy?.IsFarmPlacementPhase == true)
    {
      return ChooseOpeningFarmPlacement(state, team, profile, cancellationToken, stopwatch, initialStateHash);
    }

    CpuEvaluationCache evaluationCache = new();
    IReadOnlyList<CpuIntent> intents = _intentGenerator.Generate(state, team, profile, evaluationCache);
    EvaluationContext context = new(profile, intents, evaluationCache);
    int nodesGenerated = 0;
    int nodesEvaluated = 0;
    int duplicatesRemoved = 0;
    int evaluationCacheHits = 0;
    bool timedOut = false;
    bool nodeBudgetReached = false;
    bool cancelled = cancellationToken.IsCancellationRequested;
    ICpuGameAction? fallbackAction = null;
    int rootLegalActionCount = 0;
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache = [];
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates = [];
    EvaluationBreakdown initialBreakdown = EvaluateCached(state, team, context, evaluatedStates, ref evaluationCacheHits);
    List<SearchNode> beam = [new SearchNode(state, [], initialBreakdown.Total, initialBreakdown)];
    // Preparation is intentionally included in the time budget; when ranking begins, the search
    // records a legal root fallback before it expands any branch.
    int maximumActions = GetMaximumActionsToPlan(state, team);

    int totalDepth = maximumActions + Math.Max(0, settings.TacticalExtensionDepth);
    for (int depth = 0; depth < totalDepth && !cancelled && !timedOut && !nodeBudgetReached; depth++)
    {
      List<SearchNode> expanded = [];
      Dictionary<ulong, float> bestScoreByState = [];
      List<PendingSearchExpansion> pending = [];
      foreach (SearchNode node in beam)
      {
        if (ShouldStop(stopwatch, settings, nodesGenerated + pending.Count, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
        {
          break;
        }
        if (node.State.IsFinished || node.State.CurrentTurn != team)
        {
          expanded.Add(node);
          continue;
        }

        int placementLimit = GetPurchasePlacementLimit(settings, 16);
        IReadOnlyList<ICpuGameAction> legal = GetSearchActions(
          node.State,
          team,
          placementLimit,
          searchActionCache
        );
        if (node.Actions.Count == 0)
        {
          rootLegalActionCount = legal.Count;
        }
        if (ShouldStop(stopwatch, settings, nodesGenerated + pending.Count, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
        {
          break;
        }
        // Search actions have already passed the complete rule facade. Candidate selectors are
        // an extension point, so retain the membership gate, but avoid recalculating movement
        // paths and line-of-sight merely to prove the generated action legal a second time.
        HashSet<ICpuGameAction> legalActionSet = [.. legal];
        IReadOnlyList<ScoredAction> candidates = _candidateSelector.SelectCandidates(
          node.State, team, legal, settings, profile.Personality);
        candidates = candidates.Where(candidate => legalActionSet.Contains(candidate.Action)).ToArray();
        candidates = ApplyAttackAndReservePriorities(node.State, team, candidates);
        if (depth >= maximumActions)
        {
          // Quiescence extension: once the ordinary horizon is reached, only continue forcing
          // exchanges. This prevents the search from stopping halfway through an obvious attack
          // sequence without ballooning into another full quiet-move layer.
          candidates = candidates.Where(candidate => IsForcingAction(candidate.Action)).ToArray();
        }
        foreach (ScoredAction candidate in candidates)
        {
          // Keep the strongest legal root candidate as a safe fallback. Generation can
          // occasionally use most of a very small budget. Capture it before the post-ranking
          // time check so a live CPU turn cannot stall simply because candidate scoring consumed
          // the final millisecond of a tiny budget.
          if (node.Actions.Count == 0 && fallbackAction is null)
          {
            fallbackAction = candidate.Action;
          }

          if (ShouldStop(stopwatch, settings, nodesGenerated + pending.Count, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
          {
            break;
          }
          pending.Add(new PendingSearchExpansion(node, candidate));
        }
      }

      int parallelism = GetParallelism(settings);
      // A large beam previously ran as one indivisible Parallel.For call. On a busy board that
      // could overrun the advertised time budget by an entire layer. Small ordered batches let
      // the CPU finish a little in-flight work after the soft deadline, then stop before it
      // starts another wave.
      foreach (PendingSearchExpansion[] batch in pending.Chunk(GetEvaluationBatchSize(parallelism)))
      {
        if (ShouldStop(stopwatch, settings, nodesGenerated, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
        {
          break;
        }

        IReadOnlyList<EvaluatedSearchExpansion> evaluated = EvaluatePendingBranches(
          batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, settings,
          cancellationToken, ref evaluationCacheHits);
        foreach (EvaluatedSearchExpansion branch in evaluated)
        {
          nodesGenerated++;
          nodesEvaluated++;
          SearchNode node = branch.Pending.Node;
          ScoredAction candidate = branch.Pending.Candidate;
          // Preserve tactical urgency across the turn. Pure material evaluation otherwise
          // overvalues buying before taking an immediately available kill.
          float accumulatedActionPriority = node.Score - node.Breakdown.Total;
          // A completed match or campaign objective is decisive. Do not let accumulated
          // convenience bonuses make a purchase-before-win line outrank the move that ends
          // the mission immediately.
          float score = branch.Result.IsFinished
            ? branch.Breakdown.Total
            : branch.Breakdown.Total + accumulatedActionPriority + candidate.Score;
          ulong hash = _hasher.ComputeSearchHash(branch.Result);
          if (bestScoreByState.TryGetValue(hash, out float existingScore) && existingScore >= score)
          {
            duplicatesRemoved++;
            continue;
          }
          bestScoreByState[hash] = score;
          expanded.Add(new SearchNode(branch.Result, [.. node.Actions, candidate.Action], score, branch.Breakdown));
        }
      }

      if (expanded.Count == 0)
      {
        break;
      }
      beam = expanded
        .OrderByDescending(node => node.Score)
        .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
        .Take(Math.Max(1, settings.BeamWidth))
        .ToList();
    }

    List<(SearchNode Node, float Score, float OpponentPenalty)> ranked = [];
    foreach (SearchNode node in beam)
    {
      if (ShouldStop(stopwatch, settings, nodesGenerated, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
      {
        break;
      }
      float adjustedScore = node.Score;
      float opponentPenalty = 0f;
      if (settings.OpponentActionsToPredict > 0 && node.State.Winner is null && node.State.CurrentTurn != team)
      {
        float afterOpponent = PredictOpponentResponse(node.State, team, profile, context, stopwatch, cancellationToken,
          searchActionCache, evaluatedStates, ref evaluationCacheHits, ref nodesGenerated, ref nodesEvaluated,
          ref timedOut, ref nodeBudgetReached, ref cancelled);
        opponentPenalty = Math.Max(0f, node.Score - afterOpponent);
        adjustedScore = afterOpponent;
      }
      ranked.Add((node, adjustedScore, opponentPenalty));
    }

    if (ranked.Count == 0 && fallbackAction is not null)
    {
      ranked.Add((new SearchNode(state, [fallbackAction], initialBreakdown.Total, initialBreakdown), initialBreakdown.Total, 0f));
    }
    else if (ranked.Count == 0)
    {
      ranked.Add((beam[0], beam[0].Score, 0f));
    }
    ranked = ranked
      .OrderByDescending(entry => entry.Score)
      .ThenBy(entry => DescribeActions(entry.Node.Actions), StringComparer.Ordinal)
      .ToList();
    (SearchNode Node, float Score, float OpponentPenalty) chosen = ChooseRanked(ranked, profile, initialStateHash);
    List<CpuChoiceReport> choices = ranked.Take(5).Select(entry => new CpuChoiceReport
    {
      Actions = entry.Node.Actions.Select(action => action.Describe()).ToArray(),
      FinalScore = entry.Score,
      EvaluationTerms = entry.Node.Breakdown.Terms,
      OpponentResponsePenalty = entry.OpponentPenalty,
      Reason = entry.Node.Actions.Count == 0 ? "No legal action was available." : Explain(entry.Node.Actions)
    }).ToList();
    stopwatch.Stop();
    CpuDecisionReport report = new()
    {
      ProfileName = profile.Name,
      Difficulty = profile.Difficulty,
      Personality = profile.Personality,
      InitialStateHash = initialStateHash,
      RootLegalActionCount = rootLegalActionCount,
      Intentions = intents,
      SearchTime = stopwatch.Elapsed,
      NodesGenerated = nodesGenerated,
      NodesEvaluated = nodesEvaluated,
      DuplicateStatesRemoved = duplicatesRemoved,
      EvaluationCacheHits = evaluationCacheHits,
      TimedOut = timedOut,
      NodeBudgetReached = nodeBudgetReached,
      Cancelled = cancelled,
      TopChoices = choices
    };
    IReadOnlyList<ICpuGameAction> chosenActions = chosen.Node.Actions;
    // A completed kill is a hard tactical fact, not a heuristic preference. Under a tight
    // deadline a deeper branch can otherwise put a nonlethal attack first because its later
    // material line looks slightly better. Ensure the final legal plan starts with an available
    // lethal attack; VerifyActionSequence will discard any now-illegal follow-up actions.
    AttackAction? immediateLethal = GetImmediateLethalAttack(state, team, settings, searchActionCache);
    if (immediateLethal is not null &&
        (chosenActions.Count == 0 || !Equals(chosenActions[0], immediateLethal)))
    {
      chosenActions = [immediateLethal, .. chosenActions];
    }
    IReadOnlyList<ICpuGameAction> verifiedActions = VerifyActionSequence(state, team, profile, chosenActions);
    return new CpuTurnPlan(verifiedActions, chosen.Score, report);
  }

  private AttackAction? GetImmediateLethalAttack(
    CpuGameState state,
    NetworkTeam team,
    CpuSearchSettings settings,
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache
  )
  {
    foreach (AttackAction attack in GetSearchActions(
      state, team, GetPurchasePlacementLimit(settings, 16), searchActionCache).OfType<AttackAction>())
    {
      if (attack.TargetPieceId is null || !attack.IsLegal(state))
      {
        continue;
      }

      CpuGameState result = CpuGameRules.ApplyLegal(state, attack);
      if (!result.Pieces.Any(piece => piece.Id == attack.TargetPieceId))
      {
        return attack;
      }
    }
    return null;
  }

  private static int GetMaximumActionsToPlan(CpuGameState state, NetworkTeam team)
  {
    if (state.InitialBuy is not null)
    {
      return 1;
    }

    if (Globals.ActionLimitsEnabled)
    {
      return Math.Clamp(state.ActionsRemaining, 0, MatchRules.ActionsPerTurn);
    }

    // There is no action-point ceiling in this mode. Pieces can still only move and attack once
    // per turn, while spending remains naturally limited by the available gold. The search's
    // normal node and time budgets remain the safety bounds for large battles.
    int pieceActions = state.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null) * 2;
    int lowestPurchaseCost = UnitRules.Purchasable.Min(rule => Math.Max(1, rule.Cost));
    int affordablePurchases = state.Teams.TryGetValue(team, out CpuTeamState? stateTeam)
      ? Math.Min(24, Math.Max(0, stateTeam.Money) / lowestPurchaseCost)
      : 0;
    return Math.Max(1, Math.Min(64, pieceActions + affordablePurchases + 1));
  }

  private static IReadOnlyList<ScoredAction> ApplyAttackAndReservePriorities(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ScoredAction> candidates
  )
  {
    ScoredAction[] attacks = candidates.Where(candidate => candidate.Action is AttackAction { TargetPieceId: not null }).ToArray();
    if (attacks.Length > 0)
    {
      return attacks;
    }

    ScoredAction[] nearbyNeutralHires = candidates.Where(candidate => candidate.Action is PurchaseAction { UnitType: "Mercenary" } purchase &&
      IsFullHealthNeutralMercenaryAt(state, purchase.X, purchase.Y) &&
      candidates.Any(other => other.Action is PurchaseAction regular && regular.UnitType != "Mercenary" &&
        Distance((purchase.X, purchase.Y), (regular.X, regular.Y)) <= 4)).ToArray();
    if (nearbyNeutralHires.Length > 0)
    {
      return nearbyNeutralHires;
    }

    // A move that ends the match remains urgent, but ordinary purchases are deliberately left
    // to the army planner's counter/reserve/economy scores. Forcing every high-cash position
    // into the combat-purchase family was what caused one-unit spam and starved safe farms.
    ScoredAction[] immediateWins = candidates.Where(candidate => candidate.Action is MoveAction &&
      CpuGameRules.ApplyLegal(state, candidate.Action).IsFinished).ToArray();
    if (immediateWins.Length > 0)
    {
      return immediateWins;
    }

    if (state.Teams.TryGetValue(team, out CpuTeamState? cpuTeam) && cpuTeam.Money >= 80)
    {
      ScoredAction[] combatPurchases = candidates.Where(candidate => candidate.Action is PurchaseAction purchase &&
        UnitRules.TryGet(purchase.UnitType, out UnitRule rule) && rule.Attack > 0).ToArray();
      if (combatPurchases.Length > 0)
      {
        float bestCombatScore = combatPurchases.Max(candidate => candidate.Score);
        // The planner may correctly decide that a protected farm is the best spend on a quiet
        // board. Retain it only when it genuinely beats the best available combat role; otherwise
        // spend the surplus on one of the counter-aware combat candidates.
        ScoredAction[] highValueSpends = candidates.Where(candidate => candidate.Action is PurchaseAction purchase &&
          (UnitRules.TryGet(purchase.UnitType, out UnitRule rule) && rule.Attack > 0 ||
           purchase.UnitType == "Farm" && candidate.Score >= bestCombatScore))
          .Where(candidate => candidate.Score >= bestCombatScore - 16f)
          .ToArray();
        if (highValueSpends.Length > 0)
        {
          return highValueSpends;
        }
      }
    }

    if (!Globals.ActionLimitsEnabled)
    {
      // With unlimited team actions, every unit still has its own once-per-turn move. Keep
      // cycling through those moves instead of allowing a quiet End Turn while useful pieces
      // are idle. The next search layer rechecks attacks after each move.
      ScoredAction[] moves = candidates.Where(candidate => candidate.Action is MoveAction).ToArray();
      if (moves.Length > 0)
      {
        return moves;
      }
    }

    return candidates;
  }

  private static bool IsFullHealthNeutralMercenaryAt(CpuGameState state, int x, int y) => state.Pieces.Any(piece =>
    piece.Type == "Mercenary" && piece.Team == NetworkTeam.Neutral && piece.X == x && piece.Y == y &&
    UnitRules.TryGet(piece.Type, out UnitRule rule) && piece.Health >= rule.Health);

  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);

  /// <summary>
  /// Final defence before a worker result leaves CPU code. It is intentionally small (at most one
  /// turn) and ensures a stale or externally supplied candidate cannot be handed to presentation.
  /// </summary>
  private IReadOnlyList<ICpuGameAction> VerifyActionSequence(
    CpuGameState state,
    NetworkTeam team,
    CpuProfile profile,
    IReadOnlyList<ICpuGameAction> actions
  )
  {
    List<ICpuGameAction> verified = [];
    CpuGameState current = state;
    // Difficulty changes only the deadline. Every normal profile keeps the same tactical policy
    // once a plan is selected, including resolving available attacks before ending an unlimited
    // turn. A deliberately one-node caller requested exactly one analysed action, so do not add
    // an unsearched follow-up that was not legal in the original snapshot.
    bool preserveAvailableAttacks = profile.Search.MaxSearchNodes > 1;
    foreach (ICpuGameAction action in actions)
    {
      // A narrow beam can settle on a quiet continuation after an earlier attack. In an
      // unlimited-action match, preserve the medium-and-stronger policy of resolving every
      // available direct attack before allowing that quiet continuation.
      while (preserveAvailableAttacks && action is not AttackAction &&
             current.CurrentTurn == team && !current.IsFinished)
      {
        AttackAction? availableAttack = _actionGenerator.GenerateSearchActions(current, team, 1)
          .OfType<AttackAction>()
          .FirstOrDefault(candidate => candidate.TargetPieceId is not null);
        if (availableAttack is null)
        {
          break;
        }
        verified.Add(availableAttack);
        current = availableAttack.Apply(current);
      }
      if (current.IsFinished || current.CurrentTurn != team || !action.IsLegal(current))
      {
        break;
      }
      verified.Add(action);
      current = action.Apply(current);
    }
    while (preserveAvailableAttacks && current.CurrentTurn == team && !current.IsFinished)
    {
      AttackAction? availableAttack = _actionGenerator.GenerateSearchActions(current, team, 1)
        .OfType<AttackAction>()
        .FirstOrDefault(candidate => candidate.TargetPieceId is not null);
      if (availableAttack is null)
      {
        break;
      }
      verified.Add(availableAttack);
      current = availableAttack.Apply(current);
    }
    if (!Globals.ActionLimitsEnabled && actions.Any(action => action is MoveAction) &&
        current.CurrentTurn == team && !current.IsFinished)
    {
      // Unlimited turns are intended to let every unit contribute once. If the bounded beam has
      // already committed to moving this turn, finish the remaining legal unit moves rather than
      // end after only the highest-scored one. Recheck attacks after every movement so a newly
      // created firing line is still resolved before the turn ends.
      while (current.CurrentTurn == team && !current.IsFinished)
      {
        AttackAction? availableAttack = _actionGenerator.GenerateSearchActions(current, team, 1)
          .OfType<AttackAction>()
          .FirstOrDefault(candidate => candidate.TargetPieceId is not null);
        if (availableAttack is not null)
        {
          verified.Add(availableAttack);
          current = availableAttack.Apply(current);
          continue;
        }

        MoveAction? availableMove = _actionGenerator.GenerateSearchActions(current, team, 1)
          .OfType<MoveAction>()
          .FirstOrDefault();
        if (availableMove is null)
        {
          break;
        }
        verified.Add(availableMove);
        current = availableMove.Apply(current);
      }
    }
    if (!Globals.ActionLimitsEnabled && state.InitialBuy is null && current.CurrentTurn == team && !current.IsFinished)
    {
      EndTurnAction endTurn = new(team);
      if (endTurn.IsLegal(current))
      {
        verified.Add(endTurn);
      }
    }

    return verified;
  }

  /// <summary>
  /// Opening farms are free, one-action placements with a deterministic terrain/territory ranking.
  /// Running a full tactical beam search for them wastes the CPU budget and can make the opening
  /// feel stalled, so select the first legal protected placement directly.
  /// </summary>
  private CpuTurnPlan ChooseOpeningFarmPlacement(
    CpuGameState state,
    NetworkTeam team,
    CpuProfile profile,
    CancellationToken cancellationToken,
    Stopwatch stopwatch,
    ulong initialStateHash
  )
  {
    bool cancelled = cancellationToken.IsCancellationRequested;
    int placementLimit = Math.Clamp(profile.Search.MaximumPurchasePlacementCandidates, 1, 8);
    IReadOnlyList<ICpuGameAction> actions = cancelled
      ? []
      : _actionGenerator.GenerateSearchActions(state, team, placementLimit);
    PurchaseAction? farm = ChooseOpeningFarm(actions, state, profile, initialStateHash);
    float score = farm is null
      ? 0f
      : CpuPlacementHeuristics.GetFarmProtectionScore(state, team, farm.X, farm.Y);
    stopwatch.Stop();
    IReadOnlyList<ICpuGameAction> planActions = farm is null ? [] : [farm];
    CpuDecisionReport report = new()
    {
      ProfileName = profile.Name,
      Difficulty = profile.Difficulty,
      Personality = profile.Personality,
      InitialStateHash = initialStateHash,
      RootLegalActionCount = actions.Count,
      SearchTime = stopwatch.Elapsed,
      Cancelled = cancelled,
      TimedOut = !cancelled && stopwatch.ElapsedMilliseconds >= Math.Max(1, profile.Search.MaxSearchMilliseconds),
      TopChoices =
      [
        new CpuChoiceReport
        {
          Actions = planActions.Select(action => action.Describe()).ToArray(),
          FinalScore = score,
          Reason = farm is null
            ? "No legal opening farm placement was available."
            : "Places the highest-ranked legal farm square for terrain cover and rear-territory safety."
        }
      ]
    };
    return new CpuTurnPlan(planActions, score, report);
  }

  /// <summary>
  /// Opening farms should not make every non-Best match begin identically. Variety is limited to
  /// similarly protected legal squares, so a lower difficulty has a recognisable opening style
  /// without making an arbitrary or self-destructive placement.
  /// </summary>
  private static PurchaseAction? ChooseOpeningFarm(
    IReadOnlyList<ICpuGameAction> actions,
    CpuGameState state,
    CpuProfile profile,
    ulong stateHash
  )
  {
    (PurchaseAction Action, float Score)[] farms = actions.OfType<PurchaseAction>()
      .Where(action => action.UnitType == "Farm" && action.IsLegal(state))
      .Select(action => (Action: action, Score: CpuPlacementHeuristics.GetFarmProtectionScore(state, action.Team, action.X, action.Y)))
      .OrderByDescending(entry => entry.Score)
      .ThenBy(entry => entry.Action.Y)
      .ThenBy(entry => entry.Action.X)
      .ToArray();
    if (farms.Length == 0)
    {
      return null;
    }
    if (profile.TopChoicesForRandomSelection <= 1 ||
        (profile.MistakeChance <= 0f && profile.StrategyVariationChance <= 0f))
    {
      return farms[0].Action;
    }

    float bestScore = farms[0].Score;
    PurchaseAction[] comparable = farms
      .Where(entry => bestScore - entry.Score <= 2.5f)
      .Take(Math.Max(1, profile.TopChoicesForRandomSelection))
      .Select(entry => entry.Action)
      .ToArray();
    if (comparable.Length <= 1)
    {
      return farms[0].Action;
    }

    int seed = unchecked(profile.RandomSeed ^ (int)stateHash ^ (int)(stateHash >> 32));
    Random random = new(seed);
    return random.NextDouble() < Math.Clamp(profile.MistakeChance + profile.StrategyVariationChance + profile.Search.Randomness, 0f, 1f)
      ? comparable[1 + random.Next(comparable.Length - 1)]
      : farms[0].Action;
  }

  private float PredictOpponentResponse(
    CpuGameState state,
    NetworkTeam perspective,
    CpuProfile profile,
    EvaluationContext context,
    Stopwatch stopwatch,
    CancellationToken cancellationToken,
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int evaluationCacheHits,
    ref int nodesGenerated,
    ref int nodesEvaluated,
    ref bool timedOut,
    ref bool nodeBudgetReached,
    ref bool cancelled
  )
  {
    NetworkTeam opponent = state.CurrentTurn;
    int actionsToPredict = Globals.ActionLimitsEnabled
      ? Math.Min(profile.Search.OpponentActionsToPredict, state.ActionsRemaining)
      : profile.Search.OpponentActionsToPredict;
    if (actionsToPredict <= 0)
    {
      return EvaluateCached(state, perspective, context, evaluatedStates, ref evaluationCacheHits).Total;
    }

    // Medium mode looks only one opponent action ahead. Hard looks three and Best five, so they
    // model a longer enemy turn with a deliberately narrower beam instead of greedily fixing
    // the first reply and missing a move-then-attack combination.
    int opponentBeamWidth = Math.Max(1, profile.Search.OpponentBeamWidth);
    int placementLimit = GetPurchasePlacementLimit(profile.Search, 12);
    CpuSearchSettings opponentSettings = new()
    {
      BeamWidth = opponentBeamWidth,
      CandidatesPerNode = opponentBeamWidth,
      MaximumPurchasePlacementCandidates = profile.Search.MaximumPurchasePlacementCandidates,
      MaxSearchMilliseconds = profile.Search.MaxSearchMilliseconds
    };
    EvaluationBreakdown initialBreakdown = EvaluateCached(state, perspective, context, evaluatedStates, ref evaluationCacheHits);
    List<SearchNode> beam = [new SearchNode(state, [], initialBreakdown.Total, initialBreakdown)];

    for (int depth = 0; depth < actionsToPredict; depth++)
    {
      List<SearchNode> expanded = [];
      Dictionary<ulong, float> worstScoreByState = [];
      foreach (SearchNode node in beam)
      {
        if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
          out timedOut, out nodeBudgetReached, out cancelled))
        {
          break;
        }

        if (node.State.IsFinished || node.State.CurrentTurn != opponent)
        {
          expanded.Add(node);
          continue;
        }

        IReadOnlyList<ICpuGameAction> legal = GetSearchActions(
          node.State,
          opponent,
          placementLimit,
          searchActionCache
        );
        HashSet<ICpuGameAction> legalActionSet = [.. legal];
        IReadOnlyList<ScoredAction> candidates = _candidateSelector.SelectCandidates(
          node.State, opponent, legal, opponentSettings, CpuPersonality.Aggressive);
        foreach (ScoredAction candidate in candidates)
        {
          if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
            out timedOut, out nodeBudgetReached, out cancelled))
          {
            break;
          }

          // Keep extension-point candidates constrained to the action generator's legal set.
          if (!legalActionSet.Contains(candidate.Action))
          {
            continue;
          }

          CpuGameState result = CpuGameRules.ApplyLegal(node.State, candidate.Action);
          nodesGenerated++;
          EvaluationBreakdown breakdown = EvaluateCached(result, perspective, context, evaluatedStates, ref evaluationCacheHits);
          nodesEvaluated++;
          // The opponent minimises the CPU perspective. Collapse equivalent continuations at
          // each depth so a transposition cannot consume the narrow reply beam twice.
          ulong hash = _hasher.ComputeSearchHash(result);
          if (worstScoreByState.TryGetValue(hash, out float existingScore) && existingScore <= breakdown.Total)
          {
            continue;
          }
          worstScoreByState[hash] = breakdown.Total;
          expanded.Add(new SearchNode(result, [.. node.Actions, candidate.Action], breakdown.Total, breakdown));
        }
      }

      if (expanded.Count == 0)
      {
        break;
      }
      beam = expanded
        .OrderBy(node => node.Score)
        .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
        .Take(opponentBeamWidth)
        .ToList();
      if (cancelled || timedOut || nodeBudgetReached)
      {
        break;
      }
    }

    return beam.Min(node => node.Score);
  }

  private IReadOnlyList<ICpuGameAction> GetSearchActions(
    CpuGameState state,
    NetworkTeam team,
    int placementLimit,
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> cache
  )
  {
    (ulong stateHash, NetworkTeam team, int placementLimit) key = (_hasher.ComputeSearchHash(state), team, placementLimit);
    if (!cache.TryGetValue(key, out IReadOnlyList<ICpuGameAction>? actions))
    {
      actions = _actionGenerator.GenerateSearchActions(state, team, placementLimit);
      cache[key] = actions;
    }
    return actions;
  }

  private static int GetPurchasePlacementLimit(CpuSearchSettings settings, int minimumUsefulCandidates) =>
    Math.Max(1, Math.Min(
      Math.Max(1, settings.MaximumPurchasePlacementCandidates),
      Math.Max(minimumUsefulCandidates, settings.CandidatesPerNode * 3)
    ));

  private int GetParallelism(CpuSearchSettings settings)
  {
    if (!_canParallelizeEvaluation || settings.MaxParallelism == 1)
    {
      return 1;
    }

    if (settings.MaxParallelism > 1)
    {
      return settings.MaxParallelism;
    }

    // Leave one logical processor for the game/UI and cap worker pressure on high-core PCs.
    // Two-core machines remain single-threaded because dedicating half the machine to a turn
    // search is more disruptive than the small speed-up is worth.
    return Environment.ProcessorCount <= 2 ? 1 : Math.Min(6, Environment.ProcessorCount - 1);
  }

  private IReadOnlyList<EvaluatedSearchExpansion> EvaluatePendingBranches(
    IReadOnlyList<PendingSearchExpansion> pending,
    NetworkTeam team,
    CpuProfile profile,
    IReadOnlyList<CpuIntent> intents,
    EvaluationContext sequentialContext,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    int parallelism,
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    CancellationToken cancellationToken,
    ref int cacheHits
  )
  {
    if (pending.Count == 0)
    {
      return [];
    }

    if (parallelism <= 1 || pending.Count == 1)
    {
      List<EvaluatedSearchExpansion> sequential = new(pending.Count);
      foreach (PendingSearchExpansion branch in pending)
      {
        if (ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken))
        {
          break;
        }
        CpuGameState result = CpuGameRules.ApplyLegal(branch.Node.State, branch.Candidate.Action);
        EvaluationBreakdown breakdown = EvaluateCached(result, team, sequentialContext, evaluatedStates, ref cacheHits);
        sequential.Add(new EvaluatedSearchExpansion(branch, result, breakdown));
      }
      return sequential;
    }

    EvaluatedSearchExpansion?[] parallel = new EvaluatedSearchExpansion[pending.Count];
    using ThreadLocal<EvaluationContext> workerContexts = new(() => new EvaluationContext(profile, intents, new CpuEvaluationCache()));
    Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
    {
      if (ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken))
      {
        return;
      }
      PendingSearchExpansion branch = pending[index];
      CpuGameState result = CpuGameRules.ApplyLegal(branch.Node.State, branch.Candidate.Action);
      // Each worker owns its cache. The main cache uses Dictionary and intentionally stays on
      // the coordinator thread; this avoids locks in its hottest lookup path.
      EvaluationBreakdown breakdown = _evaluator.EvaluateWithBreakdown(result, team, workerContexts.Value!);
      if (!ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken))
      {
        parallel[index] = new EvaluatedSearchExpansion(branch, result, breakdown);
      }
    });
    return parallel.Where(branch => branch is not null).Select(branch => branch!).ToArray();
  }

  private static int GetEvaluationBatchSize(int parallelism) => Math.Clamp(
    Math.Max(1, parallelism) * 2,
    4,
    16
  );

  private static bool ShouldAbortBranchEvaluation(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    CancellationToken cancellationToken
  ) => cancellationToken.IsCancellationRequested ||
    stopwatch.ElapsedMilliseconds >= GetHardDeadlineMilliseconds(settings);

  /// <summary>
  /// The advertised limit is a soft search deadline: no new branch wave begins after it.
  /// Let a small in-flight evaluation batch complete within this tightly bounded grace window so
  /// the CPU can return its best completed line instead of discarding useful work at the exact
  /// millisecond boundary. This is intentionally short even for Best to keep the UI responsive.
  /// </summary>
  private static int GetHardDeadlineMilliseconds(CpuSearchSettings settings)
  {
    int softDeadline = Math.Max(1, settings.MaxSearchMilliseconds);
    int grace = Math.Clamp(softDeadline / 25, 12, 80);
    return softDeadline + grace;
  }

  private static bool ShouldStop(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    int nodesGenerated,
    CancellationToken cancellationToken,
    out bool timedOut,
    out bool nodeBudgetReached,
    out bool cancelled
  )
  {
    cancelled = cancellationToken.IsCancellationRequested;
    timedOut = stopwatch.ElapsedMilliseconds >= Math.Max(1, settings.MaxSearchMilliseconds);
    nodeBudgetReached = nodesGenerated >= Math.Max(1, settings.MaxSearchNodes);
    return cancelled || timedOut || nodeBudgetReached;
  }

  private static (SearchNode Node, float Score, float OpponentPenalty) ChooseRanked(
    IReadOnlyList<(SearchNode Node, float Score, float OpponentPenalty)> ranked,
    CpuProfile profile,
    ulong stateHash
  )
  {
    if (ranked.Count == 1 || profile.TopChoicesForRandomSelection <= 1 ||
        (profile.MistakeChance <= 0f && profile.StrategyVariationChance <= 0f))
    {
      return ranked[0];
    }

    float scoreWindow = Math.Max(0f, profile.Search.TopChoiceScoreWindow);
    IReadOnlyList<(SearchNode Node, float Score, float OpponentPenalty)> comparable = ranked
      .Take(Math.Max(1, profile.TopChoicesForRandomSelection))
      .Where(entry => ranked[0].Score - entry.Score <= scoreWindow)
      .ToArray();
    if (comparable.Count <= 1)
    {
      return ranked[0];
    }

    // The state hash makes a profile reproducible for a fixed seed while allowing later turns
    // to vary naturally. Only plans already close to the best score are eligible.
    int seed = unchecked(profile.RandomSeed ^ (int)stateHash ^ (int)(stateHash >> 32));
    Random random = new(seed);
    if (random.NextDouble() >= Math.Clamp(profile.MistakeChance + profile.StrategyVariationChance + profile.Search.Randomness, 0f, 1f))
    {
      return ranked[0];
    }
    return comparable[1 + random.Next(comparable.Count - 1)];
  }

  private EvaluationBreakdown EvaluateCached(
    CpuGameState state,
    NetworkTeam perspective,
    EvaluationContext context,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int cacheHits
  )
  {
    ulong hash = _hasher.ComputeSearchHash(state);
    if (evaluatedStates.TryGetValue(hash, out EvaluationBreakdown? cached))
    {
      cacheHits++;
      return cached;
    }

    EvaluationBreakdown evaluation = _evaluator.EvaluateWithBreakdown(state, perspective, context);
    evaluatedStates[hash] = evaluation;
    return evaluation;
  }

  private static bool IsForcingAction(ICpuGameAction action) => action is AttackAction or UseAbilityAction
  {
    Ability: "PickUpTreasure"
  };

  private static string DescribeActions(IEnumerable<ICpuGameAction> actions) => string.Join(" | ", actions.Select(action => action.Describe()));

  private static string Explain(IReadOnlyList<ICpuGameAction> actions) => actions[^1] switch
  {
    AttackAction => "The selected sequence prioritises a tactical attack.",
    MoveAction => "The selected sequence improves the position toward its objective.",
    PurchaseAction => "The selected sequence adds a useful unit within the available economy.",
    UseAbilityAction ability => $"The selected sequence uses {ability.Ability} at a useful moment.",
    EndTurnAction => "The selected sequence conserves the remaining actions.",
    _ => "The selected sequence has the strongest bounded utility score."
  };
}

/// <summary>Stable FNV-1a hash of all gameplay-relevant state for duplicate beam nodes.</summary>
public sealed class GameStateHasher
{
  public ulong ComputeSearchHash(CpuGameState state)
  {
    ulong hash = 14695981039346656037UL;
    Add(ref hash, state.CurrentTurn);
    Add(ref hash, state.ActionsRemaining);
    Add(ref hash, state.TurnNumber);
    Add(ref hash, state.Winner ?? NetworkTeam.Neutral);
    if (state.InitialBuy is NetworkInitialBuyState initialBuy)
    {
      Add(ref hash, true);
      Add(ref hash, initialBuy.CurrentTeam);
      Add(ref hash, initialBuy.PurchasesThisTurn);
      Add(ref hash, initialBuy.PurchasesPerTurn);
      Add(ref hash, initialBuy.RedBuyTurnsUsed);
      Add(ref hash, initialBuy.BlueBuyTurnsUsed);
      Add(ref hash, initialBuy.BuyTurnsPerTeam);
      Add(ref hash, initialBuy.RedStopped);
      Add(ref hash, initialBuy.BlueStopped);
      Add(ref hash, initialBuy.IsComplete);
      Add(ref hash, initialBuy.IsFarmPlacementPhase);
      foreach (NetworkInitialBuyTeamState teamState in (initialBuy.TeamStates ?? []).OrderBy(entry => entry.Team))
      {
        Add(ref hash, teamState.Team);
        Add(ref hash, teamState.BuyTurnsUsed);
        Add(ref hash, teamState.Stopped);
        Add(ref hash, teamState.FarmsPlaced);
      }
    }
    else
    {
      Add(ref hash, false);
    }
    foreach (NetworkPiece piece in state.Pieces.OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      Add(ref hash, piece.Id);
      Add(ref hash, piece.Type);
      Add(ref hash, piece.Team);
      Add(ref hash, piece.X);
      Add(ref hash, piece.Y);
      Add(ref hash, piece.Health);
      Add(ref hash, piece.HasMovedThisTurn);
      Add(ref hash, piece.HasAttackedThisTurn);
      Add(ref hash, piece.AttachedToId ?? string.Empty);
      Add(ref hash, piece.AttachmentKind);
      Add(ref hash, piece.MarkedTargetId ?? string.Empty);
      Add(ref hash, piece.LastBid);
      Add(ref hash, piece.EngineerBuildsThisTurn);
      Add(ref hash, piece.CannotContributeToConquestThisTurn);
      Add(ref hash, piece.CavalierFollowUpMoveAvailable);
    }
    foreach ((NetworkTeam team, CpuTeamState stateTeam) in state.Teams.OrderBy(pair => pair.Key))
    {
      Add(ref hash, team);
      Add(ref hash, stateTeam.Money);
      Add(ref hash, stateTeam.ActionsRemaining);
      Add(ref hash, stateTeam.ChosenRoyal ?? string.Empty);
    }
    foreach (KeyValuePair<(int x, int y), NetworkTeam> road in state.Roads.OrderBy(pair => pair.Key.y).ThenBy(pair => pair.Key.x))
    {
      Add(ref hash, road.Key.x); Add(ref hash, road.Key.y); Add(ref hash, road.Value);
    }
    foreach (KeyValuePair<(int x, int y), int> barricade in state.Barricades.OrderBy(pair => pair.Key.y).ThenBy(pair => pair.Key.x))
    {
      Add(ref hash, barricade.Key.x); Add(ref hash, barricade.Key.y); Add(ref hash, barricade.Value);
    }
    foreach (KeyValuePair<(int x, int y), NetworkTeam> mine in state.Mines.OrderBy(pair => pair.Key.y).ThenBy(pair => pair.Key.x))
    {
      Add(ref hash, mine.Key.x); Add(ref hash, mine.Key.y); Add(ref hash, mine.Value);
    }
    foreach (TileEdge bridge in state.RiverBridges
      .OrderBy(edge => edge.First.x).ThenBy(edge => edge.First.y)
      .ThenBy(edge => edge.Second.x).ThenBy(edge => edge.Second.y))
    {
      Add(ref hash, bridge.First.x); Add(ref hash, bridge.First.y);
      Add(ref hash, bridge.Second.x); Add(ref hash, bridge.Second.y);
    }
    Add(ref hash, state.ConquestScore);
    foreach (KeyValuePair<NetworkTeam, int> score in state.ConquestScores.OrderBy(pair => pair.Key))
    {
      Add(ref hash, score.Key); Add(ref hash, score.Value);
    }
    foreach (KeyValuePair<NetworkTeam, int> score in state.ModeScores.OrderBy(pair => pair.Key))
    {
      Add(ref hash, score.Key); Add(ref hash, score.Value);
    }
    Add(ref hash, state.TreasurePosition?.x ?? int.MinValue);
    Add(ref hash, state.TreasurePosition?.y ?? int.MinValue);
    Add(ref hash, state.TreasureCarrierId ?? string.Empty);
    foreach (CpuMoveRecord move in state.RecentMoves)
    {
      Add(ref hash, move.Team);
      Add(ref hash, move.PieceId);
      Add(ref hash, move.FromX);
      Add(ref hash, move.FromY);
      Add(ref hash, move.ToX);
      Add(ref hash, move.ToY);
      Add(ref hash, move.TurnNumber);
    }
    return hash;
  }

  private static void Add(ref ulong hash, int value)
  {
    unchecked
    {
      hash ^= (uint)value;
      hash *= 1099511628211UL;
    }
  }

  private static void Add(ref ulong hash, bool value) => Add(ref hash, value ? 1 : 0);
  private static void Add(ref ulong hash, NetworkTeam value) => Add(ref hash, (int)value);
  private static void Add(ref ulong hash, NetworkAttachmentKind value) => Add(ref hash, (int)value);

  private static void Add(ref ulong hash, string value)
  {
    foreach (char character in value)
    {
      Add(ref hash, character);
    }
    Add(ref hash, 0);
  }
}
