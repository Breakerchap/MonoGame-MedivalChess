using MedivalChess.GameBoard;
using MedivalChess.Player;

namespace MedivalChess;

internal static class UiText
{
  internal static string BuildPieceLabel(PieceDefinition definition)
  {
    return definition.Type switch
    {
      PieceType.Soldier => "So",
      PieceType.Defender => "Df",
      PieceType.Archer => "Ar",
      PieceType.Scout => "Sc",
      PieceType.Spearman => "Sp",
      PieceType.Peasant => "Pe",
      PieceType.Knight => "Kn",
      PieceType.Crossbowman => "Cb",
      PieceType.Cavalier => "Cv",
      PieceType.Chariot => "Ch",
      PieceType.Cannon => "Cn",
      PieceType.Spy => "Sy",
      PieceType.Catapult => "Ct",
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
    return $"ATTACK  {definition.Attack} damage | {FormatAction(definition.AttackShape)}";
  }

  internal static string GetTeamDisplayName(TeamName teamName)
  {
    return teamName == TeamName.Red ? "ORANGE" : teamName == TeamName.Blue ? "PURPLE" : "NEUTRAL";
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
