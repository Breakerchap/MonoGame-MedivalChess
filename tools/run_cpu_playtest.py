from pathlib import Path

root = Path('CpuPlaytestTemp')
root.mkdir(exist_ok=True)
(root / 'CpuPlaytestTemp.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../MedivalChess.CPU/MedivalChess.CPU.csproj" />
    <ProjectReference Include="../MedivalChess.Shared/MedivalChess.Shared.csproj" />
  </ItemGroup>
</Project>
''')

(root / 'Program.cs').write_text(r'''using MedivalChess.CPU;
using MedivalChess.Shared;

Globals.ActionLimitsEnabled = false;
Console.WriteLine("=== ADVERSARIAL CPU PLAYTEST ===");
Console.WriteLine($"Hard budget: {CpuProfile.Hard().Search.MaxSearchMilliseconds}ms");

RunProbe("adjacent-knight-conquest", CreateState("Conquest", 7001, 500,
  P("red-king", "King", NetworkTeam.Red, 0, 0),
  P("blue-knight", "Knight", NetworkTeam.Blue, 0, 1),
  P("blue-king", "King", NetworkTeam.Blue, 0, -6)));

RunProbe("move-then-attack", CreateState("Regicide", 7002, 0,
  P("red-knight", "Knight", NetworkTeam.Red, 0, 3),
  P("red-king", "King", NetworkTeam.Red, 3, 7),
  P("blue-archer", "Archer", NetworkTeam.Blue, 0, -2),
  P("blue-king", "King", NetworkTeam.Blue, 3, -7)));

RunProbe("two-independent-attacks", CreateState("Regicide", 7003, 0,
  P("red-left", "Soldier", NetworkTeam.Red, -2, 0),
  P("red-right", "Soldier", NetworkTeam.Red, 2, 0),
  P("red-king", "King", NetworkTeam.Red, 0, 7),
  P("blue-left", "Knight", NetworkTeam.Blue, -2, -1),
  P("blue-right", "Knight", NetworkTeam.Blue, 2, -1),
  P("blue-king", "King", NetworkTeam.Blue, 0, -7)));

RunProbe("develop-whole-army", CreateState("Regicide", 7004, 0,
  P("red-soldier", "Soldier", NetworkTeam.Red, 0, 5),
  P("red-defender", "Defender", NetworkTeam.Red, 2, 5),
  P("red-archer", "Archer", NetworkTeam.Red, -2, 5),
  P("red-king", "King", NetworkTeam.Red, 0, 8),
  P("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -4),
  P("blue-king", "King", NetworkTeam.Blue, 0, -8)));

RunGame("Regicide", NetworkTeam.Red, 8101, 12);
RunGame("Regicide", NetworkTeam.Blue, 8102, 12);
RunGame("Conquest", NetworkTeam.Red, 8103, 12);

static void RunProbe(string name, CpuGameState state)
{
  Console.WriteLine($"\n--- PROBE {name} ---");
  CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Hard(97), CancellationToken.None);
  Console.WriteLine($"PLAN|{name}|ms={plan.Report.SearchTime.TotalMilliseconds:F0}|depth={plan.Report.CompletedSearchDepth}|nodes={plan.Report.NodesEvaluated}|{string.Join(" ; ", plan.Actions.Select(a => a.Describe()))}");
  InspectCpuPlan(name, state, NetworkTeam.Red, plan);
}

static void RunGame(string mode, NetworkTeam cpuTeam, int seed, int maxTurns)
{
  Console.WriteLine($"\n=== GAME {mode} CPU={cpuTeam} seed={seed} ===");
  CpuGameState state = CreateBattle(mode, seed);
  CpuPlayer hard = new();
  AggressivePlayer human = new();
  List<string> cpuPurchases = [];
  int cpuDamage = 0;
  int humanDamage = 0;

  for (int turn = 0; turn < maxTurns && !state.IsFinished; turn++)
  {
    NetworkTeam team = state.CurrentTurn;
    bool cpu = team == cpuTeam;
    CpuTurnPlan plan = cpu
      ? hard.ChooseTurn(state, team, CpuProfile.Hard(seed + turn), CancellationToken.None)
      : human.ChooseTurn(state, team, CpuProfile.Hard(seed + turn), CancellationToken.None);

    Console.WriteLine($"TURN|{turn + 1}|{team}|{(cpu ? "CPU" : "AGGRO")}|ms={plan.Report.SearchTime.TotalMilliseconds:F0}|depth={plan.Report.CompletedSearchDepth}|{string.Join(" ; ", plan.Actions.Select(a => a.Describe()))}");
    if (cpu) InspectCpuPlan($"{mode}-t{turn + 1}", state, team, plan);

    CpuGameState current = state;
    foreach (ICpuGameAction action in plan.Actions)
    {
      if (current.IsFinished || current.CurrentTurn != team) break;
      if (!action.IsLegal(current))
      {
        Console.WriteLine($"ISSUE|ILLEGAL_PLAN|{mode}-t{turn + 1}|{action.Describe()}");
        break;
      }
      int enemyHpBefore = current.Pieces.Where(p => p.Team != team && p.Team != NetworkTeam.Neutral).Sum(p => p.Health);
      current = action.Apply(current);
      int enemyHpAfter = current.Pieces.Where(p => p.Team != team && p.Team != NetworkTeam.Neutral).Sum(p => p.Health);
      int dealt = Math.Max(0, enemyHpBefore - enemyHpAfter);
      if (cpu) cpuDamage += dealt; else humanDamage += dealt;
      if (cpu && action is PurchaseAction purchase) cpuPurchases.Add(purchase.UnitType);
    }
    if (!current.IsFinished && current.CurrentTurn == team)
    {
      EndTurnAction end = new(team);
      if (end.IsLegal(current)) current = end.Apply(current);
    }
    state = current;
  }

  var cpuPieces = state.Pieces.Where(p => p.Team == cpuTeam).GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}");
  var humanTeam = cpuTeam == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
  var humanPieces = state.Pieces.Where(p => p.Team == humanTeam).GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}");
  Console.WriteLine($"SUMMARY|{mode}|CPU={cpuTeam}|winner={state.Winner?.ToString() ?? "none"}|cpuDamage={cpuDamage}|aggroDamage={humanDamage}|cpuUnits={string.Join(',', cpuPieces)}|aggroUnits={string.Join(',', humanPieces)}|purchases={string.Join(',', cpuPurchases)}");

  if (cpuPurchases.Count >= 3)
  {
    int cheapScreens = cpuPurchases.Count(p => p is "Peasant" or "Defender");
    if (cheapScreens * 2 > cpuPurchases.Count)
      Console.WriteLine($"ISSUE|CHEAP_SCREEN_PURCHASE_MAJORITY|{mode}|{cheapScreens}/{cpuPurchases.Count}|{string.Join(',', cpuPurchases)}");
  }
}

