using MedivalChess.Shared;

namespace MedivalChess.CPU;

public static class EvaluationScores
{
  public const float Win = 1_000_000f;
  public const float Loss = -1_000_000f;
}

/// <summary>All tunable utility weights used by <see cref="StateEvaluator"/>.</summary>
public sealed class EvaluationWeights
{
  public float Material { get; init; } = 1f;
  public float Health { get; init; } = 0.35f;
  public float ImmediateThreats { get; init; } = 0.9f;
  public float RoyalSafety { get; init; } = 2f;
  public float ObjectiveProgress { get; init; } = 2.5f;
  public float IntentProgress { get; init; } = 0.75f;
  public float StrategicPosition { get; init; } = 0.9f;
  public float MapControl { get; init; } = 0.4f;
  public float Economy { get; init; } = 0.5f;
  public float Mobility { get; init; } = 0.25f;
  public float Formation { get; init; } = 0.2f;
  public float AssetSafety { get; init; } = 0.9f;
  public float AbilityUsage { get; init; } = 0.45f;
  public float ActionEfficiency { get; init; } = 0.4f;
  public float RepetitionPenalty { get; init; } = 0.8f;
}

/// <summary>Stable profile, short-term intent, and per-decision caches supplied to evaluation terms.</summary>
public sealed class EvaluationContext
{
  public EvaluationContext(
    CpuProfile profile,
    IReadOnlyList<CpuIntent>? intents = null,
    CpuEvaluationCache? cache = null
  )
  {
    Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    Intents = intents ?? [];
    Cache = cache ?? new CpuEvaluationCache();
  }

  public CpuProfile Profile { get; }
  public IReadOnlyList<CpuIntent> Intents { get; }
  public CpuEvaluationCache Cache { get; }
}

/// <summary>One independently testable contribution to CPU state utility.</summary>
public interface IEvaluationTerm
{
  string Name { get; }
  float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context);
}

public interface IStateEvaluator
{
  float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context);
}

/// <summary>Detailed score information retained in decision reports instead of hidden inside a heuristic.</summary>
public sealed record EvaluationBreakdown(float Total, IReadOnlyDictionary<string, float> Terms);

/// <summary>Composable heuristic evaluator with a terminal score for wins and losses.</summary>
public sealed class StateEvaluator : IStateEvaluator
{
  private readonly IReadOnlyList<IEvaluationTerm> _terms;
  private readonly ICpuThreatMapBuilder _threatMapBuilder;

