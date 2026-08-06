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
    int? farmForwardProjection = legalActions.Any(action => action is PurchaseAction { UnitType: "Farm" })
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : null;

    return legalActions
      .Select(action => Score(state, team, action, piecesById, goals, farmForwardProjection, personality ?? CpuPersonality.Balanced))
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
    CpuPersonality personality
  ) => action switch
  {
    AttackAction attack => ScoreAttack(attack, piecesById),
    MoveAction move => ScoreMove(state, team, move, piecesById, goals),
    PurchaseAction purchase => ScorePurchase(state, purchase, farmForwardProjection),
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
    return new ScoredAction(action, score, score > 0 ? "Moves toward an objective" : "Repositions a unit");
  }

  private static ScoredAction ScorePurchase(CpuGameState state, PurchaseAction action, int? farmForwardProjection)
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
    return new ScoredAction(action, value * 0.12f + affordability, "Adds an affordable unit");
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
