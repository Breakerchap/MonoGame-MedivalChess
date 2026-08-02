namespace MedivalChess.Shared;

/// <summary>Shared player order, facing, and side selection for every match size.</summary>
public static class TeamRules
{
  private static readonly NetworkTeam[] TwoPlayerTeams = [NetworkTeam.Red, NetworkTeam.Blue];
  private static readonly NetworkTeam[] ThreePlayerTeams = [NetworkTeam.Red, NetworkTeam.Blue, NetworkTeam.Green];
  private static readonly NetworkTeam[] FourPlayerTeams = [NetworkTeam.Red, NetworkTeam.Blue, NetworkTeam.Green, NetworkTeam.Yellow];

  public static bool IsValidPlayerCount(int playerCount) => playerCount is >= 2 and <= 4;

  public static IReadOnlyList<NetworkTeam> GetActiveTeams(int playerCount) => playerCount switch
  {
    2 => TwoPlayerTeams,
    3 => ThreePlayerTeams,
    4 => FourPlayerTeams,
    _ => throw new ArgumentOutOfRangeException(nameof(playerCount), "Matches support two to four players.")
  };

  public static (int x, int y) GetForwardDirection(NetworkTeam team) => team switch
  {
    NetworkTeam.Red => (0, -1),
    NetworkTeam.Blue => (0, 1),
    NetworkTeam.Green => (1, 0),
    NetworkTeam.Yellow => (-1, 0),
    _ => throw new ArgumentOutOfRangeException(nameof(team))
  };

  public static NetworkTeam GetNextTeam(NetworkTeam team, int playerCount)
  {
    IReadOnlyList<NetworkTeam> teams = GetActiveTeams(playerCount);
    int index = -1;
    for (int candidate = 0; candidate < teams.Count; candidate++)
    {
      if (teams[candidate] == team)
      {
        index = candidate;
        break;
      }
    }
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(team), "The team is not active in this match.");
    return teams[(index + 1) % teams.Count];
  }

  public static NetworkTeam GetFirstTeam(int playerCount) => GetActiveTeams(playerCount)[0];
}
