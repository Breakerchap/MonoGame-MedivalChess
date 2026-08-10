using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// Converts the counter and combo guidance in <c>Combos.md</c> into a recruitment doctrine for
/// the current board. It deliberately works from unit rules and matchup data rather than a list
/// of one-off exceptions, so every purchasable unit is judged against the opposing army.
/// </summary>
internal sealed class CpuArmyPlanner
{
  private const float CounterThreshold = 0.35f;

  private readonly CpuGameState _state;
  private readonly NetworkTeam _team;
  private readonly NetworkPiece[] _friendly;
  private readonly NetworkPiece[] _enemies;
  private readonly Dictionary<string, int> _friendlyCounts;
  private readonly Dictionary<string, float> _recruitmentNeed;
  private readonly HashSet<string> _priorityCounters;
  private readonly float _homePressure;
  private readonly int _nearbyPowerfulEnemies;

  private CpuArmyPlanner(
    CpuGameState state,
    NetworkTeam team,
    NetworkPiece[] friendly,
    NetworkPiece[] enemies,
    Dictionary<string, int> friendlyCounts,
    Dictionary<string, float> recruitmentNeed,
    HashSet<string> priorityCounters,
    float homePressure,
    int nearbyPowerfulEnemies,
    int emergencyReserve,
    bool isSafeForFarmInvestment,
    bool isVerySafeForFarmInvestment
  )
  {
    _state = state;
    _team = team;
    _friendly = friendly;
    _enemies = enemies;
    _friendlyCounts = friendlyCounts;
    _recruitmentNeed = recruitmentNeed;
    _priorityCounters = priorityCounters;
    _homePressure = homePressure;
    _nearbyPowerfulEnemies = nearbyPowerfulEnemies;
    EmergencyReserve = emergencyReserve;
    IsSafeForFarmInvestment = isSafeForFarmInvestment;
    IsVerySafeForFarmInvestment = isVerySafeForFarmInvestment;
  }

  /// <summary>Gold retained for the cheapest useful answer to the present enemy army.</summary>
  public int EmergencyReserve { get; }
  public bool IsSafeForFarmInvestment { get; }
  public bool IsVerySafeForFarmInvestment { get; }

