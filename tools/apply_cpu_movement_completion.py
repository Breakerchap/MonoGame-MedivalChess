from pathlib import Path

# 1) Cluster movement destinations on the search-only path while preserving the exhaustive legal API.
path = Path('MedivalChess.CPU/CpuActionGenerator.cs')
text = path.read_text()
old = '''    List<ICpuGameAction> actions = [];
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team == team).OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      GenerateMoves(state, piece, actions);
      GenerateAttacks(state, piece, actions);
      GenerateAbilities(state, piece, actions);
    }
    GeneratePurchases(state, team, actions, purchasePlacementLimit, avoidOccupiedPlacements: true);
'''
new = '''    List<ICpuGameAction> actions = [];
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team == team).OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      GenerateClusteredSearchMoves(state, piece, actions);
      GenerateAttacks(state, piece, actions);
      GenerateAbilities(state, piece, actions);
    }
    GeneratePurchases(state, team, actions, purchasePlacementLimit, avoidOccupiedPlacements: true);
'''
if old not in text:
    raise SystemExit('search movement loop not found')
text = text.replace(old, new, 1)

needle = '''  private static void GenerateAttacks(CpuGameState state, NetworkPiece attacker, List<ICpuGameAction> actions)
'''
method = r'''  private sealed record SearchMoveRepresentative(
    MoveAction Action,
    bool CreatesAttack,
    bool CreatesLethalAttack,
    float AttackValue,
    int ObjectiveProgress,
    int EnemyProgress,
    int EnemyDistance,
    int ExposureCount,
    float GeneralScore
  );

  /// <summary>
  /// Search does not need every geometrically equivalent destination. Preserve a small set of
  /// strategically distinct moves per piece, with an explicit slot for move-then-attack tactics.
  /// The exhaustive GenerateLegalActions API remains unchanged.
  /// </summary>
  private static void GenerateClusteredSearchMoves(
    CpuGameState state,
    NetworkPiece piece,
    List<ICpuGameAction> actions,
    int maximumRepresentatives = 5
  )
  {
    MoveAction[] legalMoves = CpuGameRules.GetLegalMovementPaths(state, piece).Keys
      .OrderBy(position => position.y).ThenBy(position => position.x)
      .Select(position => new MoveAction(piece.Team, piece.Id, position.x, position.y))
      .Where(move => move.IsLegal(state))
      .ToArray();
    if (legalMoves.Length <= maximumRepresentatives)
    {
      actions.AddRange(legalMoves);
      return;
    }

    NetworkPiece[] enemies = state.Pieces.Where(other => other.Team != piece.Team &&
      other.Team != NetworkTeam.Neutral && other.AttachedToId is null).ToArray();
    NetworkPiece[] allies = state.Pieces.Where(other => other.Team == piece.Team &&
      other.Id != piece.Id && other.AttachedToId is null).ToArray();
    (int x, int y)[] objectives = GetPurchaseObjectivePositions(state, piece.Team).ToArray();
    int currentEnemyDistance = enemies.Select(enemy => Distance((piece.X, piece.Y), (enemy.X, enemy.Y))).DefaultIfEmpty(0).Min();
    int currentObjectiveDistance = objectives.Select(goal => Distance((piece.X, piece.Y), goal)).DefaultIfEmpty(0).Min();

    SearchMoveRepresentative[] ranked = legalMoves.Select(move =>
    {
      CpuGameState result = CpuGameRules.ApplyLegal(state, move);
      NetworkPiece? moved = result.Pieces.FirstOrDefault(candidate => candidate.Id == piece.Id);
      if (moved is null)
      {
        return new SearchMoveRepresentative(move, false, false, 0f, 0, 0, 0, int.MaxValue, float.MinValue);
      }

      float bestAttackValue = 0f;
      bool lethalAttack = false;
      foreach (NetworkPiece enemy in result.Pieces.Where(other => other.Team != piece.Team &&
        other.Team != NetworkTeam.Neutral && other.AttachedToId is null))
      {
        if (!CpuGameRules.CanDirectlyAttack(result, moved, enemy) || !UnitRules.TryGet(enemy.Type, out UnitRule enemyRule))
        {
          continue;
        }
        int damage = CpuGameRules.EstimateAttackDamage(result, moved, enemy);
        bool lethal = damage >= enemy.Health;
        float targetValue = enemyRule.Cost + enemyRule.Attack * 1.5f + enemyRule.Health * 0.2f +
          (lethal ? 55f : 0f) + (enemyRule.Category == RuleCategory.Royal ? 300f : 0f);
        bestAttackValue = Math.Max(bestAttackValue, targetValue);
        lethalAttack |= lethal;
      }

      int enemyDistance = result.Pieces.Where(other => other.Team != piece.Team &&
          other.Team != NetworkTeam.Neutral && other.AttachedToId is null)
        .Select(enemy => Distance((moved.X, moved.Y), (enemy.X, enemy.Y))).DefaultIfEmpty(0).Min();
      int objectiveDistance = objectives.Select(goal => Distance((moved.X, moved.Y), goal)).DefaultIfEmpty(currentObjectiveDistance).Min();
      int objectiveProgress = objectives.Length == 0 ? 0 : currentObjectiveDistance - objectiveDistance;
      int enemyProgress = enemies.Length == 0 ? 0 : currentEnemyDistance - enemyDistance;
      int exposure = result.Pieces.Count(enemy => enemy.Team != piece.Team && enemy.Team != NetworkTeam.Neutral &&
        enemy.AttachedToId is null && CpuGameRules.CanDirectlyAttack(result, enemy, moved));
      int allyDistance = allies.Select(ally => Distance((moved.X, moved.Y), (ally.X, ally.Y))).DefaultIfEmpty(4).Min();
      float support = Math.Max(0, 4 - allyDistance) * 2f;
      float general = bestAttackValue + objectiveProgress * 22f + enemyProgress * 5f + support - exposure * 24f;
      if (state.Configuration.GameMode == "Conquest" && MatchRules.IsConquestSquare(state.Board, (moved.X, moved.Y))) general += 35f;
      if (state.Configuration.GameMode == "Dominion" && MatchRules.GetDominionControlPoints(state.Board).Contains((moved.X, moved.Y))) general += 35f;
      return new SearchMoveRepresentative(move, bestAttackValue > 0f, lethalAttack, bestAttackValue,
        objectiveProgress, enemyProgress, enemyDistance, exposure, general);
    }).ToArray();

    List<MoveAction> selected = [];
    HashSet<MoveAction> seen = [];
    void Add(SearchMoveRepresentative? representative)
    {
      if (representative is not null && seen.Add(representative.Action)) selected.Add(representative.Action);
    }

    // Tactical slot: never prune the best move that creates a shot. Prefer a kill if one exists.
    Add(ranked.Where(option => option.CreatesAttack)
      .OrderByDescending(option => option.CreatesLethalAttack)
      .ThenByDescending(option => option.AttackValue)
      .ThenBy(option => option.ExposureCount)
      .ThenByDescending(option => option.GeneralScore)
      .FirstOrDefault());
    // Objective slot.
    Add(ranked.OrderByDescending(option => option.ObjectiveProgress)
      .ThenByDescending(option => option.GeneralScore).FirstOrDefault());
    // Aggressive contact slot.
    Add(ranked.OrderByDescending(option => option.EnemyProgress)
      .ThenBy(option => option.ExposureCount)
      .ThenByDescending(option => option.GeneralScore).FirstOrDefault());
    // Safety/retreat slot. It matters especially for threatened royals and expensive pieces.
    Add(ranked.OrderBy(option => option.ExposureCount)
      .ThenByDescending(option => option.EnemyDistance)
      .ThenByDescending(option => option.GeneralScore).FirstOrDefault());
    // Best all-round move, then deterministic fallbacks if categories overlapped.
    foreach (SearchMoveRepresentative option in ranked.OrderByDescending(option => option.GeneralScore)
      .ThenBy(option => option.Action.Describe(), StringComparer.Ordinal))
    {
      Add(option);
      if (selected.Count >= maximumRepresentatives) break;
    }

    actions.AddRange(selected.Take(maximumRepresentatives));
  }

  private static int Distance((int x, int y) first, (int x, int y) second) =>
    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);

'''
if needle not in text:
    raise SystemExit('attack insertion point not found')
