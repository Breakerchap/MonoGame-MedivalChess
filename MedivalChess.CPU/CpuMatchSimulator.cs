using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Configuration for a render-free CPU-versus-CPU playtest run.</summary>
public sealed class CpuMatchSimulationRequest
{
  public required CpuGameState InitialState { get; init; }
  public IReadOnlyDictionary<NetworkTeam, CpuProfile>? Profiles { get; init; }
  public int MaximumTurns { get; init; } = 200;
  public CancellationToken CancellationToken { get; init; }
}

/// <summary>Aggregate metrics for one team in a headless CPU match.</summary>
public sealed class CpuMatchTeamMetrics
{
  public int UnitsPurchased { get; internal set; }
  public int UnitsLost { get; internal set; }
  public int DamageDealt { get; internal set; }
  public int GoldEarned { get; internal set; }
  public int GoldSpent { get; internal set; }
}

/// <summary>One planned CPU turn retained for playtest diagnostics.</summary>
public sealed record CpuSimulatedTurn(
  NetworkTeam Team,
  IReadOnlyList<string> Actions,
  CpuDecisionReport Decision,
  bool ReachedTerminalState
);

/// <summary>Result and balance metrics from a bounded render-free CPU match.</summary>
public sealed class CpuMatchSimulationReport
{
  public string ScenarioId { get; init; } = "match";
  public string BoardId { get; init; } = string.Empty;
  public IReadOnlyDictionary<NetworkTeam, string> Profiles { get; init; } = new Dictionary<NetworkTeam, string>();
  public NetworkTeam? Winner { get; init; }
  public int TurnCount { get; init; }
  public string EndReason { get; init; } = string.Empty;
  public IReadOnlyDictionary<NetworkTeam, CpuMatchTeamMetrics> TeamMetrics { get; init; } =
    new Dictionary<NetworkTeam, CpuMatchTeamMetrics>();
  public IReadOnlyDictionary<NetworkTeam, IReadOnlyList<string>> ObjectivesCompleted { get; init; } =
    new Dictionary<NetworkTeam, IReadOnlyList<string>>();
  public TimeSpan AverageSearchTime { get; init; }
  public double AverageSearchNodesPerTurn { get; init; }
  public IReadOnlyList<CpuSimulatedTurn> Turns { get; init; } = [];
}

/// <summary>
/// Runs the CPU simulation without MonoGame, UI, or animation. It is for deterministic balance
/// playtests only; it does not train or mutate profiles.
/// </summary>
public sealed class CpuMatchSimulator
{
  private readonly ICpuPlayer _player;

  public CpuMatchSimulator(ICpuPlayer? player = null)
  {
    _player = player ?? new CpuPlayer();
  }

  public CpuMatchSimulationReport Run(CpuMatchSimulationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.InitialState);
    CpuGameState state = request.InitialState.Clone();
    IReadOnlyList<NetworkTeam> teams = TeamRules.GetActiveTeams(state.Configuration.PlayerCount);
    Dictionary<NetworkTeam, CpuProfile> profiles = teams.ToDictionary(team => team, team => ResolveProfile(state, team, request.Profiles));
    Dictionary<NetworkTeam, CpuMatchTeamMetrics> metrics = teams.ToDictionary(team => team, _ => new CpuMatchTeamMetrics());
    List<CpuSimulatedTurn> turns = [];
    long totalSearchTicks = 0;
    long totalSearchNodes = 0;
    int completedTurns = 0;
    string endReason = string.Empty;

    while (!state.IsFinished && completedTurns < Math.Max(1, request.MaximumTurns) && !request.CancellationToken.IsCancellationRequested)
    {
      NetworkTeam team = state.CurrentTurn;
      CpuTurnPlan plan = _player.ChooseTurn(state, team, profiles[team], request.CancellationToken);
      totalSearchTicks += plan.Report.SearchTime.Ticks;
      totalSearchNodes += plan.Report.NodesEvaluated;
      List<string> actionDescriptions = [];
      CpuGameState beforeTurn = state;

      foreach (ICpuGameAction action in plan.Actions)
      {
        if (state.IsFinished || state.CurrentTurn != team)
        {
          break;
        }
        if (!action.IsLegal(state))
        {
          throw new InvalidOperationException($"CPU planned an illegal headless action: {action.Describe()}");
        }

        CpuGameState next = action.Apply(state);
        RecordActionMetrics(state, next, action, metrics);
        state = next;
        actionDescriptions.Add(action.Describe());
      }

      if (!state.IsFinished && state.CurrentTurn == team)
      {
        EndTurnAction endTurn = new(team);
        if (endTurn.IsLegal(state))
        {
          CpuGameState next = endTurn.Apply(state);
          RecordActionMetrics(state, next, endTurn, metrics);
          state = next;
          actionDescriptions.Add(endTurn.Describe());
        }
        else if (ReferenceEquals(beforeTurn, state) || actionDescriptions.Count == 0)
        {
          endReason = "No legal action could advance the current turn.";
          turns.Add(new CpuSimulatedTurn(team, actionDescriptions, plan.Report, false));
          break;
        }
      }

      completedTurns++;
      turns.Add(new CpuSimulatedTurn(team, actionDescriptions, plan.Report, state.IsFinished));
    }

