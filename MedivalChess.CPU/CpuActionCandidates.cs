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
    int? farmForwardProjection = legalActions.Any(action => action is PurchaseAction { UnitType: "Farm" })
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : null;

    ScoredAction[] ranked = legalActions
      .Select(action => Score(state, team, action, piecesById, goals, farmForwardProjection,
        personality ?? CpuPersonality.Balanced))
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Kind)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .ToArray();
    return KeepTacticalDiversity(ranked, settings.CandidatesPerNode);
  }

  private ScoredAction Score(
    CpuGameState state,
    NetworkTeam team,
    ICpuGameAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    int? farmForwardProjection,
    CpuPersonality personality
  ) => action switch
  {
    AttackAction attack => ScoreAttack(state, attack, piecesById),
    MoveAction move => ScoreMove(state, team, move, piecesById, goals),
    PurchaseAction purchase => ScorePurchase(state, purchase, farmForwardProjection),
    UseAbilityAction ability => ScoreAbility(state, ability, personality),
    EndTurnAction => new ScoredAction(action, -25f, "Ends the remaining actions"),
    StopInitialBuyingAction => new ScoredAction(action, -10f, "Stops the opening buy phase"),
    _ => new ScoredAction(action, 0f, "Legal action")
  };

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
    CpuGameState result = action.Apply(state);
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
    IReadOnlyList<(int x, int y)> goals
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

    score += ScoreMoveIntoAttackRange(state, action, piece);
    score += ScoreApproachToEnemyRoyals(state, team, piece, action);
    score += ScoreApproachToInvadersNearOwnRoyal(state, team, piece, action);

    return new ScoredAction(action, score, score > 0 ? "Advances toward a target or firing position" : "Repositions a unit");
  }

  private static ScoredAction ScorePurchase(
    CpuGameState state,
    PurchaseAction action,
    int? farmForwardProjection
  )
  {
    float affordability = state.Teams[action.Team].Money > 0 ? 2f : 0f;
    if (action.UnitType == "Farm")
    {
      float protection = CpuPlacementHeuristics.GetFarmProtectionScore(
        state, action.Team, action.X, action.Y, farmForwardProjection ?? 0);
      return new ScoredAction(action, affordability + protection,
        protection > 0f ? "Places a farm in protected terrain" : "Places an income-producing farm");
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

    return new ScoredAction(action, score, reason);
  }

  private static float ScoreMoveIntoAttackRange(CpuGameState state, MoveAction action, NetworkPiece piece)
  {
    if (piece.HasAttackedThisTurn || (Globals.ActionLimitsEnabled && state.ActionsRemaining <= 1))
    {
      return 0f;
    }

    CpuGameState movedState = action.Apply(state);
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
