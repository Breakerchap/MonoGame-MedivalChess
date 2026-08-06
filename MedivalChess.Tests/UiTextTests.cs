using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;
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
  public void Encyclopedia_IncludesEveryDisplayablePieceType()
  {
    Assert.Equal(23, PieceDefinitions.Encyclopedia.Length);
    Assert.DoesNotContain(PieceDefinitions.Encyclopedia, piece => piece.Type is PieceType.Scout or PieceType.Spearman or PieceType.Cavalier);
    Assert.Equal(
      PieceDefinitions.Encyclopedia.Length,
      PieceDefinitions.Encyclopedia.Select(definition => definition.Type).Distinct().Count()
    );
  }

  [Fact]
  public void PieceLabels_MatchTheRequestedAbbreviations()
  {
    (PieceDefinition definition, string label)[] expected =
    [
      (PieceDefinitions.Soldier, "So"), (PieceDefinitions.Defender, "Df"),
      (PieceDefinitions.Archer, "Ar"), (PieceDefinitions.Peasant, "Pe"),
      (PieceDefinitions.Knight, "Kn"), (PieceDefinitions.Crossbowman, "Cb"), (PieceDefinitions.Chariot, "Ch"),
      (PieceDefinitions.Cannon, "Cn"), (PieceDefinitions.Spy, "Sy"),
      (PieceDefinitions.Catapult, "Ct"), (PieceDefinitions.Bombard, "Bd"),
      (PieceDefinitions.Ox, "Ox"), (PieceDefinitions.Engineer, "En"),
      (PieceDefinitions.Ballista, "Bl"), (PieceDefinitions.Elephant, "El"),
      (PieceDefinitions.Guard, "Gd"), (PieceDefinitions.Mercenary, "Mc"),
      (PieceDefinitions.Farm, "Fm"),
      (PieceDefinitions.King, "KI"),
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
    Assert.Equal("2 Line", UiText.FormatAction(PieceDefinitions.Cannon.Movement));
    Assert.Equal("1 Straight", UiText.FormatAction(PieceDefinitions.Peasant.AttackRange, PieceDefinitions.Peasant.AttackPattern));
  }

  [Fact]
  public void PieceDefinitions_CarryTheAbilityTextDisplayedByTheUi()
  {
    Assert.Equal("Attaches to a friendly non-royal unit and takes damage for it.", PieceDefinitions.Guard.AbilityDescription);
    Assert.Empty(PieceDefinitions.Soldier.AbilityDescription);
  }

  [Fact]
  public void TeamDisplayNames_UseOrangeAndPurple()
  {
    Assert.Equal("ORANGE", UiText.GetTeamDisplayName(TeamName.Red));
    Assert.Equal("PURPLE", UiText.GetTeamDisplayName(TeamName.Blue));
  }

  [Fact]
  public void SpriteFontSafety_RejectsUnsupportedUnicodeCharacters()
  {
    Assert.False(UiText.IsSpriteFontSafe("damage • range"));
  }
}
