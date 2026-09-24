using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess;

internal static class UiText
{
  internal static string BuildPieceLabel(PieceDefinition definition)
  {
    string label;
    if (!string.IsNullOrWhiteSpace(definition.Abbreviation))
    {
      label = new string(definition.Abbreviation.Take(3).ToArray());
    }
    else if (!string.Equals(definition.Identifier, definition.Type.ToString(), StringComparison.Ordinal))
    {
      string letters = new(definition.DisplayName.Where(char.IsLetterOrDigit).Take(3).ToArray());
      label = string.IsNullOrWhiteSpace(letters) ? "CU" : letters;
    }
    else
    {
      label = definition.Type switch
      {
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
        PieceType.Palace => "PA",
        PieceType.Baron => "BR",
        _ => "??"
      };
    }

    if (definition.Category == PieceCategory.Royal)
    {
      return label.ToUpperInvariant();
    }

    return label.Length == 0
      ? label
      : char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
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
      Shape.Any => "Square",
      Shape.Straight => "Diamond",
      Shape.Circle => "Circle",
      Shape.Line => "Line",
      Shape.Diagonal => "Diagonal",
      Shape.LineOrDiagonal => "Line or Diagonal",
      Shape.Forward => "Forward",
      Shape.AbsoluteStraightOrDiagonal => "Absolute Line or Diagonal",
      Shape.ForwardOrForwardDiagonal => "Forward or Forward Diagonal",
      Shape.ForwardDiagonal => "Forward Diagonal",
      Shape.ForwardLine => "Forward Line",
      Shape.PierceStraight => "Piercing Line",
      Shape.ChessKnight => "Knight Jump",
      Shape.MoveOnEnemy => "Capture on Landing",
      Shape.None => "None",
      _ => shape.ToString()
    };
  }

  internal static bool TryParseShapeLabel(string text, out Shape shape)
  {
    foreach (Shape candidate in Enum.GetValues<Shape>())
    {
      if (string.Equals(GetShapeLabel(candidate), text, StringComparison.OrdinalIgnoreCase))
      {
        shape = candidate;
        return true;
      }
    }

    return Enum.TryParse(text, ignoreCase: true, out shape);
  }

  internal static string SanitiseForSpriteFont(string text, ISet<char> supportedCharacters)
  {
    ArgumentNullException.ThrowIfNull(supportedCharacters);
    if (string.IsNullOrEmpty(text))
    {
      return text;
    }

    StringBuilder result = new(text.Length);
    foreach (char character in text)
    {
      if (character == '\r')
      {
        continue;
      }
      if (character == '\n')
      {
        // MonoGame handles line breaks separately; they do not need a font glyph.
        result.Append(character);
        continue;
      }
      if (supportedCharacters.Contains(character))
      {
        result.Append(character);
        continue;
      }

      string replacement = character switch
      {
        '\t' => "    ",
        '\u00A0' => " ",
        '\u00D7' => "x",
        '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' => "-",
        '\u2018' or '\u2019' => "'",
        '\u201C' or '\u201D' => "\"",
        '\u2022' => "*",
        '\u2026' => "...",
        '\u2192' => "->",
        _ => "?"
      };

      AppendSupportedReplacement(result, replacement, supportedCharacters);
    }

    return result.ToString();
  }

  private static void AppendSupportedReplacement(
    StringBuilder result,
    string replacement,
    ISet<char> supportedCharacters)
  {
    foreach (char replacementCharacter in replacement)
    {
      if (supportedCharacters.Contains(replacementCharacter))
      {
        result.Append(replacementCharacter);
      }
      else if (supportedCharacters.Contains('?'))
      {
        result.Append('?');
      }
      else if (supportedCharacters.Contains(' '))
      {
        result.Append(' ');
      }
    }
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
