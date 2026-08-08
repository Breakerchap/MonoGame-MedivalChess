using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>A render-free, clonable snapshot used exclusively for CPU planning and headless simulation.</summary>
public sealed class CpuGameState
{
  private readonly NetworkPiece[] _pieces;
  private readonly Dictionary<NetworkTeam, CpuTeamState> _teams;
  private readonly Dictionary<NetworkTeam, int> _conquestScores;
  private readonly Dictionary<NetworkTeam, int> _modeScores;
  private readonly Dictionary<(int x, int y), NetworkTeam> _roads;
  private readonly Dictionary<(int x, int y), int> _barricades;
  private readonly Dictionary<(int x, int y), NetworkTeam> _mines;
  private readonly HashSet<TileEdge> _riverBridges;
  private readonly CpuMoveRecord[] _recentMoves;

  public NetworkMatchConfiguration Configuration { get; }
  public Board Board { get; }
  public BattlefieldTerrain Terrain { get; }
  public IReadOnlyList<NetworkPiece> Pieces => _pieces;
  public IReadOnlyDictionary<NetworkTeam, CpuTeamState> Teams => _teams;
  public NetworkTeam CurrentTurn { get; }
  public int TurnNumber { get; }
  public NetworkTeam? Winner { get; }
  public NetworkInitialBuyState? InitialBuy { get; }
  public int ConquestScore { get; }
  public IReadOnlyDictionary<NetworkTeam, int> ConquestScores => _conquestScores;
  public IReadOnlyDictionary<NetworkTeam, int> ModeScores => _modeScores;
  public (int x, int y)? TreasurePosition { get; }
  public string? TreasureCarrierId { get; }
  public IReadOnlyDictionary<(int x, int y), NetworkTeam> Roads => _roads;
  public IReadOnlyDictionary<(int x, int y), int> Barricades => _barricades;
  public IReadOnlyDictionary<(int x, int y), NetworkTeam> Mines => _mines;
  public IReadOnlySet<TileEdge> RiverBridges => _riverBridges;
  /// <summary>Recent completed moves used only to discourage immediately undoing a position.</summary>
  public IReadOnlyList<CpuMoveRecord> RecentMoves => _recentMoves;
  public CpuScenarioDefinition? Scenario { get; }

  public int ActionsRemaining => Teams.TryGetValue(CurrentTurn, out CpuTeamState? team)
    ? team.ActionsRemaining
    : 0;

  public bool IsFinished => Winner is not null || Scenario?.IsTerminal(this) == true;

  public CpuGameState(
    NetworkMatchConfiguration configuration,
    IEnumerable<NetworkPiece> pieces,
    IEnumerable<CpuTeamState> teams,
    NetworkTeam currentTurn,
    int turnNumber = 0,
    BattlefieldTerrain? terrain = null,
    NetworkTeam? winner = null,
    NetworkInitialBuyState? initialBuy = null,
    int conquestScore = 0,
    IEnumerable<KeyValuePair<NetworkTeam, int>>? conquestScores = null,
    IEnumerable<KeyValuePair<NetworkTeam, int>>? modeScores = null,
    (int x, int y)? treasurePosition = null,
    string? treasureCarrierId = null,
    IEnumerable<KeyValuePair<(int x, int y), NetworkTeam>>? roads = null,
    IEnumerable<KeyValuePair<(int x, int y), int>>? barricades = null,
    IEnumerable<KeyValuePair<(int x, int y), NetworkTeam>>? mines = null,
    IEnumerable<TileEdge>? riverBridges = null,
    CpuScenarioDefinition? scenario = null,
    IEnumerable<CpuMoveRecord>? recentMoves = null,
    Board? board = null
  )
  {
    Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    Board = board ?? BoardRules.GetBoard(configuration);
    Terrain = terrain ?? TerrainRules.Create(
      Board,
      configuration.TerrainSeed,
      configuration.ForestDensity,
      configuration.WaterwayDensity,
      configuration.PlayerCount
    );
    _pieces = pieces?.ToArray() ?? throw new ArgumentNullException(nameof(pieces));
    _teams = (teams ?? throw new ArgumentNullException(nameof(teams))).ToDictionary(team => team.Team);
    _conquestScores = conquestScores?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
    _modeScores = modeScores?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
    _roads = roads?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
    _barricades = barricades?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
    _mines = mines?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
    _riverBridges = riverBridges is null ? [] : [.. riverBridges];
    _recentMoves = NormaliseRecentMoves(recentMoves ?? []);
    CurrentTurn = currentTurn;
    TurnNumber = Math.Max(0, turnNumber);
    Winner = winner;
    InitialBuy = initialBuy;
    ConquestScore = conquestScore;
    TreasurePosition = treasurePosition;
    TreasureCarrierId = treasureCarrierId;
    Scenario = scenario;
  }

