using System;
using System.Collections.Generic;
using MedivalChess.Player;

namespace MedivalChess.GameBoard;

internal static class Actions
{
  internal static List<(int x, int y)> ValidActionSquares(Piece piece, bool isMoving)
  {
    var action = isMoving ? piece.Definition.Movement : piece.Definition.AttackShape;

    switch (action.shape)
    {
      case Shape.Straight:
        return ShapeFuncs.StraightShape(action.range);

      case Shape.Any:
        return ShapeFuncs.AnyShape(action.range);

      case Shape.AbsoluteStraightOrDiagonal:
        return ShapeFuncs.AbsoluteStraightOrDiagonalShape(action.range);

      default:
        Console.WriteLine($"Shape: `{action.shape}` not added yet");
        return [];
    }
  }

  internal static void Attack(Piece attackingPiece, Piece attackedPiece)
  {
    attackedPiece.CurrentHealth -= attackingPiece.Definition.Attack;
  }

  internal static bool HandlePieceDeath(Piece piece, Team attackingTeam)
  {
    if (piece.CurrentHealth > 0) { return false; }

    attackingTeam.Money += piece.Definition.Cost / 2;
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
