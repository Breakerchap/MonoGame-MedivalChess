from pathlib import Path

# 1) Make unused actions a real state-evaluation cost during unlimited-action turns.
path = Path('MedivalChess.CPU/Evaluation.cs')
text = path.read_text()
old = '''public sealed class ActionEfficiencyEvaluation : IEvaluationTerm
{
  public string Name => "ActionEfficiency";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context) =>
    Globals.ActionLimitsEnabled && state.CurrentTurn == perspective
      ? MatchRules.ActionsPerTurn - state.ActionsRemaining
      : 0f;
}
'''
new = '''public sealed class ActionEfficiencyEvaluation : IEvaluationTerm
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder = new CpuThreatMapBuilder();

  public string Name => "ActionEfficiency";

  public float Evaluate(CpuGameState state, NetworkTeam perspective, EvaluationContext context)
  {
    if (state.CurrentTurn != perspective)
    {
      return 0f;
    }

    if (Globals.ActionLimitsEnabled)
    {
      return MatchRules.ActionsPerTurn - state.ActionsRemaining;
    }

    // Unlimited-action turns should use the army that is already on the board. Search depths are
    // often partial under a short clock, so score unused opportunities in every intermediate state
    // rather than relying on EndTurn itself to notice that half the army did nothing.
    CpuThreatMap attacks = context.Cache.GetThreatMap(state, perspective, _threatMapBuilder);
    HashSet<string> immediateAttackers = attacks.ThreatsByPiece.Values
      .SelectMany(threat => threat.AttackerIds)
      .ToHashSet(StringComparer.Ordinal);
    NetworkPiece[] enemies = state.Pieces.Where(piece =>
      piece.Team != perspective && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null).ToArray();

    float penalty = 0f;
    foreach (NetworkPiece piece in state.Pieces.Where(piece =>
      piece.Team == perspective && piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out _)))
    {
      UnitRule rule = UnitRules.GetRequired(piece.Type);
      if (!piece.HasAttackedThisTurn && immediateAttackers.Contains(piece.Id))
      {
        // Missing a shot is the clearest wasted action. This is intentionally much stronger than
        // the ordinary idle-move cost so a searched line that attacks beats one that just develops.
        float bestTargetValue = attacks.ThreatsByPiece.Values
          .Where(threat => threat.AttackerIds.Contains(piece.Id, StringComparer.Ordinal))
          .Select(threat => state.Pieces.FirstOrDefault(target => target.Id == threat.PieceId))
          .Where(target => target is not null)
          .Select(target => HangingPieceEvaluation.GetExchangeValue(target!.Type, state))
          .DefaultIfEmpty(20f)
          .Max();
        penalty += 150f + Math.Min(70f, bestTargetValue * 0.65f);
      }

      bool untouched = !piece.HasMovedThisTurn && !piece.HasAttackedThisTurn;
      if (!untouched || rule.MoveRange <= 0)
      {
        continue;
      }

      // A completely idle mobile unit receives a smaller development penalty. Scale it with how
      // far the piece still is from contributing, so holding a useful firing/defensive position is
      // far less objectionable than leaving a remote unit parked for no reason.
      int nearestEnemy = enemies
        .Select(enemy => Math.Abs(piece.X - enemy.X) + Math.Abs(piece.Y - enemy.Y))
        .DefaultIfEmpty(0)
        .Min();
      if (nearestEnemy > 0)
      {
        int usefulReach = Math.Max(1, rule.AttackRange);
        int excessDistance = Math.Max(0, nearestEnemy - usefulReach);
        float idle = 10f + Math.Min(28f, excessDistance * 2.5f);
        if (rule.Category == RuleCategory.Royal && !CpuObjectiveRules.IsRoyalEliminationObjective(state))
        {
          idle *= 0.6f;
        }
        penalty += idle;
      }
    }

    return -penalty;
  }
}
'''
if old not in text:
    raise SystemExit('ActionEfficiencyEvaluation block not found')
text = text.replace(old, new, 1)
path.write_text(text)

# 2) Make attack ordering materially more aggressive, and never let a piece that can currently
# attack move away before spending that attack.
path = Path('MedivalChess.CPU/CpuActionCandidates.cs')
text = path.read_text()
text = text.replace(
'''      return candidate with { Score = candidate.Score + 180f, Reason = $"{candidate.Reason}; takes an available attack" };''',
'''      return candidate with { Score = candidate.Score + 300f, Reason = $"{candidate.Reason}; strongly prioritises an available attack" };''', 1)
text = text.replace('''        PurchaseAction => 110f,
        UseAbilityAction => 80f,
        EndTurnAction => 220f,''',
'''        PurchaseAction => 190f,
        UseAbilityAction => 95f,
        EndTurnAction => 360f,''', 1)
