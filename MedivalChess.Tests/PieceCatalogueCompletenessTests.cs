using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class PieceCatalogueCompletenessTests
{
  [Fact]
  public void EverySourceUnitHasExactlyOneDefinition()
  {
    Assert.Equal(100, PieceDefinitions.All.Length);
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
    Assert.Equal(10, PackRules.All.Count);
    Assert.Equal(
      [Pack.Medival, Pack.Dynasty, Pack.Fantasy, Pack.Undead, Pack.Greek, Pack.Norse, Pack.Modern, Pack.WildWest, Pack.AngelsDemons, Pack.Chess],
      PackRules.All);
  }

  [Fact]
  public void SuppliedPackExportsHaveCanonicalDefinitions()
  {
    string[] identifiers =
    [
      "Swordsman", "Ashigaru", "Sumo", "Carpenter", "Banshee", "Abomination", "Elf", "Witch",
      "Officer", "Brawler", "Demolitionist", "Pickpocket", "Stagecoach", "Spartan", "Hunter", "Valkyrie",
      "Runesmith", "Jarl", "Daedalus", "Atlas", "Chronos", "Mason", "Reaper", "Lich", "Phylactery", "Sherrif"
      , "Giant", "Cyclops", "Fiend", "Cherub", "Fallen", "Gatekeeper", "Archangel", "Archdemon", "Succubus", "Herald",
      "Pawn", "ChessKnight", "Bishop", "Rook", "Queen", "ChessKing"
    ];

    Assert.All(identifiers, identifier => Assert.True(UnitRules.TryGet(identifier, out _), identifier));
  }
}
