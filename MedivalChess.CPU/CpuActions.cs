using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>A deterministic, render-free action that can be validated and simulated by the CPU.</summary>
public interface ICpuGameAction
{
  NetworkTeam Team { get; }
  CpuActionKind Kind { get; }
  bool IsLegal(CpuGameState state);
  CpuGameState Apply(CpuGameState state);
  string Describe();
}

public enum CpuActionKind
{
  Move,
  Attack,
  Purchase,
  UseAbility,
  EndTurn,
  StopInitialBuying
}

public sealed record MoveAction(NetworkTeam Team, string PieceId, int DestinationX, int DestinationY) : ICpuGameAction
{
  public CpuActionKind Kind => CpuActionKind.Move;
  public bool IsLegal(CpuGameState state) => CpuGameRules.IsLegal(state, this);
  public CpuGameState Apply(CpuGameState state) => CpuGameRules.Apply(state, this);
  public string Describe() => $"Move {PieceId} to ({DestinationX}, {DestinationY})";
}

/// <summary>Attacks a unit by stable ID, or a barricade when <see cref="TargetPieceId"/> is null.</summary>
public sealed record AttackAction(
  NetworkTeam Team,
  string AttackerId,
  string? TargetPieceId,
  int TargetX,
  int TargetY
) : ICpuGameAction
{
  public CpuActionKind Kind => CpuActionKind.Attack;
  public bool IsLegal(CpuGameState state) => CpuGameRules.IsLegal(state, this);
  public CpuGameState Apply(CpuGameState state) => CpuGameRules.Apply(state, this);
  public string Describe() => TargetPieceId is null
    ? $"Attack barricade at ({TargetX}, {TargetY}) with {AttackerId}"
    : $"Attack {TargetPieceId} with {AttackerId}";
}

/// <summary>Places a unit, or buys an enemy/neutral Mercenary standing on the destination.</summary>
public sealed record PurchaseAction(NetworkTeam Team, string UnitType, int X, int Y) : ICpuGameAction
{
  public CpuActionKind Kind => CpuActionKind.Purchase;
  public bool IsLegal(CpuGameState state) => CpuGameRules.IsLegal(state, this);
  public CpuGameState Apply(CpuGameState state) => CpuGameRules.Apply(state, this);
  public string Describe() => $"Purchase {UnitType} at ({X}, {Y})";
}

/// <summary>Uses an existing unit ability with a deterministic target square and optional target ID.</summary>
public sealed record UseAbilityAction(
  NetworkTeam Team,
  string ActorId,
  string Ability,
  string? TargetPieceId,
  int TargetX,
  int TargetY
) : ICpuGameAction
{
  public CpuActionKind Kind => CpuActionKind.UseAbility;
  public bool IsLegal(CpuGameState state) => CpuGameRules.IsLegal(state, this);
  public CpuGameState Apply(CpuGameState state) => CpuGameRules.Apply(state, this);
  public string Describe() => $"{ActorId} uses {Ability} at ({TargetX}, {TargetY})";
}

public sealed record EndTurnAction(NetworkTeam Team) : ICpuGameAction
{
  public CpuActionKind Kind => CpuActionKind.EndTurn;
  public bool IsLegal(CpuGameState state) => CpuGameRules.IsLegal(state, this);
  public CpuGameState Apply(CpuGameState state) => CpuGameRules.Apply(state, this);
  public string Describe() => "End turn";
}

public sealed record StopInitialBuyingAction(NetworkTeam Team) : ICpuGameAction
{
  public CpuActionKind Kind => CpuActionKind.StopInitialBuying;
  public bool IsLegal(CpuGameState state) => CpuGameRules.IsLegal(state, this);
  public CpuGameState Apply(CpuGameState state) => CpuGameRules.Apply(state, this);
  public string Describe() => "Stop initial buying";
}