  /// <summary>Creates a planning snapshot from an authoritative online state.</summary>
  public static CpuGameState FromNetworkState(
    NetworkGameState state,
    int turnNumber = 0,
    CpuScenarioDefinition? scenario = null,
    IEnumerable<CpuMoveRecord>? recentMoves = null
  )
  {
    ArgumentNullException.ThrowIfNull(state);
    Dictionary<(int x, int y), int> barricades = [];
    Dictionary<(int x, int y), NetworkTeam> mines = [];
    Dictionary<(int x, int y), NetworkTeam> roads = [];
    HashSet<TileEdge> bridges = [];
    foreach (NetworkImprovement improvement in state.Improvements ?? [])
    {
      switch (improvement.Type)
      {
        case "Road": roads[(improvement.X, improvement.Y)] = improvement.Owner ?? NetworkTeam.Neutral; break;
        case "Barrier": barricades[(improvement.X, improvement.Y)] = improvement.Health; break;
        case "Mine" when improvement.Owner is NetworkTeam owner: mines[(improvement.X, improvement.Y)] = owner; break;
        case "Bridge": bridges.Add(TileEdge.Between((improvement.X, improvement.Y), (improvement.X + 1, improvement.Y))); break;
      }
    }

    NetworkTreasureState? treasure = state.Treasure;
    return new CpuGameState(
      state.Configuration,
      state.Pieces,
      state.Teams.Select(team => new CpuTeamState(team.Team, team.Money, team.ActionsRemaining, team.ChosenRoyal)),
      state.CurrentTurn,
      turnNumber,
      winner: state.Winner,
      initialBuy: state.InitialBuy,
      conquestScore: state.ConquestScore,
      conquestScores: (state.ConquestScores ?? []).Select(score => KeyValuePair.Create(score.Team, score.Score)),
      modeScores: (state.ModeScores ?? []).Select(score => KeyValuePair.Create(score.Team, score.Score)),
      treasurePosition: treasure?.X is int treasureX && treasure.Y is int treasureY ? (treasureX, treasureY) : null,
      treasureCarrierId: treasure?.CarrierId,
      roads: roads,
      barricades: barricades,
      mines: mines,
      riverBridges: bridges,
      scenario: scenario,
      recentMoves: recentMoves
    );
  }

  /// <summary>Creates a deep snapshot suitable for independent planning branches.</summary>
  public CpuGameState Clone() => new(
    Configuration,
    _pieces,
    _teams.Values,
    CurrentTurn,
    TurnNumber,
    Terrain,
    Winner,
    InitialBuy,
    ConquestScore,
    _conquestScores,
    _modeScores,
    TreasurePosition,
    TreasureCarrierId,
    _roads,
    _barricades,
    _mines,
    _riverBridges,
    Scenario,
    _recentMoves,
    Board
  );

  internal CpuMutableGameState ToMutable() => new(this);

  /// <summary>
  /// Keeps only the most recent bounded movement history, but stores independent piece histories
  /// in a canonical order.  Search branches that reach the same board through commuting moves can
  /// then share a state hash without losing the per-piece information needed for reversal scoring.
  /// </summary>
  internal static CpuMoveRecord[] NormaliseRecentMoves(IEnumerable<CpuMoveRecord> moves) => moves
    .OrderBy(move => move.TurnNumber)
    .TakeLast(CpuMoveRecord.MaximumEntries)
    .OrderBy(move => move.Team)
    .ThenBy(move => move.PieceId, StringComparer.Ordinal)
    .ThenBy(move => move.TurnNumber)
    .ThenBy(move => move.FromY).ThenBy(move => move.FromX)
    .ThenBy(move => move.ToY).ThenBy(move => move.ToX)
    .ToArray();
}

