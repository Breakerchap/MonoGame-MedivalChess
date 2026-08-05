# Medieval Chess CPU Player

## Goal

Implement a flexible CPU player for **MedivalChess** that can:

- play ordinary matches as another player;
- support campaign missions with different boards, formations, restrictions, and win conditions;
- cope reasonably well with future unit and balance changes;
- offer several difficulty levels;
- remain understandable, testable, and easy to debug;
- avoid machine learning or any training requirement.

The CPU should use a combination of:

1. **legal action generation**;
2. **goal-aware utility scoring**;
3. **beam search over the CPU's three actions per turn**;
4. **limited prediction of the opponent's response**;
5. **scenario-specific weights and rules**.

Do **not** implement a neural network, reinforcement-learning system, or Stockfish-scale exhaustive search.

---

## Important project assumptions

Before changing code, inspect the existing repository and adapt this design to the actual classes and naming already present.

Known or likely existing concepts include:

- `Game1`
- `Board`
- `Piece`
- `PieceDefinition`
- `PieceDefinitions`
- `PieceSetup`
- `Movement`
- `Team`
- `TeamName`
- `PieceType`
- three actions per turn
- variable board shapes
- units with different movement, attack patterns, costs, sizes, abilities, and health
- scenario-specific starting formations and restrictions

Do not duplicate existing rule logic. The CPU must call the same movement, attack, purchase, placement, and ability validation code used by human players.

If the existing code is too tightly coupled to rendering or mouse input, first extract the game-rule logic into reusable classes rather than reimplementing it inside the CPU.

---

# Core design

## 1. Game state must be simulatable

The CPU must be able to examine hypothetical actions without modifying the real match.

Create or adapt a `GameState` class containing all gameplay data required to resolve a turn, including:

```csharp
public sealed class GameState
{
  public BoardState Board { get; }
  public IReadOnlyList<PieceState> Pieces { get; }
  public IReadOnlyDictionary<TeamName, TeamState> Teams { get; }
  public TeamName CurrentTurn { get; }
  public int ActionsRemaining { get; }
  public int TurnNumber { get; }
  public ScenarioState Scenario { get; }
}
```

The precise structure should match the existing project.

The important requirements are:

- it contains no rendering state;
- it can be cloned efficiently;
- actions can be applied to a copied state;
- scenario progress can be evaluated from it;
- the real match is changed only after the CPU has selected a final action sequence.

Prefer immutable or copy-on-write state where practical. If full immutable state is too large a refactor, use a reliable deep-copy method.

Do not use JSON serialisation to clone states during search because it will be unnecessarily slow.

---

## 2. Represent every possible decision as a game action

Create a common action abstraction.

```csharp
public interface IGameAction
{
  TeamName Team { get; }

  bool IsLegal(GameState state);

  void Apply(GameState state);

  string Describe();
}
```

Likely action types include:

```csharp
MoveAction
AttackAction
MoveAndAttackAction
PurchaseAction
PlaceUnitAction
UseAbilityAction
BuildRoadAction
BuildBarrierAction
BuildMineAction
FireMercenaryAction
HireMercenaryAction
EndTurnAction
```

Only implement action types that correspond to real game mechanics.

Every action must use the central gameplay rules. For example, `MoveAction.IsLegal` should rely on the same movement validation used for human input.

Actions should contain enough information to be deterministic, such as:

```csharp
public sealed record MoveAction(
  TeamName Team,
  int PieceId,
  BoardPosition Destination
) : IGameAction;
```

Assign each piece a stable unique ID if pieces do not already have one.

---

## 3. Central legal-action generator

Create:

```csharp
public interface IActionGenerator
{
  IReadOnlyList<IGameAction> GenerateLegalActions(
    GameState state,
    TeamName team
  );
}
```

This class should generate every currently legal action for the team, including ending the turn where allowed.

However, the search should not always analyse every legal action equally. Add a second stage that ranks and filters actions.

