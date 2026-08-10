using MedivalChess.Shared;

namespace MedivalChess.CPU;

public sealed record ScoredAction(ICpuGameAction Action, float Score, string Reason);

public interface IActionCandidateSelector
{
  IReadOnlyList<ScoredAction> SelectCandidates(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> legalActions,
    CpuSearchSettings settings,
    CpuPersonality? personality = null
  );
}

/// <summary>Ranks legal actions before beam search without changing their legality.</summary>
public sealed class CpuActionCandidateSelector : IActionCandidateSelector
{
  private readonly ICpuThreatMapBuilder _threatMapBuilder = new CpuThreatMapBuilder();

  public IReadOnlyList<ScoredAction> SelectCandidates(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> legalActions,
    CpuSearchSettings settings,
    CpuPersonality? personality = null
  )
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(legalActions);
    ArgumentNullException.ThrowIfNull(settings);

    // Candidate scoring is on the hot path. Derive board-wide inputs once instead of repeating
    // an objective scan and farm-territory scan for every legal movement or purchase.
    IReadOnlyDictionary<string, NetworkPiece> piecesById = state.Pieces.ToDictionary(piece => piece.Id, StringComparer.Ordinal);
    (int x, int y)[] goals = GetGoalPositions(state, team).ToArray();
    NetworkPiece[] relevantEnemies = state.Pieces.Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
      piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) && (rule.Attack > 0 || piece.Type == "Farm"))
      .ToArray();
    int? farmForwardProjection = legalActions.Any(action => action is PurchaseAction { UnitType: "Farm" })
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : null;
    int candidateLimit = Math.Max(1, Math.Min(settings.CandidatesPerNode, settings.PromisingCandidatesPerNode));
    IReadOnlyList<ICpuGameAction> actionsToScore = SelectActionsForDetailedScoring(
      state, team, legalActions, piecesById, goals, relevantEnemies, farmForwardProjection, candidateLimit);
    // Build the economy/counter doctrine once for the whole position. Every purchase then uses
    // the same reading of the enemy army, counter reserve, combo partners, and home safety.
    CpuArmyPlanner? armyPlan = actionsToScore.Any(action => action is PurchaseAction)
      ? CpuArmyPlanner.Create(state, team)
      : null;
    bool hasImmediateAttack = legalActions.OfType<AttackAction>().Any();
    bool hasMoveAvailable = legalActions.OfType<MoveAction>().Any();

    ScoredAction[] ranked = actionsToScore
      .Select(action => Score(state, team, action, piecesById, goals, relevantEnemies, farmForwardProjection,
        armyPlan, personality ?? CpuPersonality.Balanced, hasImmediateAttack, hasMoveAvailable))
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Kind)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .ToArray();
    return KeepTacticalDiversity(KeepStrategicallyPromisingActions(ranked, candidateLimit), candidateLimit);
  }

  /// <summary>
  /// Performs a cheap, rule-aware shortlist before the expensive simulated attack/move scoring.
  /// Purchase generation can yield hundreds of legal squares for the same role; evaluating each
  /// one would consume most of a short campaign turn before the beam begins. This stage keeps a
  /// few distinct destinations per unit/role, while the detailed scorer makes the final choice.
  /// </summary>
  private static IReadOnlyList<ICpuGameAction> SelectActionsForDetailedScoring(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> legalActions,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    IReadOnlyList<NetworkPiece> relevantEnemies,
    int? farmForwardProjection,
    int candidateLimit
  )
  {
    int scoringCapacity = Math.Clamp(candidateLimit * 4, 24, 64);
    if (legalActions.Count <= scoringCapacity)
    {
      return legalActions;
    }

    List<ICpuGameAction> selected = [];
    HashSet<string> seen = new(StringComparer.Ordinal);
    void Add(ICpuGameAction action)
    {
      if (seen.Add(action.Describe())) selected.Add(action);
    }

    // A royal-targeting attack may be a forced win; preserve it before quota sampling.
    foreach (AttackAction attack in legalActions.OfType<AttackAction>().Where(attack => attack.TargetPieceId is not null &&
      piecesById.TryGetValue(attack.TargetPieceId, out NetworkPiece? target) && IsRoyalPiece(target))) Add(attack);

    int attackQuota = Math.Max(6, scoringCapacity / 3);
    foreach (AttackAction attack in legalActions.OfType<AttackAction>()
      .OrderByDescending(attack => GetQuickAttackScore(attack, piecesById))
      .ThenBy(attack => attack.Describe(), StringComparer.Ordinal)
      .Take(attackQuota)) Add(attack);

    int moveQuota = Math.Max(6, scoringCapacity / 3);
    foreach (MoveAction move in legalActions.OfType<MoveAction>()
      .GroupBy(move => move.PieceId, StringComparer.Ordinal)
      .SelectMany(group => group
        .OrderByDescending(move => GetQuickMoveScore(state, move, piecesById, goals, relevantEnemies))
        .ThenBy(move => move.Describe(), StringComparer.Ordinal)
        .Take(2))
      .OrderByDescending(move => GetQuickMoveScore(state, move, piecesById, goals, relevantEnemies))
      .ThenBy(move => move.Describe(), StringComparer.Ordinal)
      .Take(moveQuota)) Add(move);

    int purchaseQuota = Math.Max(8, scoringCapacity / 3);
    foreach (PurchaseAction purchase in legalActions.OfType<PurchaseAction>()
      .GroupBy(purchase => purchase.UnitType, StringComparer.Ordinal)
      .SelectMany(group => group
        .OrderByDescending(purchase => GetQuickPurchasePlacementScore(state, purchase, relevantEnemies, farmForwardProjection))
        .ThenBy(purchase => purchase.Describe(), StringComparer.Ordinal)
        .Take(2))
      .OrderByDescending(purchase => GetQuickPurchasePlacementScore(state, purchase, relevantEnemies, farmForwardProjection))
      .ThenBy(purchase => purchase.Describe(), StringComparer.Ordinal)
      .Take(purchaseQuota)) Add(purchase);

    int abilityQuota = Math.Max(3, scoringCapacity / 8);
    foreach (UseAbilityAction ability in legalActions.OfType<UseAbilityAction>()
      .Where(ability => ability.Ability == "PickUpTreasure")
      .Concat(legalActions.OfType<UseAbilityAction>()
        .GroupBy(ability => (ability.ActorId, ability.Ability))
        .Select(group => group.OrderByDescending(ability => GetQuickAbilityScore(ability, piecesById)).First()))
      .OrderByDescending(ability => GetQuickAbilityScore(ability, piecesById))
      .ThenBy(ability => ability.Describe(), StringComparer.Ordinal)
      .Take(abilityQuota)) Add(ability);

    // End Turn only matters if no productive legal action exists, so leave it as the final
    // fallback rather than spending detailed scoring on it every search node.
    if (selected.Count == 0)
    {
      Add(legalActions[0]);
    }
    return selected;
  }

  private static float GetQuickAttackScore(AttackAction action, IReadOnlyDictionary<string, NetworkPiece> piecesById)
  {
    if (action.TargetPieceId is null || !piecesById.TryGetValue(action.TargetPieceId, out NetworkPiece? target))
    {
      return 2f;
    }
    if (!UnitRules.TryGet(target.Type, out UnitRule rule)) return 2f;
    float score = rule.Cost + rule.Attack * 1.5f + rule.Health * 0.15f;
    if (rule.Category == RuleCategory.Royal) score += 500f;
    if (target.Type == "Farm") score += 28f;
    return score - target.Health * 0.1f;
  }

  private static bool IsRoyalPiece(NetworkPiece piece) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    rule.Category == RuleCategory.Royal;

  private static float GetQuickMoveScore(
    CpuGameState state,
    MoveAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    IReadOnlyList<NetworkPiece> relevantEnemies
  )
  {
    if (!piecesById.TryGetValue(action.PieceId, out NetworkPiece? piece)) return float.MinValue;
    int goalProgress = goals.Count == 0 ? 0 :
      goals.Min(goal => Distance((piece.X, piece.Y), goal)) - goals.Min(goal => Distance((action.DestinationX, action.DestinationY), goal));
    int enemyProgress = relevantEnemies.Count == 0 ? 0 :
      relevantEnemies.Min(enemy => Distance((piece.X, piece.Y), (enemy.X, enemy.Y))) -
      relevantEnemies.Min(enemy => Distance((action.DestinationX, action.DestinationY), (enemy.X, enemy.Y)));
    return goalProgress * 16f + enemyProgress * 3f +
      ScoreApproachToPriorityTargets(state, action.Team, piece, action) +
      ScoreCounterEngagement(state, piece, action);
  }

  private static float GetQuickPurchasePlacementScore(
    CpuGameState state,
    PurchaseAction action,
    IReadOnlyList<NetworkPiece> relevantEnemies,
    int? farmForwardProjection
  )
  {
    if (action.UnitType == "Farm")
    {
      return CpuPlacementHeuristics.GetFarmProtectionScore(state, action.Team, action.X, action.Y, farmForwardProjection ?? 0);
    }
    bool neutralMercenary = action.UnitType == "Mercenary" && state.Pieces.Any(piece => piece.Team == NetworkTeam.Neutral &&
      piece.Type == "Mercenary" && piece.X == action.X && piece.Y == action.Y);
    int nearestEnemy = relevantEnemies.Select(enemy => Distance((action.X, action.Y), (enemy.X, enemy.Y))).DefaultIfEmpty(14).Min();
    return (neutralMercenary ? 80f : 0f) + Math.Max(0, 14 - nearestEnemy);
  }

  private static float GetQuickAbilityScore(UseAbilityAction action, IReadOnlyDictionary<string, NetworkPiece> piecesById)
  {
    if (action.Ability == "PickUpTreasure") return 1_000f;
    if (action.TargetPieceId is not null && piecesById.TryGetValue(action.TargetPieceId, out NetworkPiece? target) &&
        UnitRules.TryGet(target.Type, out UnitRule rule))
    {
      return rule.Cost + rule.Attack + (rule.Category == RuleCategory.Royal ? 100f : 0f);
    }
    return action.Ability is "Barrier" or "Mine" ? 8f : 2f;
  }

  private ScoredAction Score(
    CpuGameState state,
    NetworkTeam team,
    ICpuGameAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    IReadOnlyList<NetworkPiece> relevantEnemies,
    int? farmForwardProjection,
    CpuArmyPlanner? armyPlan,
    CpuPersonality personality,
    bool hasImmediateAttack,
    bool hasMoveAvailable
  )
  {
    ScoredAction candidate = action switch
    {
      AttackAction attack => AddStrategyScore(ScoreAttack(state, attack, piecesById), state, attack),
      MoveAction move => AddStrategyScore(ScoreMove(state, team, move, piecesById, goals, relevantEnemies), state, move),
      PurchaseAction purchase => AddStrategyScore(ScorePurchase(state, purchase, farmForwardProjection, armyPlan, personality), state, purchase),
      UseAbilityAction ability => AddStrategyScore(ScoreAbility(state, ability, personality), state, ability),
      EndTurnAction => new ScoredAction(action, -25f, "Ends the remaining actions"),
      StopInitialBuyingAction => new ScoredAction(action, -10f, "Stops the opening buy phase"),
      _ => new ScoredAction(action, 0f, "Legal action")
    };

    candidate = ApplyActionUrgency(state, action, candidate, hasImmediateAttack, hasMoveAvailable);
    return ApplyActionSafety(state, team, action, candidate);
  }

  private static ScoredAction ApplyActionUrgency(
    CpuGameState state,
    ICpuGameAction action,
    ScoredAction candidate,
    bool hasImmediateAttack,
    bool hasMoveAvailable
  )
  {
    if (action is AttackAction)
    {
      return candidate with { Score = candidate.Score + 180f, Reason = $"{candidate.Reason}; takes an available attack" };
    }

    float penalty = 0f;
    if (hasImmediateAttack)
    {
      penalty += action switch
      {
        PurchaseAction => 110f,
        UseAbilityAction => 80f,
        EndTurnAction => 220f,
        MoveAction move when state.Pieces.FirstOrDefault(piece => piece.Id == move.PieceId) is NetworkPiece mover &&
          state.Pieces.Any(target => target.Team != action.Team && target.Team != NetworkTeam.Neutral &&
            target.AttachedToId is null && CpuGameRules.CanDirectlyAttack(state, mover, target)) => 150f,
        _ => 0f
      };
    }

    if (hasMoveAvailable && action is PurchaseAction or UseAbilityAction)
    {
      penalty += 20f;
    }
    return penalty <= 0f
      ? candidate
      : candidate with { Score = candidate.Score - penalty, Reason = $"{candidate.Reason}; defers a more urgent board action" };
  }

  private static ScoredAction ApplyActionSafety(
    CpuGameState state,
    NetworkTeam team,
    ICpuGameAction action,
    ScoredAction candidate
  )
  {
    CpuGameState result;
    NetworkPiece? exposedPiece;
    switch (action)
    {
      case MoveAction move:
        result = CpuGameRules.ApplyLegal(state, move);
        exposedPiece = result.Pieces.FirstOrDefault(piece => piece.Id == move.PieceId);
        break;
      case PurchaseAction purchase:
        result = CpuGameRules.ApplyLegal(state, purchase);
        exposedPiece = result.Pieces.FirstOrDefault(piece => piece.Team == team && piece.Type == purchase.UnitType &&
          piece.X == purchase.X && piece.Y == purchase.Y);
        break;
      default:
        return candidate;
    }

    if (exposedPiece is null || exposedPiece.Team != team)
    {
      return candidate;
    }

    CpuTacticalSafety.Assessment assessment = CpuTacticalSafety.Assess(result, team, exposedPiece);
    float penalty = CpuTacticalSafety.GetActionRiskPenalty(result, exposedPiece, assessment);
    if (penalty <= 0f)
    {
      return candidate;
    }

    string danger = assessment.IsDirectlyLethal
      ? "avoids an immediate kill"
      : assessment.CanBeKilledAfterAnEnemyMove
        ? "avoids a move-and-attack kill"
        : "reduces enemy attack exposure";
    return candidate with { Score = candidate.Score - penalty, Reason = $"{candidate.Reason}; {danger}" };
  }

  private static ScoredAction AddStrategyScore(ScoredAction candidate, CpuGameState state, ICpuGameAction action)
  {
    float adjustment = CpuStrategicHeuristics.ScoreAction(state, action);
    if (Math.Abs(adjustment) < 0.01f)
    {
      return candidate;
    }
    string direction = adjustment > 0f ? "supports matchup plan" : "avoids anti-combo";
    return candidate with { Score = candidate.Score + adjustment, Reason = $"{candidate.Reason}; {direction}" };
  }

  private ScoredAction ScoreAttack(
    CpuGameState state,
    AttackAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById
  )
  {
    if (!piecesById.TryGetValue(action.AttackerId, out NetworkPiece? attacker))
    {
      return new ScoredAction(action, float.MinValue, "Missing attacker");
    }
    NetworkPiece? target = action.TargetPieceId is not null && piecesById.TryGetValue(action.TargetPieceId, out NetworkPiece? foundTarget)
      ? foundTarget
      : null;
    if (target is null)
    {
      return new ScoredAction(action, 12f, "Damages a barricade");
    }

    // Apply the real CPU rule action once for ordering. This captures guards, forest cover,
    // marks, Ballista piercing, and Bombard splash instead of assuming every attack is a simple
    // single-target hit.
    // Candidates originate in CpuActionGenerator, which has already established legality for
    // this snapshot. Revalidating attack geometry here duplicated one of the hottest paths in
    // a turn search.
    CpuGameState result = CpuGameRules.ApplyLegal(state, action);
    IReadOnlyDictionary<string, NetworkPiece> afterById = result.Pieces.ToDictionary(piece => piece.Id, StringComparer.Ordinal);
    float score = 0f;
    bool lethal = false;
    foreach (NetworkPiece before in state.Pieces.Where(piece => piece.Team != action.Team && piece.Team != NetworkTeam.Neutral))
    {
      int damage = afterById.TryGetValue(before.Id, out NetworkPiece? after)
        ? Math.Max(0, before.Health - after.Health)
        : before.Health;
      if (damage <= 0) continue;
      bool destroyed = !afterById.ContainsKey(before.Id);
      score += CombatTargetScoring.GetDamageReward(state, action.Team, before, damage);
      if (destroyed)
      {
        score += CombatTargetScoring.GetKillReward(state, action.Team, before);
      }
      lethal |= before.Id == target.Id && destroyed;
    }

    // Prefer coordinated damage that finishes a target over spreading chip damage. The
    // subsequent search still checks the full sequence, but this prevents the tactical line
    // from being pruned before its second attacker is considered.
    int supportingAttackers = state.Pieces.Count(piece => piece.Id != attacker.Id && piece.Team == action.Team &&
      CpuGameRules.CanDirectlyAttack(state, piece, target));
    score += supportingAttackers * 18f;
    score -= ScoreImmediateCounterattackRisk(result, action.Team, attacker.Id);
    return new ScoredAction(action, score, lethal ? "Immediate lethal attack" : "Damages an enemy unit");
  }

  private float ScoreImmediateCounterattackRisk(CpuGameState state, NetworkTeam team, string attackerId)
  {
    NetworkPiece? attacker = state.Pieces.FirstOrDefault(piece => piece.Id == attackerId);
    if (attacker is null) return 0f;

    float penalty = 0f;
    foreach (NetworkTeam enemy in TeamRules.GetActiveTeams(state.Configuration.PlayerCount).Where(candidate => candidate != team))
    {
      CpuPieceThreat? threat = _threatMapBuilder.Build(state, enemy).GetThreat(attackerId);
      if (threat is null) continue;
      penalty += CombatTargetScoring.GetDamageReward(state, enemy, attacker, threat.TotalExpectedDamage);
      if (threat.IsLethal) penalty += CombatTargetScoring.GetKillReward(state, enemy, attacker);
    }
    // A favourable exchange can still expose the attacker. Keep this lower than the immediate
    // capture value so the CPU attacks when the trade is sound rather than becoming passive.
    return penalty * 0.14f;
  }

  private static ScoredAction ScoreMove(
    CpuGameState state,
    NetworkTeam team,
    MoveAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    IReadOnlyList<NetworkPiece> relevantEnemies
  )
  {
    if (!piecesById.TryGetValue(action.PieceId, out NetworkPiece? piece))
    {
      return new ScoredAction(action, float.MinValue, "Missing unit");
    }

    int oldDistance = goals.Select(position => Distance((piece.X, piece.Y), position)).DefaultIfEmpty(0).Min();
    int newDistance = goals.Select(position => Distance((action.DestinationX, action.DestinationY), position)).DefaultIfEmpty(0).Min();
    float score = (oldDistance - newDistance) * 5f;
    // A target square can otherwise be pruned behind expensive purchases before beam search
    // gets the opportunity to recognise a campaign-completing move. Retain it decisively;
    // final state evaluation still decides whether the objective truly ends the mission.
    if (goals.Count > 0 && newDistance == 0)
    {
      score += 1_000f;
    }
    if (state.Configuration.GameMode == "Conquest" && MatchRules.IsConquestSquare(state.Board, (action.DestinationX, action.DestinationY)))
    {
      score += 25f;
    }
    if (state.Configuration.GameMode == "Dominion" && MatchRules.GetDominionControlPoints(state.Board).Contains((action.DestinationX, action.DestinationY)))
    {
      score += 25f;
    }

    UnitRule pieceRule = UnitRules.GetRequired(piece.Type);
    if (pieceRule.Category == RuleCategory.Royal)
    {
      (int x, int y) forward = TeamRules.GetForwardDirection(team);
      int forwardMovement = (action.DestinationX - piece.X) * forward.x + (action.DestinationY - piece.Y) * forward.y;
      // Escort is the explicit exception: that mode is won by taking the royal to the enemy
      // edge, so normal Regicide caution must not suppress the mission objective.
      if (CpuObjectiveRules.GetRoyalSafetyImportance(state) > 0f && state.Configuration.GameMode != "Escort" && forwardMovement > 0)
      {
        // Royal safety is evaluated from actual threats. Keep a modest caution here without
        // allowing a passive retreat to prune an allied move-then-attack sequence.
        score -= forwardMovement * 35f;
      }
      else if (CpuObjectiveRules.GetRoyalSafetyImportance(state) > 0f && state.Configuration.GameMode != "Escort" && forwardMovement < 0)
      {
        score += -forwardMovement * 10f;
      }

      return new ScoredAction(action, score, forwardMovement > 0
        ? "Avoids exposing the royal by moving it forward"
        : "Retreats or shelters the royal");
    }

    float firingSetup = ScoreMoveIntoAttackRange(state, action, piece);
    float royalApproach = ScoreApproachToEnemyRoyals(state, team, piece, action);
    float defensiveApproach = ScoreApproachToInvadersNearOwnRoyal(state, team, piece, action);
    float battlefieldApproach = ScoreApproachToRelevantEnemy(relevantEnemies, action, piece);
    float priorityTargetApproach = ScoreApproachToPriorityTargets(state, team, piece, action);
    float counterEngagement = ScoreCounterEngagement(state, piece, action);
    score += firingSetup + royalApproach + defensiveApproach + battlefieldApproach + priorityTargetApproach + counterEngagement;

    // A narrow search cannot afford to spend branches on a move that neither makes progress,
    // creates a firing line, protects home, nor improves contact with an enemy force. Formation
    // bonuses from CpuStrategicHeuristics can still rescue a genuine combo move afterwards.
    bool advancesGoal = goals.Count > 0 && newDistance < oldDistance;
    if (!advancesGoal && firingSetup <= 0f && royalApproach <= 0f && defensiveApproach <= 0f && battlefieldApproach <= 0f &&
        priorityTargetApproach <= 0f && counterEngagement <= 0f)
    {
      score -= 18f;
      return new ScoredAction(action, score, "Avoids an aimless reposition");
    }

    return new ScoredAction(action, score,
      firingSetup > 0f ? "Creates an immediate firing line" :
      defensiveApproach > 0f ? "Moves toward an invader threatening home assets" :
      counterEngagement > 0f ? "Pursues a favorable matchup or withdraws from a counter" :
      priorityTargetApproach > 0f ? "Pressures an opposing royal, farm, or unit" :
      advancesGoal || royalApproach > 0f ? "Advances toward a target or objective" :
      "Improves contact with the enemy force");
  }

  private static ScoredAction ScorePurchase(
    CpuGameState state,
    PurchaseAction action,
    int? farmForwardProjection,
    CpuArmyPlanner? armyPlan,
    CpuPersonality personality
  )
  {
    float affordability = state.Teams[action.Team].Money > 0 ? 2f : 0f;
    if (action.UnitType == "Farm")
    {
      float protection = CpuPlacementHeuristics.GetFarmProtectionScore(
        state, action.Team, action.X, action.Y, farmForwardProjection ?? 0);
      CpuRecruitmentAdvice advice = armyPlan?.EvaluatePurchase(action, UnitRules.GetRequired("Farm"), personality) ??
        new CpuRecruitmentAdvice(0f, "Places an income-producing farm", false, false);
      return new ScoredAction(action, affordability + protection + advice.Score,
        advice.Reason);
    }

    NetworkPiece? neutralMercenary = state.Pieces.FirstOrDefault(piece => piece.Type == "Mercenary" &&
      piece.Team == NetworkTeam.Neutral && piece.X == action.X && piece.Y == action.Y);
    if (action.UnitType == "Mercenary" && neutralMercenary is not null &&
        UnitRules.TryGet("Mercenary", out UnitRule mercenaryRule) && neutralMercenary.Health >= mercenaryRule.Health)
    {
      CpuRecruitmentAdvice advice = armyPlan?.EvaluatePurchase(action, mercenaryRule, personality) ??
        new CpuRecruitmentAdvice(0f, "Hires a full-health neutral Mercenary", false, false);
      // A full-health neutral hire is a useful tactical discount, but it must still fit the
      // counter plan and leave enough gold for a more important answer next turn.
      return new ScoredAction(action, 42f + advice.Score, advice.Reason);
    }

    float score = affordability;
    if (UnitRules.TryGet(action.UnitType, out UnitRule purchasedRule) && purchasedRule.Attack > 0)
    {
      NetworkPiece[] enemies = state.Pieces.Where(piece => piece.Team != action.Team && piece.Team != NetworkTeam.Neutral &&
        piece.AttachedToId is null).ToArray();
      int nearestEnemy = enemies.Select(piece => Distance((action.X, action.Y), (piece.X, piece.Y))).DefaultIfEmpty(12).Min();
      // Fill a battlefield role based on where the new unit can participate, not on its price.
      score += Math.Max(0, 10 - nearestEnemy) * 2f;
      if (enemies.Any(piece => piece.Type == "Farm" && Distance((action.X, action.Y), (piece.X, piece.Y)) <= 8))
      {
        score += 8f;
      }

    }
    int ownedCount = state.Pieces.Count(piece => piece.Team == action.Team && piece.AttachedToId is null &&
      piece.Type == action.UnitType);
    string reason = "Adds an affordable unit";
    switch (action.UnitType)
    {
      case "Peasant":
        // One cheap blocker can be useful. Repeated 5-health purchases consume turns and leave
        // the royal undefended, so strongly favour a proper fighting unit after the second.
        score -= Math.Max(0, ownedCount - 1) * 28f;
        if (ownedCount >= 2) reason = "Avoids stockpiling low-impact peasants";
        break;
      case "Ballista":
        score -= ownedCount * 60f;
        if (!HasImmediateBattlefieldRole(state, action, 7))
        {
          score -= 35f;
          reason = "Defers an unsupported Ballista";
        }
        else
        {
          reason = "Adds a Ballista with a reachable firing role";
        }
        break;
      case "Mercenary":
        // Mercenaries are excellent tactical hires but their payroll means an unattended stack
        // is a liability. Only the first nearby hire is attractive by default.
        score -= ownedCount * 45f;
        if (!HasImmediateBattlefieldRole(state, action, 6))
        {
          score -= 30f;
          reason = "Defers a Mercenary with no nearby target";
        }
        else
        {
          reason = "Hires a nearby Mercenary for an immediate fight";
        }
        break;
    }

    if (UnitRules.TryGet(action.UnitType, out UnitRule rule) && armyPlan is not null)
    {
      CpuRecruitmentAdvice advice = armyPlan.EvaluatePurchase(action, rule, personality);
      score += advice.Score;
      reason = advice.Reason;
    }
    return new ScoredAction(action, score, reason);
  }

  private static float ScoreMoveIntoAttackRange(CpuGameState state, MoveAction action, NetworkPiece piece)
  {
    if (piece.HasAttackedThisTurn || (Globals.ActionLimitsEnabled && state.ActionsRemaining <= 1))
    {
      return 0f;
    }

    // The selector only scores generated legal actions, so avoid paying the movement-path
    // validation cost a second time merely to inspect the resulting attack opportunities.
    CpuGameState movedState = CpuGameRules.ApplyLegal(state, action);
    NetworkPiece? movedPiece = movedState.Pieces.FirstOrDefault(candidate => candidate.Id == piece.Id);
    if (movedPiece is null)
    {
      return 0f;
    }

    return movedState.Pieces
      .Where(target => target.Team != action.Team && target.Team != NetworkTeam.Neutral && target.AttachedToId is null)
      .Where(target => CpuGameRules.CanDirectlyAttack(movedState, movedPiece, target))
      .Select(target => CombatTargetScoring.GetRangeSetupReward(movedState, action.Team, target))
      .DefaultIfEmpty(0f)
      .Max();
  }

  private static float ScoreApproachToEnemyRoyals(CpuGameState state, NetworkTeam team, NetworkPiece piece, MoveAction action)
  {
    if (!CpuObjectiveRules.ShouldPursueEnemyRoyal(state))
    {
      return 0f;
    }
    NetworkPiece[] enemyRoyals = state.Pieces.Where(target => target.Team != team && target.Team != NetworkTeam.Neutral &&
      target.AttachedToId is null && UnitRules.TryGet(target.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal).ToArray();
    if (enemyRoyals.Length == 0)
    {
      return 0f;
    }

    int oldDistance = enemyRoyals.Min(target => Distance((piece.X, piece.Y), (target.X, target.Y)));
    int newDistance = enemyRoyals.Min(target => Distance((action.DestinationX, action.DestinationY), (target.X, target.Y)));
    return (oldDistance - newDistance) * 1.5f;
  }

  private static float ScoreApproachToInvadersNearOwnRoyal(CpuGameState state, NetworkTeam team, NetworkPiece piece, MoveAction action)
  {
    if (CpuObjectiveRules.GetRoyalSafetyImportance(state) <= 0f)
    {
      return 0f;
    }
    NetworkPiece[] ownRoyals = state.Pieces.Where(target => target.Team == team && target.AttachedToId is null &&
      UnitRules.TryGet(target.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal).ToArray();
    NetworkPiece[] invaders = state.Pieces.Where(target => target.Team != team && target.Team != NetworkTeam.Neutral &&
      target.AttachedToId is null && UnitRules.TryGet(target.Type, out UnitRule rule) && rule.Attack > 0)
      .Where(target => ownRoyals.Any(royal => Distance((target.X, target.Y), (royal.X, royal.Y)) <= 7)).ToArray();
    if (invaders.Length == 0)
    {
      return 0f;
    }

    int oldDistance = invaders.Min(target => Distance((piece.X, piece.Y), (target.X, target.Y)));
    int newDistance = invaders.Min(target => Distance((action.DestinationX, action.DestinationY), (target.X, target.Y)));
    return (oldDistance - newDistance) * 3f;
  }

  private static float ScoreApproachToRelevantEnemy(
    IReadOnlyList<NetworkPiece> relevantEnemies,
    MoveAction action,
    NetworkPiece piece
  )
  {
    // This is deliberately broader than royal pursuit. Campaign battles often have to clear a
    // defender, artillery piece, or farm before the royal is reachable; a short horizon still
    // needs a reason to move a suitable unit toward that useful fight.
    if (relevantEnemies.Count == 0)
    {
      return 0f;
    }

    int oldDistance = relevantEnemies.Min(target => Distance((piece.X, piece.Y), (target.X, target.Y)));
    int newDistance = relevantEnemies.Min(target => Distance((action.DestinationX, action.DestinationY), (target.X, target.Y)));
    return Math.Max(0, oldDistance - newDistance) * 1.5f;
  }

  private static float ScoreApproachToPriorityTargets(
    CpuGameState state,
    NetworkTeam team,
    NetworkPiece piece,
    MoveAction action
  )
  {
    float best = 0f;
    foreach (NetworkPiece enemy in state.Pieces.Where(candidate => candidate.Team != team &&
      candidate.Team != NetworkTeam.Neutral && candidate.AttachedToId is null && UnitRules.TryGet(candidate.Type, out _)))
    {
      int distanceChange = Distance((piece.X, piece.Y), (enemy.X, enemy.Y)) -
        Distance((action.DestinationX, action.DestinationY), (enemy.X, enemy.Y));
      if (distanceChange <= 0)
      {
        continue;
      }

      UnitRule rule = UnitRules.GetRequired(enemy.Type);
      float priority = rule.Category == RuleCategory.Royal
        ? CpuObjectiveRules.ShouldPursueEnemyRoyal(state) ? 22f : 11f
        : rule.Type == "Farm" ? 14f : 3f + rule.Cost * 0.08f + rule.Attack * 0.1f;
      best = Math.Max(best, distanceChange * priority);
    }
    return best;
  }

  private static float ScoreCounterEngagement(CpuGameState state, NetworkPiece piece, MoveAction action)
  {
    float score = 0f;
    foreach (NetworkPiece enemy in state.Pieces.Where(candidate => candidate.Team != piece.Team &&
      candidate.Team != NetworkTeam.Neutral && candidate.AttachedToId is null))
    {
      int distanceChange = Distance((piece.X, piece.Y), (enemy.X, enemy.Y)) -
        Distance((action.DestinationX, action.DestinationY), (enemy.X, enemy.Y));
      float matchup = CpuStrategicHeuristics.GetMatchupScore(state, piece, enemy);
      float enemyMatchup = CpuStrategicHeuristics.GetMatchupScore(state, enemy, piece);
      if (matchup > 0.2f)
      {
        score += distanceChange * (5f + matchup * 5f);
      }
      if (enemyMatchup > 0.6f)
      {
        // A negative distance change means the unit is withdrawing, which is preferred when it
        // is walking toward a unit listed as a counter in the matchup reference.
        score -= distanceChange * (4f + enemyMatchup * 4f);
      }
    }
    return score;
  }

  private static bool HasImmediateBattlefieldRole(CpuGameState state, PurchaseAction action, int maximumDistance) => state.Pieces
    .Where(piece => piece.Team != action.Team && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null)
    .Any(piece => Distance((action.X, action.Y), (piece.X, piece.Y)) <= maximumDistance);

  private static ScoredAction ScoreAbility(CpuGameState state, UseAbilityAction action, CpuPersonality personality)
  {
    float score = action.Ability switch
    {
      "PickUpTreasure" => 150f,
      "Mark" => 25f,
      "Mine" => 15f,
      "Barrier" => 10f,
      "Road" => 6f,
      "Attach" => 8f,
      "Fire" => -8f,
      _ => 4f
    };
    if ((action.Ability is "Barrier" or "Mine") && CpuPlacementHeuristics.ProtectsFriendlyFarm(state, action.Team, (action.TargetX, action.TargetY)))
    {
      score += action.Ability == "Barrier" ? 36f : 24f;
    }
    return new ScoredAction(action, score * Math.Max(0f, personality.AbilityUsage), $"Uses {action.Ability}");
  }

  private static IEnumerable<(int x, int y)> GetGoalPositions(CpuGameState state, NetworkTeam team)
  {
    HashSet<(int x, int y)> positions = [];
    if (state.Configuration.GameMode == "Conquest")
    {
      positions.UnionWith(state.Board.Cells.Where(position => MatchRules.IsConquestSquare(state.Board, position)));
    }
    else if (state.Configuration.GameMode == "Dominion")
    {
      positions.UnionWith(MatchRules.GetDominionControlPoints(state.Board));
    }
    else if (state.Configuration.GameMode == "Plunder" && state.TreasurePosition is (int x, int y) treasure)
    {
      positions.Add(treasure);
    }

    // Campaign intents carry the mission's target locations. Include them while candidates are
    // pruned so a narrow beam still retains a move toward an escort, capture, defence, or block
    // objective instead of relying on broad tactical evaluation to rediscover it later.
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
        if (intent.TargetPieceId is not null && state.Pieces.FirstOrDefault(piece => piece.Id == intent.TargetPieceId) is NetworkPiece targetPiece)
        {
          positions.Add((targetPiece.X, targetPiece.Y));
        }
        if (intent.Type == CpuIntentType.ProtectRoyal && intent.PieceId is not null &&
            state.Pieces.FirstOrDefault(piece => piece.Id == intent.PieceId) is NetworkPiece protectedPiece)
        {
          // The royal-protection intent identifies the friendly asset in PieceId. Keeping its
          // square in the candidate set lets defensive setup moves survive early beam pruning.
          positions.Add((protectedPiece.X, protectedPiece.Y));
        }
        if (intent.Type == CpuIntentType.Escape && intent.TargetPosition is null)
        {
          positions.UnionWith(state.Board.Cells.Where(position => MatchRules.IsOnEnemyBackEdge(state.Board, team, position)));
        }
      }
    }

    if (positions.Count == 0 && CpuObjectiveRules.ShouldPursueEnemyRoyal(state))
    {
      positions.UnionWith(state.Pieces.Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
        .Select(piece => (piece.X, piece.Y)));
    }

    return positions.OrderBy(position => position.y).ThenBy(position => position.x);
  }

  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);

  /// <summary>
  /// Converts a wide legal-action list into a deliberately small set of strategically different
  /// continuations. The beam therefore has room to search replies/deeper actions even on boards
  /// with dozens of equivalent placements. Tactical families are retained before score-only
  /// filling so one arbitrary cannon square cannot crowd out a counter purchase or useful move.
  /// </summary>
  private static IReadOnlyList<ScoredAction> KeepStrategicallyPromisingActions(
    IReadOnlyList<ScoredAction> ranked,
    int limit
  )
  {
    int count = Math.Max(1, limit);
    if (ranked.Count <= count || count == 1)
    {
      return ranked.Take(count).ToArray();
    }

    List<ScoredAction> selected = [];
    HashSet<string> seen = new(StringComparer.Ordinal);
    void Add(ScoredAction? candidate)
    {
      if (candidate is not null && selected.Count < count && seen.Add(candidate.Action.Describe()))
      {
        selected.Add(candidate);
      }
    }

    // Mission-completing moves and forced attacks are never pruned merely because buying a
    // counter has a large heuristic score. The first instance of each core family also preserves
    // the tactical diversity required by a very small beam.
    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Score >= 900f).Take(2)) Add(candidate);
    Add(ranked.FirstOrDefault(candidate => candidate.Action is AttackAction));
    Add(ranked.FirstOrDefault(candidate => candidate.Action is MoveAction && IsPlausibleMove(candidate)));
    Add(ranked.FirstOrDefault(candidate => candidate.Action is PurchaseAction && IsPlausiblePurchase(candidate)));
    Add(ranked.FirstOrDefault(candidate => candidate.Action is UseAbilityAction && IsPlausibleAbility(candidate)));

    int attackQuota = Math.Max(3, count / 2);
    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Action is AttackAction).Take(attackQuota)) Add(candidate);

    // One movement plan per piece is normally enough at this horizon. Different destinations
    // from the same piece tend to be symmetric unless they create a firing line, which is already
    // represented by the score ordering above.
    int moveQuota = Math.Max(2, count / 3);
    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Action is MoveAction && IsPlausibleMove(candidate))
      .GroupBy(candidate => ((MoveAction)candidate.Action).PieceId, StringComparer.Ordinal)
      .Select(group => group.First())
      .Take(moveQuota)) Add(candidate);

    // Purchase placement is especially combinatorial. Retain the best square for each unit
    // role, rather than filling the beam with the same unit placed one tile apart. A farm is
    // treated as its own role, so a safe economic investment can compete with a counter unit.
    int purchaseQuota = Math.Max(3, count / 3);
    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Action is PurchaseAction && IsPlausiblePurchase(candidate))
      .GroupBy(candidate => ((PurchaseAction)candidate.Action).UnitType, StringComparer.Ordinal)
      .Select(group => group.First())
      .Take(purchaseQuota)) Add(candidate);

    foreach (ScoredAction candidate in ranked.Where(candidate => candidate.Action is UseAbilityAction && IsPlausibleAbility(candidate)).Take(2)) Add(candidate);
    foreach (ScoredAction candidate in ranked.Where(IsPlausibleAction)) Add(candidate);

    // A damaged/stalemated position may have no action meeting the normal usefulness bars. Keep
    // the highest-ranked legal action as a safe fallback rather than creating an empty branch.
    if (selected.Count == 0)
    {
      Add(ranked[0]);
    }
    return selected
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Kind)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .ToArray();
  }

  private static bool IsPlausibleAction(ScoredAction candidate) => candidate.Action switch
  {
    AttackAction => true,
    MoveAction => IsPlausibleMove(candidate),
    PurchaseAction => IsPlausiblePurchase(candidate),
    UseAbilityAction => IsPlausibleAbility(candidate),
    _ => false
  };

  private static bool IsPlausibleMove(ScoredAction candidate) => candidate.Score >= -20f;

  private static bool IsPlausiblePurchase(ScoredAction candidate) => candidate.Score >= -30f ||
    candidate.Reason.Contains("needed counter", StringComparison.Ordinal);

  private static bool IsPlausibleAbility(ScoredAction candidate) => candidate.Action is UseAbilityAction { Ability: "PickUpTreasure" } ||
    candidate.Score >= 8f;

  /// <summary>
  /// Keep one high-ranked action from each tactical family before filling the beam. This stops a
  /// narrow search from containing only purchases, only quiet moves, or only attacks at one
  /// target when a different family could be the winning continuation.
  /// </summary>
  private static IReadOnlyList<ScoredAction> KeepTacticalDiversity(IReadOnlyList<ScoredAction> ranked, int limit)
  {
    int count = Math.Max(1, limit);
    if (ranked.Count <= count) return ranked;

    List<ScoredAction> selected = [];
    HashSet<string> seen = new(StringComparer.Ordinal);
    foreach (Func<ICpuGameAction, bool> family in new Func<ICpuGameAction, bool>[]
    {
      action => action is AttackAction,
      action => action is MoveAction,
      action => action is UseAbilityAction,
      action => action is PurchaseAction
    })
    {
      ScoredAction? representative = ranked.FirstOrDefault(candidate => family(candidate.Action));
      if (representative is not null && selected.Count < count && seen.Add(representative.Action.Describe()))
      {
        selected.Add(representative);
      }
    }
    foreach (ScoredAction candidate in ranked)
    {
      if (selected.Count >= count) break;
      if (seen.Add(candidate.Action.Describe())) selected.Add(candidate);
    }
    return selected
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Kind)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .ToArray();
  }
}