  public StateEvaluator(IEnumerable<IEvaluationTerm>? terms = null, ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    _threatMapBuilder = threatMapBuilder ?? new CpuThreatMapBuilder();
    _terms = terms?.ToArray() ??
    [
      new MaterialEvaluation(),
      new HealthEvaluation(),
      new ThreatEvaluation(_threatMapBuilder),
      new RoyalSafetyEvaluation(_threatMapBuilder),
      new ObjectiveEvaluation(),
      new IntentEvaluation(),
      new StrategicPositionEvaluation(),
      new MapControlEvaluation(),
      new EconomyEvaluation(),
      new MobilityEvaluation(),
      new FormationEvaluation(),
      new AssetSafetyEvaluation(_threatMapBuilder),
      new AbilityStateEvaluation(),
      new RepetitionEvaluation(),
      new ActionEfficiencyEvaluation()
    ];
  }

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context) =>
    EvaluateWithBreakdown(state, perspective, context).Total;

  public EvaluationBreakdown EvaluateWithBreakdown(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(context);
    if (state.Winner is NetworkTeam winner)
    {
      return new EvaluationBreakdown(winner == perspective ? EvaluationScores.Win : EvaluationScores.Loss,
        new Dictionary<string, float> { ["Terminal"] = winner == perspective ? EvaluationScores.Win : EvaluationScores.Loss });
    }

    // Campaign goals can end a mission in a state where the ordinary match winner is deliberately
    // unset (for example, a headless capture-location mission). Treat that outcome as decisive
    // during search so completing the mission cannot lose to an unrelated material improvement.
    if (state.Scenario is CpuScenarioDefinition scenario)
    {
      bool ownVictory = scenario.VictoryGoals.Any(goal => goal.GetStatus(state, perspective) == CpuGoalStatus.Completed);
      bool enemyVictory = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
        .Where(team => team != perspective)
        .Any(team => scenario.VictoryGoals.Any(goal => goal.GetStatus(state, team) == CpuGoalStatus.Completed));
      bool ownDefeat = scenario.DefeatConditions.Any(goal => goal.GetStatus(state, perspective) == CpuGoalStatus.Failed);
      bool enemyDefeat = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
        .Where(team => team != perspective)
        .Any(team => scenario.DefeatConditions.Any(goal => goal.GetStatus(state, team) == CpuGoalStatus.Failed));
      if (ownVictory || enemyVictory || ownDefeat || enemyDefeat)
      {
        // Mirror the shared turn-boundary resolution order: completing this team's victory is
        // decisive; otherwise its own failure or another team's victory is a loss. A defender's
        // failed scoped condition is a win for the remaining opponent.
        float terminalScore = ownVictory || (!ownDefeat && !enemyVictory && enemyDefeat)
          ? EvaluationScores.Win
          : EvaluationScores.Loss;
        return new EvaluationBreakdown(terminalScore,
          new Dictionary<string, float> { ["CampaignTerminal"] = terminalScore });
      }
    }

    Dictionary<string, float> scores = [];
    float total = 0f;
    foreach (IEvaluationTerm term in _terms)
    {
      float weighted = term.Evaluate(state, perspective, context) * GetWeight(term.Name, state, context);
      scores[term.Name] = weighted;
      total += weighted;
    }

    return new EvaluationBreakdown(total, scores);
  }

  private static float GetWeight(string name, CpuGameState state, EvaluationContext context)
  {
    EvaluationWeights weights = context.Profile.Weights;
    CpuPersonality personality = context.Profile.Personality;
    CpuScenarioWeights scenario = state.Scenario?.Weights ?? new CpuScenarioWeights();
    return name switch
    {
      "Material" => weights.Material * scenario.Material,
      "Health" => weights.Health,
      "Threat" => weights.ImmediateThreats * personality.Aggression,
      "RoyalSafety" => weights.RoyalSafety * personality.RoyalProtection * personality.Caution * scenario.RoyalSafety,
      "Objective" => weights.ObjectiveProgress * personality.ObjectiveFocus * scenario.ObjectiveProgress,
      "Intent" => weights.IntentProgress * personality.ObjectiveFocus,
      "Strategy" => weights.StrategicPosition * (0.65f + personality.ObjectiveFocus * 0.35f),
      "MapControl" => weights.MapControl * personality.ObjectiveFocus,
      "Economy" => weights.Economy * personality.EconomyFocus * scenario.Economy,
      "Mobility" => weights.Mobility,
      "Formation" => weights.Formation * personality.FormationPreference,
      "AssetSafety" => weights.AssetSafety * personality.Caution,
      "Ability" => weights.AbilityUsage * personality.AbilityUsage,
      "Repetition" => weights.RepetitionPenalty,
      "ActionEfficiency" => weights.ActionEfficiency,
      _ => 1f
    };
  }
}

/// <summary>Discourages a unit from returning directly to its most recent position without forbidding retreats.</summary>
public sealed class RepetitionEvaluation : IEvaluationTerm
{
  public string Name => "Repetition";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    if (state.RecentMoves.Count < 2)
    {
      return 0f;
    }

    float score = 0f;
    foreach (IGrouping<(NetworkTeam team, string pieceId), CpuMoveRecord> history in state.RecentMoves
      .GroupBy(move => (move.Team, move.PieceId)))
    {
      CpuMoveRecord[] moves = history.OrderBy(move => move.TurnNumber).ToArray();
      for (int index = 1; index < moves.Length; index++)
      {
        CpuMoveRecord previous = moves[index - 1];
        CpuMoveRecord current = moves[index];
        if (!current.Reverses(previous) || current.TurnNumber - previous.TurnNumber > 2)
        {
          continue;
        }

        // Scale the discouragement from the unit's rule-derived value rather than hard-coding a
        // per-piece constant, so balance changes continue to be reflected automatically.
        float value = state.Pieces.FirstOrDefault(piece => piece.Id == current.PieceId) is NetworkPiece piece
          ? MaterialEvaluation.GetUnitValue(piece.Type)
          : 40f;
        float penalty = Math.Max(10f, value * 0.25f);
        score += current.Team == perspective ? -penalty : penalty;
      }
    }
    return score;
  }
}

