from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"Missing expected block: {label}")
    return text.replace(old, new, 1)


path = Path("MedivalChess.CPU/CpuSearch.cs")
text = path.read_text()

text = replace_once(text,
'''internal sealed record EvaluatedSearchExpansion(
  PendingSearchExpansion Pending,
  CpuGameState Result,
  EvaluationBreakdown Breakdown
);
''',
'''internal sealed record EvaluatedSearchExpansion(
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
''', 'macro action type')

text = replace_once(text,
'''  public int EvaluationCacheHits { get; init; }
  public bool TimedOut { get; init; }
''',
'''  public int EvaluationCacheHits { get; init; }
  public int CandidateCacheHits { get; init; }
  public int CompletedSearchDepth { get; init; }
  public int IterativeDeepeningPasses { get; init; }
  public int PrincipalVariationPromotions { get; init; }
  public int TacticalMacrosGenerated { get; init; }
  public bool TimedOut { get; init; }
''', 'decision diagnostics')

text = replace_once(text,
'''    int evaluationCacheHits = 0;
    bool timedOut = false;
''',
'''    int evaluationCacheHits = 0;
    int candidateCacheHits = 0;
    int principalVariationPromotions = 0;
    int tacticalMacrosGenerated = 0;
    int iterativeDeepeningPasses = 0;
    int completedSearchDepth = 0;
    bool timedOut = false;
''', 'search counters')

text = replace_once(text,
'''    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache = [];
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates = [];
''',
'''    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache = [];
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> candidateCache = [];
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates = [];
''', 'candidate cache declaration')

start_marker = '    List<SearchNode> beam = [new SearchNode(state, [], initialBreakdown.Total, initialBreakdown)];\n'
end_marker = '    List<(SearchNode Node, float Score, float OpponentPenalty)> ranked = [];\n'
start = text.find(start_marker)
end = text.find(end_marker)
if start < 0 or end < 0 or end <= start:
    raise SystemExit('Could not locate main beam-search block')
new_block = '''    SearchNode rootNode = new(state, [], initialBreakdown.Total, initialBreakdown);
    List<SearchNode> beam = [rootNode];
    // Preparation is intentionally included in the time budget; when ranking begins, the search
    // records a legal root fallback before it expands any branch.
    int maximumActions = GetMaximumActionsToPlan(state, team);
    int totalDepth = maximumActions + Math.Max(0, settings.TacticalExtensionDepth);
    IReadOnlyList<ICpuGameAction> principalVariation = [];

    // True iterative deepening: restart from the root at depth 1, 2, 3, ... while reusing all
    // deterministic per-state caches. Only a fully completed iteration replaces the current best
    // beam, so a 1.4-second Hard search never loses a known-good result to a half-finished layer.
    for (int depthLimit = 1; depthLimit <= totalDepth && !cancelled && !timedOut && !nodeBudgetReached; depthLimit++)
    {
      iterativeDeepeningPasses++;
      SearchIterationResult iteration = RunSearchIteration(
        rootNode, state, team, profile, context, intents, depthLimit, maximumActions, principalVariation,
        stopwatch, cancellationToken, searchActionCache, candidateCache, evaluatedStates,
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

'''
text = text[:start] + new_block + text[end:]

text = replace_once(text,
'''          searchActionCache, evaluatedStates, ref evaluationCacheHits, ref nodesGenerated, ref nodesEvaluated,
''',
'''          searchActionCache, candidateCache, evaluatedStates, ref evaluationCacheHits, ref candidateCacheHits,
          ref nodesGenerated, ref nodesEvaluated,
''', 'opponent response call caches')

text = replace_once(text,
'''      EvaluationCacheHits = evaluationCacheHits,
      TimedOut = timedOut,
''',
'''      EvaluationCacheHits = evaluationCacheHits,
      CandidateCacheHits = candidateCacheHits,
      CompletedSearchDepth = completedSearchDepth,
      IterativeDeepeningPasses = iterativeDeepeningPasses,
      PrincipalVariationPromotions = principalVariationPromotions,
      TacticalMacrosGenerated = tacticalMacrosGenerated,
      TimedOut = timedOut,
''', 'report diagnostics values')

