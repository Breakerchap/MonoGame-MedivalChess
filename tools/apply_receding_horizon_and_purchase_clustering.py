from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
  if old not in text:
    raise SystemExit(f'{label} anchor not found')
  return text.replace(old, new, 1)

# -----------------------------------------------------------------------------
# CpuSearch.cs: receding-horizon continuation for unlimited-action turns.
# -----------------------------------------------------------------------------
path = Path('MedivalChess.CPU/CpuSearch.cs')
text = path.read_text()

text = replace_once(text,
'''  public int TacticalMacrosGenerated { get; init; }
  public bool TimedOut { get; init; }''',
'''  public int TacticalMacrosGenerated { get; init; }
  public int RecedingHorizonReplans { get; init; }
  public int RecedingHorizonActionsCommitted { get; init; }
  public bool TimedOut { get; init; }''',
'report fields')

text = replace_once(text,
'''    List<SearchNode> beam = [rootNode];
    // Preparation is intentionally included in the time budget; when ranking begins, the search
    // records a legal root fallback before it expands any branch.
    int maximumActions = GetMaximumActionsToPlan(state, team);
    int totalDepth = maximumActions + Math.Max(0, settings.TacticalExtensionDepth);
    IReadOnlyList<ICpuGameAction> principalVariation = [];
''',
'''    List<SearchNode> beam = [rootNode];
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
''',
'root search setup')

text = replace_once(text,
'''      SearchIterationResult iteration = RunSearchIteration(
        rootNode, state, team, profile, context, intents, depthLimit, maximumActions, principalVariation,
        stopwatch, cancellationToken, searchActionCache, candidateCache, evaluatedStates,''',
'''      SearchIterationResult iteration = RunSearchIteration(
        rootNode, state, team, profile, context, intents, depthLimit, maximumActions, principalVariation,
        primarySearchDeadline, stopwatch, cancellationToken, searchActionCache, candidateCache, evaluatedStates,''',
'primary iteration call')

rank_anchor = '''    List<(SearchNode Node, float Score, float OpponentPenalty)> ranked = [];
'''
insert = r'''    if (!Globals.ActionLimitsEnabled && state.InitialBuy is null && !cancelled && !nodeBudgetReached &&
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

'''
text = replace_once(text, rank_anchor, insert + rank_anchor, 'receding continuation insertion')

text = replace_once(text,
'''      TacticalMacrosGenerated = tacticalMacrosGenerated,
      TimedOut = timedOut,''',
'''      TacticalMacrosGenerated = tacticalMacrosGenerated,
      RecedingHorizonReplans = recedingHorizonReplans,
      RecedingHorizonActionsCommitted = recedingHorizonActionsCommitted,
      TimedOut = timedOut,''',
'report assignment')

# Add a local segment deadline to RunSearchIteration.
text = replace_once(text,
'''    int maximumActions,
    IReadOnlyList<ICpuGameAction> principalVariation,
    Stopwatch stopwatch,''',
'''    int maximumActions,
    IReadOnlyList<ICpuGameAction> principalVariation,
    int softDeadlineMilliseconds,
    Stopwatch stopwatch,''',
'RunSearchIteration signature')

start = text.index('  private SearchIterationResult RunSearchIteration(')
end = text.index('  private IReadOnlyList<ScoredAction> SelectSearchCandidatesCached(', start)
segment = text[start:end]
segment = segment.replace(
'''ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
        out timedOut, out nodeBudgetReached, out cancelled)''',
'''ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken, softDeadlineMilliseconds,
        out timedOut, out nodeBudgetReached, out cancelled)''')
segment = segment.replace(
'''ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken,
          out timedOut, out nodeBudgetReached, out cancelled)''',
'''ShouldStop(stopwatch, profile.Search, nodesGenerated + pending.Count, cancellationToken, softDeadlineMilliseconds,
          out timedOut, out nodeBudgetReached, out cancelled)''')