```csharp
public interface IActionCandidateSelector
{
  IReadOnlyList<ScoredAction> SelectCandidates(
    GameState state,
    TeamName team,
    IReadOnlyList<IGameAction> legalActions,
    CpuSearchSettings settings
  );
}
```

The selector should prioritise actions such as:

- attacks that kill an enemy;
- attacks against important or vulnerable units;
- actions that prevent an immediate loss;
- progress towards the scenario objective;
- capturing or defending income and control points;
- protecting a royal, escort target, or required structure;
- moving units out of immediate danger;
- completing useful combinations;
- purchasing units that solve a clear tactical problem;
- using a relevant unit ability.

It should deprioritise actions such as:

- moving backwards from all objectives without a defensive reason;
- repeatedly moving the same unit with no gain;
- exposing a royal for no benefit;
- buying a unit that cannot contribute;
- wasting actions;
- undoing an earlier movement in the same turn;
- pointless road, barrier, or mine placement.

Do not completely remove unusual legal actions at high difficulty unless they are provably dominated or pointless. At lower difficulties, stronger filtering is acceptable.

---

# Search algorithm

## 4. Use beam search for the CPU's turn

The game uses three actions per turn, so searching every combination may become too expensive.

Use **beam search**:

1. Start with the current state.
2. Generate candidate first actions.
3. Simulate each action.
4. Score each resulting partial turn.
5. Keep only the best `BeamWidth` states.
6. Repeat for the second and third actions.
7. Optionally estimate the opponent's best response.
8. select the best complete action sequence.

Suggested structures:

```csharp
public sealed record SearchNode(
  GameState State,
  IReadOnlyList<IGameAction> Actions,
  float Score,
  SearchMetadata Metadata
);
```

```csharp
public sealed class CpuSearchSettings
{
  public int BeamWidth { get; init; } = 12;
  public int CandidatesPerNode { get; init; } = 16;
  public int OpponentBeamWidth { get; init; } = 6;
  public int OpponentActionsToPredict { get; init; } = 1;
  public int MaxSearchMilliseconds { get; init; } = 250;
  public float Randomness { get; init; } = 0.05f;
}
```

The values above are initial defaults, not fixed requirements.

Search must support cancellation and a time limit.

```csharp
public interface ICpuPlayer
{
  CpuTurnPlan ChooseTurn(
    GameState state,
    TeamName team,
    CpuProfile profile,
    CancellationToken cancellationToken
  );
}
```

```csharp
public sealed record CpuTurnPlan(
  IReadOnlyList<IGameAction> Actions,
  float EstimatedScore,
  CpuDecisionReport Report
);
```

The CPU should normally return up to three actions, but fewer may be valid if:

- the scenario ends;
- the CPU has no legal action;
- ending the turn is strategically preferred;
- an action consumes multiple action points;
- the game's rules otherwise end the turn.

---

## 5. Predict a limited opponent response

After finding the strongest CPU turn sequences, optionally predict the opponent's reply.

Do not initially search several full turns ahead. Start with:

- Easy: no opponent response;
- Normal: one high-priority opponent action;
- Hard: one complete opponent turn using a smaller beam;
- Very Hard, if later needed: one opponent turn plus a shallow CPU follow-up.

Use a minimax-style adjustment:

```text
final score =
  score after CPU turn
  - opponent response value × response weight
```

The opponent should use the same evaluator from the opposite team's perspective.

Avoid recursive search without strict depth and time limits.

---

# State evaluation

## 6. Build a modular heuristic evaluator

Create:

```csharp
public interface IStateEvaluator
{
  float Evaluate(
    GameState state,
    TeamName perspective,
    EvaluationContext context
  );
}
```

The evaluator should combine several separate terms:

```text
Total score =
  material
  + health
  + tactical threats
  + royal safety
  + objective progress
  + map control
  + economy
  + formation
  + mobility
  + scenario-specific score
  - immediate risks
```

Create each term separately so it can be tested and tuned.

