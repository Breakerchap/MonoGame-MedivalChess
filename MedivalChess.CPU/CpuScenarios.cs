using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>The state of one configurable campaign objective for a team.</summary>
public enum CpuGoalStatus
{
  InProgress,
  Completed,
  Failed
}

/// <summary>A campaign objective that can contribute scoring without replacing the CPU engine.</summary>
public interface ICpuScenarioGoal
{
  CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team);
  float EvaluateProgress(CpuGameState state, NetworkTeam team);
  IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team);
}

/// <summary>CPU-only campaign configuration. Match rules remain enforced by the action simulator.</summary>
public sealed class CpuScenarioDefinition
{
  public string Id { get; init; } = "match";
  public IReadOnlyList<ICpuScenarioGoal> VictoryGoals { get; init; } = [];
  public IReadOnlyList<ICpuScenarioGoal> DefeatConditions { get; init; } = [];
  public IReadOnlyList<ICpuScenarioGoal> SecondaryGoals { get; init; } = [];
  public CpuScenarioRestrictions Restrictions { get; init; } = new();
  public CpuScenarioWeights Weights { get; init; } = new();
  /// <summary>Optional campaign-owned profiles. Normal CPU is used when a team has no override.</summary>
  public IReadOnlyDictionary<NetworkTeam, CpuProfile> TeamProfiles { get; init; } =
    new Dictionary<NetworkTeam, CpuProfile>();

  public bool IsTerminal(CpuGameState state) => state.Winner is not null ||
    TeamRules.GetActiveTeams(state.Configuration.PlayerCount).Any(team =>
      VictoryGoals.Any(goal => goal.GetStatus(state, team) == CpuGoalStatus.Completed) ||
      DefeatConditions.Any(goal => goal.GetStatus(state, team) == CpuGoalStatus.Failed));

  public static CpuScenarioDefinition ForMatch(NetworkMatchConfiguration configuration) => new()
  {
    Id = configuration.GameMode,
    VictoryGoals = configuration.GameMode switch
    {
      "Regicide" => [new DefeatRoyalGoal()],
      "Conquest" => [new ScoreGoal(score => score.ConquestScores, configuration.ConquestWinScore, "Conquest")],
      "Dominion" => [new ScoreGoal(score => score.ModeScores, configuration.DominionWinScore, "Dominion")],
      "Plunder" => [new ScoreGoal(score => score.ModeScores, configuration.PlunderWinScore, "Plunder")],
      "Escort" => [new EscapeRoyalGoal()],
      _ => []
    }
  };
}

/// <summary>Optional campaign restrictions applied before legal actions are generated.</summary>
public sealed class CpuScenarioRestrictions
{
  public IReadOnlySet<string>? AllowedPurchases { get; init; }
  public IReadOnlySet<string>? AllowedAbilities { get; init; }
  public IReadOnlySet<NetworkTeam>? AllowedTeams { get; init; }
  public Func<CpuGameState, ICpuGameAction, bool>? AdditionalActionRule { get; init; }

  public bool Allows(CpuGameState state, ICpuGameAction action)
  {
    if (AllowedTeams is not null && !AllowedTeams.Contains(action.Team))
    {
      return false;
    }

    if (action is PurchaseAction purchase && AllowedPurchases is not null && !AllowedPurchases.Contains(purchase.UnitType))
    {
      return false;
    }

    if (action is UseAbilityAction ability && AllowedAbilities is not null && !AllowedAbilities.Contains(ability.Ability))
    {
      return false;
    }

    return AdditionalActionRule?.Invoke(state, action) ?? true;
  }
}

/// <summary>Scenario multipliers layered over the profile's normal evaluation weights.</summary>
public sealed class CpuScenarioWeights
{
  public float ObjectiveProgress { get; init; } = 1f;
  public float Material { get; init; } = 1f;
  public float RoyalSafety { get; init; } = 1f;
  public float Economy { get; init; } = 1f;
}

/// <summary>Simple short-term direction supplied by scenarios and tactical analysis.</summary>
public sealed record CpuIntent(
  CpuIntentType Type,
  float Priority,
  string? PieceId = null,
  string? TargetPieceId = null,
  (int x, int y)? TargetPosition = null,
  int ExpiryTurn = 0
);

public enum CpuIntentType
{
  AttackTarget,
  DefendTarget,
  CaptureLocation,
  HoldLocation,
  EscortUnit,
  RetreatUnit,
  ProtectRoyal,
  GatherGold,
  PurchaseUnit,
  BlockRoute,
  Escape
}