# Flatten any search-only macro defensively before the final legality verifier.
text = replace_once(text,
'''    IReadOnlyList<ICpuGameAction> chosenActions = chosen.Node.Actions;
''',
'''    IReadOnlyList<ICpuGameAction> chosenActions = ExpandSearchActions(chosen.Node.Actions);
''', 'flatten macros')

# Insert the iterative search helper before SelectSearchCandidates.
insert_marker = '  private IReadOnlyList<ScoredAction> SelectSearchCandidates(\n'
idx = text.find(insert_marker)
if idx < 0:
    raise SystemExit('Could not locate SelectSearchCandidates insertion point')
helper = r'''  private SearchIterationResult RunSearchIteration(
    SearchNode rootNode,
    CpuGameState rootState,
    NetworkTeam team,
    CpuProfile profile,
    EvaluationContext context,
    IReadOnlyList<CpuIntent> intents,
    int depthLimit,
    int maximumActions,
    IReadOnlyList<ICpuGameAction> principalVariation,
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
      if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
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
        if (ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken,
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
          if (ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken,
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
        if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
          out timedOut, out nodeBudgetReached, out cancelled))
        {
          return new SearchIterationResult(beam, false, fallbackAction, rootLegalActionCount, pvPromotions, macrosGenerated);
        }
        IReadOnlyList<EvaluatedSearchExpansion> evaluated = EvaluatePendingBranches(
          batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, profile.Search,
          cancellationToken, ref evaluationCacheHits);
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

'''
text = text[:idx] + helper + text[idx:]

# Opponent-response signature and candidate selection/application.
text = replace_once(text,
'''    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int evaluationCacheHits,
    ref int nodesGenerated,
''',
'''    Dictionary<(ulong stateHash, NetworkTeam team, int placementLimit), IReadOnlyList<ICpuGameAction>> searchActionCache,
    Dictionary<(ulong stateHash, NetworkTeam team, int candidates, int promising, CpuPersonality personality), IReadOnlyList<ScoredAction>> candidateCache,
    Dictionary<ulong, EvaluationBreakdown> evaluatedStates,
    ref int evaluationCacheHits,
    ref int candidateCacheHits,
    ref int nodesGenerated,
''', 'opponent signature caches')

text = replace_once(text,
'''        IReadOnlyList<ScoredAction> candidates = SelectSearchCandidates(
          node.State, opponent, legal, opponentSettings, CpuPersonality.Aggressive);
        foreach (ScoredAction candidate in candidates)
''',
'''        IReadOnlyList<ScoredAction> candidates = SelectSearchCandidatesCached(
          node.State, opponent, legal, opponentSettings, CpuPersonality.Aggressive, candidateCache, ref candidateCacheHits);
        candidates = AddTacticalMacroCandidates(
          node.State, opponent, candidates, opponentSettings, CpuPersonality.Aggressive, searchActionCache,
          ref nodesEvaluated /* diagnostic-only sink for opponent macros; node count remains bounded below */);
        foreach (ScoredAction candidate in candidates)
''', 'opponent cached candidates')

# The previous replacement temporarily abuses nodesEvaluated as a ref sink; replace with a proper local.
text = replace_once(text,
'''        candidates = AddTacticalMacroCandidates(
          node.State, opponent, candidates, opponentSettings, CpuPersonality.Aggressive, searchActionCache,
          ref nodesEvaluated /* diagnostic-only sink for opponent macros; node count remains bounded below */);
''',
'''        int opponentMacros = 0;
        candidates = AddTacticalMacroCandidates(
          node.State, opponent, candidates, opponentSettings, CpuPersonality.Aggressive, searchActionCache,
          ref opponentMacros);
''', 'opponent macro local')

