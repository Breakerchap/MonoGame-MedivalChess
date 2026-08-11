from pathlib import Path

# Evaluation: add explicit hanging/exchange safety term.
path = Path('MedivalChess.CPU/Evaluation.cs')
text = path.read_text()
text = text.replace(
'''      new ThreatEvaluation(_threatMapBuilder),
      new RoyalSafetyEvaluation(_threatMapBuilder),''',
'''      new ThreatEvaluation(_threatMapBuilder),
      new HangingPieceEvaluation(_threatMapBuilder),
      new RoyalSafetyEvaluation(_threatMapBuilder),''')
text = text.replace(
'''      "Threat" => weights.ImmediateThreats * personality.Aggression,
      "RoyalSafety" => weights.RoyalSafety * personality.RoyalProtection * personality.Caution * scenario.RoyalSafety *''',
'''      "Threat" => weights.ImmediateThreats * personality.Aggression,
      "HangingPieces" => weights.AssetSafety * personality.Caution * 0.85f,
      "RoyalSafety" => weights.RoyalSafety * personality.RoyalProtection * personality.Caution * scenario.RoyalSafety *''')
needle = '''public sealed class RoyalSafetyEvaluation : IEvaluationTerm
{'''
insert = r'''/// <summary>
/// Explicit exchange-safety evaluation for pieces that can be damaged or removed immediately.
/// Unlike flat material, this uses a compressed purchase-cost curve only while judging a concrete
/// tactical loss, so a Knight is more worth saving than a Peasant without teaching the CPU to
/// passively hoard expensive units. A credible recapture reduces, but never erases, the danger.
/// </summary>
public sealed class HangingPieceEvaluation : IEvaluationTerm
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder;

  public HangingPieceEvaluation(ICpuThreatMapBuilder? threatMapBuilder = null)
  {
    _threatMapBuilder = threatMapBuilder ?? new CpuThreatMapBuilder();
  }

  public string Name => "HangingPieces";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    float score = 0f;
    foreach (NetworkPiece target in state.Pieces.Where(piece =>
      piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category != RuleCategory.Royal))
    {
      float worstRisk = 0f;
      foreach (NetworkTeam enemy in TeamRules.GetActiveTeams(state.Configuration.PlayerCount).Where(team => team != target.Team))
      {
        CpuPieceThreat? threat = context.Cache.GetThreatMap(state, enemy, _threatMapBuilder).GetThreat(target.Id);
        if (threat is null)
        {
          continue;
        }

        // Guard damage is redirected to the attached Guard. Judge the health/value actually at
        // risk first rather than incorrectly calling the protected premium unit itself hanging.
        NetworkPiece exposed = state.Pieces.FirstOrDefault(piece =>
          piece.AttachedToId == target.Id && piece.AttachmentKind == NetworkAttachmentKind.Guard) ?? target;
        float exposedValue = GetExchangeValue(exposed.Type, state);
        float incomingRatio = Math.Min(1.5f, threat.TotalExpectedDamage / (float)Math.Max(1, exposed.Health));
        bool lethal = threat.TotalExpectedDamage >= exposed.Health;
        float risk = exposedValue * incomingRatio * 0.45f;
        if (lethal)
        {
          risk += exposedValue * 1.1f;
        }
        if (threat.AttackerCount > 1)
        {
          risk += exposedValue * Math.Min(0.28f, (threat.AttackerCount - 1) * 0.08f);
        }

        float recaptureValue = GetBestRecaptureValue(state, target.Team, threat);
        if (recaptureValue > 0f)
        {
          risk -= Math.Min(risk * 0.65f, recaptureValue * 0.7f);
        }
        worstRisk = Math.Max(worstRisk, Math.Max(0f, risk));
      }
      score += target.Team == perspective ? -worstRisk : worstRisk;
    }
    return score;
  }

  internal static float GetExchangeValue(string type, CpuGameState state)
  {
    if (!UnitRules.TryGet(type, out UnitRule rule))
    {
      return 20f;
    }

    if (rule.Category == RuleCategory.Royal)
    {
      return MaterialEvaluation.GetUnitValue(type, state);
    }

    // Price matters here because this is a concrete sacrifice/trade calculation. Compress the
    // curve above 40 gold so costly specialists are protected without becoming untouchable.
    float cost = Math.Max(0f, rule.Cost);
    return 20f + Math.Min(cost, 40f) * 0.9f + Math.Max(0f, cost - 40f) * 0.45f;
  }

  private static float GetBestRecaptureValue(CpuGameState state, NetworkTeam defendingTeam, CpuPieceThreat threat)
  {
    float best = 0f;
    foreach (string attackerId in threat.AttackerIds)
    {
      NetworkPiece? attacker = state.Pieces.FirstOrDefault(piece => piece.Id == attackerId);
      if (attacker is null || !UnitRules.TryGet(attacker.Type, out _))
      {
        continue;
      }

      float attackerValue = GetExchangeValue(attacker.Type, state);
      foreach (NetworkPiece defender in state.Pieces.Where(piece =>
        piece.Team == defendingTeam && piece.Id != threat.PieceId && piece.AttachedToId is null &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Attack > 0 && !piece.HasAttackedThisTurn))
      {
        if (!CpuGameRules.CanDirectlyAttack(state, defender, attacker))
        {
          continue;
        }

        int damage = CpuGameRules.EstimateAttackDamage(state, defender, attacker);
        float recoverable = damage >= attacker.Health
          ? attackerValue
          : attackerValue * Math.Clamp(damage / (float)Math.Max(1, attacker.Health), 0f, 1f) * 0.45f;
        best = Math.Max(best, recoverable);
      }
    }
    return best;
  }
}

'''
if needle not in text:
    raise SystemExit('RoyalSafety insertion point not found')
