using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Creates advisory, short-lived intentions from scenarios, immediate threats, and personality.</summary>
public interface ICpuIntentGenerator
{
  IReadOnlyList<CpuIntent> Generate(
    CpuGameState state,
    NetworkTeam team,
    CpuProfile profile,
    CpuEvaluationCache? cache = null
  );
}

public sealed class CpuIntentGenerator : ICpuIntentGenerator
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder;

  public CpuIntentGenerator(ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    _threatMapBuilder = threatMapBuilder ?? new CpuThreatMapBuilder();
  }

  public IReadOnlyList<CpuIntent> Generate(
    CpuGameState state,
    NetworkTeam team,
    CpuProfile profile,
    CpuEvaluationCache? cache = null
  )
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(profile);
    List<CpuIntent> intents = state.Scenario is null
      ? []
      : state.Scenario.VictoryGoals
        .Concat(state.Scenario.SecondaryGoals)
        // A campaign failure condition is often the most urgent reason to defend a route or
        // protected unit. Its intents remain advisory, but must participate in normal planning.
        .Concat(state.Scenario.DefeatConditions)
        .SelectMany(goal => goal.GenerateIntents(state, team))
        .ToList();

    foreach (NetworkTeam enemy in TeamRules.GetActiveTeams(state.Configuration.PlayerCount).Where(candidate => candidate != team))
    {
      CpuThreatMap enemyThreats = cache?.GetThreatMap(state, enemy, _threatMapBuilder) ?? _threatMapBuilder.Build(state, enemy);
      foreach (CpuPieceThreat threat in enemyThreats.ThreatsByPiece.Values)
      {
        NetworkPiece? target = state.Pieces.FirstOrDefault(piece => piece.Id == threat.PieceId && piece.Team == team);
        if (target is null || !UnitRules.TryGet(target.Type, out UnitRule rule))
        {
          continue;
        }
        if (rule.Category == RuleCategory.Royal)
        {
          intents.Add(new CpuIntent(CpuIntentType.ProtectRoyal, threat.IsLethal ? 180f : 120f, PieceId: target.Id,
            ExpiryTurn: state.TurnNumber + 1));
        }
        else if (rule.Type == "Farm")
        {
          intents.Add(new CpuIntent(CpuIntentType.DefendTarget, threat.IsLethal ? 100f : 65f, TargetPieceId: target.Id,
            ExpiryTurn: state.TurnNumber + 1));
        }
      }
    }

    if (profile.Personality.EconomyFocus > 1.15f && state.Teams.GetValueOrDefault(team)?.Money > 0)
    {
      intents.Add(new CpuIntent(CpuIntentType.PurchaseUnit, 25f * profile.Personality.EconomyFocus,
        ExpiryTurn: state.TurnNumber + 1));
    }
    if (profile.Personality.Aggression > 1.15f)
    {
      intents.Add(new CpuIntent(CpuIntentType.AttackTarget, 20f * profile.Personality.Aggression,
        ExpiryTurn: state.TurnNumber + 1));
    }

    return intents
      .OrderByDescending(intent => intent.Priority)
      .ThenBy(intent => intent.Type)
      .ThenBy(intent => intent.PieceId, StringComparer.Ordinal)
      .ThenBy(intent => intent.TargetPieceId, StringComparer.Ordinal)
      .Take(12)
      .ToArray();
  }
}