```csharp
public interface IEvaluationTerm
{
  string Name { get; }

  float Evaluate(
    GameState state,
    TeamName perspective,
    EvaluationContext context
  );
}
```

Suggested terms:

```text
MaterialEvaluation
HealthEvaluation
ThreatEvaluation
RoyalSafetyEvaluation
ObjectiveEvaluation
MapControlEvaluation
EconomyEvaluation
FormationEvaluation
MobilityEvaluation
ScenarioEvaluation
RepetitionPenalty
ActionEfficiencyEvaluation
```

The evaluator should return extremely large positive or negative scores for definite wins and losses.

```csharp
public static class EvaluationScores
{
  public const float Win = 1_000_000f;
  public const float Loss = -1_000_000f;
}
```

Do not use `float.PositiveInfinity`, because it can complicate sorting and arithmetic.

---

## 7. Material and health scoring

Do not value units using attack alone.

Create a base strategic value for each unit definition using either:

1. an explicit `CpuValue` field; or
2. a calculated estimate derived from cost and capabilities.

Prefer an optional explicit value with a cost-based fallback:

```csharp
public sealed class PieceDefinition
{
  // Existing properties...

  public float? CpuValue { get; init; }
}
```

Fallback example:

```text
base value =
  cost
  + health contribution
  + attack contribution
  + mobility contribution
  + range contribution
  + ability contribution
```

The exact formula must remain easy to tune.

A damaged unit should retain part of its value:

```text
current value =
  base value × health fraction modifier
```

Do not make value perfectly linear with health. A one-health unit can still attack, block, capture, or fulfil an objective.

Royal and scenario-critical units must use special values based on the current win conditions.

---

## 8. Tactical threats

The CPU must recognise at least:

- attacks available immediately;
- units that can be killed this turn;
- enemy attacks likely next turn;
- attacks on royal or objective units;
- multiple attackers focusing one target;
- counterattacks after moving;
- dangerous ranged lanes;
- blocked movement routes;
- units trapped or nearly trapped.

Create helper methods such as:

```csharp
ThreatMap BuildThreatMap(GameState state, TeamName team);
```

A threat map should record:

- which squares can be attacked;
- expected damage to each square or piece;
- number of attackers;
- strongest attacker;
- whether the attack is lethal;
- whether the threatened unit is strategically important.

Cache threat maps within a single state evaluation where possible.

---

## 9. Objective-aware behaviour

Campaign scenarios must not require a completely separate CPU implementation.

Create scenario goals:

```csharp
public interface IScenarioGoal
{
  GoalStatus GetStatus(GameState state, TeamName team);

  float EvaluateProgress(GameState state, TeamName team);

  IEnumerable<CpuIntent> GenerateIntents(
    GameState state,
    TeamName team
  );
}
```

Possible goals include:

```text
DefeatRoyalGoal
EliminateAllEnemiesGoal
SurviveTurnsGoal
CaptureLocationsGoal
HoldAreaGoal
EscortUnitGoal
ProtectUnitGoal
EscapeBoardGoal
PreventEscapeGoal
DestroyStructuresGoal
AccumulateGoldGoal
PlunderGoal
ReachLocationGoal
DefendLocationGoal
CustomScriptedGoal
```

Each mission may contain:

- one or more victory goals;
- one or more defeat conditions;
- optional secondary goals;
- restricted unit types;
- restricted purchases;
- scripted reinforcements;
- starting formations;
- special board rules;
- turn limits;
- altered evaluation weights.

Example:

```csharp
public sealed class ScenarioDefinition
{
  public string Id { get; init; } = "";
  public IReadOnlyList<IScenarioGoal> VictoryGoals { get; init; } = [];
  public IReadOnlyList<IScenarioGoal> DefeatConditions { get; init; } = [];
  public ScenarioRestrictions Restrictions { get; init; } = new();
  public CpuScenarioWeights CpuWeights { get; init; } = new();
}
```

