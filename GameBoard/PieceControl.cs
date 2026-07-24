using System;
using System.Collections.Generic;
using MedivalChess.Player;

namespace MedivalChess.GameBoard;

internal static class Actions
{
  internal static List<(int x, int y)> ValidMovementStepDirections(Piece piece)
  {
    return piece.Definition.Movement.shape switch
    {
      Shape.Straight => ShapeFuncs.StraightShape(1),
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
        piece.Position.x + offset.x * piece.Definition.Size.x,
        piece.Position.y + offset.y * piece.Definition.Size.y
      ));
    }

    return destinations;
  }

  internal static bool CanAttackSquare(Piece piece, (int x, int y) targetPosition)
  {
    if (piece.Definition.AttackShape.shape == Shape.MoveOnEnemy)
    {
      return false;
    }

    List<(int x, int y)> attackOffsets = ValidActionSquares(piece, false);

    foreach ((int x, int y) origin in piece.OccupiedSquares())
    {
      var offset = (x: targetPosition.x - origin.x, y: targetPosition.y - origin.y);
      if (attackOffsets.Contains(offset))
      {
        return true;
      }
    }

    return false;
  }

  internal static List<(int x, int y)> ValidActionSquares(Piece piece, bool isMoving)
  {
    var action = isMoving ? piece.Definition.Movement : piece.Definition.AttackShape;
    List<(int x, int y)> squares;

    switch (action.shape)
    {
      case Shape.Straight:
        squares = ShapeFuncs.StraightShape(action.range);
        break;

      case Shape.Any:
      case Shape.FourSquare:
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
        squares = ShapeFuncs.StraightShape(action.range);
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
      squares.RemoveAll(square => ShapeFuncs.Distance(square) < piece.Definition.MinimumAttackRange);
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
    float defeatedTeamRefundMultiplier
  )
  {
    if (piece.CurrentHealth > 0) { return false; }

    attackingTeam.Money += RoundToNearestFive(piece.Definition.Cost * killerRefundMultiplier);
    defeatedTeam.Money += RoundToNearestFive(piece.Definition.Cost * defeatedTeamRefundMultiplier);
    return true;
  }

  private static int RoundToNearestFive(float amount)
  {
    return (int)MathF.Round(amount / 5f, MidpointRounding.AwayFromZero) * 5;
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

  internal static List<(int x, int y)> StraightShape(int range)
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
    int direction = team == TeamName.Red ? -1 : 1;

    for (int distance = 1; distance <= range; distance++)
    {
      validSquares.Add((0, direction * distance));
      if (includeDiagonals)
      {
        validSquares.Add((-distance, direction * distance));
        validSquares.Add((distance, direction * distance));
      }
    }

    return validSquares;
  }

  internal static int Distance((int x, int y) offset)
  {
    return Math.Max(Math.Abs(offset.x), Math.Abs(offset.y));
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