text = replace_once(text,
'''          if (!legalActionSet.Contains(candidate.Action))
''',
'''          if (candidate.Action is not TacticalMacroAction && !legalActionSet.Contains(candidate.Action))
''', 'opponent macro membership')

text = replace_once(text,
'''          CpuGameState result = CpuGameRules.ApplyLegal(node.State, candidate.Action);
''',
'''          CpuGameState result = ApplySearchAction(node.State, candidate.Action);
''', 'opponent apply macro')

text = replace_once(text,
'''          expanded.Add(new SearchNode(result, [.. node.Actions, candidate.Action], breakdown.Total, breakdown));
''',
'''          expanded.Add(new SearchNode(result, [.. node.Actions, .. GetConcreteActions(candidate.Action)], breakdown.Total, breakdown));
''', 'opponent flatten macro')

# Search branch evaluation must understand macros and share the thread-safe evaluation cache.
text = text.replace('CpuGameState result = CpuGameRules.ApplyLegal(branch.Node.State, branch.Candidate.Action);',
                    'CpuGameState result = ApplySearchAction(branch.Node.State, branch.Candidate.Action);')
text = replace_once(text,
'''    using ThreadLocal<EvaluationContext> workerContexts = new(() => new EvaluationContext(profile, intents, new CpuEvaluationCache()));
''',
'''    using ThreadLocal<EvaluationContext> workerContexts = new(() => new EvaluationContext(profile, intents, sequentialContext.Cache));
''', 'shared worker evaluation cache')

text = replace_once(text,
'''  private static bool IsForcingAction(ICpuGameAction action) => action is AttackAction or UseAbilityAction
  {
    Ability: "PickUpTreasure"
  };
''',
'''  private static bool IsForcingAction(ICpuGameAction action) => action switch
  {
    TacticalMacroAction macro => macro.Actions.Any(IsForcingAction),
    AttackAction => true,
    UseAbilityAction { Ability: "PickUpTreasure" } => true,
    _ => false
  };
''', 'forcing macro support')

path.write_text(text)

# Make the evaluation artefact cache safe to share across parallel workers. Lazy ensures only one
# expensive threat-map build wins even if several workers request the same state simultaneously.
threat_path = Path("MedivalChess.CPU/CpuThreats.cs")
threat = threat_path.read_text()
threat = replace_once(threat,
'''using MedivalChess.Shared;
''',
'''using System.Collections.Concurrent;
using System.Threading;
using MedivalChess.Shared;
''', 'concurrent cache usings')
threat = replace_once(threat,
'''  private readonly Dictionary<(ulong stateHash, NetworkTeam team), CpuThreatMap> _threatMaps = [];
  private readonly Dictionary<CpuGameState, ulong> _stateHashes = [];
''',
'''  private readonly ConcurrentDictionary<(ulong stateHash, NetworkTeam team), Lazy<CpuThreatMap>> _threatMaps = new();
  private readonly ConcurrentDictionary<CpuGameState, ulong> _stateHashes = new();
''', 'concurrent cache fields')
threat = replace_once(threat,
'''    (ulong stateHash, NetworkTeam team) key = (GetStateHash(state), attackingTeam);
    if (!_threatMaps.TryGetValue(key, out CpuThreatMap? map))
    {
      map = builder.Build(state, attackingTeam);
      _threatMaps[key] = map;
    }
    return map;
''',
'''    (ulong stateHash, NetworkTeam team) key = (GetStateHash(state), attackingTeam);
    return _threatMaps.GetOrAdd(key, _ => new Lazy<CpuThreatMap>(
      () => builder.Build(state, attackingTeam), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
''', 'thread-safe threat map lookup')
threat = replace_once(threat,
'''    if (!_stateHashes.TryGetValue(state, out ulong hash))
    {
      hash = _hasher.ComputeSearchHash(state);
      _stateHashes[state] = hash;
    }
    return hash;
''',
'''    return _stateHashes.GetOrAdd(state, _ => _hasher.ComputeSearchHash(state));
''', 'thread-safe state hash lookup')
threat_path.write_text(threat)
