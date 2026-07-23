using MedivalChess.GameBoard;
using Xunit;

namespace MedivalChess.Tests;

public class UiTextTests
{
  [Fact]
  public void AttackDetails_UsesOnlyAsciiGlyphs()
  {
    string text = UiText.BuildAttackDetails(PieceDefinitions.King);

    Assert.Equal("ATTACK  15 damage | 1 Any", text);
    Assert.True(UiText.IsSpriteFontSafe(text));
  }

  [Fact]
  public void PieceLabels_AreSpriteFontSafeForAllDefinitions()
  {
    foreach (PieceDefinition definition in PieceDefinitions.All)
    {
      string label = UiText.BuildPieceLabel(definition);

      Assert.True(label.Length <= 2);
      Assert.True(UiText.IsSpriteFontSafe(label), definition.Type.ToString());
    }
  }

  [Fact]
  public void PieceLabels_MatchTheRequestedAbbreviations()
  {
    (PieceDefinition definition, string label)[] expected =
    [
      (PieceDefinitions.Soldier, "So"), (PieceDefinitions.Defender, "Df"),
      (PieceDefinitions.Archer, "Ar"), (PieceDefinitions.Scout, "Sc"),
      (PieceDefinitions.Spearman, "Sp"), (PieceDefinitions.Peasant, "Pe"),
      (PieceDefinitions.Knight, "Kn"), (PieceDefinitions.Crossbowman, "Cb"),
      (PieceDefinitions.Cavalier, "Cv"), (PieceDefinitions.Chariot, "Ch"),
      (PieceDefinitions.Cannon, "Cn"), (PieceDefinitions.Spy, "Sy"),
      (PieceDefinitions.Catapult, "Ct"), (PieceDefinitions.FieldHospital, "FH"),
      (PieceDefinitions.Ambulance, "Am"), (PieceDefinitions.Teacher, "Te"),
      (PieceDefinitions.Ox, "Ox"), (PieceDefinitions.Engineer, "En"),
      (PieceDefinitions.Ballista, "Bl"), (PieceDefinitions.Elephant, "El"),
      (PieceDefinitions.Guard, "Gd"), (PieceDefinitions.Mercenary, "Mc"),
      (PieceDefinitions.Assassin, "As"), (PieceDefinitions.King, "KI"),
      (PieceDefinitions.Princess, "PR"), (PieceDefinitions.Palace, "PA"),
      (PieceDefinitions.Baron, "BR"), (PieceDefinitions.Emissary, "EM")
    ];

    Assert.Equal(PieceDefinitions.All.Length, expected.Length);
    foreach ((PieceDefinition definition, string label) in expected)
    {
      Assert.Equal(label, UiText.BuildPieceLabel(definition));
    }
  }

  [Fact]
  public void ActionDescriptions_UseShortLabelsThatFitTheSidebar()
  {
    Assert.Equal("2 Line/Diag", UiText.FormatAction(PieceDefinitions.Cannon.Movement));
    Assert.Equal("1 Fwd/Diag", UiText.FormatAction(PieceDefinitions.Peasant.AttackShape));
  }

  [Fact]
  public void SpriteFontSafety_RejectsUnsupportedUnicodeCharacters()
  {
    Assert.False(UiText.IsSpriteFontSafe("damage • range"));
  }
}
