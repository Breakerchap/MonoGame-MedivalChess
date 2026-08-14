using System;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess;

internal static class UiText
{
  internal static string BuildPieceLabel(PieceDefinition definition)
  {
    if (!string.IsNullOrWhiteSpace(definition.Abbreviation))
    {
      return new string(definition.Abbreviation.Take(3).ToArray()).ToUpperInvariant();
    }
    if (!string.Equals(definition.Identifier, definition.Type.ToString(), StringComparison.Ordinal))
    {
      string letters = new(definition.DisplayName.Where(char.IsLetterOrDigit).Take(3).ToArray());
      return string.IsNullOrWhiteSpace(letters) ? "CU" : letters.ToUpperInvariant();
    }
    return definition.Type switch
    {
      PieceType.Soldier => "So",
      PieceType.Defender => "Df",
      PieceType.Archer => "Ar",
      PieceType.Peasant => "Pe",
      PieceType.Knight => "Kn",
      PieceType.Crossbowman => "Cb",
      PieceType.Cavalier => "Cv",
      PieceType.Chariot => "Ch",
      PieceType.Cannon => "Cn",
      PieceType.Spy => "Sy",
      PieceType.Catapult => "Ct",
      PieceType.Bombard => "Bd",
      PieceType.Ox => "Ox",
      PieceType.Engineer => "En",
      PieceType.Ballista => "Bl",
      PieceType.Elephant => "El",
      PieceType.Guard => "Gd",
      PieceType.Mercenary => "Mc",
      PieceType.Farm => "Fm",
      PieceType.King => "KI",
      PieceType.Princess => "PR",
      PieceType.Palace => "PA",
      PieceType.Baron => "BR",
      PieceType.Emissary => "EM",
      _ => "??"
    };
  }

  internal static string BuildAttackDetails(PieceDefinition definition)
  {
    return $"ATTACK  {definition.Attack} damage | {FormatAction(definition.AttackRange, definition.AttackPattern)}";
  }

  internal static string GetTeamDisplayName(TeamName teamName)
  {
    return teamName switch
    {
      TeamName.Red => "ORANGE",
      TeamName.Blue => "PURPLE",
      TeamName.Green => "GREEN",
      TeamName.Yellow => "GOLD",
      _ => "NEUTRAL"
    };
  }

  internal static string FormatAction((int range, Shape shape) action)
  {
    return $"{action.range} {GetShapeLabel(action.shape)}";
  }

  internal static string FormatAction(MovementDefinition movement)
  {
    string distance = movement.Minimum == movement.Maximum || movement.Minimum <= 1
      ? movement.Maximum.ToString()
      : $"{movement.Minimum}-{movement.Maximum}";
    return $"{distance} {GetShapeLabel(movement.Shape)}";
  }

  internal static string FormatAction(AttackRange range, Shape shape)
  {
    string distance = range.Minimum == range.Maximum
      ? range.Maximum.ToString()
      : $"{range.Minimum}-{range.Maximum}";
    return $"{distance} {GetShapeLabel(shape)}";
  }

  internal static string GetShapeLabel(Shape shape)
  {
    return shape switch
    {
      Shape.Straight => "Straight",
      Shape.Circle => "Circle",
      Shape.Line => "Line",
      Shape.Forward => "Forward",
      Shape.AbsoluteStraightOrDiagonal => "Line/Diag",
      Shape.ForwardOrForwardDiagonal => "Fwd/Diag",
      Shape.ForwardLine => "Forward Line",
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
