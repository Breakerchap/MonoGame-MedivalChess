namespace MedivalChess.Shared;

public enum NetworkTeam
{
  Red,
  Blue
}

public sealed record NetworkPiece(
  string Id,
  string Type,
  NetworkTeam Team,
  int X,
  int Y,
  int Health,
  bool HasMovedThisTurn = false,
  bool HasAttackedThisTurn = false
);

public sealed record NetworkTeamState(NetworkTeam Team, int Money, int ActionsRemaining, string? ChosenRoyal);

public sealed record NetworkMatchConfiguration(
  string BoardSize,
  string ForestDensity,
  string WaterwayDensity,
  string GameMode,
  int TerrainSeed,
  int StartingCash,
  float KillerRefundMultiplier,
  float DefeatedTeamRefundMultiplier,
  int InitialBuysPerTurn,
  int InitialBuyTurnsPerTeam,
  int ConquestWinScore
);

public sealed record NetworkGameState(
  string JoinCode,
  NetworkTeam CurrentTurn,
  IReadOnlyList<NetworkPiece> Pieces,
  IReadOnlyList<NetworkTeamState> Teams,
  NetworkMatchConfiguration Configuration,
  long Version,
  int PlayerCount,
  bool MatchReady
);

public sealed record CreateGameRequest(NetworkMatchConfiguration Configuration);

public sealed record JoinGameRequest(string JoinCode, string? ReconnectToken = null);

public sealed record MoveRequest(string PieceId, int ToX, int ToY);

public sealed record RoyalSelectionRequest(string RoyalType);

public sealed record RoomJoinResult(
  bool Accepted,
  string? Error,
  string? JoinCode,
  NetworkTeam? Team,
  string? ReconnectToken,
  NetworkGameState? State
);

public sealed record ActionResult(bool Accepted, string? Error, NetworkGameState? State);