/// <summary>Gameplay data owned by one team in a CPU simulation.</summary>
/// <summary>CPU-side team economy and per-turn action budget. Campaigns may override the normal limit per team.</summary>
public sealed record CpuTeamState(
  NetworkTeam Team,
  int Money,
  int ActionsRemaining,
  string? ChosenRoyal = null,
  int ActionLimit = MatchRules.ActionsPerTurn
);

/// <summary>Compact, bounded move history retained in snapshots so repetition scoring is deterministic.</summary>
public sealed record CpuMoveRecord(
  NetworkTeam Team,
  string PieceId,
  int FromX,
  int FromY,
  int ToX,
  int ToY,
  int TurnNumber
)
{
  public const int MaximumEntries = 8;

  public bool Reverses(CpuMoveRecord earlier) =>
    Team == earlier.Team && PieceId == earlier.PieceId &&
    FromX == earlier.ToX && FromY == earlier.ToY && ToX == earlier.FromX && ToY == earlier.FromY;
}

internal sealed class CpuMutableGameState
{
  internal CpuMutableGameState(CpuGameState source)
  {
    Source = source;
    Pieces = [.. source.Pieces];
    Teams = source.Teams.ToDictionary(pair => pair.Key, pair => pair.Value);
    ConquestScores = source.ConquestScores.ToDictionary(pair => pair.Key, pair => pair.Value);
    ModeScores = source.ModeScores.ToDictionary(pair => pair.Key, pair => pair.Value);
    Roads = source.Roads.ToDictionary(pair => pair.Key, pair => pair.Value);
    Barricades = source.Barricades.ToDictionary(pair => pair.Key, pair => pair.Value);
    Mines = source.Mines.ToDictionary(pair => pair.Key, pair => pair.Value);
    RiverBridges = [.. source.RiverBridges];
    CurrentTurn = source.CurrentTurn;
    TurnNumber = source.TurnNumber;
    Winner = source.Winner;
    InitialBuy = source.InitialBuy;
    ConquestScore = source.ConquestScore;
    TreasurePosition = source.TreasurePosition;
    TreasureCarrierId = source.TreasureCarrierId;
    RecentMoves = source.RecentMoves.ToArray();
  }

  internal CpuGameState Source { get; }
  internal List<NetworkPiece> Pieces { get; }
  internal Dictionary<NetworkTeam, CpuTeamState> Teams { get; }
  internal Dictionary<NetworkTeam, int> ConquestScores { get; }
  internal Dictionary<NetworkTeam, int> ModeScores { get; }
  internal Dictionary<(int x, int y), NetworkTeam> Roads { get; }
  internal Dictionary<(int x, int y), int> Barricades { get; }
  internal Dictionary<(int x, int y), NetworkTeam> Mines { get; }
  internal HashSet<TileEdge> RiverBridges { get; }
  internal NetworkTeam CurrentTurn { get; set; }
  internal int TurnNumber { get; set; }
  internal NetworkTeam? Winner { get; set; }
  internal NetworkInitialBuyState? InitialBuy { get; set; }
  internal int ConquestScore { get; set; }
  internal (int x, int y)? TreasurePosition { get; set; }
  internal string? TreasureCarrierId { get; set; }
  internal CpuMoveRecord[] RecentMoves { get; private set; }

  internal void RecordMove(NetworkTeam team, string pieceId, int fromX, int fromY, int toX, int toY)
  {
    CpuMoveRecord move = new(team, pieceId, fromX, fromY, toX, toY, TurnNumber);
    RecentMoves = CpuGameState.NormaliseRecentMoves(RecentMoves.Append(move));
  }

  internal CpuGameState Freeze() => new(
    Source.Configuration,
    Pieces,
    Teams.Values,
    CurrentTurn,
    TurnNumber,
    Source.Terrain,
    Winner,
    InitialBuy,
    ConquestScore,
    ConquestScores,
    ModeScores,
    TreasurePosition,
    TreasureCarrierId,
    Roads,
    Barricades,
    Mines,
    RiverBridges,
    Source.Scenario,
    RecentMoves,
    Source.Board
  );
}