public sealed class MaterialEvaluation : IEvaluationTerm
{
  public string Name => "Material";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context) => state.Pieces
    .Where(piece => piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null)
    .Sum(piece => (piece.Team == perspective ? 1f : -1f) * GetUnitValue(piece.Type));

  internal static float GetUnitValue(string type)
  {
    if (!UnitRules.TryGet(type, out UnitRule rule))
    {
      return 0f;
    }

    float abilityValue = string.IsNullOrWhiteSpace(rule.AbilityDescription) ? 0f : 6f;
    float royalValue = rule.Category == RuleCategory.Royal ? 180f : 0f;
    return rule.Cost + rule.Health * 0.45f + rule.Attack * 1.7f + rule.MoveRange * 2f +
      rule.AttackRange * 1.25f + abilityValue + royalValue;
  }
}

public sealed class HealthEvaluation : IEvaluationTerm
{
  public string Name => "Health";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context) => state.Pieces
    .Where(piece => piece.Team != NetworkTeam.Neutral && UnitRules.TryGet(piece.Type, out _))
    .Sum(piece =>
    {
      UnitRule rule = UnitRules.GetRequired(piece.Type);
      float healthFraction = Math.Clamp(piece.Health / (float)Math.Max(1, rule.Health), 0f, 1f);
      // A damaged unit remains tactically useful, so retain 30% of its value at one health.
      float retainedValue = 0.3f + healthFraction * 0.7f;
      return (piece.Team == perspective ? 1f : -1f) * MaterialEvaluation.GetUnitValue(piece.Type) * retainedValue;
    });
}

public sealed class ThreatEvaluation : IEvaluationTerm
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder;

  public ThreatEvaluation(ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    _threatMapBuilder = threatMapBuilder ?? new CpuThreatMapBuilder();
  }

  public string Name => "Threat";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float value = ScoreThreats(state, context.Cache.GetThreatMap(state, perspective, _threatMapBuilder));
    foreach (NetworkTeam enemy in TeamRules.GetActiveTeams(state.Configuration.PlayerCount).Where(team => team != perspective))
    {
      value -= ScoreThreats(state, context.Cache.GetThreatMap(state, enemy, _threatMapBuilder));
    }
    return value;
  }

  private static float ScoreThreats(CpuGameState state, CpuThreatMap map) => map.ThreatsByPiece.Values.Sum(threat =>
  {
    NetworkPiece? target = state.Pieces.FirstOrDefault(piece => piece.Id == threat.PieceId);
    if (target is null)
    {
      return 0f;
    }

    float targetValue = MaterialEvaluation.GetUnitValue(target.Type);
    float damageValue = Math.Min(targetValue, threat.TotalExpectedDamage * 3f);
    float lethalValue = threat.IsLethal ? targetValue * 0.7f : 0f;
    float focusValue = Math.Max(0, threat.AttackerCount - 1) * 2f;
    float importantValue = threat.IsStrategicallyImportant ? targetValue * 0.5f : 0f;
    return damageValue + lethalValue + focusValue + importantValue;
  });
}

