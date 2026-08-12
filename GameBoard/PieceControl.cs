using System;
using System.Collections.Generic;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess.GameBoard;

internal static class Actions
{
  internal static List<(int x, int y)> ValidMovementStepDirections(Piece piece)
  {
    return piece.Definition.Movement.shape switch
    {
      Shape.Straight or Shape.Line => ShapeFuncs.OrthogonalStepDirections(),
      Shape.Any =>
      [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1)
      ],
      Shape.Forward => ShapeFuncs.ForwardShape(piece.Team, 1, false),
      Shape.ForwardOrForwardDiagonal => ShapeFuncs.ForwardShape(piece.Team, 1, true),
      Shape.AbsoluteStraightOrDiagonal => ShapeFuncs.AbsoluteStraightOrDiagonalShape(1),
      Shape.MoveOnEnemy =>
      [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1)
      ],
      _ => []
    };
  }

  internal static bool IsValidMovementDestination(Piece piece, (int x, int y) destination)
  {
    return ValidMovementDestinations(piece).Contains(destination);
  }

  internal static List<(int x, int y)> ValidMovementDestinations(Piece piece)
  {
    List<(int x, int y)> destinations = [];

    foreach ((int x, int y) offset in ValidActionSquares(piece, true))
    {
      if (offset == (0, 0))
      {
        continue;
      }

      destinations.Add((
        piece.Position.x + offset.x,
        piece.Position.y + offset.y
      ));
    }

    return destinations;
  }

  internal static bool CanAttackSquare(Piece piece, (int x, int y) targetPosition)
  {
    if (piece.Definition.AttackShape.shape is Shape.MoveOnEnemy or Shape.None)
    {
      return false;
    }

    foreach ((int x, int y) origin in piece.OccupiedSquares())
    {
      if (UnitRules.CanAttackOffset(
        ToRuleShape(piece.Definition.AttackShape.shape),
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

  private static RuleShape ToRuleShape(Shape shape) => shape switch
  {
    Shape.Any => RuleShape.Any,
    Shape.Straight => RuleShape.Straight,
    Shape.Line => RuleShape.Line,
    Shape.Forward => RuleShape.Forward,
    Shape.AbsoluteStraightOrDiagonal => RuleShape.AbsoluteStraightOrDiagonal,
    Shape.ForwardOrForwardDiagonal => RuleShape.ForwardOrForwardDiagonal,
    Shape.PierceStraight => RuleShape.PierceStraight,
    _ => RuleShape.None
  };

  internal static List<(int x, int y)> ValidActionSquares(Piece piece, bool isMoving)
  {
    var action = isMoving ? piece.Definition.Movement : piece.Definition.AttackShape;
    List<(int x, int y)> squares;

    switch (action.shape)
    {
      case Shape.Straight:
        squares = ShapeFuncs.StraightShape(action.range);
        break;

      case Shape.Line:
        squares = ShapeFuncs.LineShape(action.range);
        break;

      case Shape.Any:
        squares = ShapeFuncs.AnyShape(action.range);
        break;

      case Shape.Forward:
        squares = ShapeFuncs.ForwardShape(piece.Team, action.range, false);
        break;

      case Shape.ForwardOrForwardDiagonal:
        squares = ShapeFuncs.ForwardShape(piece.Team, action.range, true);
        break;

      case Shape.AbsoluteStraightOrDiagonal:
        squares = ShapeFuncs.AbsoluteStraightOrDiagonalShape(action.range);
        break;

      case Shape.PierceStraight:
        squares = ShapeFuncs.LineShape(action.range);
        break;

      case Shape.MoveOnEnemy:
        squares = ShapeFuncs.AnyShape(action.range);
        break;

      case Shape.None:
        return [];

      default:
        Console.WriteLine($"Shape: `{action.shape}` not added yet");
        return [];
    }

    if (!isMoving && piece.Definition.MinimumAttackRange > 0)
    {
      squares.RemoveAll(square => ShapeFuncs.Distance(action.shape, square) < piece.Definition.MinimumAttackRange);
    }

    return squares;
  }

  internal static void Attack(Piece attackingPiece, Piece attackedPiece)
  {
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

internal static class ShapeFuncs
{
  internal static List<(int x, int y)> AnyShape(int range)
  {
    List<(int x, int y)> validSquares = new();
    for (int x = -range; x <= range; x++)
    {
      for (int y = -range; y <= range; y++)
      {
        validSquares.Add((x, y));
      }
    }

    return validSquares;
  }

  internal static List<(int x, int y)> OrthogonalStepDirections()
  {
    return [(1, 0), (-1, 0), (0, 1), (0, -1)];
  }

  internal static List<(int x, int y)> StraightShape(int range)
  {
    List<(int x, int y)> validSquares = new();
    for (int x = -range; x <= range; x++)
    {
      for (int y = -range; y <= range; y++)
      {
        if (Math.Abs(x) + Math.Abs(y) <= range)
        {
          validSquares.Add((x, y));
        }
      }
    }

    return validSquares;
  }

  internal static List<(int x, int y)> LineShape(int range)
  {
    List<(int x, int y)> validSquares = new();

    for (int distance = 1; distance <= range; distance++)
    {
      validSquares.Add((distance, 0));
      validSquares.Add((-distance, 0));
      validSquares.Add((0, distance));
      validSquares.Add((0, -distance));
    }

    return validSquares;
  }

  internal static List<(int x, int y)> ForwardShape(TeamName team, int range, bool includeDiagonals)
  {
    List<(int x, int y)> validSquares = new();
    (int x, int y) direction = TeamRules.GetForwardDirection(team.ToNetworkTeam());

    for (int distance = 1; distance <= range; distance++)
    {
      validSquares.Add((direction.x * distance, direction.y * distance));
      if (includeDiagonals)
      {
        if (direction.x == 0)
        {
          validSquares.Add((-distance, direction.y * distance));
          validSquares.Add((distance, direction.y * distance));
        }
        else
        {
          validSquares.Add((direction.x * distance, -distance));
          validSquares.Add((direction.x * distance, distance));
        }
      }
    }

    return validSquares;
  }

  internal static int Distance(Shape shape, (int x, int y) offset)
  {
    return shape == Shape.Straight
      ? Math.Abs(offset.x) + Math.Abs(offset.y)
      : Math.Max(Math.Abs(offset.x), Math.Abs(offset.y));
  }

  internal static List<(int x, int y)> AbsoluteStraightOrDiagonalShape(int range)
  {
    List<(int x, int y)> validSquares = new();
    for (int i = 1; i <= range; i++)
    {
      validSquares.Add((i, 0));
      validSquares.Add((i, i));
      validSquares.Add((i, -i));
      validSquares.Add((-i, 0));
      validSquares.Add((-i, i));
      validSquares.Add((-i, -i));
      validSquares.Add((0, i));
      validSquares.Add((0, -i));
    }

    return validSquares;
  }
}
