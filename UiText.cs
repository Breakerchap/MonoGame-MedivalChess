using MedivalChess.GameBoard;

namespace MedivalChess;

internal static class UiText
{
  internal static string BuildPieceLabel(PieceDefinition definition)
  {
    string typeName = definition.Type.ToString();
    return typeName.Length <= 2 ? typeName : typeName[..2];
  }

  internal static string BuildAttackDetails(PieceDefinition definition)
  {
    return $"ATTACK  {definition.Attack} damage | {FormatAction(definition.AttackShape)}";
  }

  internal static string FormatAction((int range, Shape shape) action)
  {
    return $"{action.range} {GetShapeLabel(action.shape)}";
  }

  internal static string GetShapeLabel(Shape shape)
  {
    return shape switch
    {
      Shape.Straight => "Line",
      Shape.Forward => "Forward",
      Shape.AbsoluteStraightOrDiagonal => "Line/Diag",
      Shape.ForwardOrForwardDiagonal => "Fwd/Diag",
      Shape.FourSquare => "4-square",
      Shape.PierceStraight => "Pierce",
      Shape.MoveOnEnemy => "Enemy",
      _ => shape.ToString()
    };
  }

  internal static bool IsSpriteFontSafe(string text)
  {
    foreach (char character in text)
    {
      if (character < ' ' || character > '~')
      {
        return false;
      }
    }

    return true;
  }
}
