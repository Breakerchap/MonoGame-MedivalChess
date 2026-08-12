using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuTacticalSafetyTests
{
  [Fact]
  public void HangingPiece_PenalisesLosingAnExpensiveUnitMoreThanACheapOne()
  {
    CpuGameState knightState = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0));
    CpuGameState peasantState = CreateState(
      Piece("target", "Peasant", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0));
    HangingPieceEvaluation evaluator = new();
    EvaluationContext context = new(new CpuProfile());

    float knightScore = evaluator.Evaluate(knightState, NetworkTeam.Red, context);
    float peasantScore = evaluator.Evaluate(peasantState, NetworkTeam.Red, new EvaluationContext(new CpuProfile()));

    Assert.True(knightScore < peasantScore - 20f, $"knight={knightScore}, peasant={peasantScore}");
  }

  [Fact]
  public void HangingPiece_RecaptureMakesTheExchangeLessDangerous()
  {
    CpuGameState undefended = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0));
    CpuGameState defended = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0),
      Piece("defender", "Soldier", NetworkTeam.Red, 2, 0));
    HangingPieceEvaluation evaluator = new();

    float undefendedScore = evaluator.Evaluate(undefended, NetworkTeam.Red, new EvaluationContext(new CpuProfile()));
    float defendedScore = evaluator.Evaluate(defended, NetworkTeam.Red, new EvaluationContext(new CpuProfile()));

    Assert.True(defendedScore > undefendedScore + 8f, $"defended={defendedScore}, undefended={undefendedScore}");
  }

  [Fact]
  public void OpponentSearchPolicy_UsesFullSearchForLethalPremiumThreat()
  {
    CpuGameState state = CreateState(
      Piece("target", "Knight", NetworkTeam.Red, 0, 0, health: 1),
      Piece("attacker", "Soldier", NetworkTeam.Blue, 1, 0),
      currentTurn: NetworkTeam.Blue);
    CpuProfile profile = CpuProfile.Hard();

    CpuOpponentSearchShape shape = CpuOpponentSearchPolicy.Choose(
      state, NetworkTeam.Red, profile, new EvaluationContext(profile));

    Assert.Equal(profile.Search.OpponentActionsToPredict, shape.ActionsToPredict);
    Assert.Equal(profile.Search.OpponentBeamWidth, shape.BeamWidth);
    Assert.True(shape.IsTactical);
  }

  [Fact]
  public void OpponentSearchPolicy_ReducesQuietReplySearch()
  {
    CpuGameState state = CreateState(
      Piece("red", "Knight", NetworkTeam.Red, 0, 0),
      Piece("blue", "Soldier", NetworkTeam.Blue, 8, 8),
      currentTurn: NetworkTeam.Blue);
    CpuProfile profile = CpuProfile.Hard();

    CpuOpponentSearchShape shape = CpuOpponentSearchPolicy.Choose(
      state, NetworkTeam.Red, profile, new EvaluationContext(profile));

    Assert.True(shape.ActionsToPredict < profile.Search.OpponentActionsToPredict);
    Assert.True(shape.BeamWidth < profile.Search.OpponentBeamWidth);
    Assert.False(shape.IsTactical);
    Assert.True(shape.ActionsToPredict >= 2);
  }

  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y, int? health = null)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return new NetworkPiece(id, type, team, x, y, health ?? rule.Health);
  }

  private static CpuGameState CreateState(
    NetworkPiece first,
    NetworkPiece second,
    NetworkPiece? third = null,
    NetworkTeam currentTurn = NetworkTeam.Red)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 9917, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    NetworkPiece[] pieces = third is null ? [first, second] : [first, second, third];
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 0, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 0, MatchRules.ActionsPerTurn)
      ],
      currentTurn,
      terrain: new BattlefieldTerrain());
  }
}
