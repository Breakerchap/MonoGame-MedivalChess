using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public class UiTextTests
{
  [Fact]
  public void PieceLabels_PreserveAllThreeAbbreviationCharacters()
  {
    PieceDefinition definition = new(
      PieceType.Swordsman,
      "abc",
      Pack.Medival,
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

  [Fact]
  public void SanitiseForSpriteFont_TransliteratesCommonUnsupportedCharacters()
  {
    HashSet<char> ascii = Enumerable.Range(' ', '~' - ' ' + 1)
      .Select(value => (char)value)
      .ToHashSet();

    string sanitised = UiText.SanitiseForSpriteFont(
      "1×1 – dash — Muse → Circle • “quoted”…\t✓",
      ascii);

    Assert.Equal("1x1 - dash - Muse -> Circle * \"quoted\"...    ?", sanitised);
    Assert.All(sanitised, character =>
      Assert.True(character == '\n' || ascii.Contains(character), $"Unsupported output character U+{(int)character:X4}."));
  }

  [Fact]
  public void EveryAuthoritativeUnitLabelAndAbilityCanBeMadeSpriteFontSafe()
  {
    HashSet<char> ascii = Enumerable.Range(' ', '~' - ' ' + 1)
      .Select(value => (char)value)
      .ToHashSet();

    foreach (PieceDefinition definition in PieceDefinitions.All)
    {
      foreach (string text in new[] { definition.DisplayName, definition.AbilityDescription })
      {
        string sanitised = UiText.SanitiseForSpriteFont(text, ascii);
        Assert.All(sanitised, character =>
          Assert.True(character == '\n' || ascii.Contains(character),
            $"{definition.SourceUnitId}: unsupported output character U+{(int)character:X4}."));
      }
    }
  }
}
