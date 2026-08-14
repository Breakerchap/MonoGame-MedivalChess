using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class PieceCatalogueCompletenessTests
{
  [Fact]
  public void EveryPieceTypeHasExactlyOneDefinition()
  {
    PieceType[] pieceTypes = Enum.GetValues<PieceType>();

    Assert.Equal(pieceTypes.Length, PieceDefinitions.All.Length);
    foreach (PieceType pieceType in pieceTypes)
    {
      Assert.Single(PieceDefinitions.All, definition => definition.Type == pieceType);
      Assert.True(UnitRules.TryGet(pieceType.ToString(), out _), $"Missing shared rule for {pieceType}.");
    }
  }

  [Fact]
  public void WorkbookOnlyAddsTheRequestedNonLegacyPacks()
  {
    Assert.Contains(Pack.Norse, PackRules.All);
    Assert.Contains(Pack.WildWest, PackRules.All);
    Assert.DoesNotContain(PackRules.All, pack => pack.ToString().Contains("Legacy", StringComparison.OrdinalIgnoreCase));
  }
}