segment = segment.replace(
'''batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, profile.Search,
          cancellationToken, ref evaluationCacheHits''',
'''batch, team, profile, intents, context, evaluatedStates, parallelism, stopwatch, profile.Search,
          softDeadlineMilliseconds, cancellationToken, ref evaluationCacheHits''')
text = text[:start] + segment + text[end:]

# Insert the receding-horizon helper before candidate caching.
helper_anchor = '  private IReadOnlyList<ScoredAction> SelectSearchCandidatesCached('
helper = r'''  private List<SearchNode> ContinueRecedingHorizon(
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

'''
text = replace_once(text, helper_anchor, helper + helper_anchor, 'receding helper')

# EvaluatePendingBranches receives the current segment deadline.
text = replace_once(text,
'''    Stopwatch stopwatch,
    CpuSearchSettings settings,
    CancellationToken cancellationToken,
    ref int cacheHits''',
'''    Stopwatch stopwatch,
    CpuSearchSettings settings,
    int softDeadlineMilliseconds,
    CancellationToken cancellationToken,
    ref int cacheHits''',
'EvaluatePendingBranches signature')

start = text.index('  private IReadOnlyList<EvaluatedSearchExpansion> EvaluatePendingBranches(')
end = text.index('  private static int GetEvaluationBatchSize(', start)
segment = text[start:end]
segment = segment.replace(
'ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken)',
'ShouldAbortBranchEvaluation(stopwatch, settings, cancellationToken, softDeadlineMilliseconds)')
text = text[:start] + segment + text[end:]

text = replace_once(text,
'''  private static bool ShouldAbortBranchEvaluation(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    CancellationToken cancellationToken
  ) => cancellationToken.IsCancellationRequested ||
    stopwatch.ElapsedMilliseconds >= GetHardDeadlineMilliseconds(settings);
''',
'''  private static bool ShouldAbortBranchEvaluation(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    CancellationToken cancellationToken,
    int softDeadlineMilliseconds
  ) => cancellationToken.IsCancellationRequested ||
    stopwatch.ElapsedMilliseconds >= GetHardDeadlineMilliseconds(settings, softDeadlineMilliseconds);
''',
'branch abort deadline')

text = replace_once(text,
'''  private static int GetHardDeadlineMilliseconds(CpuSearchSettings settings)
  {
    int softDeadline = Math.Max(1, settings.MaxSearchMilliseconds);
    int grace = Math.Clamp(softDeadline / 25, 12, 80);
    return softDeadline + grace;
  }
''',
'''  private static int GetHardDeadlineMilliseconds(CpuSearchSettings settings) =>
    GetHardDeadlineMilliseconds(settings, Math.Max(1, settings.MaxSearchMilliseconds));

  private static int GetHardDeadlineMilliseconds(CpuSearchSettings settings, int requestedSoftDeadline)
  {
    int globalSoftDeadline = Math.Max(1, settings.MaxSearchMilliseconds);
    int softDeadline = Math.Clamp(requestedSoftDeadline, 1, globalSoftDeadline);
    int grace = Math.Clamp(globalSoftDeadline / 25, 12, 80);
    return Math.Min(globalSoftDeadline + grace, softDeadline + grace);
  }
''',
'hard deadline overload')

old_should_stop = '''  private static bool ShouldStop(
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
'''
new_should_stop = '''  private static bool ShouldStop(
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
'''
text = replace_once(text, old_should_stop, new_should_stop, 'ShouldStop overload')

path.write_text(text)

# -----------------------------------------------------------------------------
# CpuActionGenerator.cs: cluster search-only purchase placements.
# -----------------------------------------------------------------------------
path = Path('MedivalChess.CPU/CpuActionGenerator.cs')
text = path.read_text()