static void InspectCpuPlan(string label, CpuGameState initial, NetworkTeam team, CpuTurnPlan plan)
{
  CpuActionGenerator generator = new();
  HashSet<string> startAttackers = generator.GenerateLegalActions(initial, team).OfType<AttackAction>()
    .Select(a => a.AttackerId).ToHashSet(StringComparer.Ordinal);
  HashSet<string> moveAttackCapable = FindMoveAttackCapablePieces(initial, team);
  HashSet<string> attacked = [];
  CpuGameState current = initial;

  foreach (ICpuGameAction action in plan.Actions)
  {
    if (!action.IsLegal(current)) break;
    if (action is AttackAction attack) attacked.Add(attack.AttackerId);
    if (action is EndTurnAction)
    {
      ReportBeforeEnd(label, current, team, startAttackers, moveAttackCapable, attacked, generator);
    }
    current = action.Apply(current);
  }

  if (!plan.Actions.Any(a => a is EndTurnAction) && current.CurrentTurn == team && !current.IsFinished)
    ReportBeforeEnd(label, current, team, startAttackers, moveAttackCapable, attacked, generator);
}

static void ReportBeforeEnd(
  string label,
  CpuGameState current,
  NetworkTeam team,
  HashSet<string> startAttackers,
  HashSet<string> moveAttackCapable,
  HashSet<string> attacked,
  CpuActionGenerator generator)
{
  AttackAction[] remainingAttacks = generator.GenerateLegalActions(current, team).OfType<AttackAction>().ToArray();
  if (remainingAttacks.Length > 0)
    Console.WriteLine($"ISSUE|ENDS_WITH_ATTACK_AVAILABLE|{label}|{string.Join(',', remainingAttacks.Select(a => a.AttackerId).Distinct())}");

  foreach (string id in startAttackers.Where(id => !attacked.Contains(id)))
    Console.WriteLine($"ISSUE|STARTED_WITH_ATTACK_BUT_NEVER_ATTACKED|{label}|{id}");

  foreach (string id in moveAttackCapable.Where(id => !attacked.Contains(id)))
    Console.WriteLine($"ISSUE|MOVE_ATTACK_OPPORTUNITY_UNUSED|{label}|{id}");

  var legalMoves = generator.GenerateLegalActions(current, team).OfType<MoveAction>().ToArray();
  foreach (NetworkPiece piece in current.Pieces.Where(p => p.Team == team && p.AttachedToId is null && !p.HasMovedThisTurn && !p.HasAttackedThisTurn))
  {
    if (!UnitRules.TryGet(piece.Type, out UnitRule rule) || rule.MoveRange <= 0 || rule.Type == "Farm") continue;
    if (legalMoves.Any(m => m.PieceId == piece.Id))
      Console.WriteLine($"ISSUE|IDLE_MOBILE_UNIT|{label}|{piece.Id}:{piece.Type}");
  }

  NetworkTeam enemy = team == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
  CpuThreatMap enemyThreats = new CpuThreatMapBuilder().Build(current, enemy);
  foreach (NetworkPiece royal in current.Pieces.Where(p => p.Team == team && UnitRules.TryGet(p.Type, out UnitRule r) && r.Category == RuleCategory.Royal))
  {
    CpuPieceThreat? threat = enemyThreats.GetThreat(royal.Id);
    if (threat?.IsLethal == true)
      Console.WriteLine($"ISSUE|ROYAL_LEFT_IN_LETHAL_THREAT|{label}|{royal.Id}|damage={threat.TotalExpectedDamage}|hp={royal.Health}");
  }
}

