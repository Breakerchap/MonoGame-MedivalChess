using MedivalChess.CPU;
using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class CpuPlanningEfficiencyTests
{
  [Fact]
  public void UnlimitedTurn_ImmediateAttackHappensBeforePurchase()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 500,
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0),
        Piece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, 1));
      CpuProfile profile = new()
      {
        Name = "Combat before shopping test",
        Difficulty = CpuDifficultyLevel.Hard,
        Search = new CpuSearchSettings
        {
          BeamWidth = 8,
          CandidatesPerNode = 12,
          PromisingCandidatesPerNode = 10,
          OpponentActionsToPredict = 0,
          TacticalExtensionDepth = 1,
          MaxSearchNodes = 8_000,
          MaximumPurchasePlacementCandidates = 12,
          MaxSearchMilliseconds = 700,
          MaxParallelism = 1,
          Randomness = 0f
        },
        TopChoicesForRandomSelection = 1,
        MistakeChance = 0f,
        StrategyVariationChance = 0f
      };

      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);

      AttackAction first = Assert.IsType<AttackAction>(plan.Actions.First());
      Assert.Equal("red-soldier", first.AttackerId);
      Assert.Equal("blue-peasant", first.TargetPieceId);
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Theory]
  [InlineData("Conquest")]
  [InlineData("Regicide")]
  public void ThreatenedKing_RespondsToAdjacentKnightBeforeShopping(string gameMode)
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      NetworkMatchConfiguration configuration = new(
        "Small", "Light", "Light", gameMode, 7719, 500, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
      CpuGameState state = new(
        configuration,
        [
          Piece("red-king", "King", NetworkTeam.Red, 0, 0),
          Piece("blue-knight", "Knight", NetworkTeam.Blue, 0, 1),
          Piece("blue-king", "King", NetworkTeam.Blue, 0, -6)
        ],
        [
          new CpuTeamState(NetworkTeam.Red, 500, MatchRules.ActionsPerTurn),
          new CpuTeamState(NetworkTeam.Blue, 500, MatchRules.ActionsPerTurn)
        ],
        NetworkTeam.Red,
        terrain: new BattlefieldTerrain());
      CpuProfile profile = new()
      {
        Name = "Royal response test",
        Difficulty = CpuDifficultyLevel.Hard,
        Search = new CpuSearchSettings
        {
          BeamWidth = 10,
          CandidatesPerNode = 14,
          PromisingCandidatesPerNode = 12,
          OpponentActionsToPredict = 0,
          TacticalExtensionDepth = 1,
          MaxSearchNodes = 12_000,
          MaximumPurchasePlacementCandidates = 12,
          MaxSearchMilliseconds = 800,
          MaxParallelism = 1,
          Randomness = 0f
        },
        TopChoicesForRandomSelection = 1,
        MistakeChance = 0f,
        StrategyVariationChance = 0f
      };

      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
      ICpuGameAction first = Assert.IsAssignableFrom<ICpuGameAction>(plan.Actions.First());

      Assert.False(first is PurchaseAction, string.Join(" | ", plan.Actions.Select(action => action.Describe())));
      Assert.True(first is AttackAction { AttackerId: "red-king", TargetPieceId: "blue-knight" } or
        MoveAction { PieceId: "red-king" },
        string.Join(" | ", plan.Actions.Select(action => action.Describe())));
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void SearchPurchases_ClusterManyLegalSquaresIntoFewRepresentatives()
  {
    CpuGameState state = CreateState(
      money: 500,
      Piece("red", "Soldier", NetworkTeam.Red, 0, 0),
      Piece("blue", "Soldier", NetworkTeam.Blue, 8, 8));
    CpuActionGenerator generator = new();

    PurchaseAction[] exhaustive = generator.GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType == "Soldier")
      .ToArray();
    PurchaseAction[] search = generator.GenerateSearchActions(state, NetworkTeam.Red, 96)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType == "Soldier")
      .ToArray();

    Assert.True(exhaustive.Length > search.Length,
      $"exhaustive={exhaustive.Length}, search={search.Length}");
    Assert.InRange(search.Length, 1, 12);
    Assert.Equal(search.Length, search.Select(action => (action.X, action.Y)).Distinct().Count());
  }

  [Fact]
  public void FarmPlacement_HeavilyPrefersRearTerritory()
  {
    CpuGameState state = CreateFarmState(
      money: 500,
      Piece("red", "Soldier", NetworkTeam.Red, 0, 0),
      Piece("blue", "Soldier", NetworkTeam.Blue, 8, 8));
    CpuActionGenerator generator = new();
    PurchaseAction[] farms = generator.GenerateLegalActions(state, NetworkTeam.Red)
      .OfType<PurchaseAction>()
      .Where(action => action.UnitType == "Farm")
      .ToArray();
    Assert.NotEmpty(farms);

    (int x, int y) forward = TeamRules.GetForwardDirection(NetworkTeam.Red);
    int Projection(PurchaseAction action) => action.X * forward.x + action.Y * forward.y;
    PurchaseAction rear = farms.OrderBy(Projection).First();
    PurchaseAction front = farms.OrderByDescending(Projection).First();
    Assert.True(Projection(rear) < Projection(front));

    CpuSearchSettings settings = new()
    {
      CandidatesPerNode = 4,
      PromisingCandidatesPerNode = 4,
      MaximumPurchasePlacementCandidates = 12
    };
    IReadOnlyList<ScoredAction> scored = new CpuActionCandidateSelector().SelectCandidates(
      state, NetworkTeam.Red, [rear, front], settings, CpuPersonality.Balanced);
    float rearScore = scored.Single(candidate => Equals(candidate.Action, rear)).Score;
    float frontScore = scored.Single(candidate => Equals(candidate.Action, front)).Score;

    Assert.True(rearScore >= frontScore + 20f, $"rear={rearScore}, front={frontScore}");
  }

  [Fact]
  public void ActionEfficiency_HeavilyPenalisesLeavingAnAvailableAttackUnused()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 0),
        Piece("blue-peasant", "Peasant", NetworkTeam.Blue, 0, 1));
      ActionEfficiencyEvaluation term = new();
      EvaluationContext context = new(CpuProfile.Hard(17));
      float idle = term.Evaluate(state, NetworkTeam.Red, context);
      AttackAction attack = new(NetworkTeam.Red, "red-soldier", "blue-peasant", 0, 1);
      Assert.True(attack.IsLegal(state));
      float afterAttack = term.Evaluate(attack.Apply(state), NetworkTeam.Red, context);

      Assert.True(afterAttack >= idle + 150f, $"idle={idle}, after={afterAttack}");
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void ActionEfficiency_PenalisesLeavingAMobileRemoteUnitCompletelyIdle()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 5),
        Piece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -5));
      ActionEfficiencyEvaluation term = new();
      EvaluationContext context = new(CpuProfile.Hard(18));
      float idle = term.Evaluate(state, NetworkTeam.Red, context);
      MoveAction move = new CpuActionGenerator().GenerateLegalActions(state, NetworkTeam.Red)
        .OfType<MoveAction>()
        .Where(action => action.PieceId == "red-soldier")
        .OrderBy(action => Math.Abs(action.DestinationY - (-5)))
        .First();
      float afterMove = term.Evaluate(move.Apply(state), NetworkTeam.Red, context);

      Assert.True(afterMove > idle + 10f, $"idle={idle}, after={afterMove}, move={move.Describe()}");
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void UnlimitedTurn_NeverEndsWhileALegalAttackRemains()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-soldier-one", "Soldier", NetworkTeam.Red, 0, 0),
        Piece("red-soldier-two", "Soldier", NetworkTeam.Red, 2, 0),
        Piece("blue-defender-one", "Defender", NetworkTeam.Blue, 0, 1),
        Piece("blue-defender-two", "Defender", NetworkTeam.Blue, 2, 1));
      CpuProfile profile = CpuProfile.Hard(19);

      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, profile, CancellationToken.None);
      CpuGameState current = state;
      foreach (ICpuGameAction action in plan.Actions)
      {
        if (action is EndTurnAction)
        {
          bool attackRemains = new CpuActionGenerator().GenerateSearchActions(current, NetworkTeam.Red, 1)
            .OfType<AttackAction>()
            .Any();
          Assert.False(attackRemains, string.Join(" | ", plan.Actions.Select(candidate => candidate.Describe())));
        }
        Assert.True(action.IsLegal(current), action.Describe());
        current = action.Apply(current);
      }

      Assert.Contains(plan.Actions, action => action is AttackAction { AttackerId: "red-soldier-one" });
      Assert.Contains(plan.Actions, action => action is AttackAction { AttackerId: "red-soldier-two" });
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  [Fact]
  public void SearchMovement_ClustersDestinationsPerPiece()
  {
    CpuGameState state = CreateState(
      money: 0,
      Piece("red-knight", "Knight", NetworkTeam.Red, 0, 5),
      Piece("red-archer", "Archer", NetworkTeam.Red, 2, 5),
      Piece("red-soldier", "Soldier", NetworkTeam.Red, -2, 5),
      Piece("blue-king", "King", NetworkTeam.Blue, 0, -8));
    CpuActionGenerator generator = new();
    MoveAction[] exhaustive = generator.GenerateLegalActions(state, NetworkTeam.Red).OfType<MoveAction>().ToArray();
    MoveAction[] search = generator.GenerateSearchActions(state, NetworkTeam.Red, 1).OfType<MoveAction>().ToArray();

    Assert.True(exhaustive.Length > search.Length, $"exhaustive={exhaustive.Length}, search={search.Length}");
    Assert.All(search.GroupBy(move => move.PieceId), group => Assert.InRange(group.Count(), 1, 5));
  }

  [Fact]
  public void SearchMovement_PreservesAMoveThatCreatesAnAttack()
  {
    CpuGameState state = CreateState(
      money: 0,
      Piece("red-knight", "Knight", NetworkTeam.Red, 0, 3),
      Piece("red-king", "King", NetworkTeam.Red, 3, 7),
      Piece("blue-archer", "Archer", NetworkTeam.Blue, 0, -2),
      Piece("blue-king", "King", NetworkTeam.Blue, 3, -7));
    CpuActionGenerator generator = new();
    MoveAction[] exhaustiveAttackMoves = generator.GenerateLegalActions(state, NetworkTeam.Red).OfType<MoveAction>()
      .Where(move => move.PieceId == "red-knight")
      .Where(move =>
      {
        CpuGameState moved = move.Apply(state);
        NetworkPiece knight = moved.Pieces.Single(piece => piece.Id == "red-knight");
        return moved.Pieces.Any(enemy => enemy.Team == NetworkTeam.Blue &&
          CpuGameRules.CanDirectlyAttack(moved, knight, enemy));
      }).ToArray();
    Assert.NotEmpty(exhaustiveAttackMoves);

    MoveAction[] searchMoves = generator.GenerateSearchActions(state, NetworkTeam.Red, 1).OfType<MoveAction>()
      .Where(move => move.PieceId == "red-knight").ToArray();
    Assert.Contains(searchMoves, move =>
    {
      CpuGameState moved = move.Apply(state);
      NetworkPiece knight = moved.Pieces.Single(piece => piece.Id == "red-knight");
      return moved.Pieces.Any(enemy => enemy.Team == NetworkTeam.Blue &&
        CpuGameRules.CanDirectlyAttack(moved, knight, enemy));
    });
  }

  [Fact]
  public void UnlimitedTurn_CompletesClearlyUsefulMovesForUntouchedCombatUnits()
  {
    bool previous = Globals.ActionLimitsEnabled;
    Globals.ActionLimitsEnabled = false;
    try
    {
      CpuGameState state = CreateState(
        money: 0,
        Piece("red-knight", "Knight", NetworkTeam.Red, -2, 5),
        Piece("red-archer", "Archer", NetworkTeam.Red, 2, 5),
        Piece("red-soldier", "Soldier", NetworkTeam.Red, 0, 4),
        Piece("red-defender", "Defender", NetworkTeam.Red, -1, 6),
        Piece("red-king", "King", NetworkTeam.Red, 0, 8),
        Piece("blue-knight", "Knight", NetworkTeam.Blue, 2, -5),
        Piece("blue-archer", "Archer", NetworkTeam.Blue, -2, -5),
        Piece("blue-soldier", "Soldier", NetworkTeam.Blue, 0, -4),
        Piece("blue-defender", "Defender", NetworkTeam.Blue, 1, -6),
        Piece("blue-king", "King", NetworkTeam.Blue, 0, -8));
      CpuTurnPlan plan = new CpuPlayer().ChooseTurn(state, NetworkTeam.Red, CpuProfile.Hard(812), CancellationToken.None);
      HashSet<string> active = plan.Actions.SelectMany(action => action switch
      {
        MoveAction move => new[] { move.PieceId },
        AttackAction attack => new[] { attack.AttackerId },
        UseAbilityAction ability => new[] { ability.ActorId },
        _ => Array.Empty<string>()
      }).ToHashSet(StringComparer.Ordinal);

      Assert.Contains("red-knight", active);
      Assert.Contains("red-archer", active);
      Assert.Contains("red-soldier", active);
      Assert.Contains("red-defender", active);
      Assert.IsType<EndTurnAction>(plan.Actions[^1]);
    }
    finally
    {
      Globals.ActionLimitsEnabled = previous;
    }
  }

  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y)
  {
    UnitRule rule = UnitRules.GetRequired(type);
    return new NetworkPiece(id, type, team, x, y, rule.Health);
  }

  private static CpuGameState CreateState(int money, params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 11821, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: false);
    return CreateState(configuration, money, pieces);
  }

  private static CpuGameState CreateFarmState(int money, params NetworkPiece[] pieces)
  {
    NetworkMatchConfiguration configuration = new(
      "Small", "Light", "Light", "Conquest", 11821, 200, 0f, 0f, 2, 1, 15, FarmsEnabled: true);
    return CreateState(configuration, money, pieces);
  }

  private static CpuGameState CreateState(
    NetworkMatchConfiguration configuration,
    int money,
    NetworkPiece[] pieces)
  {
    return new CpuGameState(
      configuration,
      pieces,
      [
        new CpuTeamState(NetworkTeam.Red, money, MatchRules.ActionsPerTurn),
        new CpuTeamState(NetworkTeam.Blue, money, MatchRules.ActionsPerTurn)
      ],
      NetworkTeam.Red,
      terrain: new BattlefieldTerrain());
  }
}