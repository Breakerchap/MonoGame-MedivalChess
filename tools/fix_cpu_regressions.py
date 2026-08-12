from pathlib import Path
import re

# Restore the known-good search implementation from immediately before receding-horizon planning.
# The workflow checks out full history, so this commit is available locally.
import subprocess
search = subprocess.check_output([
    'git', 'show',
    '65ac2ce9803e120371bb38a585e26a4e54904d3d:MedivalChess.CPU/CpuSearch.cs'
], text=True)

# In unlimited-action play there is no strategic reason to spend gold while an existing unit can
# attack right now. Likewise, a directly threatened royal must respond on the board before shopping.
needle = '''    if (immediateWins.Length > 0)\n    {\n      return immediateWins;\n    }\n\n'''
replacement = '''    if (immediateWins.Length > 0)\n    {\n      return immediateWins;\n    }\n\n    if (!Globals.ActionLimitsEnabled)\n    {\n      bool immediateCombat = candidates.Any(candidate => candidate.Action is AttackAction);\n      bool royalEmergency = IsRoyalUnderDirectThreat(state, team);\n      if (immediateCombat || royalEmergency)\n      {\n        ScoredAction[] boardResponses = candidates\n          .Where(candidate => candidate.Action is AttackAction or MoveAction or UseAbilityAction)\n          .ToArray();\n        if (boardResponses.Length > 0)\n        {\n          // Purchases cannot act this turn and therefore cannot improve an immediate exchange.\n          // Resolve the fight / royal emergency first, then reconsider spending from the resulting state.\n          return boardResponses;\n        }\n      }\n    }\n\n'''
if needle not in search:
    raise SystemExit('Search priority insertion point not found')
search = search.replace(needle, replacement, 1)

helper_needle = '''  private static bool IsFullHealthNeutralMercenaryAt(CpuGameState state, int x, int y) => state.Pieces.Any(piece =>\n'''
helper = '''  private static bool IsRoyalUnderDirectThreat(CpuGameState state, NetworkTeam team) => state.Pieces\n    .Where(piece => piece.Team == team && piece.AttachedToId is null &&\n      UnitRules.TryGet(piece.Type, out UnitRule rule) && rule.Category == RuleCategory.Royal)\n    .Any(royal => state.Pieces.Any(enemy => enemy.Team != team && enemy.Team != NetworkTeam.Neutral &&\n      enemy.AttachedToId is null && CpuGameRules.CanDirectlyAttack(state, enemy, royal)));\n\n  private static bool IsFullHealthNeutralMercenaryAt(CpuGameState state, int x, int y) => state.Pieces.Any(piece =>\n'''
if helper_needle not in search:
    raise SystemExit('Search helper insertion point not found')
search = search.replace(helper_needle, helper, 1)
Path('MedivalChess.CPU/CpuSearch.cs').write_text(search)

# Fix the purchase economics. Cost is compressed so premium units matter more without making them
# sacred, while cash is a reserve/resource rather than being valued one-for-one with board strength.
path = Path('MedivalChess.CPU/Evaluation.cs')
text = path.read_text()
old_material = '''    // Board presence is deliberately flat for normal pieces. Price belongs in the tactical\n    // reward for destroying or damaging a target, rather than making the CPU hoard expensive\n    // units simply because they cost more.\n    if (rule.Category != RuleCategory.Royal)\n    {\n      return 20f;\n    }\n'''
new_material = '''    if (rule.Category != RuleCategory.Royal)\n    {\n      // Board value follows replacement cost, but on a compressed curve so a premium specialist\n      // is worth protecting without becoming five times as important as a cheap screen. This also\n      // prevents the purchase evaluator from treating a 10-gold Peasant and a 55-gold Knight as\n      // identical material while simultaneously charging the full difference in cash.\n      if (rule.Type == "Farm") return 30f;\n      float cost = Math.Max(0f, rule.Cost);\n      return 18f + Math.Min(cost, 40f) * 1.35f + Math.Max(0f, cost - 40f) * 0.70f;\n    }\n'''
if old_material not in text:
    raise SystemExit('Material block not found')