The CPU must always check scenario rules before generating actions.

---

# Intent system

## 10. Give the CPU short-term intentions

Utility scoring alone may cause indecisive or repetitive movement.

Add temporary intentions that last for part of a turn or several turns:

```csharp
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
```

```csharp
public sealed record CpuIntent(
  CpuIntentType Type,
  float Priority,
  int? PieceId = null,
  int? TargetPieceId = null,
  BoardPosition? TargetPosition = null,
  int ExpiryTurn = 0
);
```

Generate intentions from:

- scenario objectives;
- immediate tactical threats;
- army composition;
- CPU personality;
- recent events.

Intentions should influence scoring, not override legality.

The CPU should be allowed to abandon an intention when:

- it becomes impossible;
- the target dies;
- the scenario changes;
- a much more urgent threat appears;
- following it would cause a clear loss.

---

# Difficulty and personality

## 11. Difficulty levels

Difficulty should change search quality, not simply give the CPU unfair statistics.

Suggested profiles:

### Easy

- narrow beam;
- no opponent prediction;
- substantial randomness;
- ignores some low-priority threats;
- may select from the top few actions rather than always taking the best;
- shorter time limit.

### Normal

- medium beam;
- searches all three CPU actions;
- predicts one opponent action;
- low randomness;
- uses all major evaluation terms.

### Hard

- wider beam;
- predicts a complete opponent turn;
- lower randomness;
- better candidate selection;
- stronger threat analysis;
- longer but still bounded time limit.

Example:

```csharp
public sealed class CpuDifficulty
{
  public string Name { get; init; } = "";
  public CpuSearchSettings Search { get; init; } = new();
  public EvaluationWeights Weights { get; init; } = new();
  public float MistakeChance { get; init; }
  public int TopChoicesForRandomSelection { get; init; } = 1;
}
```

Do not implement difficulty by choosing a completely random legal move. Easy AI should still appear to have an understandable goal.

---

## 12. Personalities

Campaign opponents should be able to feel different without needing unique AI code.

Create configurable personality modifiers:

```csharp
public sealed class CpuPersonality
{
  public float Aggression { get; init; } = 1f;
  public float Caution { get; init; } = 1f;
  public float ObjectiveFocus { get; init; } = 1f;
  public float EconomyFocus { get; init; } = 1f;
  public float RoyalProtection { get; init; } = 1f;
  public float FormationPreference { get; init; } = 1f;
  public float AbilityUsage { get; init; } = 1f;
}
```

Possible presets:

```text
Balanced
Aggressive
Defensive
Greedy
Reckless
ObjectiveFocused
Swarmer
```

Personalities modify evaluation weights and candidate priorities. They must not change the actual game rules.

---

# Preventing stupid behaviour

## 13. Repetition and reversal penalties

Track recent CPU actions and penalise:

- moving a piece from A to B and immediately back to A;
- repeatedly switching between two equal positions;
- moving several pieces without advancing any objective;
- repeatedly considering an impossible purchase;
- ending turns with unused actions when useful actions exist.

Do not ban reversals completely. Retreating to a previous square may sometimes be correct.

---

## 14. Combination awareness

Because a turn contains three actions, the CPU should recognise combinations such as:

- move, then attack;
- weaken a target, then finish it with another unit;
- move a blocker, then attack through the opened lane;
- build a road, then use its movement benefit;
- purchase, place, then attack where rules permit;
- defend an objective, then reposition a ranged unit;
- hire a mercenary, then use it;
- move two units to surround or trap an enemy.

Action evaluation should include both:

- immediate action score;
- resulting-state score.

Do not greedily lock in the first action before considering later actions.

---

## 15. Action-order normalisation

Different action orders may reach the same final state.

Where practical, detect equivalent states and keep only the strongest route to each state.

Create a compact state hash including gameplay-relevant data:

```csharp
ulong ComputeSearchHash(GameState state);
```

The hash should include:

- unit IDs, teams, positions, and health;
- team money and relevant resources;
- actions remaining;
- current player;
- scenario progress;
- temporary effects;
- constructed objects;
- relevant cooldowns or statuses.

It should exclude:

- animation state;
- camera;
- UI selection;
- particle effects;
- sound;
- non-gameplay timestamps.

Use a dictionary to prevent duplicate search nodes during a single search.

---

# Performance

## 16. Keep search bounded

The CPU must not freeze the game.

Use:

- maximum search time;
- maximum beam width;
- maximum candidates per node;
- cancellation tokens;
- cached evaluations;
- cached legal actions where state identity matches;
- duplicate-state removal;
- prioritised action generation;
- optional background calculation.

If using a background thread:

- never modify MonoGame graphics resources from it;
- never mutate the live game state from it;
- return a completed `CpuTurnPlan` to the main thread;
- verify each action is still legal before applying it.

The search should be deterministic when given the same:

- game state;
- CPU profile;
- random seed.

Add a seed to the CPU profile or match state.

---

## 17. Apply CPU actions visibly

After the CPU chooses a turn, do not instantly teleport the match to the final state unless a fast-simulation mode is active.

The normal game should:

1. calculate the CPU plan;
2. queue the chosen actions;
3. apply them one at a time through the normal gameplay system;
4. show movement and attack animations;
5. pause briefly between actions;
6. revalidate before applying;
7. stop if the scenario ends.

The AI decision system and the action-animation system must remain separate.

---

# Debugging tools

## 18. Decision reports

Every CPU decision should optionally produce a debug report.

```csharp
public sealed class CpuDecisionReport
{
  public TimeSpan SearchTime { get; init; }
  public int NodesGenerated { get; init; }
  public int NodesEvaluated { get; init; }
  public int DuplicateStatesRemoved { get; init; }
  public IReadOnlyList<CpuChoiceReport> TopChoices { get; init; } = [];
}
```

Each reported choice should show:

```text
Action sequence
Final score
Material score
Objective score
Threat score
Royal-safety score
Economy score
Personality modifiers
Opponent-response penalty
Reason it was selected
```

Add an optional debug overlay or console command that can display:

- current CPU intentions;
- threat maps;
- legal candidate actions;
- top action sequences;
- evaluation breakdown;
- state hashes;
- search duration.

This is important for balancing the game and fixing apparently irrational moves.

---

# Suggested code organisation

Adapt this to the repository rather than forcing these exact paths.

```text
MedivalChess/
  CPU/
    ICpuPlayer.cs
    CpuPlayer.cs
    CpuTurnPlan.cs
    CpuProfile.cs
    CpuDifficulty.cs
    CpuPersonality.cs

    Actions/
      IGameAction.cs
      MoveAction.cs
      AttackAction.cs
      PurchaseAction.cs
      UseAbilityAction.cs
      EndTurnAction.cs

    Search/
      BeamSearch.cs
      SearchNode.cs
      CpuSearchSettings.cs
      GameStateHasher.cs
      SearchCache.cs

    Evaluation/
      IStateEvaluator.cs
      StateEvaluator.cs
      IEvaluationTerm.cs
      EvaluationWeights.cs
      MaterialEvaluation.cs
      ThreatEvaluation.cs
      RoyalSafetyEvaluation.cs
      ObjectiveEvaluation.cs
      EconomyEvaluation.cs
      MobilityEvaluation.cs

    Goals/
      IScenarioGoal.cs
      DefeatRoyalGoal.cs
      SurviveTurnsGoal.cs
      CaptureLocationsGoal.cs
      EscortUnitGoal.cs
      EscapeBoardGoal.cs

    Intentions/
      CpuIntent.cs
      CpuIntentType.cs
      CpuIntentGenerator.cs

    Debugging/
      CpuDecisionReport.cs
      CpuDebugFormatter.cs
```

Do not create unnecessary files for tiny types if the existing project style groups related records and enums.