text = text.replace('''          state.Pieces.Any(target => target.Team != action.Team && target.Team != NetworkTeam.Neutral &&
            target.AttachedToId is null && CpuGameRules.CanDirectlyAttack(state, mover, target)) => 150f,''',
'''          state.Pieces.Any(target => target.Team != action.Team && target.Team != NetworkTeam.Neutral &&
            target.AttachedToId is null && CpuGameRules.CanDirectlyAttack(state, mover, target)) => 280f,''', 1)
path.write_text(text)

# 3) Enforce the attack-before-ending invariant in search and final plan validation.
path = Path('MedivalChess.CPU/CpuSearch.cs')
text = path.read_text()
needle = '''    if (!Globals.ActionLimitsEnabled)
    {
      bool immediateCombat = candidates.Any(candidate => candidate.Action is AttackAction);
      bool royalEmergency = IsRoyalUnderDirectThreat(state, team);
      if (immediateCombat || royalEmergency)
      {
        ScoredAction[] boardResponses = candidates
          .Where(candidate => candidate.Action is AttackAction or MoveAction or UseAbilityAction)
          .ToArray();
        if (boardResponses.Length > 0)
        {
          // Purchases cannot act this turn and therefore cannot improve an immediate exchange.
          // Resolve the fight / royal emergency first, then reconsider spending from the resulting state.
          return boardResponses;
        }
      }
    }
'''
replacement = '''    if (!Globals.ActionLimitsEnabled)
    {
      ScoredAction[] attacks = candidates.Where(candidate => candidate.Action is AttackAction).ToArray();
      bool royalEmergency = IsRoyalUnderDirectThreat(state, team);
      if (attacks.Length > 0 || royalEmergency)
      {
        HashSet<string> attackers = attacks
          .Select(candidate => ((AttackAction)candidate.Action).AttackerId)
          .ToHashSet(StringComparer.Ordinal);
        ScoredAction[] boardResponses = candidates
          .Where(candidate => candidate.Action switch
          {
            AttackAction => true,
            // Once a piece has a shot, do not let it move away and throw that attack away. Other
            // units may still reposition or use a setup ability while the attack obligation remains.
            MoveAction move => !attackers.Contains(move.PieceId),
            UseAbilityAction => true,
            _ => false
          })
          .ToArray();
        if (boardResponses.Length > 0)
        {
          return boardResponses
            .OrderByDescending(candidate => candidate.Action is AttackAction)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();
        }
      }
    }
'''
if needle not in text:
    raise SystemExit('Immediate combat priority block not found')
text = text.replace(needle, replacement, 1)

old_verify = '''    // Ending an unlimited turn is bookkeeping, not a strategic continuation. It is safe to add
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
'''
new_verify = '''    if (!Globals.ActionLimitsEnabled && state.InitialBuy is null && current.CurrentTurn == team && !current.IsFinished)
    {
      // Attack availability is now a CPU invariant rather than merely a heuristic. A short search
      // may time out on the move half of a move->attack line; complete any attacks that are legal in
      // the exact searched state before EndTurn. This never invents movement or purchases.
      for (int forcedAttack = 0; forcedAttack < 64 && current.CurrentTurn == team && !current.IsFinished; forcedAttack++)
      {
        AttackAction? attack = ChooseMandatoryAttack(current, team);
        if (attack is null)
        {
          break;
        }
        verified.Add(attack);
        current = attack.Apply(current);
      }

      EndTurnAction endTurn = new(team);
      if (endTurn.IsLegal(current) && ChooseMandatoryAttack(current, team) is null)
      {
        verified.Add(endTurn);
      }
    }
'''
if old_verify not in text:
    raise SystemExit('Verify end-turn block not found')
text = text.replace(old_verify, new_verify, 1)

