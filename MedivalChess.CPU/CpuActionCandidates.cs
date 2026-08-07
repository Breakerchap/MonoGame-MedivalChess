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
    // a royal/objective scan and farm-territory scan for every legal movement or purchase.
    IReadOnlyDictionary<string, NetworkPiece> piecesById = state.Pieces.ToDictionary(piece => piece.Id, StringComparer.Ordinal);
    (int x, int y)[] goals = GetGoalPositions(state, team).ToArray();
    RoyalDefenceContext defence = GetRoyalDefenceContext(state, team);
    int? farmForwardProjection = legalActions.Any(action => action is PurchaseAction { UnitType: "Farm" })
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : null;

    return legalActions
      .Select(action => Score(state, team, action, piecesById, goals, farmForwardProjection, defence,
        personality ?? CpuPersonality.Balanced))
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Action.Kind)
      .ThenBy(candidate => candidate.Action.Describe(), StringComparer.Ordinal)
      .Take(Math.Max(1, settings.CandidatesPerNode))
      .ToArray();
  }

  private static ScoredAction Score(
    CpuGameState state,
    NetworkTeam team,
    ICpuGameAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    int? farmForwardProjection,
    RoyalDefenceContext defence,
    CpuPersonality personality
  ) => action switch
  {
    AttackAction attack => ScoreAttack(attack, piecesById),
    MoveAction move => ScoreMove(state, team, move, piecesById, goals, defence),
    PurchaseAction purchase => ScorePurchase(state, purchase, farmForwardProjection, defence),
    UseAbilityAction ability => ScoreAbility(state, ability, personality),
    EndTurnAction => new ScoredAction(action, -25f, "Ends the remaining actions"),
    StopInitialBuyingAction => new ScoredAction(action, -10f, "Stops the opening buy phase"),
    _ => new ScoredAction(action, 0f, "Legal action")
  };

  private static ScoredAction ScoreAttack(AttackAction action, IReadOnlyDictionary<string, NetworkPiece> piecesById)
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

    int damage = UnitRules.GetRequired(attacker.Type).Attack;
    float targetValue = MaterialEvaluation.GetUnitValue(target.Type);
    bool lethal = damage >= target.Health;
    // A confirmed kill removes an enemy action and should outrank a speculative purchase.
    float score = damage * 2f + targetValue * (lethal ? 1.35f : 0.22f) + (lethal ? 500f : 0f);
    if (UnitRules.TryGet(target.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
    {
      score += 500f;
    }
    return new ScoredAction(action, score, lethal ? "Immediate lethal attack" : "Damages an enemy unit");
  }

  private static ScoredAction ScoreMove(
    CpuGameState state,
    NetworkTeam team,
    MoveAction action,
    IReadOnlyDictionary<string, NetworkPiece> piecesById,
    IReadOnlyList<(int x, int y)> goals,
    RoyalDefenceContext defence
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
      if (state.Configuration.GameMode != "Escort" && forwardMovement > 0)
      {
        // A royal should never stroll towards the centre just because an enemy royal is there.
        // Search can still override this only through a decisive immediate win.
        score -= forwardMovement * 280f;
      }
      else if (state.Configuration.GameMode != "Escort" && forwardMovement < 0)
      {
        score += -forwardMovement * 75f;
      }

      if (defence.HasPressure)
      {
        int oldThreatDistance = defence.Invaders.Select(invader => Distance((piece.X, piece.Y), (invader.X, invader.Y)))
          .DefaultIfEmpty(0).Min();
        int newThreatDistance = defence.Invaders.Select(invader => Distance((action.DestinationX, action.DestinationY), (invader.X, invader.Y)))
          .DefaultIfEmpty(oldThreatDistance).Min();
        score += (newThreatDistance - oldThreatDistance) * 85f;
      }
      return new ScoredAction(action, score, forwardMovement > 0
        ? "Avoids exposing the royal by moving it forward"
        : "Retreats or shelters the royal");
    }

    if (defence.HasPressure)
    {
      int oldRoyalDistance = defence.Royals.Select(royal => Distance((piece.X, piece.Y), (royal.X, royal.Y)))
        .DefaultIfEmpty(0).Min();
      int newRoyalDistance = defence.Royals.Select(royal => Distance((action.DestinationX, action.DestinationY), (royal.X, royal.Y)))
        .DefaultIfEmpty(oldRoyalDistance).Min();
      score += (oldRoyalDistance - newRoyalDistance) * 32f;
      if (newRoyalDistance <= 3)
      {
        score += 45f;
      }
      if (CanThreatenAnyInvader(pieceRule, team, action.DestinationX, action.DestinationY, defence.Invaders))
      {
        score += 70f;
      }
      return new ScoredAction(action, score, "Moves a defender into the endangered royal's perimeter");
    }

    return new ScoredAction(action, score, score > 0 ? "Moves toward an objective" : "Repositions a unit");
  }

  private static ScoredAction ScorePurchase(
    CpuGameState state,
    PurchaseAction action,
    int? farmForwardProjection,
    RoyalDefenceContext defence
  )
  {
    float value = MaterialEvaluation.GetUnitValue(action.UnitType);
    float affordability = state.Teams[action.Team].Money > 0 ? 2f : 0f;
    if (action.UnitType == "Farm")
    {
      float protection = CpuPlacementHeuristics.GetFarmProtectionScore(
        state, action.Team, action.X, action.Y, farmForwardProjection ?? 0);
      return new ScoredAction(action, value * 0.12f + affordability + protection,
        protection > 0f ? "Places a farm in protected terrain" : "Places an income-producing farm");
    }

    float score = value * 0.04f + affordability;
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

    if (defence.HasPressure)
    {
      int royalDistance = defence.Royals.Select(royal => Distance((action.X, action.Y), (royal.X, royal.Y)))
        .DefaultIfEmpty(10).Min();
      bool defensiveUnit = action.UnitType is "Defender" or "Soldier" or "Knight" or "Guard" or "Archer" or "Crossbowman";
      if (defensiveUnit && royalDistance <= 4)
      {
        score += 55f - royalDistance * 8f;
        reason = "Places a defender near the threatened royal";
      }
      else if (!defensiveUnit)
      {
        score -= 16f;
      }
    }
    return new ScoredAction(action, score, reason);
  }

  private static RoyalDefenceContext GetRoyalDefenceContext(CpuGameState state, NetworkTeam team)
  {
    NetworkPiece[] royals = state.Pieces
      .Where(piece => piece.Team == team && piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
        rule.Category == RuleCategory.Royal)
      .ToArray();
    if (royals.Length == 0)
    {
      return new RoyalDefenceContext([], []);
    }

    NetworkPiece[] invaders = state.Pieces
      .Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Attack > 0)
      .Where(piece =>
      {
        UnitRule rule = UnitRules.GetRequired(piece.Type);
        int reachNextTurn = rule.MoveRange + Math.Max(1, rule.AttackRange) + 3;
        return royals.Any(royal => Distance((piece.X, piece.Y), (royal.X, royal.Y)) <= reachNextTurn);
      })
      .ToArray();
    return new RoyalDefenceContext(royals, invaders);
  }

  private static bool CanThreatenAnyInvader(
    UnitRule rule,
    NetworkTeam team,
    int x,
    int y,
    IEnumerable<NetworkPiece> invaders
  ) => invaders.Any(invader => UnitRules.TryGet(invader.Type, out UnitRule targetRule) &&
    UnitRules.CanAttack(rule, x, y, team, targetRule, invader.X, invader.Y));

  private static bool HasImmediateBattlefieldRole(CpuGameState state, PurchaseAction action, int maximumDistance) => state.Pieces
    .Where(piece => piece.Team != action.Team && piece.Team != NetworkTeam.Neutral && piece.AttachedToId is null)
    .Any(piece => Distance((action.X, action.Y), (piece.X, piece.Y)) <= maximumDistance);

  private sealed record RoyalDefenceContext(
    IReadOnlyList<NetworkPiece> Royals,
    IReadOnlyList<NetworkPiece> Invaders
  )
  {
    public bool HasPressure => Invaders.Count > 0;
  }

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

    if (positions.Count == 0)
    {
      positions.UnionWith(state.Pieces.Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
        .Select(piece => (piece.X, piece.Y)));
    }

    return positions.OrderBy(position => position.y).ThenBy(position => position.x);
  }

  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);
}
