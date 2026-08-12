namespace MedivalChess.CPU;

/// <summary>Bounded search controls. Every value is explicit so matches are reproducible and tunable.</summary>
public sealed class CpuSearchSettings
{
  public int BeamWidth { get; init; } = 12;
  public int CandidatesPerNode { get; init; } = 16;
  /// <summary>
  /// Hard ceiling for actions that survive strategic pruning. <see cref="CandidatesPerNode"/>
  /// remains an outer compatibility/configuration cap; this lower ceiling makes short searches
  /// spend their budget on distinct, plausible plans rather than near-identical placements.
  /// </summary>
  public int PromisingCandidatesPerNode { get; init; } = 16;
  public int OpponentBeamWidth { get; init; } = 6;
  public int OpponentActionsToPredict { get; init; } = 1;
  /// <summary>Extra attack-only plies after the ordinary turn horizon to resolve forcing exchanges.</summary>
  public int TacticalExtensionDepth { get; init; }
  /// <summary>
  /// Deterministic cap on simulated actions. This complements the wall-clock guard so a fixed
  /// seed produces the same completed search work when the machine is otherwise busy.
  /// </summary>
  public int MaxSearchNodes { get; init; } = 800;
  /// <summary>Maximum purchase placements inspected by the bounded search; exhaustive generation is unchanged.</summary>
  public int MaximumPurchasePlacementCandidates { get; init; } = 48;
  /// <summary>Only near-equal plans inside this score window are eligible for seeded variety.</summary>
  public float TopChoiceScoreWindow { get; init; } = 24f;
  public int MaxSearchMilliseconds { get; init; } = 250;
  /// <summary>
  /// Maximum worker threads used to evaluate independent search branches. Set to zero to use a
  /// conservative machine-dependent limit; one keeps the search single-threaded.
  /// </summary>
  public int MaxParallelism { get; init; }
  public float Randomness { get; init; } = 0.05f;
}

public enum CpuDifficultyLevel
{
  Easy,
  Medium,
  Hard,
  Best,
  // Retained so existing saved/debug code can compile. New UI and profiles use Medium.
  Normal = Medium
}

/// <summary>Evaluation modifiers that change priorities without changing gameplay legality.</summary>
public sealed class CpuPersonality
{
  public float Aggression { get; init; } = 1f;
  public float Caution { get; init; } = 1f;
  public float ObjectiveFocus { get; init; } = 1f;
  public float EconomyFocus { get; init; } = 1f;
  public float RoyalProtection { get; init; } = 1f;
  public float FormationPreference { get; init; } = 1f;
  public float AbilityUsage { get; init; } = 1f;

  public static CpuPersonality Balanced { get; } = new();
  public static CpuPersonality Aggressive { get; } = new() { Aggression = 1.45f, Caution = 0.7f, RoyalProtection = 0.8f };
  public static CpuPersonality Defensive { get; } = new() { Aggression = 0.7f, Caution = 1.4f, RoyalProtection = 1.5f, FormationPreference = 1.3f };
  public static CpuPersonality Greedy { get; } = new() { EconomyFocus = 1.6f, ObjectiveFocus = 0.8f, Aggression = 0.8f };
  public static CpuPersonality Reckless { get; } = new() { Aggression = 1.65f, Caution = 0.55f, RoyalProtection = 0.6f, AbilityUsage = 1.3f };
  public static CpuPersonality ObjectiveFocused { get; } = new() { ObjectiveFocus = 1.75f, EconomyFocus = 0.8f };
  public static CpuPersonality Swarmer { get; } = new() { Aggression = 1.2f, FormationPreference = 1.55f, EconomyFocus = 1.15f };
}

/// <summary>A complete deterministic CPU configuration for one team or campaign opponent.</summary>
public sealed class CpuProfile
{
  public string Name { get; init; } = "CPU";
  public CpuDifficultyLevel Difficulty { get; init; } = CpuDifficultyLevel.Medium;
  public CpuSearchSettings Search { get; init; } = new();
  public EvaluationWeights Weights { get; init; } = new();
  public CpuPersonality Personality { get; init; } = CpuPersonality.Balanced;
  public int RandomSeed { get; init; } = 1;
  /// <summary>
  /// Chance to select a different plan only when it is already inside the near-best score window.
  /// This is strategic variety, not an intentional mistake.
  /// </summary>
  public float StrategyVariationChance { get; init; }
  public float MistakeChance { get; init; }
  public int TopChoicesForRandomSelection { get; init; } = 1;