text = text.replace(old_material, new_material, 1)
if '    return money + forecast;' not in text:
    raise SystemExit('Economy return not found')
text = text.replace('    return money + forecast;',
'''    // Gold is optionality, not board control. Valuing it one-for-one made cheap units exploit\n    // the evaluator because an expensive purchase lost far more cash score than it gained material.\n    return money * 0.5f + forecast;''', 1)
old_asset = '''      float importance = rule.Type == "Farm" ? 4f : rule.Category == RuleCategory.Royal\n        ? 0.35f + CpuObjectiveRules.GetRoyalSafetyImportance(state) * 2.15f\n        : 0.35f;'''
new_asset = '''      float importance = rule.Type == "Farm" ? 4f : rule.Category == RuleCategory.Royal\n        ? 0.8f + CpuObjectiveRules.GetRoyalSafetyImportance(state) * 1.7f\n        : 0.35f;'''
if old_asset not in text:
    raise SystemExit('Asset safety importance not found')
text = text.replace(old_asset, new_asset, 1)
path.write_text(text)

# Screens are useful, but a whole army of Defenders/Peasants is not. Apply a combined saturation
# penalty after two screens unless the current enemy composition genuinely makes one a priority counter.
path = Path('MedivalChess.CPU/CpuArmyPlanner.cs')
text = path.read_text()
needle = '''    float duplicatePenalty = GetDuplicatePenalty(rule, owned, isPriorityCounter);\n    score -= duplicatePenalty;\n\n'''
replacement = '''    float duplicatePenalty = GetDuplicatePenalty(rule, owned, isPriorityCounter);\n    score -= duplicatePenalty;\n\n    if (rule.Type is "Defender" or "Peasant")\n    {\n      int screenCount = _friendly.Count(piece => piece.Type is "Defender" or "Peasant");\n      int combatCount = _friendly.Count(piece => UnitRules.TryGet(piece.Type, out UnitRule friendlyRule) &&\n        friendlyRule.Category != RuleCategory.Royal && friendlyRule.Type != "Farm" && friendlyRule.Attack > 0);\n      int softScreenCap = Math.Max(2, (combatCount + 1) / 2);\n      if (screenCount >= softScreenCap)\n      {\n        float saturationPenalty = 26f + Math.Max(0, screenCount - softScreenCap) * 18f;\n        score -= saturationPenalty * (isPriorityCounter ? 0.45f : 1f);\n      }\n    }\n\n'''
if needle not in text:
    raise SystemExit('Army planner saturation insertion point not found')
text = text.replace(needle, replacement, 1)
needle = '''    float basePenalty = rule.Cost >= 45 ? 19f : rule.Cost >= 30 ? 13f : 9f;\n    if (rule.Category is RuleCategory.Mechanical or RuleCategory.Intelligence or RuleCategory.Transport)\n'''
replacement = '''    float basePenalty = rule.Cost >= 45 ? 19f : rule.Cost >= 30 ? 13f : 9f;\n    if (rule.Type == "Peasant") basePenalty += 10f;\n    else if (rule.Type == "Defender") basePenalty += 6f;\n    if (rule.Category is RuleCategory.Mechanical or RuleCategory.Intelligence or RuleCategory.Transport)\n'''
if needle not in text:
    raise SystemExit('Duplicate penalty block not found')
text = text.replace(needle, replacement, 1)
path.write_text(text)

