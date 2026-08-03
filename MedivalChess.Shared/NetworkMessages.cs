namespace MedivalChess.Shared;

public enum NetworkTeam
{
  Red,
  Blue,
  Green,
  Yellow,
  Neutral
}

public enum NetworkAttachmentKind
{
  None,
  Guard,
  Carried
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
  int LastBid = 0,
  int EngineerBuildsThisTurn = 0,
  bool CannotContributeToConquestThisTurn = false
);

public sealed record NetworkImprovement(
  string Type,
  int X,
  int Y,
  int Health = 0,
  NetworkTeam? Owner = null
);

public sealed record NetworkTeamState(NetworkTeam Team, int Money, int ActionsRemaining, string? ChosenRoyal);
public sealed record NetworkConquestTeamState(NetworkTeam Team, int Score);

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
  int ConquestWinScore,
  bool FarmsEnabled = Globals.FarmsEnabled,
  int FarmIncomePerTurn = Globals.FarmIncomePerTurn,
  bool UnitMaintenanceEnabled = Globals.UnitMaintenanceEnabled,
  int UnitMaintenancePercent = Globals.UnitMaintenancePercent,
  int UnitPricePercent = Globals.UnitPricePercent,
  int PlayerCount = 2,
  bool InterestEnabled = Globals.InterestEnabled,
  int InterestPercent = Globals.InterestPercent,
  int EscortRoyalHealthPercent = Globals.DefaultEscortRoyalHealthPercent
);

/// <summary>Opening-buy progress for one team. The legacy Red/Blue fields on
/// <see cref="NetworkInitialBuyState"/> remain populated for two-player clients.</summary>
public sealed record NetworkInitialBuyTeamState(NetworkTeam Team, int BuyTurnsUsed, bool Stopped, int FarmsPlaced = 0);

public sealed record NetworkInitialBuyState(
  NetworkTeam CurrentTeam,
  int PurchasesThisTurn,
  int PurchasesPerTurn,
  int RedBuyTurnsUsed,
  int BlueBuyTurnsUsed,
  int BuyTurnsPerTeam,
  bool RedStopped,
  bool BlueStopped,
  bool IsComplete,
  IReadOnlyList<NetworkInitialBuyTeamState>? TeamStates = null,
  bool IsFarmPlacementPhase = false
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
  int ConquestScore = 0,
  IReadOnlyList<NetworkConquestTeamState>? ConquestScores = null
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