---

# Implementation order

Implement in small working stages.

## Phase 1 — Extract reusable game rules

- Identify all human-player movement, attack, purchase, ability, and turn rules.
- Remove dependencies on mouse input, rendering, and UI where necessary.
- Create a reusable `GameState`.
- Create reliable state cloning.
- Add stable piece IDs.
- Add tests for cloning and applying actions.

Do not begin strategic search until simulated actions behave identically to real actions.

## Phase 2 — Basic legal CPU

- Implement action representations.
- Implement legal action generation.
- Create a simple evaluator using material, health, and immediate wins/losses.
- Make the CPU choose the highest-scoring single action.
- Let it take up to three sequential actions.
- Add deterministic random tie-breaking.

At the end of this phase, the CPU should complete legal turns without crashing, even if it is strategically weak.

## Phase 3 — Full-turn beam search

- Search complete three-action sequences.
- Add candidate ranking.
- Add duplicate-state detection.
- Add a time limit.
- Add debug decision reports.
- Compare greedy decisions with beam-search decisions in tests.

## Phase 4 — Tactical awareness

- Add threat maps.
- Add lethal attack detection.
- Add royal safety.
- Add retreats and defensive moves.
- Add combination awareness.
- Add limited opponent response prediction.

## Phase 5 — Campaign goals

- Implement the scenario-goal interface.
- Add goal progress evaluation.
- Add scenario restrictions.
- Add at least:
  - defeat royal;
  - eliminate enemies;
  - survive for turns;
  - capture or hold locations;
  - escort or protect a unit;
  - escape or prevent escape.
- Ensure scenario goals alter behaviour without replacing the CPU engine.

## Phase 6 — Difficulty and personalities

- Add Easy, Normal, and Hard.
- Add personality weight modifiers.
- Add controlled mistakes and top-choice randomness.
- Add campaign-specific CPU profiles.

## Phase 7 — Optimisation and polish

- Profile search.
- Reduce allocations in hot loops.
- Cache repeated calculations.
- Add visible action playback.
- Add debug overlay.
- Run automated CPU-versus-CPU matches for balance testing.

---

# Testing requirements

Use the project's existing test framework, or create a separate test project if none exists.

At minimum, add tests for:

## Rules and state

- cloning a state does not affect the original;
- simulated movement matches real movement;
- simulated attacks match real attacks;
- unit death and removal work correctly;
- purchases and placement obey restrictions;
- three-action turn accounting is correct;
- scenario victory and defeat are detected.

## CPU legality

- every returned action is legal;
- the CPU does not act for the wrong team;
- the CPU does not exceed its actions;
- the CPU stops after winning or losing;
- the CPU handles no-action states;
- the CPU handles unusual board shapes;
- the CPU handles missing unit types safely.

## Basic strategy

Create small deterministic board states where the correct behaviour is obvious:

- take an undefended lethal attack;
- avoid exposing the royal to immediate death;
- finish a damaged enemy instead of spreading pointless damage;
- capture the final required objective;
- protect an escort target;
- move towards an escape tile;
- block an escaping enemy;
- prefer a winning action over material gain;
- avoid purchasing when it prevents a required objective;
- use a two- or three-action combination when a greedy move misses it.

## Performance

- search respects its time limit;
- cancellation stops search;
- deterministic seeds reproduce decisions;
- duplicate-state detection reduces repeated nodes;
- the CPU can complete many CPU-versus-CPU turns without memory growth or crashes.

---

# Automated balancing support

Add a headless or accelerated simulation mode that can run CPU-versus-CPU matches without rendering.

It should be able to record:

```text
Scenario ID
Board ID
CPU profiles
Winner
Turn count
Units purchased
Units lost
Damage dealt
Gold earned and spent
Objectives completed
Average search time
Search nodes per turn
Reason the match ended
```

This is not machine-learning training. It is automated playtesting that can reveal:

