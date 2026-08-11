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

internal sealed record SearchIterationResult(
  List<SearchNode> Beam,
  bool Completed,
  ICpuGameAction? FallbackAction,
  int RootLegalActionCount,
  int PrincipalVariationPromotions,
  int TacticalMacrosGenerated
);

public readonly record struct CpuOpponentSearchShape(int ActionsToPredict, int BeamWidth, bool IsTactical);

/// <summary>
/// Concentrates reply-search effort on positions where the opponent has a concrete tactical shot.
/// Quiet positions still receive a reply check, but lethal attacks, strategically important
/// targets, and expensive exposed assets retain the full configured opponent horizon and beam.
/// </summary>
public static class CpuOpponentSearchPolicy
{
  public static CpuOpponentSearchShape Choose(
    CpuGameState state,
    NetworkTeam perspective,
    CpuProfile profile,
    EvaluationContext context,
    ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    int configuredActions = Globals.ActionLimitsEnabled
      ? Math.Min(profile.Search.OpponentActionsToPredict, state.ActionsRemaining)
      : profile.Search.OpponentActionsToPredict;
    int configuredBeam = Math.Max(1, profile.Search.OpponentBeamWidth);
    if (configuredActions <= 1 || state.CurrentTurn == perspective || state.CurrentTurn == NetworkTeam.Neutral)
    {
      return new CpuOpponentSearchShape(Math.Max(0, configuredActions), configuredBeam, false);
    }

    ICpuThreatMapBuilder builder = threatMapBuilder ?? new CpuThreatMapBuilder();
    CpuThreatMap map = context.Cache.GetThreatMap(state, state.CurrentTurn, builder);
    float urgency = 0f;
    int threatened = 0;
    foreach (NetworkPiece target in state.Pieces.Where(piece => piece.Team == perspective && piece.AttachedToId is null))
    {
      CpuPieceThreat? threat = map.GetThreat(target.Id);
      if (threat is null || !UnitRules.TryGet(target.Type, out UnitRule rule))
      {
        continue;
      }

      threatened++;
      urgency += 1f;
      if (threat.IsLethal) urgency += 4f;
      if (threat.IsStrategicallyImportant) urgency += 3f;
      if (rule.Cost >= 40) urgency += 2f;
      if (state.TreasureCarrierId == target.Id) urgency += 3f;
    }

    if (urgency >= 5f)
    {
      return new CpuOpponentSearchShape(configuredActions, configuredBeam, true);
    }

    if (threatened > 0)
    {
      int activeActions = Math.Max(2, (int)Math.Ceiling(configuredActions * 0.7));
      int activeBeam = Math.Max(6, (int)Math.Ceiling(configuredBeam * 0.7));
      return new CpuOpponentSearchShape(Math.Min(configuredActions, activeActions), Math.Min(configuredBeam, activeBeam), true);
    }

    // No immediate tactical contact: keep enough search to catch move-then-attack macros, while
    // avoiding five-ply/full-beam proof of a quiet reply that can consume the whole turn budget.
    int quietActions = Math.Max(2, (int)Math.Ceiling(configuredActions * 0.4));
    int quietBeam = Math.Max(4, (int)Math.Ceiling(configuredBeam * 0.5));
    return new CpuOpponentSearchShape(Math.Min(configuredActions, quietActions), Math.Min(configuredBeam, quietBeam), false);
  }
}

/// <summary>
/// Search-only compound action. It consumes two ordinary legal actions in one beam expansion,
/// then is flattened back to those ordinary actions before a CpuTurnPlan leaves the CPU layer.
/// </summary>
public sealed class TacticalMacroAction : ICpuGameAction
{
  public TacticalMacroAction(NetworkTeam team, IReadOnlyList<ICpuGameAction> actions)
  {
    if (actions is null || actions.Count < 2) throw new ArgumentException("A tactical macro requires at least two actions.", nameof(actions));
    if (actions.Any(action => action.Team != team)) throw new ArgumentException("Every macro action must belong to the same team.", nameof(actions));
    Team = team;
    Actions = actions.ToArray();
  }

  public NetworkTeam Team { get; }
  public IReadOnlyList<ICpuGameAction> Actions { get; }
  public CpuActionKind Kind => Actions[0].Kind;

  public bool IsLegal(CpuGameState state)
  {
    CpuGameState current = state;
    foreach (ICpuGameAction action in Actions)
    {
      if (current.IsFinished || current.CurrentTurn != Team || !action.IsLegal(current)) return false;
      current = action.Apply(current);
    }
    return true;
  }

  public CpuGameState Apply(CpuGameState state)
  {
    if (!IsLegal(state)) throw new InvalidOperationException($"Illegal CPU tactical macro: {Describe()}");
    CpuGameState current = state;
    foreach (ICpuGameAction action in Actions) current = CpuGameRules.ApplyLegal(current, action);
    return current;
  }