text = replace_once(text,
'''public sealed class CpuActionGenerator : ICpuActionGenerator
{
''',
'''public sealed class CpuActionGenerator : ICpuActionGenerator
{
  private readonly record struct PurchasePlacementCluster(
    int TerritoryBand,
    int ForwardBand,
    int EnemyDistanceBand,
    int ObjectiveDistanceBand,
    bool Forest,
    bool Supported
  );

''',
'placement cluster record')

text = replace_once(text,
'''    IEnumerable<UnitRule> purchaseRules = openingFarmPlacement
      ? [farmRule]
      : UnitRules.Purchasable.Where(rule => rule.Type == "Mercenary" ||
        availableMoney >= GetPurchaseCost(state, rule));
    foreach (UnitRule rule in purchaseRules.OrderBy(rule => rule.Type, StringComparer.Ordinal))
    {
      int legalPlacements = 0;
''',
'''    IEnumerable<UnitRule> purchaseRules = openingFarmPlacement
      ? [farmRule]
      : UnitRules.Purchasable.Where(rule => rule.Type == "Mercenary" ||
        availableMoney >= GetPurchaseCost(state, rule));
    NetworkPiece[] placementEnemies = openingFarmPlacement
      ? []
      : state.Pieces.Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null).ToArray();
    (int x, int y)[] placementObjectives = openingFarmPlacement
      ? []
      : GetPurchaseObjectivePositions(state, team).ToArray();
    (int x, int y) forward = TeamRules.GetForwardDirection(team);
    int minForwardProjection = state.Board.Cells.Select(position => position.x * forward.x + position.y * forward.y).DefaultIfEmpty(0).Min();
    int maxForwardProjection = state.Board.Cells.Select(position => position.x * forward.x + position.y * forward.y).DefaultIfEmpty(0).Max();

    foreach (UnitRule rule in purchaseRules.OrderBy(rule => rule.Type, StringComparer.Ordinal))
    {
      if (!openingFarmPlacement && placementLimit is int searchLimit)
      {
        foreach ((int x, int y) position in GetClusteredPurchasePositions(
          state, team, rule, positions, searchLimit, avoidOccupiedPlacements,
          placementEnemies, placementObjectives, minForwardProjection, maxForwardProjection, furthestForwardProjection))
        {
          actions.Add(new PurchaseAction(team, rule.Type, position.x, position.y));
        }
        continue;
      }

      int legalPlacements = 0;
''',
'clustered purchase routing')

