using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class PieceCatalogueCompletenessTests
{
  [Fact]
  public void EverySourceUnitHasExactlyOneDefinition()
  {
    // Chess King is checkmate-only and Field Hospital lacks core combat stats; every other
    // data-ready source row is represented, including Qilin's minimum legal X form and Serpent's segment.
    Assert.Equal(152, PieceDefinitions.All.Length);
    Assert.Equal(PieceDefinitions.All.Length, PieceDefinitions.All.Select(definition => definition.Type).Distinct().Count());
    foreach (PieceDefinition definition in PieceDefinitions.All)
    {
      Assert.True(UnitRules.TryGet(definition.Identifier, out _), $"Missing shared rule for {definition.Identifier}.");
    }
  }

  [Fact]
  public void WorkbookOnlyAddsTheRequestedNonLegacyPacks()
  {
    Assert.Contains(Pack.Medival, PackRules.All);
    Assert.Contains(Pack.Norse, PackRules.All);
    Assert.Contains(Pack.WildWest, PackRules.All);
    Assert.Equal(11, PackRules.All.Count);
    Assert.Equal(
      [Pack.Medival, Pack.Dynasty, Pack.Fantasy, Pack.Undead, Pack.Greek, Pack.Norse, Pack.Modern, Pack.WildWest, Pack.AngelsDemons, Pack.Chess, Pack.Legacy],
      PackRules.All);
  }

  [Fact]
  public void SuppliedPackExportsHaveCanonicalDefinitions()
  {
    string[] identifiers =
    [
      "Swordsman", "Ashigaru", "Sumo", "Carpenter", "Banshee", "Abomination", "Elf", "Witch",
      "Officer", "Brawler", "Demolitionist", "Pickpocket", "Stagecoach", "Spartan", "Hunter", "Valkyrie",
      "Runesmith", "Daedalus", "Atlas", "Chronos", "Mason", "Reaper", "Lich", "Phylactery", "Sheriff"
      , "Giant", "Cyclops", "Fiend", "Cherub", "Fallen", "Gatekeeper", "Archangel", "Archdemon", "Succubus", "Herald",
      "Pawn", "ChessKnight", "Bishop", "Rook", "Queen"
    ];

    Assert.All(identifiers, identifier => Assert.True(UnitRules.TryGet(identifier, out _), identifier));

    PieceDefinition sheriff = PieceDefinitions.All.Single(unit =>
      unit.SourceUnitId == "wild_west.sheriff");
    Assert.Equal(PieceType.Sheriff, sheriff.Type);
    Assert.Equal(PieceCategory.Royal, sheriff.Category);
  }

  [Fact]
  public void UnchoosableRowsStayInTheEncyclopediaButNeverInThePurchaseRoster()
  {
    PieceDefinition terracotta = PieceDefinitions.Encyclopedia.Single(unit => unit.Type == PieceType.TerracottaWarrior);
    PieceDefinition flesh = PieceDefinitions.Encyclopedia.Single(unit => unit.Type == PieceType.Flesh);

    Assert.False(terracotta.IsPurchasable);
    Assert.False(flesh.IsPurchasable);
    Assert.DoesNotContain(PieceDefinitions.Purchasable, unit => !unit.IsPurchasable);
    Assert.Contains(Pack.Legacy, PieceDefinitions.Encyclopedia.Select(unit => unit.Pack));
  }

  [Fact]
  public void VariableAndFormationUnitsHaveDocumentedBaselineDefinitions()
  {
    PieceDefinition qilin = PieceDefinitions.All.Single(unit => unit.Type == PieceType.Qilin);
    PieceDefinition serpent = PieceDefinitions.All.Single(unit => unit.Type == PieceType.Serpent);

    Assert.Equal((20, 40, 40), (qilin.Attack, qilin.Health, qilin.Cost)); // X = 40
    Assert.Equal((1, 1), serpent.Size);
    Assert.Equal(40, serpent.Health);
  }
}