  public string Describe() => $"Macro[{string.Join(" -> ", Actions.Select(action => action.Describe()))}]";
}

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
  public int CandidateCacheHits { get; init; }
  public int CompletedSearchDepth { get; init; }
  public int IterativeDeepeningPasses { get; init; }
  public int PrincipalVariationPromotions { get; init; }
  public int TacticalMacrosGenerated { get; init; }
  public int RecedingHorizonReplans { get; init; }
  public int RecedingHorizonActionsCommitted { get; init; }
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
    int candidateCacheHits = 0;
    int principalVariationPromotions = 0;
    int tacticalMacrosGenerated = 0;
    int iterativeDeepeningPasses = 0;
    int completedSearchDepth = 0;
    bool timedOut = false;
    bool nodeBudgetReached = false;
    bool cancelled = cancellationToken.IsCancellationRequested;
    ICpuGameAction? fallbackAction = null;
    int rootLegalActionCount = 0;
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache = [];
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> candidateCache = [];
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates = [];
    EvaluationBreakdown initialBreakdown = EvaluateCached(state, team, context, evaluatedStates, ref evaluationCacheHits);
    SearchNode rootNode = new(state, [], initialBreakdown.Total, initialBreakdown);
    List<SearchNode> beam = [rootNode];
    int recedingHorizonReplans = 0;
    int recedingHorizonActionsCommitted = 0;
    // Preparation is intentionally included in the time budget; when ranking begins, the search
    // records a legal root fallback before it expands any branch.
    int maximumActions = GetMaximumActionsToPlan(state, team);
    // Unlimited turns can contain dozens of concrete actions. Search a strong local horizon and
    // then re-plan after committing the first linked pair in simulation instead of spending the
    // entire budget extending one brittle turn-long line.
    int totalDepth = Globals.ActionLimitsEnabled
      ? maximumActions + Math.Max(0, settings.TacticalExtensionDepth)
      : Math.Min(4, maximumActions);
    int recedingPlanningDeadline = GetRecedingPlanningDeadline(settings);
    int primarySearchDeadline = Globals.ActionLimitsEnabled
      ? Math.Max(1, settings.MaxSearchMilliseconds)
      : GetNextRecedingSegmentDeadline(stopwatch, settings, recedingPlanningDeadline, 0);
    IReadOnlyList<ICpuGameAction> principalVariation = [];

    // True iterative deepening: restart from the root at depth 1, 2, 3, ... while reusing all
    // deterministic per-state caches. Only a fully completed iteration replaces the current best
    // beam, so a 1.4-second Hard search never loses a known-good result to a half-finished layer.
    for (int depthLimit = 1; depthLimit <= totalDepth && !cancelled && !timedOut && !nodeBudgetReached; depthLimit++)
    {
      iterativeDeepeningPasses++;
      SearchIterationResult iteration = RunSearchIteration(
        rootNode, state, team, profile, context, intents, depthLimit, maximumActions, principalVariation,
        primarySearchDeadline, stopwatch, cancellationToken, searchActionCache, candidateCache, evaluatedStates,
        ref nodesGenerated, ref nodesEvaluated, ref duplicatesRemoved, ref evaluationCacheHits,
        ref candidateCacheHits, ref timedOut, ref nodeBudgetReached, ref cancelled);
      fallbackAction ??= iteration.FallbackAction;
      rootLegalActionCount = Math.Max(rootLegalActionCount, iteration.RootLegalActionCount);
      principalVariationPromotions += iteration.PrincipalVariationPromotions;
      tacticalMacrosGenerated += iteration.TacticalMacrosGenerated;
      if (!iteration.Completed)
      {
        break;
      }

      beam = iteration.Beam;
      completedSearchDepth = depthLimit;
      principalVariation = beam
        .OrderByDescending(node => node.Score)
        .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
        .First().Actions;

      // Once every surviving line has already ended the turn/match there is nothing deeper to
      // discover. Stop early rather than replaying the same terminal frontier.
      if (beam.All(node => node.State.IsFinished || node.State.CurrentTurn != team))
      {
        break;
      }
    }

    if (!Globals.ActionLimitsEnabled && state.InitialBuy is null && !cancelled && !nodeBudgetReached &&
        beam.Any(node => node.Actions.Count > 0) && stopwatch.ElapsedMilliseconds < recedingPlanningDeadline)
    {
      beam = ContinueRecedingHorizon(
        state, team, profile, context, intents, beam, recedingPlanningDeadline,
        stopwatch, cancellationToken, searchActionCache, candidateCache, evaluatedStates,
        ref nodesGenerated, ref nodesEvaluated, ref duplicatesRemoved, ref evaluationCacheHits,
        ref candidateCacheHits, ref iterativeDeepeningPasses, ref completedSearchDepth,
        ref principalVariationPromotions, ref tacticalMacrosGenerated,
        ref timedOut, ref nodeBudgetReached, ref cancelled,
        out recedingHorizonReplans, out recedingHorizonActionsCommitted);
    }

    List<(SearchNode Node, float Score, float OpponentPenalty)> ranked = [];
    foreach (SearchNode node in beam)
    {
      if (cancelled || nodeBudgetReached)
      {
        break;
      }

      float adjustedScore = node.Score;
      float opponentPenalty = 0f;
      // If deepening consumed the soft budget, keep the best fully completed beam instead of
      // throwing that work away and falling back to the first legal root action. Opponent reply
      // search is optional refinement and only runs while actual decision time remains.
      bool replyTimeAvailable = !timedOut &&
        stopwatch.ElapsedMilliseconds < Math.Max(1, settings.MaxSearchMilliseconds);
      if (replyTimeAvailable && settings.OpponentActionsToPredict > 0 &&
          node.State.Winner is null && node.State.CurrentTurn != team)
      {
        float afterOpponent = PredictOpponentResponse(node.State, team, profile, context, stopwatch, cancellationToken,
          searchActionCache, candidateCache, evaluatedStates, ref evaluationCacheHits, ref candidateCacheHits,
          ref nodesGenerated, ref nodesEvaluated,
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
      CandidateCacheHits = candidateCacheHits,
      CompletedSearchDepth = completedSearchDepth,
      IterativeDeepeningPasses = iterativeDeepeningPasses,
      PrincipalVariationPromotions = principalVariationPromotions,
      TacticalMacrosGenerated = tacticalMacrosGenerated,
      RecedingHorizonReplans = recedingHorizonReplans,
      RecedingHorizonActionsCommitted = recedingHorizonActionsCommitted,
      TimedOut = timedOut,
      NodeBudgetReached = nodeBudgetReached,
      Cancelled = cancelled,
      TopChoices = choices
    };
    IReadOnlyList<ICpuGameAction> chosenActions = ExpandSearchActions(chosen.Node.Actions);
    // Only an attack that ends the match or scenario is allowed to override the searched first
    // action. Ordinary unit kills remain highly valued candidates, but do not hijack a stronger
    // move/ability line after the beam has already compared them.
    AttackAction? immediateWin = GetImmediateWinningAttack(state, team, settings, searchActionCache);
    if (immediateWin is not null &&
        (chosenActions.Count == 0 || !Equals(chosenActions[0], immediateWin)))
    {
      chosenActions = [immediateWin, .. chosenActions];
    }
    IReadOnlyList<ICpuGameAction> verifiedActions = VerifyActionSequence(state, team, chosenActions);
    return new CpuTurnPlan(verifiedActions, chosen.Score, report);
  }

  private SearchIterationResult RunSearchIteration(
    SearchNode rootNode,
    CpuGameState rootState,
    NetworkTeam team,
    CpuProfile profile,
    EvaluationContext context,
    IReadOnlyList<CpuIntent> intents,
    int depthLimit,
    int maximumActions,
    IReadOnlyList<ICpuGameAction> principalVariation,
    int softDeadlineMilliseconds,
    Stopwatch stopwatch,
    CancellationToken cancellationToken,
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache,
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> candidateCache,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int nodesGenerated,
    ref int nodesEvaluated,
    ref int duplicatesRemoved,
    ref int evaluationCacheHits,
    ref int candidateCacheHits,
    ref bool timedOut,
    ref bool nodeBudgetReached,
    ref bool cancelled
  )
  {
    List<SearchNode> beam = [rootNode];
    ICpuGameAction? fallbackAction = null;
    int rootLegalActionCount = 0;
    int pvPromotions = 0;
    int macrosGenerated = 0;

    // At most depthLimit expansion waves are required because every candidate contributes at
    // least one concrete action; a two-action macro simply reaches the requested depth sooner.
    for (int wave = 0; wave < depthLimit; wave++)
    {
      if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken, softDeadlineMilliseconds,
        out timedOut, out nodeBudgetReached, out cancelled))
      {
        return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
      }

      List<SearchNode> expanded = [];
      Dictionary<ulong, float> bestScoreByState = [];
      List<PendingSearchExpansion> pending = [];
      bool expandedAny = false;
      foreach (SearchNode node in beam)
      {
        if (ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken, softDeadlineMilliseconds,
          out timedOut, out nodeBudgetReached, out cancelled))
        {
          return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
        }
        if (node.State.IsFinished || node.State.CurrentTurn != team || node.Actions.Count >= depthLimit)
        {
          expanded.Add(node);
          continue;
        }

        int placementLimit = GetPurchasePlacementLimit(profile.Search, 16);
        IReadOnlyList<ICpuGameAction> legal = GetSearchActions(node.State, team, placementLimit, searchActionCache);
        if (node.Actions.Count == 0) rootLegalActionCount = legal.Count;
        HashSet<ICpuGameAction> legalActionSet = [.. legal];
        IReadOnlyList<ScoredAction> candidates = SelectSearchCandidatesCached(
          node.State, team, legal, profile.Search, profile.Personality, candidateCache, ref candidateCacheHits);
        candidates = candidates.Where(candidate => candidate.Action is TacticalMacroAction || legalActionSet.Contains(candidate.Action)).ToArray();
        candidates = ApplyAttackAndReservePriorities(node.State, team, candidates);

        // Build linked two-action tactics only from already-promising first actions. The macro is
        // an optimisation, not new strategy: every component remains independently legal and the
        // final plan is flattened back to those exact actions.
        IReadOnlyList<ScoredAction> withMacros = AddTacticalMacroCandidates(
          node.State, team, candidates, profile.Search, profile.Personality, searchActionCache, ref macrosGenerated);
        if (node.Actions.Count >= maximumActions)
        {
          withMacros = withMacros.Where(candidate => IsForcingAction(candidate.Action)).ToArray();
        }

        withMacros = withMacros
          .Where(candidate => node.Actions.Count + GetConcreteActionCount(candidate.Action) <= depthLimit)
          .ToArray();
        if (withMacros.Count == 0)
        {
          expanded.Add(node);
          continue;
        }

        // Principal-variation reuse: when this node follows the previous completed best line,
        // search its known continuation first. This improves ordering without forcing that line.
        if (IsPrincipalVariationPrefix(node.Actions, principalVariation))
        {
          int pvIndex = node.Actions.Count;
          ScoredAction? promoted = withMacros.FirstOrDefault(candidate => MatchesPrincipalVariation(candidate.Action, principalVariation, pvIndex));
          if (promoted is not null && !ReferenceEquals(promoted, withMacros[0]))
          {
            withMacros = [promoted, .. withMacros.Where(candidate => !Equals(candidate.Action, promoted.Action))];
            pvPromotions++;
          }
        }

        foreach (ScoredAction candidate in withMacros)
        {
          IReadOnlyList<ICpuGameAction> concrete = GetConcreteActions(candidate.Action);
          if (node.Actions.Count == 0 && fallbackAction is null) fallbackAction = concrete[0];
          if (ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken, softDeadlineMilliseconds,
            out timedOut, out nodeBudgetReached, out cancelled))
          {
            return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
          }
          pending.Add(new PendingSearchExpansion(node, candidate));
          expandedAny = true;
        }
      }

      int parallelism = GetParallelism(profile.Search);
      foreach (PendingSearchExpansion[] batch in pending.Chunk(GetEvaluationBatchSize(parallelism)))
      {
        if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken, softDeadlineMilliseconds,
          out timedOut, out nodeBudgetReached, out cancelled))
        {
          return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
        }
        IReadOnlyList<EvaluatedSearchExpansion> evaluated = EvaluatePendingBranches(
          batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, profile.Search,
          softDeadlineMilliseconds, cancellationToken, ref evaluationCacheHits);
        if (evaluated.Count < batch.Length)
        {
          cancelled = cancellationToken.IsCancellationRequested;
          timedOut = !cancelled && stopwatch.ElapsedMilliseconds >= Math.Max(1, profile.Search.MaxSearchMilliseconds);
          return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
        }
        foreach (EvaluatedSearchExpansion branch in evaluated)
        {
          nodesGenerated++;
          nodesEvaluated++;
          SearchNode parent = branch.Pending.Node;
          ScoredAction candidate = branch.Pending.Candidate;
          float accumulatedActionPriority = parent.Score - parent.Breakdown.Total;
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
          expanded.Add(new SearchNode(
            branch.Result,
            [.. parent.Actions, .. GetConcreteActions(candidate.Action)],
            score,
            branch.Breakdown));
        }
      }

      if (expanded.Count == 0)
      {
        break;
      }
      beam = expanded
        .OrderByDescending(node => node.Score)
        .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
        .Take(Math.Max(1, profile.Search.BeamWidth))
        .ToList();
      if (!expandedAny || beam.All(node => node.State.IsFinished || node.State.CurrentTurn != team || node.Actions.Count >= depthLimit))
      {
        break;
      }
    }

    return new SearchIterationResult(beam, true, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
  }

  private List<SearchNode> ContinueRecedingHorizon(
    CpuGameState initialState,
    NetworkTeam team,
    CpuProfile profile,
    EvaluationContext context,
    IReadOnlyList<CpuIntent> intents,
    IReadOnlyList<SearchNode> initialBeam,
    int planningDeadlineMilliseconds,
    Stopwatch stopwatch,
    CancellationToken cancellationToken,
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache,
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> candidateCache,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int nodesGenerated,
    ref int nodesEvaluated,
    ref int duplicatesRemoved,
    ref int evaluationCacheHits,
    ref int candidateCacheHits,
    ref int iterativeDeepeningPasses,
    ref int completedSearchDepth,
    ref int principalVariationPromotions,
    ref int tacticalMacrosGenerated,
    ref bool timedOut,
    ref bool nodeBudgetReached,
    ref bool cancelled,
    out int replans,
    out int actionsCommitted
  )
  {
    const int segmentDepth = 4;
    const int commitPerSegment = 2;
    const int maximumSegments = 8;
    replans = 0;
    actionsCommitted = 0;

    List<ICpuGameAction> committed = [];
    CpuGameState current = initialState;
    IReadOnlyList<ICpuGameAction> carryTail = [];
    SearchNode? segmentBest = initialBeam
      .Where(node => node.Actions.Count > 0)
      .OrderByDescending(node => node.Score)
      .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
      .FirstOrDefault();
    int segmentIndex = 0;

    while (segmentBest is not null && segmentIndex < maximumSegments &&
           !current.IsFinished && current.CurrentTurn == team &&
           !cancelled && !nodeBudgetReached)
    {
      int actuallyCommitted = 0;
      foreach (ICpuGameAction action in segmentBest.Actions.Take(commitPerSegment))
      {
        if (current.IsFinished || current.CurrentTurn != team || !action.IsLegal(current))
        {
          break;
        }
        committed.Add(action);
        current = CpuGameRules.ApplyLegal(current, action);
        actuallyCommitted++;
        actionsCommitted++;
        if (current.IsFinished || current.CurrentTurn != team)
        {
          break;
        }
      }

      carryTail = segmentBest.Actions.Skip(actuallyCommitted).ToArray();
      if (actuallyCommitted == 0 || current.IsFinished || current.CurrentTurn != team ||
          stopwatch.ElapsedMilliseconds >= planningDeadlineMilliseconds)
      {
        break;
      }

      segmentIndex++;
      replans++;
      int segmentDeadline = GetNextRecedingSegmentDeadline(
        stopwatch, profile.Search, planningDeadlineMilliseconds, segmentIndex);
      if (stopwatch.ElapsedMilliseconds >= segmentDeadline)
      {
        break;
      }

      EvaluationBreakdown rootBreakdown = EvaluateCached(
        current, team, context, evaluatedStates, ref evaluationCacheHits);
      SearchNode root = new(current, [], rootBreakdown.Total, rootBreakdown);
      List<SearchNode> segmentBeam = [root];
      IReadOnlyList<ICpuGameAction> principalVariation = carryTail;
      int maximumActions = GetMaximumActionsToPlan(current, team);
      int depthCap = Math.Min(segmentDepth, maximumActions);
      bool completedAnyDepth = false;

      for (int depthLimit = 1; depthLimit <= depthCap && !cancelled && !nodeBudgetReached &&
           stopwatch.ElapsedMilliseconds < segmentDeadline; depthLimit++)
      {
        iterativeDeepeningPasses++;
        SearchIterationResult iteration = RunSearchIteration(
          root, current, team, profile, context, intents, depthLimit, maximumActions, principalVariation,
          segmentDeadline, stopwatch, cancellationToken, searchActionCache, candidateCache, evaluatedStates,
          ref nodesGenerated, ref nodesEvaluated, ref duplicatesRemoved, ref evaluationCacheHits,
          ref candidateCacheHits, ref timedOut, ref nodeBudgetReached, ref cancelled);
        principalVariationPromotions += iteration.PrincipalVariationPromotions;
        tacticalMacrosGenerated += iteration.TacticalMacrosGenerated;
        if (!iteration.Completed)
        {
          break;
        }

        completedAnyDepth = true;
        segmentBeam = iteration.Beam;
        completedSearchDepth = Math.Max(completedSearchDepth, depthLimit);
        SearchNode bestCompleted = segmentBeam
          .OrderByDescending(node => node.Score)
          .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
          .First();
        principalVariation = bestCompleted.Actions;
        if (segmentBeam.All(node => node.State.IsFinished || node.State.CurrentTurn != team))
        {
          break;
        }
      }

      if (!completedAnyDepth)
      {
        break;
      }

      segmentBest = segmentBeam
        .Where(node => node.Actions.Count > 0)
        .OrderByDescending(node => node.Score)
        .ThenBy(node => DescribeActions(node.Actions), StringComparer.Ordinal)
        .FirstOrDefault();
    }

    // Keep the uncommitted tail of the last completed local search as a safe continuation when
    // the shared turn budget expires. It was searched from the exact state reached by the
    // committed prefix, so this is stronger than inventing extra actions in the verifier.
    foreach (ICpuGameAction action in carryTail)
    {
      if (current.IsFinished || current.CurrentTurn != team || !action.IsLegal(current))
      {
        break;
      }
      committed.Add(action);
      current = CpuGameRules.ApplyLegal(current, action);
    }

    EvaluationBreakdown finalBreakdown = EvaluateCached(
      current, team, context, evaluatedStates, ref evaluationCacheHits);
    return [new SearchNode(current, committed, finalBreakdown.Total, finalBreakdown)];
  }

  private static int GetRecedingPlanningDeadline(CpuSearchSettings settings)
  {
    int total = Math.Max(1, settings.MaxSearchMilliseconds);
    int reserve = Math.Clamp(total / 10, 20, 160);
    return Math.Max(1, total - reserve);
  }

  private static int GetNextRecedingSegmentDeadline(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    int planningDeadlineMilliseconds,
    int segmentIndex
  )
  {
    int elapsed = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
    int remaining = Math.Max(0, planningDeadlineMilliseconds - elapsed);
    if (remaining <= 0)
    {
      return elapsed;
    }

    double fraction = segmentIndex == 0 ? 0.34 : 0.45;
    int desired = Math.Max(1, (int)Math.Ceiling(remaining * fraction));
    int minimumSlice = Math.Clamp(Math.Max(1, settings.MaxSearchMilliseconds) / 14, 30, 140);
    int maximumSlice = Math.Clamp(Math.Max(1, settings.MaxSearchMilliseconds) / 3, 80, 500);
    int slice = Math.Min(remaining, Math.Max(Math.Min(minimumSlice, remaining), Math.Min(maximumSlice, desired)));
    return Math.Min(planningDeadlineMilliseconds, elapsed + Math.Max(1, slice));
  }

  private IReadOnlyList<ScoredAction> SelectSearchCandidatesCached(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> legal,
    CpuSearchSettings settings,
    CpuPersonality personality,
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> cache,
    ref int cacheHits
  )
  {
    var key = (_hasher.ComputeSearchHash(state), team, settings.CandidatesPerNode, settings.PromisingCandidatesPerNode, personality);
    if (cache.TryGetValue(key, out IReadOnlyList<ScoredAction>? cached))
    {
      cacheHits++;
      return cached;
    }
    IReadOnlyList<ScoredAction> selected = SelectSearchCandidates(state, team, legal, settings, personality);
    cache[key] = selected;
    return selected;
  }

  private IReadOnlyList<ScoredAction> AddTacticalMacroCandidates(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ScoredAction> candidates,
    CpuSearchSettings settings,
    CpuPersonality personality,
    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache,
    ref int macrosGenerated
  )
  {
    if (candidates.Count == 0 || (Globals.ActionLimitsEnabled && state.ActionsRemaining < 2)) return candidates;
    int limit = Math.Max(1, Math.Min(settings.CandidatesPerNode, settings.PromisingCandidatesPerNode));
    List<ScoredAction> macros = [];
    foreach (ScoredAction first in candidates
      .Where(candidate => candidate.Action is MoveAction || candidate.Action is UseAbilityAction { Ability: "Mark" })
      .Take(6))
    {
      CpuGameState afterFirst = CpuGameRules.ApplyLegal(state, first.Action);
      if (afterFirst.IsFinished || afterFirst.CurrentTurn != team) continue;
      IReadOnlyList<ICpuGameAction> followLegal = GetSearchActions(
        afterFirst, team, GetPurchasePlacementLimit(settings, 8), searchActionCache);
      ICpuGameAction[] linked = followLegal.Where(action => IsLinkedTacticalFollowUp(state, first.Action, action)).ToArray();
      if (linked.Length == 0) continue;
      IReadOnlyList<ScoredAction> followCandidates = _candidateSelector.SelectCandidates(afterFirst, team, linked, settings, personality);
      foreach (ScoredAction follow in followCandidates.Take(2))
      {
        TacticalMacroAction macro = new(team, [first.Action, follow.Action]);
        float synergy = (first.Action, follow.Action) switch
        {
          (MoveAction, AttackAction) => 34f,
          (MoveAction, UseAbilityAction { Ability: "Attach" }) => 24f,
          (UseAbilityAction { Ability: "Mark" }, AttackAction) => 38f,
          _ => 12f
        };
        macros.Add(new ScoredAction(macro, first.Score + follow.Score + synergy, "Linked tactical action pair"));
        macrosGenerated++;
      }
    }

    if (macros.Count == 0) return candidates;
    return candidates.Concat(macros)
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .Take(limit)
      .ToArray();
  }

  private static bool IsLinkedTacticalFollowUp(CpuGameState before, ICpuGameAction first, ICpuGameAction follow) => (first, follow) switch
  {
    (MoveAction move, AttackAction attack) => attack.AttackerId == move.PieceId,
    (MoveAction move, UseAbilityAction { Ability: "Attach" } ability) =>
      ability.ActorId == move.PieceId && before.Pieces.FirstOrDefault(piece => piece.Id == move.PieceId)?.Type == "Guard",
    (UseAbilityAction { Ability: "Mark", TargetPieceId: not null } mark, AttackAction attack) =>
      attack.TargetPieceId == mark.TargetPieceId && attack.AttackerId != mark.ActorId,
    _ => false
  };

  private static CpuGameState ApplySearchAction(CpuGameState state, ICpuGameAction action) =>
    action is TacticalMacroAction macro ? macro.Apply(state) : CpuGameRules.ApplyLegal(state, action);

  private static IReadOnlyList<ICpuGameAction> GetConcreteActions(ICpuGameAction action) =>
    action is TacticalMacroAction macro ? macro.Actions : [action];

  private static IReadOnlyList<ICpuGameAction> ExpandSearchActions(IEnumerable<ICpuGameAction> actions) =>
    actions.SelectMany(GetConcreteActions).ToArray();

  private static int GetConcreteActionCount(ICpuGameAction action) =>
    action is TacticalMacroAction macro ? macro.Actions.Count : 1;

  private static bool IsPrincipalVariationPrefix(IReadOnlyList<ICpuGameAction> actions, IReadOnlyList<ICpuGameAction> pv) =>
    actions.Count <= pv.Count && actions.Select((action, index) => Equals(action, pv[index])).All(equal => equal);

  private static bool MatchesPrincipalVariation(ICpuGameAction action, IReadOnlyList<ICpuGameAction> pv, int index)
  {
    IReadOnlyList<ICpuGameAction> concrete = GetConcreteActions(action);
    if (index + concrete.Count > pv.Count) return false;
    for (int offset = 0; offset < concrete.Count; offset++)
    {
      if (!Equals(concrete[offset], pv[index + offset])) return false;
    }
    return true;
  }

  private IReadOnlyList<ScoredAction> SelectSearchCandidates(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> legal,
    CpuSearchSettings settings,
    CpuPersonality personality
  )
  {
    if (CpuObjectiveRules.IsRoyalEliminationObjective(state))
    {
      return _candidateSelector.SelectCandidates(state, team, legal, settings, personality);
    }

    // The candidate selector deliberately gives enemy royals a large quick-preservation bonus so
    // Regicide cannot prune a winning attack. In other modes that same cheap bonus can crowd out
    // Conquest/campaign/farm lines before the objective-aware evaluator ever sees them. Split the
    // shortlist so royal attacks compete on their detailed tactical score instead of receiving a
    // Regicide-only pruning privilege.
    AttackAction[] royalAttacks = legal.OfType<AttackAction>()
      .Where(attack => attack.TargetPieceId is not null && IsEnemyRoyalTarget(state, team, attack.TargetPieceId))
      .ToArray();
    if (royalAttacks.Length == 0)
    {
      return _candidateSelector.SelectCandidates(state, team, legal, settings, personality);
    }

    HashSet<ICpuGameAction> royalAttackSet = royalAttacks.Cast<ICpuGameAction>().ToHashSet();
    ICpuGameAction[] ordinaryLegal = legal.Where(action => !royalAttackSet.Contains(action)).ToArray();
    IReadOnlyList<ScoredAction> ordinary = ordinaryLegal.Length == 0
      ? []
      : _candidateSelector.SelectCandidates(state, team, ordinaryLegal, settings, personality);
    IReadOnlyList<ScoredAction> royal = _candidateSelector.SelectCandidates(
      state, team, royalAttacks.Cast<ICpuGameAction>().ToArray(), settings, personality);
    int limit = Math.Max(1, Math.Min(settings.CandidatesPerNode, settings.PromisingCandidatesPerNode));
    return ordinary.Concat(royal)
      .GroupBy(candidate => candidate.Action)
      .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Kind)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .Take(limit)
      .ToArray();
  }

  private static bool IsEnemyRoyalTarget(CpuGameState state, NetworkTeam team, string targetPieceId) =>
    state.Pieces.FirstOrDefault(piece => piece.Id == targetPieceId) is NetworkPiece target &&
    target.Team != team && target.Team != NetworkTeam.Neutral &&
    UnitRules.TryGet(target.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal;

  private AttackAction? GetImmediateWinningAttack(
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
      if (result.IsFinished)
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
    // Game- or scenario-ending actions are genuinely forcing. Ordinary attacks are not: they now
    // remain in the same candidate pool as movement and abilities so the beam can compare a poke
    // with a stronger reposition-then-attack line instead of blindly taking the first combat.
    ScoredAction[] immediateWins = candidates.Where(candidate =>
      candidate.Action is not EndTurnAction && CpuGameRules.ApplyLegal(state, candidate.Action).IsFinished).ToArray();
    if (immediateWins.Length > 0)
    {
      return immediateWins;
    }

    ScoredAction[] nearbyNeutralHires = candidates.Where(candidate => candidate.Action is PurchaseAction { UnitType: "Mercenary" } purchase &&
      IsFullHealthNeutralMercenaryAt(state, purchase.X, purchase.Y) &&
      candidates.Any(other => other.Action is PurchaseAction regular && regular.UnitType != "Mercenary" &&
        Distance((purchase.X, purchase.Y), (regular.X, regular.Y)) <= 4)).ToArray();
    if (nearbyNeutralHires.Length > 0)
    {
      ScoredAction[] boardActions = candidates.Where(candidate => candidate.Action is AttackAction or MoveAction or UseAbilityAction).ToArray();
      return boardActions.Concat(nearbyNeutralHires)
        .DistinctBy(candidate => candidate.Action)
        .OrderByDescending(candidate => candidate.Score)
        .ToArray();
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
        // spend the surplus on one of the counter-aware combat candidates. Board actions remain
        // eligible because spending can usually happen after a tactically stronger move/attack.
        ScoredAction[] highValueSpends = candidates.Where(candidate => candidate.Action is PurchaseAction purchase &&
          (UnitRules.TryGet(purchase.UnitType, out UnitRule rule) && rule.Attack > 0 ||
           purchase.UnitType == "Farm" && candidate.Score >= bestCombatScore))
          .Where(candidate => candidate.Score >= bestCombatScore - 16f)
          .ToArray();
        if (highValueSpends.Length > 0)
        {
          ScoredAction[] boardActions = candidates.Where(candidate => candidate.Action is AttackAction or MoveAction or UseAbilityAction).ToArray();
          return boardActions.Concat(highValueSpends)
            .DistinctBy(candidate => candidate.Action)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        }
      }
    }

    if (!Globals.ActionLimitsEnabled)
    {
      // With unlimited team actions, useful board actions should happen before a quiet End Turn,
      // but attacks, moves and abilities must compete with each other. The search—not generator
      // order—decides whether to strike now or reposition first.
      ScoredAction[] boardActions = candidates.Where(candidate => candidate.Action is AttackAction or MoveAction or UseAbilityAction).ToArray();
      if (boardActions.Length > 0)
      {
        return boardActions;
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
  /// Final legality defence before a worker result leaves CPU code. Strategy belongs to the beam:
  /// this method validates the chosen line but never invents extra attacks or movement by taking
  /// the first action returned by the generator.
  /// </summary>
  private static IReadOnlyList<ICpuGameAction> VerifyActionSequence(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> actions
  )
  {
    List<ICpuGameAction> verified = [];
    CpuGameState current = state;
    foreach (ICpuGameAction action in actions)
    {
      if (current.IsFinished || current.CurrentTurn != team || !action.IsLegal(current))
      {
        break;
      }
      verified.Add(action);
      current = action.Apply(current);
    }

    // Ending an unlimited turn is bookkeeping, not a strategic continuation. It is safe to add
    // after the searched line; unlike the former verifier this never chooses an unsearched attack
    // or move on the CPU's behalf.
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
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> candidateCache,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int evaluationCacheHits,
    ref int candidateCacheHits,
    ref int nodesGenerated,
    ref int nodesEvaluated,
    ref bool timedOut,
    ref bool nodeBudgetReached,
    ref bool cancelled
  )
  {
    NetworkTeam opponent = state.CurrentTurn;
    CpuOpponentSearchShape replyShape = CpuOpponentSearchPolicy.Choose(state, perspective, profile, context);
    int actionsToPredict = replyShape.ActionsToPredict;
    if (actionsToPredict <= 0)
    {
      return EvaluateCached(state, perspective, context, evaluatedStates, ref evaluationCacheHits).Total;
    }

    // Tactical positions keep the full configured response horizon. Quiet positions use a
    // narrower reply proof, freeing the same fixed turn budget for more principal search work.
    int opponentBeamWidth = replyShape.BeamWidth;
    int placementLimit = GetPurchasePlacementLimit(profile.Search, 12);
    CpuSearchSettings opponentSettings = new()
    {
      BeamWidth = opponentBeamWidth,
      CandidatesPerNode = opponentBeamWidth,
      PromisingCandidatesPerNode = Math.Min(profile.Search.PromisingCandidatesPerNode, opponentBeamWidth),
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
        IReadOnlyList<ScoredAction> candidates = SelectSearchCandidatesCached(
          node.State, opponent, legal, opponentSettings, CpuPersonality.Aggressive, candidateCache, ref candidateCacheHits);
        int opponentMacros = 0;
        candidates = AddTacticalMacroCandidates(
          node.State, opponent, candidates, opponentSettings, CpuPersonality.Aggressive, searchActionCache,
          ref opponentMacros);
        foreach (ScoredAction candidate in candidates)
        {
          if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
            out timedOut, out nodeBudgetReached, out cancelled))
          {
            break;
          }

          // Keep extension-point candidates constrained to the action generator's legal set.
          if (candidate.Action is not TacticalMacroAction && !legalActionSet.Contains(candidate.Action))
          {
            continue;
          }

          CpuGameState result = ApplySearchAction(node.State, candidate.Action);
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
          expanded.Add(new SearchNode(result, [.. node.Actions, .. GetConcreteActions(candidate.Action)], breakdown.Total, breakdown));
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
    int softDeadlineMilliseconds,
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
        if (ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken, softDeadlineMilliseconds))
        {
          break;
        }
        CpuGameState result = ApplySearchAction(branch.Node.State, branch.Candidate.Action);
        EvaluationBreakdown breakdown = EvaluateCached(result, team, sequentialContext, evaluatedStates, ref cacheHits);
        sequential.Add(new EvaluatedSearchExpansion(branch, result, breakdown));
      }
      return sequential;
    }

    EvaluatedSearchExpansion?[] parallel = new EvaluatedSearchExpansion[pending.Count];
    using ThreadLocal<EvaluationContext> workerContexts = new(() => new EvaluationContext(profile, intents, sequentialContext.Cache));
    Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
    {
      if (ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken, softDeadlineMilliseconds))
      {
        return;
      }
      PendingSearchExpansion branch = pending[index];
      CpuGameState result = ApplySearchAction(branch.Node.State, branch.Candidate.Action);
      // Each worker owns its cache. The main cache uses Dictionary and intentionally stays on
      // the coordinator thread; this avoids locks in its hottest lookup path.
      EvaluationBreakdown breakdown = _evaluator.EvaluateWithBreakdown(result, team, workerContexts.Value!);
      if (!ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken, softDeadlineMilliseconds))
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
    CancellationToken cancellationToken,
    int softDeadlineMilliseconds
  ) => cancellationToken.IsCancellationRequested ||
    stopwatch.ElapsedMilliseconds >= GetHardDeadlineMilliseconds(settings, softDeadlineMilliseconds);

  /// <summary>
  /// The advertised limit is a soft search deadline: no new branch wave begins after it.
  /// Let a small in-flight evaluation batch complete within this tightly bounded grace window so
  /// the CPU can return its best completed line instead of discarding useful work at the exact
  /// millisecond boundary. This is intentionally short even for Best to keep the UI responsive.
  /// </summary>
  private static int GetHardDeadlineMilliseconds(CpuSearchSettings settings) =>
    GetHardDeadlineMilliseconds(settings, Math.Max(1, settings.MaxSearchMilliseconds));

  private static int GetHardDeadlineMilliseconds(CpuSearchSettings settings, int requestedSoftDeadline)
  {
    int globalSoftDeadline = Math.Max(1, settings.MaxSearchMilliseconds);
    int softDeadline = Math.Clamp(requestedSoftDeadline, 1, globalSoftDeadline);
    int grace = Math.Clamp(globalSoftDeadline / 25, 12, 80);
    return Math.Min(globalSoftDeadline + grace, softDeadline + grace);
  }

  private static bool ShouldStop(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    int nodesGenerated,
    CancellationToken cancellationToken,
    out bool timedOut,
    out bool nodeBudgetReached,
    out bool cancelled
  ) => ShouldStop(
    stopwatch, settings, nodesGenerated, cancellationToken, Math.Max(1, settings.MaxSearchMilliseconds),
    out timedOut, out nodeBudgetReached, out cancelled);

  private static bool ShouldStop(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    int nodesGenerated,
    CancellationToken cancellationToken,
    int softDeadlineMilliseconds,
    out bool timedOut,
    out bool nodeBudgetReached,
    out bool cancelled
  )
  {
    cancelled = cancellationToken.IsCancellationRequested;
    int globalDeadline = Math.Max(1, settings.MaxSearchMilliseconds);
    timedOut = stopwatch.ElapsedMilliseconds >= globalDeadline;
    bool localDeadlineReached = stopwatch.ElapsedMilliseconds >= Math.Clamp(softDeadlineMilliseconds, 1, globalDeadline);
    nodeBudgetReached = nodesGenerated >= Math.Max(1, settings.MaxSearchNodes);
    return cancelled || timedOut || localDeadlineReached || nodeBudgetReached;
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

  private static bool IsForcingAction(ICpuGameAction action) => action switch
  {
    TacticalMacroAction macro => macro.Actions.Any(IsForcingAction),
    AttackAction => true,
    UseAbilityAction { Ability: "PickUpTreasure" } => true,
    _ => false
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