text = text.replace(needle, insert + needle)
path.write_text(text)

# Search: adaptive opponent search + preserve completed beam after timeout.
path = Path('MedivalChess.CPU/CpuSearch.cs')
text = path.read_text()
records = '''internal sealed record SearchIterationResult(
  List<SearchNode> Beam,
  bool Completed,
  ICpuGameAction? FallbackAction,
  int RootLegalActionCount,
  int PrincipalVariationPromotions,
  int TacticalMacrosGenerated
);
'''
policy = records + r'''
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
'''
if records not in text:
    raise SystemExit('SearchIterationResult block not found')
text = text.replace(records, policy)
old_rank = '''    List<(SearchNode Node, float Score, float OpponentPenalty)> ranked = [];
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
          searchActionCache, candidateCache, evaluatedStates, ref evaluationCacheHits, ref candidateCacheHits,
          ref nodesGenerated, ref nodesEvaluated,
          ref timedOut, ref nodeBudgetReached, ref cancelled);
        opponentPenalty = Math.Max(0f, node.Score - afterOpponent);
        adjustedScore = afterOpponent;
      }
      ranked.Add((node, adjustedScore, opponentPenalty));
    }
'''
new_rank = '''    List<(SearchNode Node, float Score, float OpponentPenalty)> ranked = [];
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
'''
if old_rank not in text:
    raise SystemExit('ranking block not found')
text = text.replace(old_rank, new_rank)
old_shape = '''    NetworkTeam opponent = state.CurrentTurn;
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
'''
new_shape = '''    NetworkTeam opponent = state.CurrentTurn;
    CpuOpponentSearchShape replyShape = CpuOpponentSearchPolicy.Choose(state, perspective, profile, context);
    int actionsToPredict = replyShape.ActionsToPredict;
    if (actionsToPredict <= 0)
    {
      return EvaluateCached(state, perspective, context, evaluatedStates, ref evaluationCacheHits).Total;
    }

    // Tactical positions keep the full configured response horizon. Quiet positions use a
    // narrower reply proof, freeing the same fixed turn budget for more principal search work.
    int opponentBeamWidth = replyShape.BeamWidth;
'''
if old_shape not in text:
    raise SystemExit('opponent shape block not found')