text = text.replace(needle, method + needle, 1)
# There is already a Distance helper later for purchase clustering; remove the later duplicate only.
dup = '''  private static int Distance((int x, int y) first, (int x, int y) second) =>\n    Math.Abs(first.x - second.x) + Math.Abs(first.y - second.y);\n'''
# method inserted one exact copy; retain first and remove last if two.
if text.count(dup) > 1:
    idx = text.rfind(dup)
    text = text[:idx] + text[idx + len(dup):]
path.write_text(text)

# 2) Reduce detailed-scoring work slightly now that search movement is already representative.
path = Path('MedivalChess.CPU/CpuActionCandidates.cs')
text = path.read_text()
text = text.replace('''    int scoringCapacity = Math.Clamp(candidateLimit * 4, 24, 64);''',
                    '''    int scoringCapacity = Math.Clamp(candidateLimit * 3, 24, 48);''', 1)
path.write_text(text)

# 3) Complete clearly useful actions for untouched units after the timed search, and feed more workers.
path = Path('MedivalChess.CPU/CpuSearch.cs')
text = path.read_text()
text = text.replace(
'''    IReadOnlyList<ICpuGameAction> verifiedActions = VerifyActionSequence(state, team, chosenActions);''',
'''    IReadOnlyList<ICpuGameAction> verifiedActions = VerifyAndCompleteActionSequence(state, team, chosenActions, profile);''', 1)
text = text.replace(
'''  private static IReadOnlyList<ICpuGameAction> VerifyActionSequence(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> actions
  )
  {
    List<ICpuGameAction> verified = [];
    CpuGameState current = state;
    foreach (ICpuGameAction action in actions)
    {
      if (current.IsFinished || current.CurrentTurn != team || !action.IsLegal(current))
      {
        break;
      }
      verified.Add(action);
      current = action.Apply(current);
    }

    if (!Globals.ActionLimitsEnabled && state.InitialBuy is null && current.CurrentTurn == team && !current.IsFinished)
    {
      // Attack availability is now a CPU invariant rather than merely a heuristic. A short search
      // may time out on the move half of a move->attack line; complete any attacks that are legal in
      // the exact searched state before EndTurn. This never invents movement or purchases.
      for (int forcedAttack = 0; forcedAttack < 64 && current.CurrentTurn == team && !current.IsFinished; forcedAttack++)
      {
        AttackAction? attack = ChooseMandatoryAttack(current, team);
        if (attack is null)
        {
          break;
        }
        verified.Add(attack);
        current = attack.Apply(current);
      }

      EndTurnAction endTurn = new(team);
      if (endTurn.IsLegal(current) && ChooseMandatoryAttack(current, team) is null)
      {
        verified.Add(endTurn);
      }
    }

    return verified;
  }
''',
'''  private IReadOnlyList<ICpuGameAction> VerifyAndCompleteActionSequence(
    CpuGameState state,
    NetworkTeam team,
    IReadOnlyList<ICpuGameAction> actions,
    CpuProfile profile
  )
  {
    List<ICpuGameAction> verified = [];
    CpuGameState current = state;
    foreach (ICpuGameAction action in actions)
    {
      // In unlimited mode EndTurn is deliberately deferred until the cheap completion pass has
      // checked untouched units. The searched strategic prefix is still preserved exactly.
      if (!Globals.ActionLimitsEnabled && action is EndTurnAction)
      {
        break;
      }
      if (current.IsFinished || current.CurrentTurn != team || !action.IsLegal(current))
      {
        break;
      }
      verified.Add(action);
      current = action.Apply(current);
    }

    if (!Globals.ActionLimitsEnabled && state.InitialBuy is null && current.CurrentTurn == team && !current.IsFinished)
    {
      ForceMandatoryAttacks(ref current, team, verified);

      // A short beam can find a good tactical prefix without reaching every independent unit on a
      // crowded turn. Give untouched mobile pieces one conservative chance to make a clearly useful
      // move. This is deliberately not another search: it only consumes already-clustered moves and
      // refuses marginal/negative repositioning. Any shot created by the move is then mandatory.
      CpuSearchSettings completionSettings = new()
      {
        CandidatesPerNode = 12,
        PromisingCandidatesPerNode = 12,
        MaximumPurchasePlacementCandidates = 1,
        Randomness = 0f
      };
      for (int completion = 0; completion < 24 && current.CurrentTurn == team && !current.IsFinished; completion++)
      {
        NetworkPiece[] untouched = current.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null &&
          !piece.HasMovedThisTurn && !piece.HasAttackedThisTurn && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
          rule.MoveRange > 0).ToArray();
        if (untouched.Length == 0) break;
        HashSet<string> untouchedIds = untouched.Select(piece => piece.Id).ToHashSet(StringComparer.Ordinal);
        MoveAction[] moves = _actionGenerator.GenerateSearchActions(current, team, 1).OfType<MoveAction>()
          .Where(move => untouchedIds.Contains(move.PieceId) && move.IsLegal(current)).ToArray();
        if (moves.Length == 0) break;

        IReadOnlyList<ScoredAction> rankedMoves = _candidateSelector.SelectCandidates(
          current, team, moves, completionSettings, profile.Personality);
        (ScoredAction candidate, bool createsAttack)? best = rankedMoves
          .Select(candidate =>
          {
            MoveAction move = (MoveAction)candidate.Action;
            CpuGameState result = CpuGameRules.ApplyLegal(current, move);
            NetworkPiece? moved = result.Pieces.FirstOrDefault(piece => piece.Id == move.PieceId);
            bool createsAttack = moved is not null && result.Pieces.Any(enemy => enemy.Team != team &&
              enemy.Team != NetworkTeam.Neutral && enemy.AttachedToId is null &&
              CpuGameRules.CanDirectlyAttack(result, moved, enemy));
            return (candidate, createsAttack);
          })
          .Where(entry => IsClearlyUsefulCompletionMove(current, (MoveAction)entry.candidate.Action,
            entry.candidate.Score, entry.createsAttack))
          .OrderByDescending(entry => entry.createsAttack)
          .ThenByDescending(entry => entry.candidate.Score)
          .Cast<(ScoredAction candidate, bool createsAttack)?>()
          .FirstOrDefault();
        if (best is null) break;

        MoveAction chosenMove = (MoveAction)best.Value.candidate.Action;
        verified.Add(chosenMove);
        current = chosenMove.Apply(current);
        ForceMandatoryAttacks(ref current, team, verified);
      }

      EndTurnAction endTurn = new(team);
      if (endTurn.IsLegal(current) && ChooseMandatoryAttack(current, team) is null)
      {
        verified.Add(endTurn);
      }
    }

    return verified;
  }

  private static bool IsClearlyUsefulCompletionMove(
    CpuGameState state,
    MoveAction move,
    float score,
    bool createsAttack
  )
  {
    NetworkPiece? piece = state.Pieces.FirstOrDefault(candidate => candidate.Id == move.PieceId);
    if (piece is null || !UnitRules.TryGet(piece.Type, out UnitRule rule)) return false;
    // Attack-enabling moves can pass with a lower score because the following attack is guaranteed;
    // quiet movement must be meaningfully positive. Royals need extra evidence before being moved by
    // the fallback rather than the main search.
    float threshold = createsAttack ? 0f : 12f;
    if (rule.Category == RuleCategory.Royal) threshold += 12f;
    return score >= threshold;
  }

  private static void ForceMandatoryAttacks(
    ref CpuGameState current,
    NetworkTeam team,
    List<ICpuGameAction> verified
  )
  {
    for (int forcedAttack = 0; forcedAttack < 64 && current.CurrentTurn == team && !current.IsFinished; forcedAttack++)
    {
      AttackAction? attack = ChooseMandatoryAttack(current, team);
      if (attack is null) break;
      verified.Add(attack);
      current = attack.Apply(current);
    }
  }
''', 1)
text = text.replace(
'''    // Leave one logical processor for the game/UI and cap worker pressure on high-core PCs.
    // Two-core machines remain single-threaded because dedicating half the machine to a turn
    // search is more disruptive than the small speed-up is worth.
    return Environment.ProcessorCount <= 2 ? 1 : Math.Min(6, Environment.ProcessorCount - 1);''',
'''    // Keep two logical processors free for MonoGame/OS on larger systems while letting the
    // built-in evaluator use substantially more of modern 8-12+ thread CPUs. Small machines stay
    // conservative. The explicit MaxParallelism setting can still override this policy.
    if (Environment.ProcessorCount <= 2) return 1;
    if (Environment.ProcessorCount <= 4) return Math.Max(1, Environment.ProcessorCount - 2);
    return Math.Min(10, Math.Max(2, Environment.ProcessorCount - 2));''', 1)
text = text.replace(
'''  private static int GetEvaluationBatchSize(int parallelism) => Math.Clamp(
    Math.Max(1, parallelism) * 2,
    4,
    16
  );''',
'''  private static int GetEvaluationBatchSize(int parallelism) => Math.Clamp(
    Math.Max(1, parallelism) * 2,
    4,
    20
  );''', 1)
path.write_text(text)

# 4) Regression tests for clustering, move->attack preservation and crowded-turn completion.
path = Path('MedivalChess.Tests/CpuPlanningEfficiencyTests.cs')
text = path.read_text()
insert = '''  private static NetworkPiece Piece(string id, string type, NetworkTeam team, int x, int y)
'''
tests = r'''  [Fact]
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

'''
if insert not in text:
    raise SystemExit('test insertion point not found')
text = text.replace(insert, tests + insert, 1)
path.write_text(text)
