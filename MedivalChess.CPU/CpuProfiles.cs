namespace MedivalChess.CPU;

/// <summary>Bounded search controls. Every value is explicit so matches are reproducible and tunable.</summary>
public sealed class CpuSearchSettings
{
  public int BeamWidth { get; init; } = 12;
  public int CandidatesPerNode { get; init; } = 16;
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

  public static CpuProfile Easy(int seed = 1) => new()
  {
    Name = "Easy CPU",
    Difficulty = CpuDifficultyLevel.Easy,
    RandomSeed = seed,
    // Easy is a shallow, understandable opponent rather than a random one. It only varies
    // between close plans and never samples from the full legal-action list.
    StrategyVariationChance = 0.7f,
    MistakeChance = 0.08f,
    TopChoicesForRandomSelection = 2,
    Weights = new EvaluationWeights
    {
      RoyalSafety = 1.25f,
      AssetSafety = 0.5f,
      Formation = 0.1f
    },
    Search = new CpuSearchSettings
    {
      BeamWidth = 3,
      CandidatesPerNode = 6,
      OpponentBeamWidth = 0,
      OpponentActionsToPredict = 0,
      TacticalExtensionDepth = 0,
      MaxSearchNodes = 42,
      MaximumPurchasePlacementCandidates = 18,
      MaxSearchMilliseconds = 60,
      TopChoiceScoreWindow = 14f,
      Randomness = 0.08f
    }
  };

  public static CpuProfile Medium(int seed = 1) => new()
  {
    Name = "Medium CPU",
    Difficulty = CpuDifficultyLevel.Medium,
    RandomSeed = seed,
    // Small seeded variety keeps repeated games from looking scripted without choosing a weak plan.
    StrategyVariationChance = 0.6f,
    MistakeChance = 0.08f,
    TopChoicesForRandomSelection = 3,
    Weights = new EvaluationWeights
    {
      RoyalSafety = 4f,
      AssetSafety = 2f,
      Formation = 0.55f,
      StrategicPosition = 1.1f,
      Economy = 0.75f
    },
    Search = new CpuSearchSettings
    {
      // The local CPU runs on a worker, but it still has a visible hard ceiling. The deterministic
      // node cap normally finishes this much sooner; three seconds is the requested failsafe for
      // unusually complex boards or slow machines.
      BeamWidth = 6,
      CandidatesPerNode = 9,
      OpponentBeamWidth = 2,
      OpponentActionsToPredict = 1,
      TacticalExtensionDepth = 1,
      MaxSearchNodes = 180,
      MaximumPurchasePlacementCandidates = 12,
      MaxSearchMilliseconds = 3_000,
      Randomness = 0.06f
    }
  };

  /// <summary>Compatibility alias for code and levels authored before Medium replaced Normal.</summary>
  public static CpuProfile Normal(int seed = 1) => Medium(seed);

  public static CpuProfile Hard(int seed = 1) => new()
  {
    Name = "Hard CPU",
    Difficulty = CpuDifficultyLevel.Hard,
    RandomSeed = seed,
    // Hard remains very strong, but a small seeded choice among essentially equal plans keeps
    // repeated matches from becoming a memorised script.
    StrategyVariationChance = 0.4f,
    MistakeChance = 0.02f,
    TopChoicesForRandomSelection = 2,
    Weights = new EvaluationWeights
    {
      RoyalSafety = 7.5f,
      AssetSafety = 4f,
      Formation = 0.9f,
      StrategicPosition = 1.15f,
      Economy = 0.9f
    },
    Search = new CpuSearchSettings
    {
      BeamWidth = 18,
      CandidatesPerNode = 24,
      OpponentBeamWidth = 8,
      OpponentActionsToPredict = 3,
      TacticalExtensionDepth = 2,
      MaxSearchNodes = 2_400,
      MaximumPurchasePlacementCandidates = 60,
      MaxSearchMilliseconds = 650,
      TopChoiceScoreWindow = 12f,
      Randomness = 0.02f
    }
  };

  /// <summary>
  /// The strongest local profile. It is intentionally bounded and cancellable so it cannot freeze
  /// the game, but examines a much wider decision tree and assumes a strong full-turn reply.
  /// </summary>
  public static CpuProfile Best(int seed = 1) => new()
  {
    Name = "Best CPU",
    Difficulty = CpuDifficultyLevel.Best,
    RandomSeed = seed,
    StrategyVariationChance = 0f,
    MistakeChance = 0f,
    TopChoicesForRandomSelection = 1,
    Weights = new EvaluationWeights
    {
      RoyalSafety = 12f,
      AssetSafety = 7f,
      Formation = 1.2f,
      StrategicPosition = 1.2f,
      Economy = 1f
    },
    Search = new CpuSearchSettings
    {
      BeamWidth = 36,
      CandidatesPerNode = 48,
      OpponentBeamWidth = 16,
      OpponentActionsToPredict = 5,
      TacticalExtensionDepth = 3,
      // Keep the deterministic node cap below the wall-clock safeguard now that the broader
      // movement definitions produce more legal branches per position.
      MaxSearchNodes = 8_000,
      MaximumPurchasePlacementCandidates = 96,
      MaxSearchMilliseconds = 5_000,
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
