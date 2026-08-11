using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuGuardStrategyTests
{
  [Fact]
  public void GuardAttach_HeavilyPrefersTheMoreExpensiveUnit()
  {
    NetworkPiece guard = Piece("guard", "Guard", 0, 0);
    NetworkPiece peasant = Piece("peasant", "Peasant", 1, 0);
    NetworkPiece knight = Piece("knight", "Knight", 0, 1);
    CpuGameState state = CreateState(guard, peasant, knight);
    UseAbilityAction cheap = new(NetworkTeam.Red, guard.Id, "Attach", peasant.Id, peasant.X, peasant.Y);
    UseAbilityAction expensive = new(NetworkTeam.Red, guard.Id, "Attach", knight.Id, knight.X, knight.Y);

    Assert.True(cheap.IsLegal(state));
    Assert.True(expensive.IsLegal(state));
    float cheapScore = CpuStrategicHeuristics.ScoreAction(state, cheap);
    float expensiveScore = CpuStrategicHeuristics.ScoreAction(state, expensive);

    Assert.True(expensiveScore >= cheapScore + 45f, $"cheap={cheapScore}, expensive={expensiveScore}");
  }

  [Fact]
  public void GuardCandidateSelection_PicksTheExpensiveAttachment()
  {
    NetworkPiece guard = Piece("guard", "Guard", 0, 0);
    NetworkPiece peasant = Piece("peasant", "Peasant", 1, 0);
    NetworkPiece knight = Piece("knight", "Knight", 0, 1);
    CpuGameState state = CreateState(guard, peasant, knight);
    UseAbilityAction cheap = new(NetworkTeam.Red, guard.Id, "Attach", peasant.Id, peasant.X, peasant.Y);
    UseAbilityAction expensive = new(NetworkTeam.Red, guard.Id, "Attach", knight.Id, knight.X, knight.Y);

    ScoredAction selected = Assert.Single(new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red, [cheap, expensive], new CpuSearchSettings
      {
        CandidatesPerNode = 1,
        PromisingCandidatesPerNode = 1
      }));

    Assert.Equal(expensive, selected.Action);
  }

  [Fact]
  public void GuardMovement_PrefersClosingOnTheMoreExpensiveUnguardedUnit()
  {
    NetworkPiece guard = Piece("guard", "Guard", 0, 0);
    NetworkPiece knight = Piece("knight", "Knight", 4, 0);
    NetworkPiece peasant = Piece("peasant", "Peasant", 0, 4);
    CpuGameState state = CreateState(guard, knight, peasant);
    MoveAction towardKnight = new(NetworkTeam.Red, guard.Id, 1, 0);
    MoveAction towardPeasant = new(NetworkTeam.Red, guard.Id, 0, 1);

    float expensiveApproach = CpuStrategicHeuristics.ScoreAction(state, towardKnight);
    float cheapApproach = CpuStrategicHeuristics.ScoreAction(state, towardPeasant);

    Assert.True(expensiveApproach > cheapApproach + 8f,
      $"toward knight={expensiveApproach}, toward peasant={cheapApproach}");
  }

  [Fact]
  public void AttachedGuard_StateValueScalesWithProtectedUnitCost()
  {
    NetworkPiece knight = Piece("knight", "Knight", 1, 0);
    NetworkPiece peasant = Piece("peasant", "Peasant", 1, 0);
    NetworkPiece knightGuard = Piece("guard", "Guard", 1, 0) with
    {
      AttachedToId = knight.Id,
      AttachmentKind = NetworkAttachmentKind.Guard
    };
    NetworkPiece peasantGuard = Piece("guard", "Guard", 1, 0) with
    {
      AttachedToId = peasant.Id,
      AttachmentKind = NetworkAttachmentKind.Guard
    };

    float protectsKnight = CpuStrategicHeuristics.ScoreTeam(CreateState(knight, knightGuard), NetworkTeam.Red);
    float protectsPeasant = CpuStrategicHeuristics.ScoreTeam(CreateState(peasant, peasantGuard), NetworkTeam.Red);

    Assert.True(protectsKnight > protectsPeasant + 20f,
      $"knight={protectsKnight}, peasant={protectsPeasant}");
  }

  [Fact]
  public void GuardPurchase_IsMoreValuableWhenAnExpensiveAssetNeedsProtection()
  {
    CpuGameState knightState = CreateState(Piece("knight", "Knight", 2, 2));
    CpuGameState peasantState = CreateState(Piece("peasant", "Peasant", 2, 2));
    PurchaseAction buyGuard = new(NetworkTeam.Red, "Guard", 0, 0);

    float knightScore = CpuStrategicHeuristics.ScoreAction(knightState, buyGuard);
    float peasantScore = CpuStrategicHeuristics.ScoreAction(peasantState, buyGuard);

    Assert.True(knightScore > peasantScore + 10f,
      $"knight={knightScore}, peasant={peasantScore}");
  }

  private static NetworkPiece Piece(string id, string type, int x, int y)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return new NetworkPiece(id, type, NetworkTeam.Red, x, y, rule.Health);
  }

  private static CpuGameState CreateState(params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Regicide", 9921, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain());
  }
}
