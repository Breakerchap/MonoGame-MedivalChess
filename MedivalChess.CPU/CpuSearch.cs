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
  public TimeSpan SearchTime { get; init; }
  public int NodesGenerated { get; init; }
  public int NodesEvaluated { get; init; }
  public int DuplicateStatesRemoved { get; init; }
  public bool TimedOut { get; init; }
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

  public CpuPlayer(
    CpuActionGenerator? actionGenerator = null,
    IActionCandidateSelector? candidateSelector = null,
    StateEvaluator? evaluator = null,
    GameStateHasher? hasher = null
  )
  {
    _actionGenerator = actionGenerator ?? new CpuActionGenerator();
    _candidateSelector = candidateSelector ?? new CpuActionCandidateSelector();
    _evaluator = evaluator ?? new StateEvaluator();
    _hasher = hasher ?? new GameStateHasher();
  }

  public CpuTurnPlan ChooseTurn(CpuGameState state, NetworkTeam team, CpuProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(profile);
    Stopwatch stopwatch = Stopwatch.StartNew();
    CpuSearchSettings settings = profile.Search;
    EvaluationContext context = new(profile, GetIntents(state, team));
    int nodesGenerated = 0;
    int nodesEvaluated = 0;
    int duplicatesRemoved = 0;
    bool timedOut = false;
    bool cancelled = cancellationToken.IsCancellationRequested;
    ICpuGameAction? fallbackAction = null;
    EvaluationBreakdown initialBreakdown = _evaluator.EvaluateWithBreakdown(state, team, context);
    List<SearchNode> beam = [new SearchNode(state, [], initialBreakdown.Total, initialBreakdown)];
    int maximumActions = state.InitialBuy is null
      ? Math.Clamp(state.ActionsRemaining, 0, MatchRules.ActionsPerTurn)
      : 1;

    for (int depth = 0; depth < maximumActions && !cancelled && !timedOut; depth++)
    {
      List<SearchNode> expanded = [];
      Dictionary<ulong, float> bestScoreByState = [];
      foreach (SearchNode node in beam)
      {
        if (ShouldStop(stopwatch, settings, cancellationToken, out timedOut, out cancelled))
        {
          break;
        }
        if (node.State.IsFinished || node.State.CurrentTurn != team)
        {
          expanded.Add(node);
          continue;
        }

        IReadOnlyList<ICpuGameAction> legal = _actionGenerator.GenerateSearchActions(
          node.State,
          team,
          Math.Max(16, settings.CandidatesPerNode * 3)
        );
        // Keep the first legal root action as a safe fallback. Generation can occasionally use
        // most of a very small budget, but a live CPU turn must never stall because search timed out.
        if (node.Actions.Count == 0 && fallbackAction is null)
        {
          fallbackAction = legal.FirstOrDefault();
        }
        IReadOnlyList<ScoredAction> candidates = _candidateSelector.SelectCandidates(node.State, team, legal, settings);
        foreach (ScoredAction candidate in candidates)
        {
          if (ShouldStop(stopwatch, settings, cancellationToken, out timedOut, out cancelled))
          {
            break;
          }

          CpuGameState result = candidate.Action.Apply(node.State);
          nodesGenerated++;
          EvaluationBreakdown breakdown = _evaluator.EvaluateWithBreakdown(result, team, context);
          nodesEvaluated++;
          // Preserve tactical urgency across the turn. Pure material evaluation otherwise
          // overvalues buying before taking an immediately available kill.
          float accumulatedActionPriority = node.Score - node.Breakdown.Total;
          float score = breakdown.Total + accumulatedActionPriority + candidate.Score;
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
      if (ShouldStop(stopwatch, settings, cancellationToken, out timedOut, out cancelled))
      {
        break;
      }
      float adjustedScore = node.Score;
      float opponentPenalty = 0f;
      if (settings.OpponentActionsToPredict > 0 && node.State.Winner is null && node.State.CurrentTurn != team)
      {
        float afterOpponent = PredictOpponentResponse(node.State, team, profile, context, stopwatch, cancellationToken, ref nodesGenerated, ref nodesEvaluated, ref timedOut, ref cancelled);
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
    (SearchNode Node, float Score, float OpponentPenalty) chosen = ChooseRanked(ranked, profile);
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
      SearchTime = stopwatch.Elapsed,
      NodesGenerated = nodesGenerated,
      NodesEvaluated = nodesEvaluated,
      DuplicateStatesRemoved = duplicatesRemoved,
      TimedOut = timedOut,
      Cancelled = cancelled,
      TopChoices = choices
    };
    return new CpuTurnPlan(chosen.Node.Actions, chosen.Score, report);
  }

  private float PredictOpponentResponse(
    CpuGameState state,
    NetworkTeam perspective,
    CpuProfile profile,
    EvaluationContext context,
    Stopwatch stopwatch,
    CancellationToken cancellationToken,
    ref int nodesGenerated,
    ref int nodesEvaluated,
    ref bool timedOut,
    ref bool cancelled
  )
  {
    NetworkTeam opponent = state.CurrentTurn;
    CpuGameState current = state;
    int actionsToPredict = Math.Min(profile.Search.OpponentActionsToPredict, current.ActionsRemaining);
    for (int actionCount = 0; actionCount < actionsToPredict; actionCount++)
    {
      if (ShouldStop(stopwatch, profile.Search, cancellationToken, out timedOut, out cancelled) || current.CurrentTurn != opponent)
      {
        break;
      }
      IReadOnlyList<ICpuGameAction> legal = _actionGenerator.GenerateSearchActions(
        current,
        opponent,
        Math.Max(12, profile.Search.OpponentBeamWidth * 3)
      );
      CpuSearchSettings opponentSettings = new()
      {
        BeamWidth = Math.Max(1, profile.Search.OpponentBeamWidth),
        CandidatesPerNode = Math.Max(1, profile.Search.OpponentBeamWidth),
        MaxSearchMilliseconds = profile.Search.MaxSearchMilliseconds
      };
      IReadOnlyList<ScoredAction> candidates = _candidateSelector.SelectCandidates(current, opponent, legal, opponentSettings);
      SearchNode? worstForPerspective = null;
      foreach (ScoredAction candidate in candidates)
      {
        if (ShouldStop(stopwatch, profile.Search, cancellationToken, out timedOut, out cancelled))
        {
          break;
        }
        CpuGameState result = candidate.Action.Apply(current);
        nodesGenerated++;
        EvaluationBreakdown breakdown = _evaluator.EvaluateWithBreakdown(result, perspective, context);
        nodesEvaluated++;
        SearchNode node = new(result, [candidate.Action], breakdown.Total, breakdown);
        if (worstForPerspective is null || node.Score < worstForPerspective.Score ||
            (node.Score == worstForPerspective.Score && DescribeActions(node.Actions).CompareTo(DescribeActions(worstForPerspective.Actions)) < 0))
        {
          worstForPerspective = node;
        }
      }
      if (worstForPerspective is null)
      {
        break;
      }
      current = worstForPerspective.State;
    }
    return _evaluator.Evaluate(current, perspective, context);
  }

  private static bool ShouldStop(
    Stopwatch stopwatch,
    CpuSearchSettings settings,
    CancellationToken cancellationToken,
    out bool timedOut,
    out bool cancelled
  )
  {
    cancelled = cancellationToken.IsCancellationRequested;
    timedOut = stopwatch.ElapsedMilliseconds >= Math.Max(1, settings.MaxSearchMilliseconds);
    return cancelled || timedOut;
  }

  private static (SearchNode Node, float Score, float OpponentPenalty) ChooseRanked(
    IReadOnlyList<(SearchNode Node, float Score, float OpponentPenalty)> ranked,
    CpuProfile profile
  )
  {
    if (ranked.Count == 1 || profile.TopChoicesForRandomSelection <= 1 || profile.MistakeChance <= 0f)
    {
      return ranked[0];
    }
    Random random = new(profile.RandomSeed);
    if (random.NextDouble() >= Math.Clamp(profile.MistakeChance + profile.Search.Randomness, 0f, 1f))
    {
      return ranked[0];
    }
    return ranked[random.Next(Math.Min(ranked.Count, profile.TopChoicesForRandomSelection))];
  }

  private static IReadOnlyList<CpuIntent> GetIntents(CpuGameState state, NetworkTeam team) =>
    state.Scenario?.VictoryGoals.SelectMany(goal => goal.GenerateIntents(state, team)).ToArray() ?? [];

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