text = text.replace(old_shape, new_shape)
path.write_text(text)

# Regression tests for both behaviours.
Path('MedivalChess.Tests/CpuTacticalSafetyTests.cs').write_text(r'''using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuTacticalSafetyTests
{
  [Fact]
  public void HangingPiece_PenalisesLosingAnExpensiveUnitMoreThanACheapOne()
  {
    CpuGameState knightState = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0));
    CpuGameState peasantState = CreateState(
      Piece("target", "Peasant", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0));
    HangingPieceEvaluation evaluator = new();
    EvaluationContext context = new(new CpuProfile());

    float knightScore = evaluator.Evaluate(knightState, NetworkTeam.Red, context);
    float peasantScore = evaluator.Evaluate(peasantState, NetworkTeam.Red, new EvaluationContext(new CpuProfile()));

    Assert.True(knightScore < peasantScore - 20f, $"knight={knightScore}, peasant={peasantScore}");
  }

  [Fact]
  public void HangingPiece_RecaptureMakesTheExchangeLessDangerous()
  {
    CpuGameState undefended = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0));
    CpuGameState defended = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0),
      Piece("defender", "Soldier", NetworkTeam.Red, 2, 0));
    HangingPieceEvaluation evaluator = new();

    float undefendedScore = evaluator.Evaluate(undefended, NetworkTeam.Red, new EvaluationContext(new CpuProfile()));
    float defendedScore = evaluator.Evaluate(defended, NetworkTeam.Red, new EvaluationContext(new CpuProfile()));

    Assert.True(defendedScore > undefendedScore + 8f, $"defended={defendedScore}, undefended={undefendedScore}");
  }

  [Fact]
  public void OpponentSearchPolicy_UsesFullSearchForLethalPremiumThreat()
  {
    CpuGameState state = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0),
      currentTurn: NetworkTeam.Blue);
    CpuProfile profile = CpuProfile.Hard();

    CpuOpponentSearchShape shape = CpuOpponentSearchPolicy.Choose(
      state, NetworkTeam.Red, profile, new EvaluationContext(profile));

    Assert.Equal(profile.Search.OpponentActionsToPredict, shape.ActionsToPredict);
    Assert.Equal(profile.Search.OpponentBeamWidth, shape.BeamWidth);
    Assert.True(shape.IsTactical);
  }

  [Fact]
  public void OpponentSearchPolicy_ReducesQuietReplySearch()
  {
    CpuGameState state = CreateState(
      Piece("red", "Knight", NetworkTeam.Red, 0, 0),
      Piece("blue", "Soldier", NetworkTeam.Blue, 8, 8),
      currentTurn: NetworkTeam.Blue);
    CpuProfile profile = CpuProfile.Hard();

    CpuOpponentSearchShape shape = CpuOpponentSearchPolicy.Choose(
      state, NetworkTeam.Red, profile, new EvaluationContext(profile));

    Assert.True(shape.ActionsToPredict < profile.Search.OpponentActionsToPredict);
    Assert.True(shape.BeamWidth < profile.Search.OpponentBeamWidth);
    Assert.False(shape.IsTactical);
    Assert.True(shape.ActionsToPredict >= 2);
  }

  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y, int? health = null)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return new NetworkPiece(id, type, team, x, y, health ?? rule.Health);
  }

  private static CpuGameState CreateState(
    NetworkPiece first,
    NetworkPiece second,
    NetworkPiece? third = null,
    NetworkTeam currentTurn = NetworkTeam.Red)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 9917, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    NetworkPiece[] pieces = third is null ? [first, second] : [first, second, third];
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      currentTurn,
      terrain: new BattlefieldTerrain());
  }
}
''')
