namespace MedivalChess.Shared;

public enum NetworkTeam
{
  Red,
  Blue
}

public sealed record NetworkPiece(string Id, string Type, NetworkTeam Team, int X, int Y, int Health);

public sealed record NetworkGameState(string JoinCode, NetworkTeam CurrentTurn, IReadOnlyList<NetworkPiece> Pieces, long Version, int PlayerCount, bool MatchReady);

public sealed record CreateGameRequest(IReadOnlyList<NetworkPiece> Pieces);

public sealed record JoinGameRequest(string JoinCode);

public sealed record MoveRequest(string PieceId, int ToX, int ToY);

public sealed record RoyalSelectionRequest(string RoyalType);

public sealed record RoomJoinResult(bool Accepted, string? Error, string? JoinCode, NetworkTeam? Team, NetworkGameState? State);

public sealed record ActionResult(bool Accepted, string? Error, NetworkGameState? State);