public sealed class DefeatRoyalGoal : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team)
  {
    if (state.Winner is NetworkTeam winner)
    {
      return winner == team ? CpuGoalStatus.Completed : CpuGoalStatus.Failed;
    }

    bool enemyRoyalExists = state.Pieces.Any(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal);
    return enemyRoyalExists ? CpuGoalStatus.InProgress : CpuGoalStatus.Completed;
  }

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) => state.Pieces
    .Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral && UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
    .Sum(piece => -piece.Health);

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) => state.Pieces
    .Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral && UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
    .Select(piece => new CpuIntent(CpuIntentType.AttackTarget, 100f, TargetPieceId: piece.Id));
}

public sealed class EscapeRoyalGoal : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team) => state.Winner is NetworkTeam winner
    ? winner == team ? CpuGoalStatus.Completed : CpuGoalStatus.Failed
    : CpuGoalStatus.InProgress;

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) => state.Pieces
    .Where(piece => piece.Team == team && UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
    .Select(piece => piece.OccupiedSquares(UnitRules.GetRequired(piece.Type)).Min(square => DistanceToEnemyEdge(state.Board, team, square)))
    .DefaultIfEmpty(100)
    .Min() * -1f;

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) => state.Pieces
    .Where(piece => piece.Team == team && UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)
    .Select(piece => new CpuIntent(CpuIntentType.Escape, 100f, PieceId: piece.Id));

  private static int DistanceToEnemyEdge(Board board, NetworkTeam team, (int x, int y) position) => team switch
  {
    NetworkTeam.Red => position.y - board.MinY,
    NetworkTeam.Blue => board.MinY + board.BoardArray.GetLength(0) - 1 - position.y,
    NetworkTeam.Green => board.MinX + board.BoardArray.GetLength(1) - 1 - position.x,
    NetworkTeam.Yellow => position.x - board.MinX,
    _ => 0
  };
}

/// <summary>Wins when the specified team has removed every enemy unit.</summary>
public sealed class EliminateEnemiesGoal : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team)
  {
    if (state.Winner is NetworkTeam winner)
    {
      return winner == team ? CpuGoalStatus.Completed : CpuGoalStatus.Failed;
    }
    return state.Pieces.Any(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral)
      ? CpuGoalStatus.InProgress
      : CpuGoalStatus.Completed;
  }

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) => -state.Pieces
    .Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral)
    .Sum(piece => MaterialEvaluation.GetUnitValue(piece.Type));

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) => state.Pieces
    .Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral)
    .Select(piece => new CpuIntent(CpuIntentType.AttackTarget, 80f, TargetPieceId: piece.Id));
}

/// <summary>Campaign survival objective measured against the simulation's completed turn count.</summary>
public sealed class SurviveTurnsGoal(int turnsToSurvive) : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team) => state.Winner is NetworkTeam winner
    ? winner == team ? CpuGoalStatus.Completed : CpuGoalStatus.Failed
    : state.TurnNumber >= turnsToSurvive ? CpuGoalStatus.Completed : CpuGoalStatus.InProgress;

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) =>
    Math.Clamp(state.TurnNumber, 0, Math.Max(1, turnsToSurvive));

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) =>
    [new CpuIntent(CpuIntentType.ProtectRoyal, 70f, ExpiryTurn: state.TurnNumber + 1)];
}

/// <summary>Scores control of a supplied set of campaign locations.</summary>
public sealed class HoldLocationsGoal(IEnumerable<(int x, int y)> locations, int locationsRequired = 1) : ICpuScenarioGoal
{
  private readonly (int x, int y)[] _locations = locations.Distinct().ToArray();

  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team) => ControlledLocations(state, team) >= locationsRequired
    ? CpuGoalStatus.Completed
    : CpuGoalStatus.InProgress;

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) => ControlledLocations(state, team);

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) => _locations
    .Select(location => new CpuIntent(CpuIntentType.HoldLocation, 65f, TargetPosition: location, ExpiryTurn: state.TurnNumber + 1));

  private int ControlledLocations(CpuGameState state, NetworkTeam team) => _locations.Count(location =>
    state.Pieces.Any(piece => piece.Team == team && piece.AttachedToId is null &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) && piece.OccupiedSquares(rule).Contains(location)) &&
    !state.Pieces.Any(piece => piece.Team is not NetworkTeam.Neutral && piece.Team != team && piece.AttachedToId is null &&
      UnitRules.TryGet(piece.Type, out UnitRule rule) && piece.OccupiedSquares(rule).Contains(location)));
}

