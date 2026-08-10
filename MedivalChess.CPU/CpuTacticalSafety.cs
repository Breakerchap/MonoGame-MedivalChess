using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// Looks one opposing turn ahead for the safety decisions that must survive candidate pruning.
/// It uses the shared movement and attack rules, so terrain, line of sight, attachments, and
/// piece-specific ranges are accounted for before a risky move or purchase reaches the search.
/// </summary>
public static class CpuTacticalSafety
{
  public readonly record struct Assessment(
    int DirectDamage,
    int StrongestMoveAttackDamage,
    int MoveAttackers,
    bool IsDirectlyLethal,
    bool CanBeKilledAfterAnEnemyMove
  );

  public static Assessment Assess(CpuGameState state, NetworkTeam defendingTeam, NetworkPiece target)
  {
    if (target.Team != defendingTeam || target.AttachedToId is not null ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return default;
    }

    int directDamage = 0;
    int strongestMoveAttack = 0;
    int moveAttackers = 0;
    foreach (NetworkTeam enemyTeam in TeamRules.GetActiveTeams(state.Configuration.PlayerCount)
      .Where(team => team != defendingTeam))
    {
      CpuGameState enemyTurn = CreateReadyEnemyTurn(state, enemyTeam);
      NetworkPiece? targetInEnemyTurn = enemyTurn.Pieces.FirstOrDefault(piece => piece.Id == target.Id);
      if (targetInEnemyTurn is null)
      {
        continue;
      }

      foreach (NetworkPiece attacker in enemyTurn.Pieces.Where(piece =>
        piece.Team == enemyTeam && piece.AttachedToId is null &&
        UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Attack > 0))
      {
        if (CpuGameRules.CanDirectlyAttack(enemyTurn, attacker, targetInEnemyTurn))
        {
          directDamage += CpuGameRules.EstimateAttackDamage(enemyTurn, attacker, targetInEnemyTurn);
          continue;
        }

        int bestDamageAfterMove = GetBestMoveAttackDamage(enemyTurn, attacker, targetInEnemyTurn, targetRule);
        if (bestDamageAfterMove > 0)
        {
          strongestMoveAttack = Math.Max(strongestMoveAttack, bestDamageAfterMove);
          moveAttackers++;
        }
      }
    }

    return new Assessment(
      directDamage,
      strongestMoveAttack,
      moveAttackers,
      directDamage >= target.Health,
      strongestMoveAttack >= target.Health
    );
  }

  /// <summary>Returns a deliberately steep penalty for a disposable action that leaves its unit en prise.</summary>
  public static float GetActionRiskPenalty(CpuGameState state, NetworkPiece target, Assessment assessment)
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule rule))
    {
      return 0f;
    }

    float value = Math.Max(18f, MaterialEvaluation.GetUnitValue(target.Type, state));
    float directFraction = Math.Clamp(assessment.DirectDamage / (float)Math.Max(1, target.Health), 0f, 2f);
    float moveFraction = Math.Clamp(assessment.StrongestMoveAttackDamage / (float)Math.Max(1, target.Health), 0f, 1f);
    float penalty = directFraction * (value * 2.5f + 32f) +
      moveFraction * (value * 1.8f + 18f) + assessment.MoveAttackers * 8f;

    if (assessment.IsDirectlyLethal)
    {
      penalty += 1_000f + value * 12f;
    }
    if (assessment.CanBeKilledAfterAnEnemyMove)
    {
      // A move-and-attack kill is less immediate than a direct kill, but it should still outweigh
      // ordinary positional gains. Search may override this with a forcing win or winning trade.
      penalty += 260f + value * 5f;
    }
    if (rule.Category == RuleCategory.Royal)
    {
      penalty *= CpuObjectiveRules.IsRoyalEliminationObjective(state) ? 4f : 1.5f;
    }
    return penalty;
  }

  private static int GetBestMoveAttackDamage(
    CpuGameState enemyTurn,
    NetworkPiece attacker,
    NetworkPiece target,
    UnitRule targetRule
  )
  {
    int bestDamage = 0;
    if (!UnitRules.TryGet(attacker.Type, out UnitRule attackerRule))
    {
      return bestDamage;
    }
    foreach ((int x, int y) destination in CpuGameRules.GetLegalMovementPaths(enemyTurn, attacker).Keys)
    {
      // Most legal destinations cannot possibly reach the target. This inexpensive guard keeps
      // the exact shared line-of-sight check focused on realistic move-and-attack candidates.
      int distance = AttackDistanceToFootprint(destination, target, targetRule, attackerRule.AttackPattern);
      if (distance < attackerRule.MinimumAttackRange || distance > attackerRule.AttackRange)
      {
        continue;
      }

      NetworkPiece movedAttacker = attacker with { X = destination.x, Y = destination.y, HasMovedThisTurn = true };
      if (CpuGameRules.CanDirectlyAttack(enemyTurn, movedAttacker, target))
      {
        bestDamage = Math.Max(bestDamage, CpuGameRules.EstimateAttackDamage(enemyTurn, movedAttacker, target));
      }
    }
    return bestDamage;
  }

  private static CpuGameState CreateReadyEnemyTurn(CpuGameState state, NetworkTeam enemyTeam) => new(
    state.Configuration,
    state.Pieces.Select(piece => piece.Team == enemyTeam
      ? piece with
      {
        HasMovedThisTurn = false,
        HasAttackedThisTurn = false,
        CavalierFollowUpMoveAvailable = false
      }
      : piece),
    state.Teams.Values,
    enemyTeam,
    state.TurnNumber,
    state.Terrain,
    state.Winner,
    state.InitialBuy,
    state.ConquestScore,
    state.ConquestScores,
    state.ModeScores,
    state.TreasurePosition,
    state.TreasureCarrierId,
    state.Roads,
    state.Barricades,
    state.Mines,
    state.RiverBridges,
    state.Scenario,
    state.RecentMoves,
    state.Board
  );

  private static int AttackDistanceToFootprint(
    (int x, int y) position,
    NetworkPiece target,
    UnitRule targetRule,
    RuleShape pattern
  )
  {
    int closestX = Math.Clamp(position.x, target.X, target.X + targetRule.Width - 1);
    int closestY = Math.Clamp(position.y, target.Y, target.Y + targetRule.Height - 1);
    int dx = Math.Abs(position.x - closestX);
    int dy = Math.Abs(position.y - closestY);
    return pattern == RuleShape.Straight ? dx + dy : Math.Max(dx, dy);
  }
}
