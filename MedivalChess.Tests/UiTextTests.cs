using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public class UiTextTests
{
  [Fact]
  public void PieceLabels_PreserveAllThreeAbbreviationCharacters()
  {
    PieceDefinition definition = new(
      PieceType.Soldier,
      "abc",
      Pack.Base,
      (1, Shape.Any),
      1,
      1,
      (1, 1),
      (1, 1),
      Shape.Any,
      1
    );

    Assert.Equal("ABC", UiText.BuildPieceLabel(definition));
  }

  [Fact]
  public void PieceLabels_AreSpriteFontSafeForAllDefinitions()
  {
    foreach (PieceDefinition definition in PieceDefinitions.All)
    {
      string label = UiText.BuildPieceLabel(definition);

      Assert.True(label.Length <= 3);
      Assert.True(UiText.IsSpriteFontSafe(label), definition.Type.ToString());
    }
  }

  [Fact]
  public void FormatAction_ShowsTheEntireInclusiveAttackRange()
  {
    Assert.Equal("2-3 Any", UiText.FormatAction(new AttackRange(2, 3), Shape.Any));
  }

  [Fact]
  public void SpriteFontSafety_RejectsUnsupportedUnicodeCharacters()
  {
    Assert.False(UiText.IsSpriteFontSafe("damage • range"));
  }
}
