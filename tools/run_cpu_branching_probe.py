from pathlib import Path
root = Path('CpuBranchProbeTemp')
root.mkdir(exist_ok=True)
(root/'CpuBranchProbeTemp.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="../MedivalChess.CPU/MedivalChess.CPU.csproj"/><ProjectReference Include="../MedivalChess.Shared/MedivalChess.Shared.csproj"/></ItemGroup></Project>''')
(root/'Program.cs').write_text(r'''using MedivalChess.CPU;
using MedivalChess.Shared;
Globals.ActionLimitsEnabled = false;
foreach (int money in new[] { 0, 130 })
{
  CpuGameState state = Create(money);
  CpuActionGenerator gen = new();
  var searchActions = gen.GenerateSearchActions(state, NetworkTeam.Red, 96);
  Console.WriteLine($"ROOT|money={money}|all={searchActions.Count}|moves={searchActions.OfType<MoveAction>().Count()}|attacks={searchActions.OfType<AttackAction>().Count()}|purchases={searchActions.OfType<PurchaseAction>().Count()}|abilities={searchActions.OfType<UseAbilityAction>().Count()}");
  CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Hard(444), CancellationToken.None);
  CpuGameState current = state;
  foreach (var action in plan.Actions)
  {
    if (action is EndTurnAction) break;
    if (action.IsLegal(current)) current = action.Apply(current);
  }
  int idle = current.Pieces.Count(p => p.Team == NetworkTeam.Red && p.AttachedToId is null && !p.HasMovedThisTurn && !p.HasAttackedThisTurn && UnitRules.TryGet(p.Type, out UnitRule r) && r.MoveRange > 0);
  Console.WriteLine($"RESULT|money={money}|ms={plan.Report.SearchTime.TotalMilliseconds:F0}|depth={plan.Report.CompletedSearchDepth}|nodes={plan.Report.NodesEvaluated}|idle={idle}|{string.Join(" ; ", plan.Actions.Select(a => a.Describe()))}");
}

static CpuGameState Create(int money)
{
  NetworkMatchConfiguration c = new("Small", "None", "None", "Regicide", 9911, money, 0f, 0f, 2, 1, 15, FarmsEnabled:false, UnitMaintenanceEnabled:false);
  return new CpuGameState(c,
    new[]{ P("red-king","King",NetworkTeam.Red,0,8), P("red-knight","Knight",NetworkTeam.Red,-2,5), P("red-archer","Archer",NetworkTeam.Red,2,5), P("red-soldier","Soldier",NetworkTeam.Red,0,4), P("red-defender","Defender",NetworkTeam.Red,-1,6), P("blue-king","King",NetworkTeam.Blue,0,-8), P("blue-knight","Knight",NetworkTeam.Blue,2,-5), P("blue-archer","Archer",NetworkTeam.Blue,-2,-5), P("blue-soldier","Soldier",NetworkTeam.Blue,0,-4), P("blue-defender","Defender",NetworkTeam.Blue,1,-6)},
    new[]{new CpuTeamState(NetworkTeam.Red,money,MatchRules.ActionsPerTurn), new CpuTeamState(NetworkTeam.Blue,money,MatchRules.ActionsPerTurn)},
    NetworkTeam.Red, terrain:new BattlefieldTerrain(), scenario:CpuScenarioDefinition.ForMatch(c));
}
static NetworkPiece P(string id,string type,NetworkTeam team,int x,int y){var r=UnitRules.GetRequired(type);return new NetworkPiece(id,type,team,x,y,r.Health);}
''')
