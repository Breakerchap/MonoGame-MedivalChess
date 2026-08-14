using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.GameBoard;

internal static class Actions
{
  internal static List<(int x, int y)> ValidMovementStepDirections(Piece piece) =>
    ShapeGeometryRules.GetStepDirections(
      UnitRules.ToRuleShape(piece.Definition.Movement.shape),
      piece.Team.ToNetworkTeam()
    ).ToList();

  internal static bool IsValidMovementDestination(Piece piece, (int x, int y) destination) =>
    ValidMovementDestinations(piece).Contains(destination);

  internal static List<(int x, int y)> ValidMovementDestinations(Piece piece)
  {
    return ValidActionSquares(piece, true)
      .Where(offset => offset != (0, 0))
      .Select(offset => (piece.Position.x + offset.x, piece.Position.y + offset.y))
      .ToList();
  }

  internal static bool CanAttackSquare(Piece piece, (int x, int y) targetPosition)
  {
    if (piece.Definition.AttackShape.shape is Shape.MoveOnEnemy or Shape.None)
    {
      return false;
    }

    Piece target = piece.OwnerSetup?.GetPieceAt(targetPosition);
    if (target is not null && !AbilityRules.CanDamageTarget(
      UnitRules.FromPieceDefinition(piece.Definition),
      UnitRules.FromPieceDefinition(target.Definition)))
    {
      return false;
    }

    foreach ((int x, int y) origin in piece.OccupiedSquares())
    {
      if (UnitRules.CanAttackOffset(
        UnitRules.ToRuleShape(piece.Definition.AttackShape.shape),
        piece.Definition.MinimumAttackRange,
        piece.Definition.AttackShape.range,
        piece.Team.ToNetworkTeam(),
        targetPosition.x - origin.x,
        targetPosition.y - origin.y
      ))
      {
        return true;
      }
    }

    return false;
  }

  internal static List<(int x, int y)> ValidActionSquares(Piece piece, bool isMoving)
  {
    (int range, Shape shape) action = isMoving ? piece.Definition.Movement : piece.Definition.AttackShape;
    int minimumRange = isMoving ? piece.Definition.Movement.Minimum : piece.Definition.MinimumAttackRange;
    return ShapeGeometryRules.GetOffsets(
      UnitRules.ToRuleShape(action.shape),
      minimumRange,
      action.range,
      piece.Team.ToNetworkTeam()
    );
  }

  internal static void Attack(Piece attackingPiece, Piece attackedPiece)
  {
    if (!AbilityRules.CanDamageTarget(
      UnitRules.FromPieceDefinition(attackingPiece.Definition),
      UnitRules.FromPieceDefinition(attackedPiece.Definition)))
    {
      return;
    }

    attackedPiece.CurrentHealth -= attackingPiece.Definition.Attack;
  }

  internal static bool HandlePieceDeath(
    Piece piece,
    Team attackingTeam,
    Team defeatedTeam,
    float killerRefundMultiplier,
    float defeatedTeamRefundMultiplier,
    int? unitCost = null
  )
  {
    if (piece.CurrentHealth > 0) { return false; }

    int refundCost = unitCost ?? piece.Definition.Cost;
    int killerRefund = CombatRules.RoundCurrencyToNearestFive(refundCost * killerRefundMultiplier);
    int defeatedRefund = CombatRules.RoundCurrencyToNearestFive(refundCost * defeatedTeamRefundMultiplier);
    attackingTeam.Money = (int)Math.Clamp((long)attackingTeam.Money + killerRefund, int.MinValue, int.MaxValue);
    defeatedTeam.Money = (int)Math.Clamp((long)defeatedTeam.Money + defeatedRefund, int.MinValue, int.MaxValue);
    return true;
  }
}
