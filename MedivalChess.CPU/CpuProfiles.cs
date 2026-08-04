namespace MedivalChess.CPU;

/// <summary>Bounded search controls. Every value is explicit so matches are reproducible and tunable.</summary>
public sealed class CpuSearchSettings
{
  public int BeamWidth { get; init; } = 12;
  public int CandidatesPerNode { get; init; } = 16;
  public int OpponentBeamWidth { get; init; } = 6;
  public int OpponentActionsToPredict { get; init; } = 1;
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
  Normal,
  Hard
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
  public CpuDifficultyLevel Difficulty { get; init; } = CpuDifficultyLevel.Normal;
  public CpuSearchSettings Search { get; init; } = new();
  public EvaluationWeights Weights { get; init; } = new();
  public CpuPersonality Personality { get; init; } = CpuPersonality.Balanced;
  public int RandomSeed { get; init; } = 1;
  public float MistakeChance { get; init; }
  public int TopChoicesForRandomSelection { get; init; } = 1;

  public static CpuProfile Easy(int seed = 1) => new()
  {
    Name = "Easy CPU",
    Difficulty = CpuDifficultyLevel.Easy,
    RandomSeed = seed,
    MistakeChance = 0.3f,
    TopChoicesForRandomSelection = 3,
    Search = new CpuSearchSettings
    {
      BeamWidth = 3,
      CandidatesPerNode = 6,
      OpponentBeamWidth = 0,
      OpponentActionsToPredict = 0,
      MaxSearchNodes = 42,
      MaximumPurchasePlacementCandidates = 18,
      MaxSearchMilliseconds = 60,
      Randomness = 0.35f
    }
  };

  public static CpuProfile Normal(int seed = 1) => new()
  {
    Name = "Normal CPU",
    Difficulty = CpuDifficultyLevel.Normal,
    RandomSeed = seed,
    // Small seeded variety keeps repeated games from looking scripted without choosing a weak plan.
    MistakeChance = 0.14f,
    TopChoicesForRandomSelection = 3,
    Search = new CpuSearchSettings
    {
      // The local CPU runs on a worker, but it still has a visible hard ceiling. The deterministic
      // node cap normally finishes this much sooner; three seconds is the requested failsafe for
      // unusually complex boards or slow machines.
      BeamWidth = 6,
      CandidatesPerNode = 9,
      OpponentBeamWidth = 2,
      OpponentActionsToPredict = 1,
      MaxSearchNodes = 180,
      MaximumPurchasePlacementCandidates = 12,
      MaxSearchMilliseconds = 3_000,
      Randomness = 0.06f
    }
  };

  public static CpuProfile Hard(int seed = 1) => new()
  {
    Name = "Hard CPU",
    Difficulty = CpuDifficultyLevel.Hard,
    RandomSeed = seed,
    MistakeChance = 0f,
    TopChoicesForRandomSelection = 1,
    Search = new CpuSearchSettings
    {
      BeamWidth = 18,
      CandidatesPerNode = 24,
      OpponentBeamWidth = 8,
      OpponentActionsToPredict = 3,
      MaxSearchNodes = 2_400,
      MaximumPurchasePlacementCandidates = 60,
      MaxSearchMilliseconds = 650,
      Randomness = 0f
    }
  };
}