    if (string.IsNullOrEmpty(endReason))
    {
      endReason = request.CancellationToken.IsCancellationRequested
        ? "Simulation cancelled."
        : state.Winner is not null
          ? "A team won."
          : state.IsFinished
            ? "A scenario goal ended the match."
            : completedTurns >= Math.Max(1, request.MaximumTurns)
              ? "Turn limit reached."
              : "Simulation stopped.";
    }

    int searchCount = turns.Count;
    return new CpuMatchSimulationReport
    {
      ScenarioId = state.Scenario?.Id ?? "match",
      BoardId = state.Configuration.BoardSize,
      Profiles = profiles.ToDictionary(pair => pair.Key, pair => pair.Value.Name),
      Winner = state.Winner,
      TurnCount = completedTurns,
      EndReason = endReason,
      TeamMetrics = metrics,
      ObjectivesCompleted = GetCompletedObjectives(state, teams),
      AverageSearchTime = searchCount == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(totalSearchTicks / searchCount),
      AverageSearchNodesPerTurn = searchCount == 0 ? 0d : totalSearchNodes / (double)searchCount,
      Turns = turns
    };
  }

  private static CpuProfile ResolveProfile(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyDictionary<NetworkTeam, CpuProfile>? requestedProfiles
  )
  {
    if (requestedProfiles is not null && requestedProfiles.TryGetValue(team, out CpuProfile? requested))
    {
      return requested;
    }
    if (state.Scenario?.TeamProfiles.TryGetValue(team, out CpuProfile? campaignProfile) == true)
    {
      return campaignProfile;
    }
    return CpuProfile.Normal(state.Configuration.TerrainSeed + (int)team);
  }

  private static void RecordActionMetrics(
    CpuGameState before,
    CpuGameState after,
    ICpuGameAction action,
    IReadOnlyDictionary<NetworkTeam, CpuMatchTeamMetrics> metrics
  )
  {
    if (action is PurchaseAction)
    {
      metrics[action.Team].UnitsPurchased++;
    }

    foreach (NetworkTeam team in metrics.Keys)
    {
      int beforeMoney = before.Teams.GetValueOrDefault(team)?.Money ?? 0;
      int afterMoney = after.Teams.GetValueOrDefault(team)?.Money ?? 0;
      int change = afterMoney - beforeMoney;
      if (change > 0) metrics[team].GoldEarned += change;
      if (change < 0) metrics[team].GoldSpent -= change;
    }

    Dictionary<string, NetworkPiece> afterPieces = after.Pieces.ToDictionary(piece => piece.Id, StringComparer.Ordinal);
    foreach (NetworkPiece piece in before.Pieces)
    {
      if (!afterPieces.TryGetValue(piece.Id, out NetworkPiece? next))
      {
        if (metrics.TryGetValue(piece.Team, out CpuMatchTeamMetrics? lossMetrics))
        {
          lossMetrics.UnitsLost++;
        }
        if (piece.Team != action.Team && metrics.TryGetValue(action.Team, out CpuMatchTeamMetrics? attackMetrics))
        {
          attackMetrics.DamageDealt += piece.Health;
        }
      }
      else if (piece.Team != action.Team && next.Health < piece.Health && metrics.TryGetValue(action.Team, out CpuMatchTeamMetrics? attackMetrics))
      {
        attackMetrics.DamageDealt += piece.Health - next.Health;
      }
    }
  }

  private static IReadOnlyDictionary<NetworkTeam, IReadOnlyList<string>> GetCompletedObjectives(
    CpuGameState state,
    IEnumerable<NetworkTeam> teams
  )
  {
    IReadOnlyList<ICpuScenarioGoal> goals = state.Scenario?.VictoryGoals ?? [];
    return teams.ToDictionary(
      team => team,
      team => (IReadOnlyList<string>)goals.Where(goal => goal.GetStatus(state, team) == CpuGoalStatus.Completed)
        .Select(goal => goal.GetType().Name).ToArray()
    );
  }
}