public sealed class RoyalSafetyEvaluation : IEvaluationTerm
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder;

  public RoyalSafetyEvaluation(ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    _threatMapBuilder = threatMapBuilder ?? new CpuThreatMapBuilder();
  }

  public string Name => "RoyalSafety";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float safety = 0f;
    foreach (NetworkPiece royal in state.Pieces.Where(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal))
    {
      UnitRule royalRule = UnitRules.GetRequired(royal.Type);
      float health = royal.Health / (float)Math.Max(1, royalRule.Health) * 120f;
      float danger = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
        .Where(team => team != royal.Team)
        .Select(team => context.Cache.GetThreatMap(state, team, _threatMapBuilder).GetThreat(royal.Id))
        .Where(threat => threat is not null)
        .Sum(threat => threat!.TotalExpectedDamage * 5f + (threat.IsLethal ? 120f : 0f));
      int rearDepth = state.Configuration.GameMode == "Escort"
        ? 0
        : CpuRoyalPlacementHeuristics.GetRearTerritoryDepth(
          state.Board,
          royal.Team,
          (royal.X, royal.Y),
          royalRule.Width,
          royalRule.Height,
          state.Configuration.PlayerCount
        );
      float nearbyDefence = state.Pieces
        .Where(piece => piece.Id != royal.Id && piece.Team == royal.Team && piece.AttachedToId is null)
        .Where(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Attack > 0)
        .Where(piece => DistanceToFootprint(piece, royal, royalRule) <= 3)
        .Sum(piece =>
        {
          UnitRule rule = UnitRules.GetRequired(piece.Type);
          return Math.Min(26f, 5f + rule.Attack * 0.55f + rule.AttackRange * 1.5f);
        });
      float approachingDanger = state.Pieces
        .Where(piece => piece.Team != royal.Team && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null)
        .Where(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Attack > 0)
        .Sum(piece => ScoreApproachingDanger(piece, royal, royalRule));

      // Immediate checks are not sufficient: a fast attacker one move away is already a royal
      // emergency. Depth and a small defensive screen both count, so a strong CPU retreats and
      // sends available units home before an opponent can assemble a free attack lane.
      float score = health + rearDepth * 22f + nearbyDefence - danger - approachingDanger;
      safety += royal.Team == perspective ? score : -score;
    }
    return safety;
  }

  private static float ScoreApproachingDanger(NetworkPiece attacker, NetworkPiece royal, UnitRule royalRule)
  {
    UnitRule attackerRule = UnitRules.GetRequired(attacker.Type);
    int distance = DistanceToFootprint(attacker, royal, royalRule);
    int nextTurnReach = attackerRule.MoveRange + Math.Max(1, attackerRule.AttackRange);
    if (distance > nextTurnReach + 4)
    {
      return 0f;
    }

    float urgency = Math.Max(0f, nextTurnReach + 5 - distance);
    return urgency * (2f + attackerRule.Attack * 0.75f + attackerRule.AttackRange * 1.5f);
  }

  private static int DistanceToFootprint(NetworkPiece piece, NetworkPiece royal, UnitRule royalRule)
  {
    int closestX = Math.Clamp(piece.X, royal.X, royal.X + royalRule.Width - 1);
    int closestY = Math.Clamp(piece.Y, royal.Y, royal.Y + royalRule.Height - 1);
    return Math.Abs(piece.X - closestX) + Math.Abs(piece.Y - closestY);
  }
}

public sealed class ObjectiveEvaluation : IEvaluationTerm
{
  public string Name => "Objective";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    CpuScenarioDefinition scenario = state.Scenario ?? CpuScenarioDefinition.ForMatch(state.Configuration);
    float own = scenario.VictoryGoals.Sum(goal => goal.EvaluateProgress(state, perspective)) +
      scenario.SecondaryGoals.Sum(goal => goal.EvaluateProgress(state, perspective) * 0.25f);
    float enemy = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
      .Where(team => team != perspective)
      .Select(team => scenario.VictoryGoals.Sum(goal => goal.EvaluateProgress(state, team)) +
        scenario.SecondaryGoals.Sum(goal => goal.EvaluateProgress(state, team) * 0.25f))
      .DefaultIfEmpty(0f)
      .Average();
    return own - enemy;
  }
}

