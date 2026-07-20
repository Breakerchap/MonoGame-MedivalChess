using System;
using System.Collections.Generic;

namespace MedivalChess.GameBoard;

public static class Movement
{
  public static List<(int x, int y)> ValidMovementSquares(Piece piece)
  { 
    List<(int x, int y)> validSquares = new();
    switch (piece.Definition.Movement.shape)
    {
      case Shape.Straight:
        for (int x = -piece.Definition.Movement.range; x <= piece.Definition.Movement.range; x++)
        {
          for (int y = -piece.Definition.Movement.range; y <= piece.Definition.Movement.range; y++)
          {
            if (Math.Abs(x) + Math.Abs(y) <= piece.Definition.Movement.range)
            {
              validSquares.Add((x, y));
            }
          }
        }
        break;
      
      case Shape.Any:
        for (int x = -piece.Definition.Movement.range; x <= piece.Definition.Movement.range; x++)
        {
          for (int y = -piece.Definition.Movement.range; y <= piece.Definition.Movement.range; y++)
          {
            validSquares.Add((x, y));
          }
        }
        break;

      case Shape.AbsoluteStraightOrDiagonal:
        for (int i = 1; i <= piece.Definition.Movement.range; i++)
        {
          validSquares.Add(( i,  0));
          validSquares.Add(( i,  i));
          validSquares.Add(( i, -i));
          validSquares.Add((-i,  0));
          validSquares.Add((-i,  i));
          validSquares.Add((-i, -i));
          validSquares.Add(( 0,  i));
          validSquares.Add(( 0, -i));
        }
        break;
      
      default:
        Console.WriteLine($"Shape: `{piece.Definition.Movement.shape}` not added yet");
        break;
    }

    return validSquares;
  }
}