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
  }

  public CpuTurnPlan ChooseTurn(CpuGameState state, NetworkTeam team, CpuProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(profile);
    Stopwatch stopwatch = new();
    CpuSearchSettings settings = profile.Search;
    ulong initialStateHash = _hasher.ComputeSearchHash(state);
    if (state.InitialBuy?.IsFarmPlacementPhase == true)
    {
      stopwatch.Start();
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
    // Snapshot preparation has no branching and is done exactly once. Start the bounded timer
    // after it so first-use JIT work cannot make an otherwise identical seed choose a shallower
    // plan than subsequent turns.
    stopwatch.Start();
    int maximumActions = GetMaximumActionsToPlan(state, team);

    int totalDepth = maximumActions + Math.Max(0, settings.TacticalExtensionDepth);
    for (int depth = 0; depth < totalDepth && !cancelled && !timedOut && !nodeBudgetReached; depth++)
    {
      List<SearchNode> expanded = [];
      Dictionary<ulong, float> bestScoreByState = [];
      foreach (SearchNode node in beam)
      {
        if (ShouldStop(stopwatch, settings, nodesGenerated, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
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
        IReadOnlyList<ScoredAction> candidates = _candidateSelector.SelectCandidates(
          node.State, team, legal, settings, profile.Personality);
        candidates = ApplyAttackAndReservePriorities(node.State, team, profile, candidates);
        if (depth >= maximumActions)
        {
          // Quiescence extension: once the ordinary horizon is reached, only continue forcing
          // exchanges. This prevents the search from stopping halfway through an obvious attack
          // sequence without ballooning into another full quiet-move layer.
          candidates = candidates.Where(candidate => IsForcingAction(candidate.Action)).ToArray();
        }
        foreach (ScoredAction candidate in candidates)
        {
          // Candidate selection is strategic and may be replaced in tests or future profiles;
          // never trust it as a legality authority. This also protects cached plans if a rule
          // changes while a background worker is still finishing its snapshot.
          if (!candidate.Action.IsLegal(node.State))
          {
            continue;
          }

          // Keep the strongest legal root candidate as a safe fallback. Generation can
          // occasionally use most of a very small budget. Capture it before the post-ranking
          // time check so a live CPU turn cannot stall simply because candidate scoring consumed
          // the final millisecond of a tiny budget.
          if (node.Actions.Count == 0 && fallbackAction is null)
          {
            fallbackAction = candidate.Action;
          }

          if (ShouldStop(stopwatch, settings, nodesGenerated, cancellationToken, out timedOut, out nodeBudgetReached, out cancelled))
          {
            break;
          }

          CpuGameState result = candidate.Action.Apply(node.State);
          nodesGenerated++;
          EvaluationBreakdown breakdown = EvaluateCached(result, team, context, evaluatedStates, ref evaluationCacheHits);
          nodesEvaluated++;
          // Preserve tactical urgency across the turn. Pure material evaluation otherwise
          // overvalues buying before taking an immediately available kill.
          float accumulatedActionPriority = node.Score - node.Breakdown.Total;
          // A completed match or campaign objective is decisive. Do not let accumulated
          // convenience bonuses make a purchase-before-win line outrank the move that ends
          // the mission immediately.
          float score = result.IsFinished
            ? breakdown.Total
            : breakdown.Total + accumulatedActionPriority + candidate.Score;
          ulong hash = _hasher.ComputeSearchHash(result);
          if (bestScoreByState.TryGetValue(hash, out float existingScore) && existingScore >= score)
          {
            duplicatesRemoved++;
            continue;
          }
          bestScoreByState[hash] = score;
          expanded.Add(new SearchNode(result, [.. node.Actions, candidate.Action], score, breakdown));
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
    IReadOnlyList<ICpuGameAction> verifiedActions = VerifyActionSequence(state, team, profile, chosen.Node.Actions);
    return new CpuTurnPlan(verifiedActions, chosen.Score, report);
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
    CpuProfile profile,
    IReadOnlyList<ScoredAction> candidates
  )
  {
    bool mediumOrStronger = profile.Difficulty is CpuDifficultyLevel.Medium or CpuDifficultyLevel.Hard or CpuDifficultyLevel.Best;
    ScoredAction[] attacks = candidates.Where(candidate => candidate.Action is AttackAction { TargetPieceId: not null }).ToArray();
    if (attacks.Length > 0 && mediumOrStronger)
    {
      return attacks;
    }

    if (profile.Difficulty is CpuDifficultyLevel.Hard or CpuDifficultyLevel.Best)
    {
      ScoredAction[] nearbyNeutralHires = candidates.Where(candidate => candidate.Action is PurchaseAction { UnitType: "Mercenary" } purchase &&
        IsFullHealthNeutralMercenaryAt(state, purchase.X, purchase.Y) &&
        candidates.Any(other => other.Action is PurchaseAction regular && regular.UnitType != "Mercenary" &&
          Distance((purchase.X, purchase.Y), (regular.X, regular.Y)) <= 4)).ToArray();
      if (nearbyNeutralHires.Length > 0)
      {
        return nearbyNeutralHires;
      }
    }

    if (mediumOrStronger &&
        state.Teams.TryGetValue(team, out CpuTeamState? cpuTeam) &&
        cpuTeam.Money >= CpuActionCandidateSelector.CombatPurchaseReserveThreshold)
    {
      ScoredAction[] immediateWins = candidates.Where(candidate => candidate.Action is MoveAction &&
        candidate.Action.Apply(state).IsFinished).ToArray();
      if (immediateWins.Length > 0)
      {
        return immediateWins;
      }
      ScoredAction[] combatPurchases = candidates.Where(candidate => candidate.Action is PurchaseAction purchase &&
        UnitRules.TryGet(purchase.UnitType, out UnitRule rule) && rule.Attack > 0).ToArray();
      if (combatPurchases.Length > 0)
      {
        return combatPurchases;
      }
    }

    if (mediumOrStronger && !Globals.ActionLimitsEnabled)
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
    bool preserveAvailableAttacks = profile.Difficulty is CpuDifficultyLevel.Medium or CpuDifficultyLevel.Hard or CpuDifficultyLevel.Best;
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
        IReadOnlyList<ScoredAction> candidates = _candidateSelector.SelectCandidates(
          node.State, opponent, legal, opponentSettings, CpuPersonality.Aggressive);
        foreach (ScoredAction candidate in candidates)
        {
          if (ShouldStop(stopwatch, profile.Search, nodesGenerated, cancellationToken,
            out timedOut, out nodeBudgetReached, out cancelled))
          {
            break;
          }

          // The opponent model uses the same extension point as the primary search, so apply
          // the same non-negotiable legality gate before simulating any reply.
          if (!candidate.Action.IsLegal(node.State))
          {
            continue;
          }

          CpuGameState result = candidate.Action.Apply(node.State);
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
    }
    foreach ((NetworkTeam team, CpuTeamState stateTeam) in state.Teams.OrderBy(pair => pair.Key))
    {
      Add(ref hash, team);
      Add(ref hash, stateTeam.Money);
      Add(ref hash, stateTeam.ActionsRemaining);
      Add(ref hash, stateTeam.ChosenRoyal ?? string.Empty);
    }
    foreach ((int x, int y) road in state.Roads.OrderBy(position => position.y).ThenBy(position => position.x))
    {
      Add(ref hash, road.x); Add(ref hash, road.y);
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