static HashSet<string> FindMoveAttackCapablePieces(CpuGameState state, NetworkTeam team)
{
  CpuActionGenerator generator = new();
  HashSet<string> result = [];
  foreach (MoveAction move in generator.GenerateLegalActions(state, team).OfType<MoveAction>())
  {
    CpuGameState moved = move.Apply(state);
    if (generator.GenerateLegalActions(moved, team).OfType<AttackAction>().Any(a => a.AttackerId == move.PieceId))
      result.Add(move.PieceId);
  }
  return result;
}

static CpuGameState CreateBattle(string mode, int seed)
{
  return CreateState(mode, seed, 130,
    P("red-king", "King", NetworkTeam.Red, 0, 8),
    P("red-knight", "Knight", NetworkTeam.Red, -2, 5),
    P("red-archer", "Archer", NetworkTeam.Red, 2, 5),
    P("red-soldier", "Soldier", NetworkTeam.Red, 0, 4),
    P("red-defender", "Defender", NetworkTeam.Red, -1, 6),
    P("blue-king", "King", NetworkTeam.Blue, 0, -8),
    P("blue-knight", "Knight", NetworkTeam.Blue, 2, -5),
    P("blue-archer", "Archer", NetworkTeam.Blue, -2, -5),
    P("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -4),
    P("blue-defender", "Defender", NetworkTeam.Blue, 1, -6));
}

static CpuGameState CreateState(string mode, int seed, int money, params NetworkPiece[] pieces)
{
  NetworkMatchConfiguration config = new(
    "Small", "None", "None", mode, seed, money, 0f, 0f, 2, 1, 15,
    FarmsEnabled: false, UnitMaintenanceEnabled: false);
  return new CpuGameState(
    config,
    pieces,
    [
      new CpuTeamState(NetworkTeam.Red, money, MatchRules.ActionsPerTurn),
      new CpuTeamState(NetworkTeam.Blue, money, MatchRules.ActionsPerTurn)
    ],
    NetworkTeam.Red,
    terrain: new BattlefieldTerrain(),
    scenario: CpuScenarioDefinition.ForMatch(config));
}

static NetworkPiece P(string id, string type, NetworkTeam team, int x, int y)
{
  UnitRule rule = UnitRules.GetRequired(type);
  return new NetworkPiece(id, type, team, x, y, rule.Health);
}

sealed class AggressivePlayer : ICpuPlayer
{
  private readonly CpuActionGenerator _generator = new();

  public CpuTurnPlan ChooseTurn(CpuGameState state, NetworkTeam team, CpuProfile profile, CancellationToken cancellationToken)
  {
    List<ICpuGameAction> actions = [];
    CpuGameState current = state;
    int purchases = 0;

    for (int step = 0; step < 40 && !current.IsFinished && current.CurrentTurn == team; step++)
    {
      IReadOnlyList<ICpuGameAction> legal = _generator.GenerateLegalActions(current, team);
      AttackAction? attack = legal.OfType<AttackAction>()
        .OrderByDescending(a => ScoreAttack(current, a))
        .ThenBy(a => a.Describe(), StringComparer.Ordinal)
        .FirstOrDefault();
      if (attack is not null)
      {
        actions.Add(attack);
        current = attack.Apply(current);
        continue;
      }

      MoveAction? move = legal.OfType<MoveAction>()
        .Select(m => (move: m, score: ScoreMove(current, m)))
        .Where(x => x.score > 1f)
        .OrderByDescending(x => x.score)
        .ThenBy(x => x.move.Describe(), StringComparer.Ordinal)
        .Select(x => x.move)
        .FirstOrDefault();
      if (move is not null)
      {
        actions.Add(move);
        current = move.Apply(current);
        continue;
      }

      if (purchases < 1)
      {
        PurchaseAction? purchase = legal.OfType<PurchaseAction>()
          .Where(p => p.UnitType is "Knight" or "Crossbowman" or "Archer" or "Soldier" or "Spearman" or "Defender")
          .Select(p => (purchase: p, score: ScorePurchase(current, p)))
          .OrderByDescending(x => x.score)
          .ThenBy(x => x.purchase.Describe(), StringComparer.Ordinal)
          .Select(x => x.purchase)
          .FirstOrDefault();
        if (purchase is not null)
        {
          actions.Add(purchase);
          current = purchase.Apply(current);
          purchases++;
          continue;
        }
      }
      break;
    }

    EndTurnAction end = new(team);
    if (!current.IsFinished && current.CurrentTurn == team && end.IsLegal(current)) actions.Add(end);
    return new CpuTurnPlan(actions, 0f, new CpuDecisionReport
    {
      ProfileName = "Aggressive scripted opponent",
      Difficulty = CpuDifficultyLevel.Hard,
      SearchTime = TimeSpan.Zero
    });
  }

  private static float ScoreAttack(CpuGameState state, AttackAction attack)
  {
    NetworkPiece? attacker = state.Pieces.FirstOrDefault(p => p.Id == attack.AttackerId);
    NetworkPiece? target = attack.TargetPieceId is null ? null : state.Pieces.FirstOrDefault(p => p.Id == attack.TargetPieceId);
    if (attacker is null) return -10000f;
    if (target is null) return 10f;
    UnitRule rule = UnitRules.GetRequired(target.Type);
    int damage = CpuGameRules.EstimateAttackDamage(state, attacker, target);
    float score = damage * 4f + rule.Cost * 1.5f;
    if (damage >= target.Health) score += 180f + rule.Cost * 2f;
    if (rule.Category == RuleCategory.Royal) score += state.Configuration.GameMode == "Regicide" ? 1000f : 80f;
    if (target.Type == "Farm") score += 120f;
    return score;
  }

  private static float ScoreMove(CpuGameState state, MoveAction move)
  {
    NetworkPiece before = state.Pieces.First(p => p.Id == move.PieceId);
    CpuGameState result = move.Apply(state);
    NetworkPiece? after = result.Pieces.FirstOrDefault(p => p.Id == move.PieceId);
    if (after is null) return -1000f;

    float score = (TargetDistance(state, before) - TargetDistance(result, after)) * 18f;
    NetworkPiece[] enemies = result.Pieces.Where(p => p.Team != move.Team && p.Team != NetworkTeam.Neutral && p.AttachedToId is null).ToArray();
    if (enemies.Any(e => CpuGameRules.CanDirectlyAttack(result, after, e))) score += 260f;

    NetworkTeam enemyTeam = move.Team == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
    CpuPieceThreat? threat = new CpuThreatMapBuilder().Build(result, enemyTeam).GetThreat(after.Id);
    if (threat?.IsLethal == true) score -= 240f;
    else if (threat is not null) score -= threat.TotalExpectedDamage * 3f;
    return score;
  }

  private static float TargetDistance(CpuGameState state, NetworkPiece piece)
  {
    if (state.Configuration.GameMode == "Conquest")
    {
      return state.Board.Cells.Where(s => MatchRules.IsConquestSquare(state.Board, s))
        .Select(s => (float)(Math.Abs(piece.X - s.x) + Math.Abs(piece.Y - s.y)))
        .DefaultIfEmpty(20f).Min();
    }
    NetworkPiece? royal = state.Pieces.FirstOrDefault(p => p.Team != piece.Team && p.Team != NetworkTeam.Neutral &&
      UnitRules.TryGet(p.Type, out UnitRule r) && r.Category == RuleCategory.Royal);
    return royal is null ? 20f : Math.Abs(piece.X - royal.X) + Math.Abs(piece.Y - royal.Y);
  }

  private static float ScorePurchase(CpuGameState state, PurchaseAction purchase)
  {
    UnitRule rule = UnitRules.GetRequired(purchase.UnitType);
    NetworkPiece prototype = new("purchase", purchase.UnitType, purchase.Team, purchase.X, purchase.Y, rule.Health, true, true);
    float score = rule.Attack * 5f + rule.MoveRange * 8f + rule.AttackRange * 7f - rule.Cost * 0.3f;
    score -= TargetDistance(state, prototype) * 3f;
    return score;
  }
}
''')
