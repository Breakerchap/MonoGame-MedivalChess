namespace MedivalChess.Shared;

public enum NetworkTeam
{
  Red,
  Blue
}

public enum NetworkAttachmentKind
{
  None,
  Guard,
  Carried,
  Towed
}

public sealed record NetworkPiece(
  string Id,
  string Type,
  NetworkTeam Team,
  int X,
  int Y,
  int Health,
  bool HasMovedThisTurn = false,
  bool HasAttackedThisTurn = false,
  string? AttachedToId = null,
  NetworkAttachmentKind AttachmentKind = NetworkAttachmentKind.None,
  string? MarkedTargetId = null,
  int LastBid = 0
);

public sealed record NetworkImprovement(string Type, int X, int Y, int Health = 0);

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

public sealed record NetworkInitialBuyState(
  NetworkTeam CurrentTeam,
  int PurchasesThisTurn,
  int PurchasesPerTurn,
  int RedBuyTurnsUsed,
  int BlueBuyTurnsUsed,
  int BuyTurnsPerTeam,
  bool RedStopped,
  bool BlueStopped,
  bool IsComplete
);

public sealed record NetworkGameState(
  string JoinCode,
  NetworkTeam CurrentTurn,
  IReadOnlyList<NetworkPiece> Pieces,
  IReadOnlyList<NetworkTeamState> Teams,
  NetworkMatchConfiguration Configuration,
  long Version,
  int PlayerCount,
  bool MatchReady,
  NetworkInitialBuyState? InitialBuy = null,
  IReadOnlyList<NetworkImprovement>? Improvements = null,
  NetworkTeam? Winner = null,
  int ConquestScore = 0
);

public sealed record CreateGameRequest(NetworkMatchConfiguration Configuration);

public sealed record JoinGameRequest(
  string JoinCode,
  string? ReconnectToken = null,
  NetworkTeam? DebugTeam = null
);

public sealed record MoveRequest(string PieceId, int ToX, int ToY);

// TargetId is used for a unit.  TargetX/TargetY let the same authoritative attack flow
// damage a barricade, which is selected by board square rather than by unit id.
public sealed record AttackRequest(string AttackerId, string? TargetId, int? TargetX = null, int? TargetY = null);

public sealed record SpecialActionRequest(string ActorId, string Ability, string? TargetId, int TargetX, int TargetY);

public sealed record SkipTurnRequest();

public sealed record PurchaseRequest(string PieceType, int X, int Y);

public sealed record RoyalSelectionRequest(string RoyalType);

public sealed record DebugTeamSelectionRequest(NetworkTeam Team);

public sealed record RoomJoinResult(
  bool Accepted,
  string? Error,
  string? JoinCode,
  NetworkTeam? Team,
  string? ReconnectToken,
  NetworkGameState? State
);

public sealed record ActionResult(bool Accepted, string? Error, NetworkGameState? State);
