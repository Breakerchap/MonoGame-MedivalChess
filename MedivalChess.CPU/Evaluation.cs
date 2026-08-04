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
  public float MapControl { get; init; } = 0.4f;
  public float Economy { get; init; } = 0.5f;
  public float Mobility { get; init; } = 0.25f;
  public float Formation { get; init; } = 0.2f;
  public float ActionEfficiency { get; init; } = 0.4f;
  public float RepetitionPenalty { get; init; } = 0.8f;
}

public sealed record EvaluationContext(CpuProfile Profile, IReadOnlyList<CpuIntent>? Intents = null);

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

  public StateEvaluator(IEnumerable<IEvaluationTerm>? terms = null)
  {
    _terms = terms?.ToArray() ??
    [
      new MaterialEvaluation(),
      new HealthEvaluation(),
      new ThreatEvaluation(),
      new RoyalSafetyEvaluation(),
      new ObjectiveEvaluation(),
      new MapControlEvaluation(),
      new EconomyEvaluation(),
      new MobilityEvaluation(),
      new FormationEvaluation(),
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
      "MapControl" => weights.MapControl * personality.ObjectiveFocus,
      "Economy" => weights.Economy * personality.EconomyFocus * scenario.Economy,
      "Mobility" => weights.Mobility,
      "Formation" => weights.Formation * personality.FormationPreference,
      "ActionEfficiency" => weights.ActionEfficiency,
      _ => 1f
    };
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
  public string Name => "Threat";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float value = 0f;
    foreach (NetworkPiece attacker in state.Pieces.Where(piece => piece.Team != NetworkTeam.Neutral))
    {
      foreach (NetworkPiece target in state.Pieces.Where(target => target.Team != attacker.Team && target.Team != NetworkTeam.Neutral))
      {
        if (!CpuGameRules.CanDirectlyAttack(state, attacker, target))
        {
          continue;
        }

        float tacticalValue = Math.Min(MaterialEvaluation.GetUnitValue(target.Type), UnitRules.GetRequired(attacker.Type).Attack * 3f);
        if (target.Health <= UnitRules.GetRequired(attacker.Type).Attack)
        {
          tacticalValue += MaterialEvaluation.GetUnitValue(target.Type) * 0.7f;
        }
        value += attacker.Team == perspective ? tacticalValue : -tacticalValue;
      }
    }
    return value;
  }
}

public sealed class RoyalSafetyEvaluation : IEvaluationTerm
{
  public string Name => "RoyalSafety";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float safety = 0f;
    foreach (NetworkPiece royal in state.Pieces.Where(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal))
    {
      UnitRule royalRule = UnitRules.GetRequired(royal.Type);
      float health = royal.Health / (float)Math.Max(1, royalRule.Health) * 120f;
      float danger = state.Pieces.Where(enemy => enemy.Team != royal.Team && enemy.Team != NetworkTeam.Neutral)
        .Where(enemy => CpuGameRules.CanDirectlyAttack(state, enemy, royal))
        .Sum(enemy => UnitRules.GetRequired(enemy.Type).Attack * 5f);
      float score = health - danger;
      safety += royal.Team == perspective ? score : -score;
    }
    return safety;
  }
}

public sealed class ObjectiveEvaluation : IEvaluationTerm
{
  public string Name => "Objective";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    IReadOnlyList<ICpuScenarioGoal> goals = state.Scenario?.VictoryGoals ??
      CpuScenarioDefinition.ForMatch(state.Configuration).VictoryGoals;
    float own = goals.Sum(goal => goal.EvaluateProgress(state, perspective));
    float enemy = TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
      .Where(team => team != perspective)
      .Select(team => goals.Sum(goal => goal.EvaluateProgress(state, team)))
      .DefaultIfEmpty(0f)
      .Average();
    return own - enemy;
  }
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

public sealed class ActionEfficiencyEvaluation : IEvaluationTerm
{
  public string Name => "ActionEfficiency";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context) =>
    state.CurrentTurn == perspective ? MatchRules.ActionsPerTurn - state.ActionsRemaining : 0f;
}