helper_anchor = '''  private static bool OverlapsExistingPiece(CpuGameState state, UnitRule rule, int x, int y) => state.Pieces.Any(piece =>
'''
helper = r'''  private static IReadOnlyList<(int x, int y)> GetClusteredPurchasePositions(
    CpuGameState state,
    NetworkTeam team,
    UnitRule rule,
    IReadOnlyList<(int x, int y)> positions,
    int requestedLimit,
    bool avoidOccupiedPlacements,
    IReadOnlyList<NetworkPiece> enemies,
    IReadOnlyList<(int x, int y)> objectives,
    int minForwardProjection,
    int maxForwardProjection,
    int farmForwardProjection
  )
  {
    int maximumRepresentatives = Math.Max(1, Math.Min(requestedLimit, 12));
    var raw = positions.Select(position =>
    {
      PurchasePlacementCluster cluster = ClassifyPurchasePlacement(
        state, team, position, enemies, objectives, minForwardProjection, maxForwardProjection);
      float score = GetPurchasePlacementRepresentativeScore(
        state, team, rule, position, enemies, objectives, cluster, farmForwardProjection);
      return (Position: position, Cluster: cluster, Score: score);
    }).ToArray();

    var groups = raw
      .GroupBy(candidate => candidate.Cluster)
      .Select(group => group
        .OrderByDescending(candidate => candidate.Score)
        .ThenBy(candidate => candidate.Position.y)
        .ThenBy(candidate => candidate.Position.x)
        .Take(4)
        .ToArray())
      .OrderByDescending(group => group[0].Score)
      .ThenBy(group => group[0].Position.y)
      .ThenBy(group => group[0].Position.x)
      .ToArray();

    List<(int x, int y)> selected = [];
    HashSet<PurchasePlacementCluster> usedClusters = [];
    HashSet<(int x, int y)> usedPositions = [];

    bool TryAdd((int x, int y) position, PurchasePlacementCluster cluster)
    {
      if (selected.Count >= maximumRepresentatives || usedClusters.Contains(cluster) || usedPositions.Contains(position))
      {
        return false;
      }
      if (avoidOccupiedPlacements && rule.Type != "Mercenary" && OverlapsExistingPiece(state, rule, position.x, position.y))
      {
        return false;
      }
      PurchaseAction action = new(team, rule.Type, position.x, position.y);
      if (!action.IsLegal(state))
      {
        return false;
      }
      selected.Add(position);
      usedClusters.Add(cluster);
      usedPositions.Add(position);
      return true;
    }

    foreach (var group in groups)
    {
      foreach (var candidate in group)
      {
        if (TryAdd(candidate.Position, candidate.Cluster))
        {
          break;
        }
      }
      if (selected.Count >= maximumRepresentatives)
      {
        break;
      }
    }

    // Highly constrained boards may make the best few representatives of many clusters illegal.
    // Fill from lower-ranked representatives while still refusing duplicate strategic clusters.
    int minimumUseful = Math.Min(maximumRepresentatives, 4);
    if (selected.Count < minimumUseful)
    {
      foreach (var candidate in raw
        .OrderByDescending(candidate => candidate.Score)
        .ThenBy(candidate => candidate.Position.y)
        .ThenBy(candidate => candidate.Position.x))
      {
        TryAdd(candidate.Position, candidate.Cluster);
        if (selected.Count >= minimumUseful)
        {
          break;
        }
      }
    }

    return selected;
  }

  private static PurchasePlacementCluster ClassifyPurchasePlacement(
    CpuGameState state,
    NetworkTeam team,
    (int x, int y) position,
    IReadOnlyList<NetworkPiece> enemies,
    IReadOnlyList<(int x, int y)> objectives,
    int minForwardProjection,
    int maxForwardProjection
  )
  {
    NetworkTeam owner = MatchRules.GetSquareOwner(
      state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount);
    int territoryBand = owner == team ? 0 : owner == NetworkTeam.Neutral ? 1 : 2;
    int projection = position.x * TeamRules.GetForwardDirection(team).x +
      position.y * TeamRules.GetForwardDirection(team).y;
    float forwardFraction = maxForwardProjection <= minForwardProjection
      ? 0.5f
      : (projection - minForwardProjection) / (float)(maxForwardProjection - minForwardProjection);
    int forwardBand = forwardFraction < 0.34f ? 0 : forwardFraction < 0.67f ? 1 : 2;
    int nearestEnemy = enemies.Select(enemy => Distance(position, (enemy.X, enemy.Y))).DefaultIfEmpty(99).Min();
    int enemyBand = nearestEnemy <= 3 ? 0 : nearestEnemy <= 7 ? 1 : 2;
    int nearestObjective = objectives.Select(objective => Distance(position, objective)).DefaultIfEmpty(99).Min();
    int objectiveBand = nearestObjective <= 2 ? 0 : nearestObjective <= 6 ? 1 : 2;
    bool forest = state.Terrain.IsForest(position);
    bool supported = state.Pieces.Any(piece => piece.Team == team && piece.AttachedToId is null &&
      Distance(position, (piece.X, piece.Y)) <= 2);
    return new PurchasePlacementCluster(territoryBand, forwardBand, enemyBand, objectiveBand, forest, supported);
  }

  private static float GetPurchasePlacementRepresentativeScore(
    CpuGameState state,
    NetworkTeam team,
    UnitRule rule,
    (int x, int y) position,
    IReadOnlyList<NetworkPiece> enemies,
    IReadOnlyList<(int x, int y)> objectives,
    PurchasePlacementCluster cluster,
    int farmForwardProjection
  )
  {
    if (rule.Type == "Farm")
    {
      return CpuPlacementHeuristics.GetFarmProtectionScore(
        state, team, position.x, position.y, farmForwardProjection);
    }

    bool neutralMercenary = rule.Type == "Mercenary" && state.Pieces.Any(piece =>
      piece.Type == "Mercenary" && piece.Team == NetworkTeam.Neutral &&
      piece.X == position.x && piece.Y == position.y);
    int nearestEnemy = enemies.Select(enemy => Distance(position, (enemy.X, enemy.Y))).DefaultIfEmpty(14).Min();
    int nearestObjective = objectives.Select(objective => Distance(position, objective)).DefaultIfEmpty(12).Min();
    float score = neutralMercenary ? 600f : 0f;
    if (rule.Attack > 0)
    {
      score += Math.Max(0, 12 - nearestEnemy) * 2.5f;
      score += cluster.ForwardBand * 2f;
    }
    score += Math.Max(0, 10 - nearestObjective) * 1.5f;
    if (cluster.TerritoryBand == 0) score += 4f;
    if (cluster.Forest) score += 2.5f;
    if (cluster.Supported) score += 3f;
    return score;
  }

  private static IEnumerable<(int x, int y)> GetPurchaseObjectivePositions(CpuGameState state, NetworkTeam team)
  {
    HashSet<(int x, int y)> positions = [];
    if (state.Configuration.GameMode == "Conquest")
    {
      positions.UnionWith(state.Board.Cells.Where(MatchRules.IsConquestSquare));
    }
    else if (state.Configuration.GameMode == "Dominion")
    {
      positions.UnionWith(MatchRules.GetDominionControlPoints(state.Board));
    }
    else if (state.Configuration.GameMode == "Plunder" && state.TreasurePosition is (int x, int y) treasure)
    {
      positions.Add(treasure);
    }

    foreach (ICpuScenarioGoal goal in (state.Scenario?.VictoryGoals ?? [])
      .Concat(state.Scenario?.SecondaryGoals ?? [])
      .Concat(state.Scenario?.DefeatConditions ?? []))
    {
      foreach (CpuIntent intent in goal.GenerateIntents(state, team))
      {
        if (intent.TargetPosition is (int x, int y) targetPosition)
        {
          positions.Add(targetPosition);
        }
        if (intent.TargetPieceId is not null &&
            state.Pieces.FirstOrDefault(piece => piece.Id == intent.TargetPieceId) is NetworkPiece target)
        {
          positions.Add((target.X, target.Y));
        }
      }
    }
    return positions;
  }

'''
text = replace_once(text, helper_anchor, helper + helper_anchor, 'placement clustering helpers')
path.write_text(text)

# Allow two genuinely different purchase placements for a selected unit type into the beam.
path = Path('MedivalChess.CPU/CpuActionCandidates.cs')
text = path.read_text()
text = replace_once(text,
'''    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Action is PurchaseAction && IsPlausiblePurchase(candidate))
      .GroupBy(candidate => ((PurchaseAction)candidate.Action).UnitType, StringComparer.Ordinal)
      .Select(group => group.First())
      .Take(purchaseQuota)) Add(candidate);
''',
'''    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Action is PurchaseAction && IsPlausiblePurchase(candidate))
      .GroupBy(candidate => ((PurchaseAction)candidate.Action).UnitType, StringComparer.Ordinal)
      .SelectMany(group => group.Take(2))
      .Take(purchaseQuota)) Add(candidate);
''',
'purchase beam diversity')
path.write_text(text)

# -----------------------------------------------------------------------------
# Regression tests.
# -----------------------------------------------------------------------------
Path('MedivalChess.Tests/CpuPlanningEfficiencyTests.cs').write_text(r'''using MedivalChess.CPU;
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
''')