helper_needle = '''  /// <summary>
  /// Opening farms are free, one-action placements with a deterministic terrain/territory ranking.
'''
helper = '''  private static AttackAction? ChooseMandatoryAttack(CpuGameState state, NetworkTeam team)
  {
    return new CpuActionGenerator().GenerateSearchActions(state, team, 1)
      .OfType<AttackAction>()
      .Where(attack => attack.IsLegal(state))
      .OrderByDescending(attack => CpuGameRules.ApplyLegal(state, attack).IsFinished)
      .ThenByDescending(attack => ScoreMandatoryAttack(state, attack))
      .ThenBy(attack => attack.Describe(), StringComparer.Ordinal)
      .FirstOrDefault();
  }

  private static float ScoreMandatoryAttack(CpuGameState state, AttackAction attack)
  {
    NetworkPiece? attacker = state.Pieces.FirstOrDefault(piece => piece.Id == attack.AttackerId);
    NetworkPiece? target = attack.TargetPieceId is null
      ? null
      : state.Pieces.FirstOrDefault(piece => piece.Id == attack.TargetPieceId);
    if (attacker is null)
    {
      return float.MinValue;
    }
    if (target is null)
    {
      return 20f;
    }

    int damage = CpuGameRules.EstimateAttackDamage(state, attacker, target);
    float score = CombatTargetScoring.GetDamageReward(state, attack.Team, target, damage) +
      CpuStrategicHeuristics.ScoreAction(state, attack);
    if (damage >= target.Health)
    {
      score += CombatTargetScoring.GetKillReward(state, attack.Team, target);
    }
    return score;
  }

  /// <summary>
  /// Opening farms are free, one-action placements with a deterministic terrain/territory ranking.
'''
if helper_needle not in text:
    raise SystemExit('Mandatory attack helper insertion point not found')
text = text.replace(helper_needle, helper, 1)
path.write_text(text)

# 4) Regression tests: idle-state scoring, direct attack before movement, and no EndTurn while a
# legal attack survives the searched prefix.
path = Path('MedivalChess.Tests/CpuPlanningEfficiencyTests.cs')
text = path.read_text()
insert = '''  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y)
'''
tests = '''  [Fact]
  public void ActionEfficiency_HeavilyPenalisesLeavingAnAvailableAttackUnused()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0),
        Piece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, 1));
      ActionEfficiencyEvaluation term = new();
      EvaluationContext context = new(CpuProfile.Hard(17));
      float idle = term.Evaluate(state, NetworkTeam.Red, context);
      AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-peasant", 0, 1);
      Assert.True(attack.IsLegal(state));
      float afterAttack = term.Evaluate(attack.Apply(state), NetworkTeam.Red, context);

      Assert.True(afterAttack >= idle + 150f, $"idle={idle}, after={afterAttack}");
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void ActionEfficiency_PenalisesLeavingAMobileRemoteUnitCompletelyIdle()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 5),
        Piece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -5));
      ActionEfficiencyEvaluation term = new();
      EvaluationContext context = new(CpuProfile.Hard(18));
      float idle = term.Evaluate(state, NetworkTeam.Red, context);
      MoveAction move = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
        .OfType<MoveAction>()
        .Where(action => action.PieceId == "red-soldier")
        .OrderBy(action => Math.Abs(action.DestinationY - (-5)))
        .First();
      float afterMove = term.Evaluate(move.Apply(state), NetworkTeam.Red, context);

      Assert.True(afterMove > idle + 10f, $"idle={idle}, after={afterMove}, move={move.Describe()}");
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void UnlimitedTurn_NeverEndsWhileALegalAttackRemains()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier-one", "Soldier", NetworkTeam.Red, 0, 0),
        Piece("red-soldier-two", "Soldier", NetworkTeam.Red, 2, 0),
        Piece("blue-defender-one", "Defender", NetworkTeam.Blue, 0, 1),
        Piece("blue-defender-two", "Defender", NetworkTeam.Blue, 2, 1));
      CpuProfile profile = CpuProfile.Hard(19);

      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
      CpuGameState current = state;
      foreach (ICpuGameAction action in plan.Actions)
      {
        if (action is EndTurnAction)
        {
          bool attackRemains = new CpuActionGenerator().GenerateSearchActions(current, NetworkTeam.Red, 1)
            .OfType<AttackAction>()
            .Any();
          Assert.False(attackRemains, string.Join(" | ", plan.Actions.Select(candidate => candidate.Describe())));
        }
        Assert.True(action.IsLegal(current), action.Describe());
        current = action.Apply(current);
      }

      Assert.Contains(plan.Actions, action => action is AttackAction { AttackerId: "red-soldier-one" });
      Assert.Contains(plan.Actions, action => action is AttackAction { AttackerId: "red-soldier-two" });
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

'''
if insert not in text:
    raise SystemExit('Test insertion point not found')
text = text.replace(insert, tests + insert, 1)
path.write_text(text)