  // Difficulty controls deliberation time only. Every level evaluates the same position with the
  // same tactical candidates, reply search, and evaluation weights. This makes a quick campaign
  // opponent play like the strongest CPU when it finds a line early, rather than deliberately
  // omitting whole categories of moves or choosing mistakes.
  public static CpuProfile Easy(int seed = 1) => CreateTimeLimitedProfile(
    "Easy CPU", CpuDifficultyLevel.Easy, seed, 500);

  public static CpuProfile Medium(int seed = 1) => CreateTimeLimitedProfile(
    "Medium CPU", CpuDifficultyLevel.Medium, seed, 1_000);

  /// <summary>Compatibility alias for code and levels authored before Medium replaced Normal.</summary>
  public static CpuProfile Normal(int seed = 1) => Medium(seed);

  /// <summary>The campaign profile: the full CPU logic with a responsive 1.4-second budget.</summary>
  public static CpuProfile Hard(int seed = 1) => CreateTimeLimitedProfile(
    "Hard CPU", CpuDifficultyLevel.Hard, seed, 3_000);

  /// <summary>The full CPU logic with a five-second analysis budget.</summary>
  public static CpuProfile Best(int seed = 1) => CreateTimeLimitedProfile(
    "Best CPU", CpuDifficultyLevel.Best, seed, 8_000);

  private static CpuProfile CreateTimeLimitedProfile(
    string name,
    CpuDifficultyLevel difficulty,
    int seed,
    int maxSearchMilliseconds
  ) => new()
  {
    Name = name,
    Difficulty = difficulty,
    RandomSeed = seed,
    StrategyVariationChance = 0f,
    MistakeChance = 0f,
    TopChoicesForRandomSelection = 1,
    Weights = new EvaluationWeights
    {
      RoyalSafety = 12f,
      AssetSafety = 7f,
      Formation = 1.2f,
      // Recruitment now gives counter coverage and documented formations priority before the
      // beam. Keep board evaluation balanced so it still favours concrete tactical lines.
      Matchups = 1.1f,
      StrategicPosition = 1.2f,
      Economy = 1f
    },
    Search = new CpuSearchSettings
    {
      // This is intentionally the same search shape as the former Best profile. The generous
      // common node cap is a failsafe; normal gameplay is governed by the time budget above.
      BeamWidth = 36,
      CandidatesPerNode = 48,
      // The selector keeps at most this many strategically distinct actions.  It is shared by
      // every difficulty, so the campaign CPU differs from Best only in thinking time.
      PromisingCandidatesPerNode = 16,
      OpponentBeamWidth = 16,
      OpponentActionsToPredict = 5,
      TacticalExtensionDepth = 3,
      MaxSearchNodes = 200_000,
      MaximumPurchasePlacementCandidates = 96,
      MaxSearchMilliseconds = maxSearchMilliseconds,
      MaxParallelism = 0,
      TopChoiceScoreWindow = 12f,
      Randomness = 0f
    }
  };

  public static CpuProfile ForDifficulty(CpuDifficultyLevel difficulty, int seed = 1, CpuPersonality? personality = null)
  {
    CpuProfile baseline = difficulty switch
    {
      CpuDifficultyLevel.Easy => Easy(seed),
      CpuDifficultyLevel.Hard => Hard(seed),
      CpuDifficultyLevel.Best => Best(seed),
      _ => Medium(seed)
    };
    if (personality is null || ReferenceEquals(personality, baseline.Personality))
    {
      return baseline;
    }
    return new CpuProfile
    {
      Name = $"{baseline.Difficulty} CPU",
      Difficulty = baseline.Difficulty,
      Search = baseline.Search,
      Weights = baseline.Weights,
      Personality = personality,
      RandomSeed = baseline.RandomSeed,
      StrategyVariationChance = baseline.StrategyVariationChance,
      MistakeChance = baseline.MistakeChance,
      TopChoicesForRandomSelection = baseline.TopChoicesForRandomSelection
    };
  }
}