  public static CpuArmyPlanner Create(CpuGameState state, NetworkTeam team)
  {
    NetworkPiece[] friendly = state.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null &&
      UnitRules.TryGet(piece.Type, out _)).ToArray();
    NetworkPiece[] enemies = state.Pieces.Where(piece => piece.Team != team && piece.Team != NetworkTeam.Neutral &&
      piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out _)).ToArray();
    Dictionary<string, int> counts = friendly.GroupBy(piece => piece.Type, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    float homePressure = GetHomePressure(state, team, enemies, friendly);
    int nearbyPowerfulEnemies = CountNearbyPowerfulEnemies(state, team, enemies, friendly);
    bool ownRoyalIsThreatened = friendly.Where(piece => IsRoyal(piece)).Any(royal => enemies.Any(enemy =>
      CpuGameRules.CanDirectlyAttack(state, enemy, royal)));
    bool safe = !ownRoyalIsThreatened && homePressure < 42f && nearbyPowerfulEnemies <= 1;
    bool verySafe = safe && homePressure < 18f && nearbyPowerfulEnemies == 0;

    Dictionary<string, float> needs = UnitRules.Purchasable
      .Where(rule => rule.Type != "Farm")
      .ToDictionary(rule => rule.Type, rule => GetRecruitmentNeed(state, team, rule, enemies, friendly), StringComparer.Ordinal);
    float bestNeed = needs.Values.DefaultIfEmpty(0f).Max();
    HashSet<string> priorityCounters = needs
      .Where(pair => pair.Value >= Math.Max(14f, bestNeed * 0.72f))
      .Select(pair => pair.Key)
      .ToHashSet(StringComparer.Ordinal);
    int reserve = GetEmergencyReserve(state, needs, priorityCounters);

    return new CpuArmyPlanner(state, team, friendly, enemies, counts, needs, priorityCounters,
      homePressure, nearbyPowerfulEnemies, reserve, safe, verySafe);
  }

  /// <summary>Scores a legal purchase's counter value, economy impact, role, and duplication risk.</summary>
  public CpuRecruitmentAdvice EvaluatePurchase(PurchaseAction action, UnitRule rule, CpuPersonality personality)
  {
    if (rule.Type == "Farm")
    {
      return EvaluateFarm(action, rule, personality);
    }

    float score = 0f;
    float need = _recruitmentNeed.GetValueOrDefault(rule.Type);
    bool isPriorityCounter = _priorityCounters.Contains(rule.Type) && need >= 14f;
    int owned = _friendlyCounts.GetValueOrDefault(rule.Type);
    int price = GetPrice(rule);
    int remainingGold = (_state.Teams.GetValueOrDefault(_team)?.Money ?? 0) - price;

    // The first answer to an uncovered threat matters much more than another unit that merely
    // has a favourable matchup. This is what makes Cavalier/Catapult-style responses win over
    // duplicating a unit the enemy already counters.
    score += need * (isPriorityCounter ? 2.6f : 1.25f);
    score -= GetEnemyCounterPressure(rule, action) * 22f;

    float duplicatePenalty = GetDuplicatePenalty(rule, owned, isPriorityCounter);
    score -= duplicatePenalty;

    if (!HasBattlefieldRole(rule, action))
    {
      // Expensive artillery or specialist pieces without a reachable target, threatened asset,
      // or useful defensive role should not consume the counter reserve just because gold exists.
      score -= 26f + Math.Min(20f, rule.Cost * 0.22f);
    }

    if (remainingGold < EmergencyReserve && !isPriorityCounter)
    {
      score -= 58f + (EmergencyReserve - remainingGold) * 1.5f;
    }
    else if (isPriorityCounter)
    {
      score += 18f;
    }

    // Cheap blockers remain useful around vulnerable ranged units, but only in the formation
    // described by Combos.md. They are actively discouraged into enemy AoE or after the screen
    // is already large enough.
    if (rule.Type == "Peasant")
    {
      bool protectsRanged = _friendly.Any(piece => IsValuableRanged(piece) && Distance((action.X, action.Y), (piece.X, piece.Y)) <= 4);
      bool enemyAreaDamage = _enemies.Any(piece => piece.Type is "Bombard" or "Ballista" or "Elephant");
      score += protectsRanged ? 12f : -12f;
      if (enemyAreaDamage) score -= 34f;
      if (owned >= 2) score -= (owned - 1) * 18f;
    }

    // A supporting unit gets a modest bonus only when the matching partner is already present.
    // This preserves the documented formations without causing the CPU to force a combo when a
    // direct counter is urgently needed.
    score += GetComboSupportBonus(rule.Type);

    score *= Math.Max(0.55f, personality.Aggression * 0.55f + personality.Caution * 0.2f + 0.25f);
    string reason = isPriorityCounter
      ? $"Buys {rule.Type} as a needed counter to the enemy composition"
      : owned > 0 && duplicatePenalty >= 16f
        ? $"Avoids overproducing {rule.Type} when other roles are needed"
        : HasBattlefieldRole(rule, action)
          ? $"Adds {rule.Type} with a usable counter or combo role"
          : $"Defers {rule.Type} until it has a battlefield role";
    return new CpuRecruitmentAdvice(score, reason, isPriorityCounter, false);
  }

  private CpuRecruitmentAdvice EvaluateFarm(PurchaseAction action, UnitRule rule, CpuPersonality personality)
  {
    int ownedFarms = _friendlyCounts.GetValueOrDefault("Farm");
    int desiredFarms = IsVerySafeForFarmInvestment ? 4 : IsSafeForFarmInvestment ? 3 : ownedFarms;
    int remainingGold = (_state.Teams.GetValueOrDefault(_team)?.Money ?? 0) - GetPrice(rule);
    float protection = CpuPlacementHeuristics.GetFarmProtectionScore(_state, _team, action.X, action.Y);

    if (!IsSafeForFarmInvestment || IsFarmLocationUnderImmediatePressure(action))
    {
      return new CpuRecruitmentAdvice(
        -110f - _homePressure * 0.8f,
        "Defers a farm because powerful enemies are too close to home assets",
        false,
        false);
    }

    float score = 70f + protection * 2.4f + (desiredFarms - ownedFarms) * 24f;
    if (ownedFarms >= desiredFarms) score -= 52f + (ownedFarms - desiredFarms) * 22f;
    if (remainingGold < EmergencyReserve) score -= 38f + (EmergencyReserve - remainingGold) * 1.25f;
    if (_enemies.Length == 0) score += 24f;
    score *= Math.Max(0.55f, personality.EconomyFocus);
    return new CpuRecruitmentAdvice(
      score,
      ownedFarms < desiredFarms
        ? "Invests in a protected farm while the home side is safe"
        : "Avoids over-investing in farms after reaching the safe economy target",
      false,
      ownedFarms < desiredFarms);
  }

  private static float GetRecruitmentNeed(
    CpuGameState state,
    NetworkTeam team,
    UnitRule candidateRule,
    IReadOnlyList<NetworkPiece> enemies,
    IReadOnlyList<NetworkPiece> friendly
  )
  {
    if (candidateRule.Attack <= 0) return 0f;
    float score = 0f;
    foreach (NetworkPiece enemy in enemies)
    {
      float importance = GetEnemyImportance(enemy);
      NetworkPiece prototype = CreateRecruitmentPrototype(candidateRule, team, enemy);
      float matchup = CpuStrategicHeuristics.GetRecruitmentMatchupScore(state, prototype, enemy);
      if (matchup <= CounterThreshold) continue;

      bool alreadyCovered = friendly.Any(piece => CpuStrategicHeuristics.GetRecruitmentMatchupScore(state, piece, enemy) > CounterThreshold);
      score += matchup * importance * (alreadyCovered ? 4.5f : 13f);
    }
    return score;
  }

  private float GetEnemyCounterPressure(UnitRule candidateRule, PurchaseAction action)
  {
    NetworkPiece prototype = new("recruitment-prototype", candidateRule.Type, _team, action.X, action.Y, candidateRule.Health);
    return _enemies.Sum(enemy => Math.Max(0f, CpuStrategicHeuristics.GetRecruitmentMatchupScore(_state, enemy, prototype)) *
      GetEnemyImportance(enemy));
  }

  private float GetDuplicatePenalty(UnitRule rule, int owned, bool isPriorityCounter)
  {
    if (owned == 0) return 0f;
    float basePenalty = rule.Cost >= 45 ? 19f : rule.Cost >= 30 ? 13f : 9f;
    if (rule.Category is RuleCategory.Mechanical or RuleCategory.Intelligence or RuleCategory.Transport)
    {
      basePenalty += 8f;
    }
    return basePenalty * owned * (isPriorityCounter ? 0.35f : 1f);
  }

  private bool HasBattlefieldRole(UnitRule rule, PurchaseAction action)
  {
    if (_enemies.Length == 0) return false;
    int contributionRange = rule.MoveRange * 2 + Math.Max(1, rule.AttackRange) + 3;
    if (_enemies.Any(enemy => Distance((action.X, action.Y), (enemy.X, enemy.Y)) <= contributionRange)) return true;

    // A defensive screen or direct counter near a pressured home side still has an immediate
    // role even when the enemy has not crossed all the way to its target yet.
    return _homePressure >= 28f &&
      (rule.Category is RuleCategory.Melee or RuleCategory.Ranged or RuleCategory.Mechanical);
  }

  private bool IsFarmLocationUnderImmediatePressure(PurchaseAction action)
  {
    UnitRule farm = UnitRules.GetRequired("Farm");
    for (int y = 0; y < farm.Height; y++)
    for (int x = 0; x < farm.Width; x++)
    {
      if (_enemies.Any(enemy => CpuGameRules.CanDirectlyAttackSquare(_state, enemy, action.X + x, action.Y + y)))
      {
        return true;
      }
    }
    return false;
  }

  private float GetComboSupportBonus(string type) => type switch
  {
    "Spy" when _friendly.Any(piece => piece.Type is "Cannon" or "Ballista" or "Crossbowman") => 11f,
    "Guard" when _friendly.Any(piece => piece.Type is "Cannon" or "Catapult" or "Ballista" or "Crossbowman" or "Spy") => 10f,
    "Defender" when _friendly.Any(piece => piece.Type is "Archer" or "Crossbowman" or "Cannon" or "Catapult" or "Ballista" or "Bombard") => 10f,
    "Engineer" when _friendly.Any(piece => piece.Type is "Cannon" or "Catapult" or "Ballista" or "Bombard") => 10f,
    "Ox" when _friendly.Any(piece => piece.Type is "Cannon" or "Catapult" or "Ballista") => 9f,
    "Peasant" when _friendly.Any(IsValuableRanged) => 6f,
    "Cavalier" when _friendly.Any(piece => piece.Type is "Archer" or "Crossbowman" or "Cannon" or "Princess") => 7f,
    "Knight" when _friendly.Any(piece => piece.Type == "Archer") => 7f,
    "Spearman" when _friendly.Any(piece => piece.Type == "Archer") => 8f,
    "Bombard" when _friendly.Any(piece => piece.Type is "Defender" or "Elephant" or "Engineer") => 8f,
    _ => 0f
  };

  private static int GetEmergencyReserve(
    CpuGameState state,
    IReadOnlyDictionary<string, float> needs,
    IReadOnlySet<string> priorityCounters
  )
  {
    int cheapestCounter = UnitRules.Purchasable
      .Where(rule => priorityCounters.Contains(rule.Type) && needs.GetValueOrDefault(rule.Type) >= 14f)
      .Select(rule => GetPrice(state.Configuration, rule))
      .DefaultIfEmpty(0)
      .Min();
    return cheapestCounter == 0 ? 25 : Math.Max(25, cheapestCounter);
  }

  private int GetPrice(UnitRule rule) => GetPrice(_state.Configuration, rule);

  private static int GetPrice(NetworkMatchConfiguration configuration, UnitRule rule) => rule.Type == "Farm"
    ? rule.Cost
    : EconomyRules.GetUnitPrice(rule.Cost, configuration.UnitPricePercent);

  private static float GetHomePressure(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<NetworkPiece> enemies,
    IReadOnlyList<NetworkPiece> friendly
  )
  {
    NetworkPiece[] assets = friendly.Where(piece => IsRoyal(piece) || piece.Type == "Farm").ToArray();
    float pressure = 0f;
    foreach (NetworkPiece enemy in enemies)
    {
      if (!UnitRules.TryGet(enemy.Type, out UnitRule rule) || rule.Attack <= 0) continue;
      float power = GetEnemyImportance(enemy);
      bool inHomeTerritory = BoardRules.IsInTeamTerritory(state.Board, state.Configuration.GameMode,
        state.Configuration.PlayerCount, team, enemy.X, enemy.Y);
      int reach = rule.MoveRange + Math.Max(1, rule.AttackRange) + 2;
      int closestAsset = assets.Select(asset => Distance(enemy, asset)).DefaultIfEmpty(reach + 6).Min();
      float proximity = closestAsset <= reach ? 1f : closestAsset <= reach + 3 ? 0.55f : inHomeTerritory ? 0.75f : 0f;
      pressure += power * proximity;
      if (assets.Any(asset => CpuGameRules.CanDirectlyAttack(state, enemy, asset))) pressure += 42f;
    }
    return pressure;
  }

  private static int CountNearbyPowerfulEnemies(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<NetworkPiece> enemies,
    IReadOnlyList<NetworkPiece> friendly
  )
  {
    NetworkPiece[] assets = friendly.Where(piece => IsRoyal(piece) || piece.Type == "Farm").ToArray();
    return enemies.Count(enemy => UnitRules.TryGet(enemy.Type, out UnitRule rule) &&
      (rule.Cost >= 40 || rule.Attack >= 20) &&
      (BoardRules.IsInTeamTerritory(state.Board, state.Configuration.GameMode, state.Configuration.PlayerCount, team, enemy.X, enemy.Y) ||
       assets.Any(asset => Distance(enemy, asset) <= rule.MoveRange + Math.Max(1, rule.AttackRange) + 4)));
  }

  private static NetworkPiece CreateRecruitmentPrototype(UnitRule rule, NetworkTeam team, NetworkPiece enemy) => new(
    "recruitment-prototype",
    rule.Type,
    team,
    enemy.X + Math.Max(1, rule.MinimumAttackRange + 1),
    enemy.Y,
    rule.Health
  );

  private static float GetEnemyImportance(NetworkPiece piece)
  {
    if (!UnitRules.TryGet(piece.Type, out UnitRule rule)) return 1f;
    return 1f + rule.Cost / 35f + rule.Attack / 30f + rule.Health / 80f;
  }

  private static bool IsRoyal(NetworkPiece piece) => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
    rule.Category == RuleCategory.Royal;

  private static bool IsValuableRanged(NetworkPiece piece) => piece.Type is "Crossbowman" or "Cannon" or "Catapult" or "Ballista";

  private static int Distance(NetworkPiece first, NetworkPiece second) => Distance((first.X, first.Y), (second.X, second.Y));
  private static int Distance((int x, int y) first, (int x, int y) second) => Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);
}

internal readonly record struct CpuRecruitmentAdvice(
  float Score,
  string Reason,
  bool IsPriorityCounter,
  bool IsFarmInvestment
);