/// <summary>Escorts one stable-ID unit to a location while keeping it alive.</summary>
public sealed class EscortUnitGoal(string pieceId, (int x, int y) destination) : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team)
  {
    NetworkPiece? piece = state.Pieces.FirstOrDefault(candidate => candidate.Id == pieceId);
    if (piece is null)
    {
      return CpuGoalStatus.Failed;
    }
    return UnitRules.TryGet(piece.Type, out UnitRule rule) && piece.OccupiedSquares(rule).Contains(destination)
      ? CpuGoalStatus.Completed
      : CpuGoalStatus.InProgress;
  }

  public float EvaluateProgress(CpuGameState state, NetworkTeam team)
  {
    NetworkPiece? piece = state.Pieces.FirstOrDefault(candidate => candidate.Id == pieceId);
    return piece is null ? -1_000f : -(Math.Abs(piece.X - destination.x) + Math.Abs(piece.Y - destination.y));
  }

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) =>
    [new CpuIntent(CpuIntentType.EscortUnit, 90f, PieceId: pieceId, TargetPosition: destination, ExpiryTurn: state.TurnNumber + 2)];
}

/// <summary>Requires a protected unit to remain alive, optionally until an existing goal completes.</summary>
public sealed class ProtectUnitGoal(string pieceId) : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team) => state.Pieces.Any(piece => piece.Id == pieceId)
    ? CpuGoalStatus.InProgress
    : CpuGoalStatus.Failed;

  public float EvaluateProgress(CpuGameState state, NetworkTeam team)
  {
    NetworkPiece? piece = state.Pieces.FirstOrDefault(candidate => candidate.Id == pieceId);
    return piece?.Health ?? -1_000f;
  }

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) =>
    [new CpuIntent(CpuIntentType.DefendTarget, 100f, TargetPieceId: pieceId, ExpiryTurn: state.TurnNumber + 1)];
}

/// <summary>Campaign defence condition that fails when an enemy unit reaches a protected exit square.</summary>
public sealed class PreventEscapeGoal(IEnumerable<(int x, int y)> exits) : ICpuScenarioGoal
{
  private readonly HashSet<(int x, int y)> _exits = [.. exits];

  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team) => state.Pieces.Any(piece =>
    piece.Team != team && piece.Team != NetworkTeam.Neutral && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    piece.OccupiedSquares(rule).Any(_exits.Contains)) ? CpuGoalStatus.Failed : CpuGoalStatus.InProgress;

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) => state.Pieces.Count(piece =>
    piece.Team != team && piece.Team != NetworkTeam.Neutral && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    piece.OccupiedSquares(rule).Any(_exits.Contains)) * -1_000f;

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) => _exits
    .Select(exit => new CpuIntent(CpuIntentType.BlockRoute, 85f, TargetPosition: exit, ExpiryTurn: state.TurnNumber + 1));
}

public sealed class ScoreGoal(
  Func<CpuGameState, IReadOnlyDictionary<NetworkTeam, int>> scores,
  int targetScore,
  string name
) : ICpuScenarioGoal
{
  public CpuGoalStatus GetStatus(CpuGameState state, NetworkTeam team)
  {
    if (state.Winner is NetworkTeam winner)
    {
      return winner == team ? CpuGoalStatus.Completed : CpuGoalStatus.Failed;
    }

    return scores(state).GetValueOrDefault(team) >= targetScore ? CpuGoalStatus.Completed : CpuGoalStatus.InProgress;
  }

  public float EvaluateProgress(CpuGameState state, NetworkTeam team) => scores(state).GetValueOrDefault(team);

  public IEnumerable<CpuIntent> GenerateIntents(CpuGameState state, NetworkTeam team) =>
    [new CpuIntent(CpuIntentType.HoldLocation, 50f, TargetPosition: null, ExpiryTurn: state.TurnNumber + 1)];

  public override string ToString() => name;
}

internal static class CpuPieceExtensions
{
  internal static IEnumerable<(int x, int y)> OccupiedSquares(this NetworkPiece piece, UnitRule rule)
  {
    for (int y = 0; y < rule.Height; y++)
    {
      for (int x = 0; x < rule.Width; x++)
      {
        yield return (piece.X + x, piece.Y + y);
      }
    }
  }
}
