using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class NewUnitsAndPacksTests
{
  [Fact]
  public void NewPackUnitsExposeAuthoredStatsWithoutNewAbilityBehaviour()
  {
    Assert.Equal(Pack.Dynasty, PieceDefinitions.Ninja.Pack);
    Assert.Equal((1, 4), (PieceDefinitions.Ninja.Movement.Minimum, PieceDefinitions.Ninja.Movement.Maximum));
    Assert.Equal(5, PieceDefinitions.Ninja.Attack);
    Assert.Equal(10, PieceDefinitions.Ninja.Health);
    Assert.Equal((2, 4), (PieceDefinitions.Ninja.AttackRange.Minimum, PieceDefinitions.Ninja.AttackRange.Maximum));
    Assert.Equal(40, PieceDefinitions.Ninja.Cost);

    Assert.Equal(Pack.Fantasy, PieceDefinitions.Dragon.Pack);
    Assert.Equal((2, 3), PieceDefinitions.Dragon.Size);
    Assert.Equal(Shape.ForwardLine, PieceDefinitions.Dragon.AttackPattern);
    Assert.Equal(90, PieceDefinitions.Dragon.Cost);

    Assert.Equal(Pack.Greek, PieceDefinitions.Pegasus.Pack);
    Assert.Equal((2, 4), (PieceDefinitions.Pegasus.Movement.Minimum, PieceDefinitions.Pegasus.Movement.Maximum));
    Assert.Null(PieceDefinitions.Pegasus.Abbreviation);

    Assert.Equal(10, PieceDefinitions.Princess.Attack);

    Assert.Equal(Pack.Chess, PieceDefinitions.Queen.Pack);
    Assert.Equal(Shape.LineOrDiagonal, PieceDefinitions.Queen.Movement.shape);
    Assert.Equal(Shape.None, PieceDefinitions.Queen.AttackPattern);
    Assert.Equal(60, PieceDefinitions.Queen.Attack);
  }

  [Fact]
  public void GeneratedOnlyUnitsAreNotPurchasableOrSelectableRoyals()
  {
    Assert.Contains(PieceDefinitions.Flesh, PieceDefinitions.All);
    Assert.DoesNotContain(PieceDefinitions.Flesh, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.TerracottaWarrior, PieceDefinitions.All);
    Assert.DoesNotContain(PieceDefinitions.TerracottaWarrior, PieceDefinitions.Purchasable);
    Assert.DoesNotContain(PieceDefinitions.TerracottaWarrior, PieceDefinitions.Royals);
  }

  [Fact]
  public void IncompletePdfUnitsAreNotInvented()
  {
    Assert.DoesNotContain(PieceDefinitions.All, definition => definition.Type == PieceType.Ghoul);
    Assert.DoesNotContain(PieceDefinitions.All, definition => definition.DisplayName == "President");
  }

  [Fact]
  public void PackRulesNormaliseAndFilterDefinitions()
  {
    Assert.True(PackRules.TryNormaliseAllowedPacks(["chess", "Base", "Chess"], out string[] packs));
    Assert.Equal(["Base", "Chess"], packs);
    Assert.True(PackRules.IsAllowed(PieceDefinitions.Soldier, packs));
    Assert.True(PackRules.IsAllowed(PieceDefinitions.Queen, packs));
    Assert.False(PackRules.IsAllowed(PieceDefinitions.Ninja, packs));
    Assert.False(PackRules.TryNormaliseAllowedPacks([], out _));
    Assert.False(PackRules.TryNormaliseAllowedPacks(["NotAPack"], out _));
  }

  [Fact]
  public void UnitRuleListsRespectExplicitPurchasableAndRoyalCollections()
  {
    Assert.DoesNotContain(UnitRules.Purchasable, rule => rule.Type == "Flesh");
    Assert.DoesNotContain(UnitRules.Purchasable, rule => rule.Type == "TerracottaWarrior");
    Assert.DoesNotContain(UnitRules.Royals, rule => rule.Type == "TerracottaWarrior");
    Assert.Contains(UnitRules.Royals, rule => rule.Type == "Emperor");
  }

  [Fact]
  public void NewMovementShapesUseTheirOwnGeometry()
  {
    UnitRule bishop = UnitRules.GetRequired("Bishop");
    Assert.True(UnitRules.CanMove(bishop, 0, 0, 4, 4));
    Assert.False(UnitRules.CanMove(bishop, 0, 0, 4, 3));

    UnitRule queen = UnitRules.GetRequired("Queen");
    Assert.True(UnitRules.CanMove(queen, 0, 0, 0, 7));
    Assert.True(UnitRules.CanMove(queen, 0, 0, 6, 6));
    Assert.False(UnitRules.CanMove(queen, 0, 0, 6, 4));

    UnitRule knight = UnitRules.GetRequired("ChessKnight");
    Assert.True(UnitRules.CanMove(knight, 0, 0, 2, 1));
    Assert.False(UnitRules.CanMove(knight, 0, 0, 1, 1));
  }

  [Fact]
  public void PegasusCannotStopInsideItsMinimumMovementDistance()
  {
    UnitRule pegasus = UnitRules.GetRequired("Pegasus");
    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      pegasus,
      (0, 0),
      NetworkTeam.Red,
      _ => true,
      (_, _) => true,
      _ => 1,
      (_, _) => false
    );

    Assert.DoesNotContain((1, 0), paths.Keys);
    Assert.Contains((2, 0), paths.Keys);
    Assert.Contains((4, 0), paths.Keys);
  }

  [Fact]
  public void CampaignPurchasesRespectAllowedPacks()
  {
    CampaignLevelDefinition level = CampaignLevelDefinition.CreateNew();
    level.Restrictions.AllowedPacks = [Pack.Greek.ToString()];

    IReadOnlyList<string> purchasable = CampaignUnitResolver.GetPurchasableIdentifiers(level);
    Assert.Contains("Chariot", purchasable);
    Assert.Contains("Pegasus", purchasable);
    Assert.DoesNotContain("Soldier", purchasable);
    Assert.DoesNotContain("Ninja", purchasable);
  }
}