/// <summary>Small, temporary directional score so intentions guide choices without overriding rules or objectives.</summary>
public sealed class IntentEvaluation : IEvaluationTerm
{
  public string Name => "Intent";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float score = 0f;
    foreach (CpuIntent intent in context.Intents.Where(intent => intent.ExpiryTurn >= state.TurnNumber))
    {
      NetworkPiece? piece = intent.PieceId is null ? null : state.Pieces.FirstOrDefault(candidate => candidate.Id == intent.PieceId);
      NetworkPiece? target = intent.TargetPieceId is null ? null : state.Pieces.FirstOrDefault(candidate => candidate.Id == intent.TargetPieceId);
      float priority = intent.Priority / 100f;
      score += intent.Type switch
      {
        CpuIntentType.AttackTarget when target is null => priority * 30f,
        CpuIntentType.AttackTarget when target.Team != perspective => priority * MaterialEvaluation.GetUnitValue(target.Type) * 0.1f,
        CpuIntentType.DefendTarget when target?.Team == perspective => priority * target.Health * 0.08f,
        CpuIntentType.ProtectRoyal when piece?.Team == perspective => priority * piece.Health * 0.08f,
        CpuIntentType.EscortUnit or CpuIntentType.Escape when piece?.Team == perspective && intent.TargetPosition is (int x, int y) position =>
          -priority * Distance((piece.X, piece.Y), position),
        CpuIntentType.HoldLocation or CpuIntentType.CaptureLocation when intent.TargetPosition is (int x, int y) position =>
          priority * OccupancyScore(state, perspective, position),
        CpuIntentType.BlockRoute when intent.TargetPosition is (int x, int y) position =>
          priority * -state.Pieces.Count(candidate => candidate.Team != perspective && candidate.Team != NetworkTeam.Neutral &&
            candidate.X == position.x && candidate.Y == position.y),
        CpuIntentType.PurchaseUnit => priority * state.Teams.GetValueOrDefault(perspective)?.Money * 0.01f ?? 0f,
        _ => 0f
      };
    }
    return score;
  }

  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);

  private static float OccupancyScore(CpuGameState state, NetworkTeam team, (int x, int y) position) =>
    state.Pieces.Where(piece => piece.AttachedToId is null && piece.Team != NetworkTeam.Neutral)
      .Sum(piece => piece.Team == team && piece.X == position.x && piece.Y == position.y ? 1f :
        piece.Team != team && piece.X == position.x && piece.Y == position.y ? -1f : 0f);
}

/// <summary>
/// Rewards useful staging before a piece can score or attack immediately. This gives the search
/// a reason to develop an attack, escort an objective unit, or return treasure instead of only
/// valuing the final action of that plan.
/// </summary>
public sealed class StrategicPositionEvaluation : IEvaluationTerm
{
  public string Name => "Strategy";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float own = ScoreTeam(state, perspective);
    float enemyAverage = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
      .Where(team => team != perspective)
      .Select(team => ScoreTeam(state, team))
      .DefaultIfEmpty(0f)
      .Average();
    return own - enemyAverage;
  }

  private static float ScoreTeam(CpuGameState state, NetworkTeam team)
  {
    List<(int x, int y)> targets = GetTargets(state, team);
    if (targets.Count == 0)
    {
      return 0f;
    }

    float score = 0f;
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null))
    {
      if (!UnitRules.TryGet(piece.Type, out UnitRule rule) || rule.Type == "Farm")
      {
        continue;
      }

      int distance = targets.Min(target => Distance((piece.X, piece.Y), target));
      int reach = Math.Max(1, rule.MoveRange + Math.Max(1, rule.AttackRange));
      // Better range and stronger units make forward staging more valuable, but the score is
      // deliberately smooth so every square moved toward a future target has some value.
      float readiness = 20f / (1f + distance / (float)reach);
      float unitImportance = Math.Clamp(MaterialEvaluation.GetUnitValue(piece.Type) / 70f, 0.4f, 2.2f);
      score += readiness * unitImportance;
    }

    if (state.Configuration.GameMode == "Plunder" && state.TreasureCarrierId is not null)
    {
      NetworkPiece? carrier = state.Pieces.FirstOrDefault(piece => piece.Id == state.TreasureCarrierId);
      if (carrier?.Team == team)
      {
        int returnDistance = state.Board.Cells
          .Where(square => MatchRules.GetSquareOwner(state.Board, state.Configuration.GameMode, square,
            state.Configuration.PlayerCount) == team)
          .Select(square => Distance((carrier.X, carrier.Y), square))
          .DefaultIfEmpty(100)
          .Min();
        score += 90f / (1f + returnDistance);
      }
    }
    return score;
  }

  private static List<(int x, int y)> GetTargets(CpuGameState state, NetworkTeam team)
  {
    HashSet<(int x, int y)> targets = [];
    switch (state.Configuration.GameMode)
    {
      case "Conquest":
        targets.UnionWith(state.Board.Cells.Where(square => MatchRules.IsConquestSquare(state.Board, square)));
        break;
      case "Dominion":
        targets.UnionWith(MatchRules.GetDominionControlPoints(state.Board));
        break;
      case "Plunder" when state.TreasureCarrierId is null && state.TreasurePosition is (int x, int y) treasure:
        targets.Add(treasure);
        break;
    }

    CpuScenarioDefinition scenario = state.Scenario ?? CpuScenarioDefinition.ForMatch(state.Configuration);
    foreach (ICpuScenarioGoal goal in scenario.VictoryGoals.Concat(scenario.SecondaryGoals))
    {
      foreach (CpuIntent intent in goal.GenerateIntents(state, team))
      {
        if (intent.TargetPosition is (int x, int y) position) targets.Add(position);
        if (intent.TargetPieceId is not null && state.Pieces.FirstOrDefault(piece => piece.Id == intent.TargetPieceId) is NetworkPiece target)
        {
          targets.Add((target.X, target.Y));
        }
      }
    }

    targets.UnionWith(state.Pieces
      .Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
      .Select(piece => (piece.X, piece.Y)));
    return targets.ToList();
  }

  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);
}

