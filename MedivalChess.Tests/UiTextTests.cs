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