# Replace the receding-horizon regression test with the actual invariant we want: fight before shopping.
path = Path('MedivalChess.Tests/CpuPlanningEfficiencyTests.cs')
text = path.read_text()
pattern = re.compile(r'''  \[Fact\]\n  public void UnlimitedTurn_ReplansAfterCommittingAShortSegment\(\)\n  \{.*?\n  \}\n\n(?=  \[Fact\]\n  public void SearchPurchases_)''', re.S)
new_test = '''  [Fact]\n  public void UnlimitedTurn_ImmediateAttackHappensBeforePurchase()\n  {\n    bool previous = Globals.ActionLimitsEnabled;\n    Globals.ActionLimitsEnabled = false;\n    try\n    {\n      CpuGameState state = CreateState(\n        money: 500,\n        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0),\n        Piece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, 1));\n      CpuProfile profile = new()\n      {\n        Name = "Combat before shopping test",\n        Difficulty = CpuDifficultyLevel.Hard,\n        Search = new CpuSearchSettings\n        {\n          BeamWidth = 8,\n          CandidatesPerNode = 12,\n          PromisingCandidatesPerNode = 10,\n          OpponentActionsToPredict = 0,\n          TacticalExtensionDepth = 1,\n          MaxSearchNodes = 8_000,\n          MaximumPurchasePlacementCandidates = 12,\n          MaxSearchMilliseconds = 700,\n          MaxParallelism = 1,\n          Randomness = 0f\n        },\n        TopChoicesForRandomSelection = 1,\n        MistakeChance = 0f,\n        StrategyVariationChance = 0f\n      };\n\n      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);\n\n      AttackAction first = Assert.IsType<AttackAction>(plan.Actions.First());\n      Assert.Equal("red-soldier", first.AttackerId);\n      Assert.Equal("blue-peasant", first.TargetPieceId);\n    }\n    finally\n    {\n      Globals.ActionLimitsEnabled = previous;\n    }\n  }\n\n'''
text, count = pattern.subn(new_test, text, count=1)
if count != 1:
    raise SystemExit(f'Receding test replacement count={count}')

insert_before = '''  [Fact]\n  public void SearchPurchases_ClusterManyLegalSquaresIntoFewRepresentatives()\n'''
king_test = '''  [Theory]\n  [InlineData("Conquest")]\n  [InlineData("Regicide")]\n  public void ThreatenedKing_RespondsToAdjacentKnightBeforeShopping(string gameMode)\n  {\n    bool previous = Globals.ActionLimitsEnabled;\n    Globals.ActionLimitsEnabled = false;\n    try\n    {\n      NetworkMatchConfiguration configuration = new(\n        "Small", "Light", "Light", gameMode, 7719, 500, 0f, 0f, 2, 1, 15, FarmsEnabled: false);\n      CpuGameState state = new(\n        configuration,\n        [\n          Piece("red-king", "King", NetworkTeam.Red, 0, 0),\n          Piece("blue-knight", "Knight", NetworkTeam.Blue, 0, 1),\n          Piece("blue-king", "King", NetworkTeam.Blue, 0, -6)\n        ],\n        [\n          new CpuTeamState(NetworkTeam.Red, 500, MatchRules.ActionsPerTurn),\n          new CpuTeamState(NetworkTeam.Blue, 500, MatchRules.ActionsPerTurn)\n        ],\n        NetworkTeam.Red,\n        terrain: new BattlefieldTerrain());\n      CpuProfile profile = new()\n      {\n        Name = "Royal response test",\n        Difficulty = CpuDifficultyLevel.Hard,\n        Search = new CpuSearchSettings\n        {\n          BeamWidth = 10,\n          CandidatesPerNode = 14,\n          PromisingCandidatesPerNode = 12,\n          OpponentActionsToPredict = 0,\n          TacticalExtensionDepth = 1,\n          MaxSearchNodes = 12_000,\n          MaximumPurchasePlacementCandidates = 12,\n          MaxSearchMilliseconds = 800,\n          MaxParallelism = 1,\n          Randomness = 0f\n        },\n        TopChoicesForRandomSelection = 1,\n        MistakeChance = 0f,\n        StrategyVariationChance = 0f\n      };\n\n      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);\n      ICpuGameAction first = Assert.IsAssignableFrom<ICpuGameAction>(plan.Actions.First());\n\n      Assert.False(first is PurchaseAction, string.Join(" | ", plan.Actions.Select(action => action.Describe())));\n      Assert.True(first is AttackAction { AttackerId: "red-king", TargetPieceId: "blue-knight" } or\n        MoveAction { PieceId: "red-king" },\n        string.Join(" | ", plan.Actions.Select(action => action.Describe())));\n    }\n    finally\n    {\n      Globals.ActionLimitsEnabled = previous;\n    }\n  }\n\n'''
if insert_before not in text:
    raise SystemExit('Planning test insertion point not found')