public sealed class MapControlEvaluation : IEvaluationTerm
{
  public string Name => "MapControl";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    IEnumerable<(int x, int y)> points = state.Configuration.GameMode switch
    {
      "Conquest" => state.Board.Cells.Where(square => MatchRules.IsConquestSquare(state.Board, square)),
      "Dominion" => MatchRules.GetDominionControlPoints(state.Board),
      "Plunder" when state.TreasurePosition is (int x, int y) treasure => [treasure],
      _ => []
    };
    float score = 0f;
    foreach ((int x, int y) point in points)
    {
      foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.AttachedToId is null && piece.Team != NetworkTeam.Neutral))
      {
        if (!UnitRules.TryGet(piece.Type, out UnitRule rule) || !piece.OccupiedSquares(rule).Contains(point))
        {
          continue;
        }
        score += piece.Team == perspective ? 1f : -1f;
      }
    }
    return score;
  }
}

public sealed class EconomyEvaluation : IEvaluationTerm
{
  public string Name => "Economy";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float own = state.Teams.GetValueOrDefault(perspective)?.Money ?? 0;
    float enemyAverage = (float)TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
      .Where(team => team != perspective)
      .Select(team => state.Teams.GetValueOrDefault(team)?.Money ?? 0)
      .DefaultIfEmpty(0)
      .Average();
    return own - enemyAverage;
  }
}

public sealed class MobilityEvaluation : IEvaluationTerm
{
  public string Name => "Mobility";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float value = 0f;
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team != NetworkTeam.Neutral && !piece.HasMovedThisTurn))
    {
      if (!UnitRules.TryGet(piece.Type, out UnitRule rule))
      {
        continue;
      }
      value += (piece.Team == perspective ? 1f : -1f) * rule.MoveRange;
    }
    return value;
  }
}

public sealed class FormationEvaluation : IEvaluationTerm
{
  public string Name => "Formation";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float score = 0f;
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null))
    {
      int nearbyFriendlies = state.Pieces.Count(other => other.Id != piece.Id && other.Team == piece.Team &&
        Math.Abs(other.X - piece.X) + Math.Abs(other.Y - piece.Y) <= 2);
      score += (piece.Team == perspective ? 1f : -1f) * Math.Min(nearbyFriendlies, 3);
    }
    return score;
  }
}

