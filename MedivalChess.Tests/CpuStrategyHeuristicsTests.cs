using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuStrategyHeuristicsTests
{
  [Fact]
  public void CounterTable_FavoursSoldierAgainstArcherAndWarnsArcherAgainstSoldier()
  {
    CpuGameState state = CreateState(
      new NetworkPiece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-archer", "Archer", NetworkTeam.Red, 2, 0, 10)
    );
    NetworkPiece soldier = state.Pieces.Single(piece => piece.Id == "red-soldier");
    NetworkPiece archer = state.Pieces.Single(piece => piece.Id == "red-archer");

    Assert.True(CpuStrategicHeuristics.GetMatchupScore(state, soldier, archer) > 0f);
    Assert.True(CpuStrategicHeuristics.GetMatchupScore(state, archer, soldier) < 0f);
  }

  [Fact]
  public void SpyCannonCombo_OnlyRewardsAMarkWithAnImmediateFollowUp()
  {
    CpuGameState supported = CreateState(
      new NetworkPiece("red-spy", "Spy", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-cannon", "Cannon", NetworkTeam.Red, -3, -3, 15),
      new NetworkPiece("blue-knight", "Knight", NetworkTeam.Blue, 0, -3, 30)
    );
    UseAbilityAction mark = new(NetworkTeam.Red, "red-spy", "Mark", "blue-knight", 0, -3);
    CpuGameState unsupported = CreateState(
      new NetworkPiece("red-spy", "Spy", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("blue-knight", "Knight", NetworkTeam.Blue, 0, -3, 30)
    );
    UseAbilityAction unsupportedMark = new(NetworkTeam.Red, "red-spy", "Mark", "blue-knight", 0, -3);

    Assert.True(mark.IsLegal(supported));
    Assert.True(unsupportedMark.IsLegal(unsupported));
    Assert.True(CpuStrategicHeuristics.ScoreAction(supported, mark) > 0f);
    Assert.True(CpuStrategicHeuristics.ScoreAction(unsupported, unsupportedMark) < 0f);
  }

  [Fact]
  public void AntiComboEvaluation_PenalisesAClusterThatBombardCanHit()
  {
    CpuGameState clustered = CreateState(
      new NetworkPiece("red-target", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-peasant-a", "Peasant", NetworkTeam.Red, 1, 0, 5),
      new NetworkPiece("red-peasant-b", "Peasant", NetworkTeam.Red, -1, 0, 5),
      new NetworkPiece("blue-bombard", "Bombard", NetworkTeam.Blue, 0, -3, 15)
    );
    CpuGameState spread = CreateState(
      new NetworkPiece("red-target", "Soldier", NetworkTeam.Red, 0, 0, 15),
      new NetworkPiece("red-peasant-a", "Peasant", NetworkTeam.Red, 4, 0, 5),
      new NetworkPiece("red-peasant-b", "Peasant", NetworkTeam.Red, -4, 0, 5),
      new NetworkPiece("blue-bombard", "Bombard", NetworkTeam.Blue, 0, -3, 15)
    );

    Assert.True(CpuStrategicHeuristics.ScoreTeam(clustered, NetworkTeam.Red) <
      CpuStrategicHeuristics.ScoreTeam(spread, NetworkTeam.Red));
  }

  private static CpuGameState CreateState(params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Regicide", 1234, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false
    );
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, 200, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, 200, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain()
    );
  }
}