- overpowered units;
- dominant openings;
- unfair scenarios;
- weak CPU weights;
- stalemates;
- excessive match length;
- broken campaign restrictions.

Keep this simulator separate from normal gameplay presentation.

---

# Code quality rules

- Use Australian spelling in comments and documentation where natural.
- Follow the repository's existing indentation and naming conventions.
- Use clear names rather than abbreviations inside CPU code.
- Do not place all CPU logic in `Game1`.
- Do not duplicate movement or combat rules.
- Avoid static global mutable CPU state.
- Avoid unexplained magic numbers.
- Put tunable values in profiles or weight objects.
- Document non-obvious heuristics.
- Keep search deterministic for testing.
- Add XML documentation to important public interfaces.
- Prefer dependency injection through constructors over direct singleton access.
- Do not silently swallow exceptions.
- Do not make rendering classes responsible for strategic decisions.
- Do not add machine-learning packages.

---

# Initial evaluation weights

Use these only as starting values and place them in configuration rather than hard-coding them throughout the evaluator.

```csharp
public sealed class EvaluationWeights
{
  public float Material { get; init; } = 1.0f;
  public float Health { get; init; } = 0.35f;
  public float ImmediateThreats { get; init; } = 0.9f;
  public float RoyalSafety { get; init; } = 2.0f;
  public float ObjectiveProgress { get; init; } = 2.5f;
  public float MapControl { get; init; } = 0.4f;
  public float Economy { get; init; } = 0.5f;
  public float Mobility { get; init; } = 0.25f;
  public float Formation { get; init; } = 0.2f;
  public float ActionEfficiency { get; init; } = 0.4f;
  public float RepetitionPenalty { get; init; } = 0.8f;
}
```

Scenario definitions and personality profiles may modify these values.

For example:

```text
Assassination mission:
  RoyalSafety: lower for attacker
  ObjectiveProgress: very high
  EnemyRoyalThreat: extremely high

Survival mission:
  OwnUnitSurvival: high
  DefensivePositioning: high
  ObjectiveProgress: based on turns survived

Plunder mission:
  Gold and plunder score: high
  Material: moderate
  Escape safety: high after collecting required loot
```

---

# Acceptance criteria

The work is complete when:

1. A CPU-controlled team can legally complete full turns.
2. The same CPU can be used in ordinary matches and campaign missions.
3. Campaign win conditions influence CPU behaviour.
4. Different difficulty profiles visibly affect decision quality and search cost.
5. Different personalities visibly affect priorities without changing rules.
6. The CPU can consider combinations across all three actions.
7. The CPU recognises immediate wins, losses, royal threats, and obvious kills.
8. Search remains within configured time limits.
9. Decisions can be reproduced with a fixed seed.
10. A debug report explains why a sequence was chosen.
11. Automated tests cover legality, scenarios, basic tactics, and performance.
12. No training process or machine-learning dependency is required.

---

# Instructions for Codex while working

1. Inspect the repository before proposing class names or edits.
2. Summarise the current game-state, turn, action, and scenario architecture.
3. Identify any rule logic currently tied to UI or rendering.
4. Implement the CPU incrementally in the phases above.
5. Keep the project compiling after each meaningful change.
6. Reuse existing gameplay rules instead of duplicating them.
7. Add or update tests with each phase.
8. Explain any necessary architecture changes before making a large refactor.
9. Do not delete working features merely because they complicate the CPU.
10. Do not implement later phases as empty placeholders.
11. When a detail is unclear, infer it from the existing code and choose the least invasive design.
12. Record all tunable CPU values in profiles or scenario configuration.
13. At the end, provide:
    - a summary of files changed;
    - how the search works;
    - how to create a CPU profile;
    - how to attach a CPU to a team;
    - how a campaign scenario supplies goals and weights;
    - how to run the tests;
    - known limitations and sensible next improvements.

The priority is a CPU that is **flexible, debuggable, and good enough to feel intentional**, not one that tries to calculate perfect play.