text = text.replace(insert_before, king_test + insert_before, 1)
path.write_text(text)

# Update old flat-material assertion and add a screen-spam recruitment regression.
path = Path('MedivalChess.Tests/CpuAdvancedTests.cs')
text = path.read_text()
pattern = re.compile(r'''  \[Fact\]\n  public void MaterialEvaluation_DoesNotValueAnExpensiveUnitMoreThanAnyOtherNormalUnit\(\)\n  \{.*?\n  \}\n\n(?=  \[Fact\]\n  public void TacticalTargetRewards)''', re.S)
material_test = '''  [Fact]\n  public void MaterialEvaluation_UsesCompressedReplacementCostForNormalUnits()\n  {\n    float peasant = MaterialEvaluation.GetUnitValue("Peasant");\n    float defender = MaterialEvaluation.GetUnitValue("Defender");\n    float knight = MaterialEvaluation.GetUnitValue("Knight");\n\n    Assert.True(peasant < defender, $"peasant={peasant}, defender={defender}");\n    Assert.True(defender < knight, $"defender={defender}, knight={knight}");\n    Assert.True(knight < peasant * 3f, $"peasant={peasant}, knight={knight}");\n  }\n\n'''
text, count = pattern.subn(material_test, text, count=1)
if count != 1:
    raise SystemExit(f'Material test replacement count={count}')

insert_before = '''  [Fact]\n  public void OpeningFarmCpu_RemainsFastAndFindsTheSecondFarmAfterOccupiedTopSquares()\n'''
screen_test = '''  [Fact]\n  public void PurchaseRanking_PenalisesAnArmyAlreadySaturatedWithCheapScreens()\n  {\n    CpuGameState state = CreateState(\n      [\n        new NetworkPiece("red-king", "King", NetworkTeam.Red, 0, 8, 95),\n        new NetworkPiece("red-defender-one", "Defender", NetworkTeam.Red, -2, 6, 25),\n        new NetworkPiece("red-defender-two", "Defender", NetworkTeam.Red, -1, 6, 25),\n        new NetworkPiece("red-peasant-one", "Peasant", NetworkTeam.Red, 1, 6, 5),\n        new NetworkPiece("red-peasant-two", "Peasant", NetworkTeam.Red, 2, 6, 5),\n        new NetworkPiece("blue-archer", "Archer", NetworkTeam.Blue, 0, 0, 10),\n        new NetworkPiece("blue-king", "King", NetworkTeam.Blue, 0, -8, 95)\n      ],\n      redMoney: 500\n    );\n    IReadOnlyDictionary<string, PurchaseAction> options = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)\n      .OfType<PurchaseAction>()\n      .Where(action => action.UnitType is "Peasant" or "Defender" or "Soldier")\n      .GroupBy(action => action.UnitType)\n      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);\n\n    IReadOnlyList<ScoredAction> ranked = new CpuActionCandidateSelector().SelectCandidates(\n      state, NetworkTeam.Red,\n      [options["Peasant"], options["Defender"], options["Soldier"]],\n      new CpuSearchSettings { CandidatesPerNode = 3, PromisingCandidatesPerNode = 3 });\n\n    float soldier = ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Soldier" }).Score;\n    float defender = ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Defender" }).Score;\n    float peasant = ranked.Single(candidate => candidate.Action is PurchaseAction { UnitType: "Peasant" }).Score;\n    Assert.True(soldier > defender, $"soldier={soldier}, defender={defender}");\n    Assert.True(soldier > peasant, $"soldier={soldier}, peasant={peasant}");\n  }\n\n'''
if insert_before not in text:
    raise SystemExit('Screen test insertion point not found')
text = text.replace(insert_before, screen_test + insert_before, 1)
path.write_text(text)