/// <summary>
/// Values concrete, already-applied ability effects.  This deliberately scores state rather than
/// granting an abstract bonus for owning an ability, so the personality changes choices without
/// changing unit statistics or legality.
/// </summary>
public sealed class AbilityStateEvaluation : IEvaluationTerm
{
  public string Name => "Ability";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float score = 0f;
    IReadOnlyDictionary<string, NetworkPiece> pieces = state.Pieces.ToDictionary(piece => piece.Id, StringComparer.Ordinal);
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team != NetworkTeam.Neutral))
    {
      float effectValue = 0f;
      if (piece.MarkedTargetId is not null && pieces.TryGetValue(piece.MarkedTargetId, out NetworkPiece? marked) &&
          marked.Team != piece.Team)
      {
        // Marking improves the next allied attack; cap it by the target's rule-derived value so
        // future balance changes do not make this a fixed per-unit valuation.
        effectValue += Math.Min(20f, MaterialEvaluation.GetUnitValue(marked.Type) * 0.15f);
      }
      if (piece.AttachmentKind == NetworkAttachmentKind.Guard && piece.AttachedToId is not null)
      {
        effectValue += Math.Min(12f, MaterialEvaluation.GetUnitValue(piece.Type) * 0.12f);
      }
      if (piece.AttachmentKind == NetworkAttachmentKind.Carried && piece.AttachedToId is not null)
      {
        effectValue += Math.Min(8f, MaterialEvaluation.GetUnitValue(piece.Type) * 0.08f);
      }
      score += piece.Team == perspective ? effectValue : -effectValue;
    }

    foreach (NetworkTeam mineOwner in state.Mines.Values)
    {
      float mineValue = 5f;
      score += mineOwner == perspective ? mineValue : -mineValue;
    }
    return score;
  }
}

/// <summary>Values forest cover and defenders around farms, royals, and other strategic assets.</summary>
public sealed class AssetSafetyEvaluation : IEvaluationTerm
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder;

  public AssetSafetyEvaluation(ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    _threatMapBuilder = threatMapBuilder ?? new CpuThreatMapBuilder();
  }

  public string Name => "AssetSafety";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float score = 0f;
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null))
    {
      if (!UnitRules.TryGet(piece.Type, out UnitRule rule))
      {
        continue;
      }

      int forestSquares = OccupiedSquares(piece, rule).Count(state.Terrain.IsForest);
      int nearbyDefenders = state.Pieces.Count(other => other.Id != piece.Id && other.Team == piece.Team &&
        other.AttachedToId is null && Math.Abs(other.X - piece.X) + Math.Abs(other.Y - piece.Y) <= 3);
      float importance = rule.Type == "Farm" ? 4f : rule.Category == RuleCategory.Royal ? 2.5f : 0.35f;
      float assetScore = forestSquares * importance * 3f + Math.Min(nearbyDefenders, 3) * importance;
      foreach (NetworkTeam enemy in TeamRules.GetActiveTeams(state.Configuration.PlayerCount).Where(team => team != piece.Team))
      {
        CpuPieceThreat? threat = context.Cache.GetThreatMap(state, enemy, _threatMapBuilder).GetThreat(piece.Id);
        if (threat is not null)
        {
          assetScore -= threat.TotalExpectedDamage * importance;
          if (threat.IsLethal) assetScore -= MaterialEvaluation.GetUnitValue(piece.Type) * importance;
        }
      }
      score += piece.Team == perspective ? assetScore : -assetScore;
    }
    return score;
  }

  private static IEnumerable<(int x, int y)> OccupiedSquares(NetworkPiece piece, UnitRule rule)
  {
    for (int y = 0; y < rule.Height; y++)
    for (int x = 0; x < rule.Width; x++)
    {
      yield return (piece.X + x, piece.Y + y);
    }
  }
}

public sealed class ActionEfficiencyEvaluation : IEvaluationTerm
{
  public string Name => "ActionEfficiency";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context) =>
    Globals.ActionLimitsEnabled && state.CurrentTurn == perspective
      ? MatchRules.ActionsPerTurn - state.ActionsRemaining
      : 0f;
}
