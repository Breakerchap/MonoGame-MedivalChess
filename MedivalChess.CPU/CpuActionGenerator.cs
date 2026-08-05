using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>Produces legal, deterministic actions from a CPU game snapshot.</summary>
public interface ICpuActionGenerator
{
  IReadOnlyList<ICpuGameAction> GenerateLegalActions(CpuGameState state, NetworkTeam team);
}

/// <summary>
/// Complete legal-action generator. Candidate ranking is deliberately separate so low difficulty
/// can prune aggressively without weakening the rules or high-difficulty search.
/// </summary>
public sealed class CpuActionGenerator : ICpuActionGenerator
{
  /// <summary>
  /// Generates a bounded, prioritised subset for search. <see cref="GenerateLegalActions"/> remains
  /// the exhaustive public legality API; this method only controls search cost.
  /// </summary>
  public IReadOnlyList<ICpuGameAction> GenerateSearchActions(CpuGameState state, NetworkTeam team, int purchasePlacementLimit)
  {
    ArgumentNullException.ThrowIfNull(state);
    if (state.IsFinished || team != state.CurrentTurn || !state.Teams.ContainsKey(team))
    {
      return [];
    }

    if (state.InitialBuy is not null)
    {
      // The public API below remains exhaustive. Search deliberately samples only the best
      // placement zone because thousands of opening squares are strategically interchangeable.
      List<ICpuGameAction> openingActions = [];
      GeneratePurchases(state, team, openingActions, purchasePlacementLimit, avoidOccupiedPlacements: true);
      AddIfLegal(state, new StopInitialBuyingAction(team), openingActions);
      return openingActions;
    }

    List<ICpuGameAction> actions = [];
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team == team).OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      GenerateMoves(state, piece, actions);
      GenerateAttacks(state, piece, actions);
      GenerateAbilities(state, piece, actions);
    }
    GeneratePurchases(state, team, actions, purchasePlacementLimit, avoidOccupiedPlacements: true);
    AddIfLegal(state, new EndTurnAction(team), actions);
    return actions;
  }

  public IReadOnlyList<ICpuGameAction> GenerateLegalActions(CpuGameState state, NetworkTeam team)
  {
    ArgumentNullException.ThrowIfNull(state);
    if (state.IsFinished || team != state.CurrentTurn || !state.Teams.ContainsKey(team))
    {
      return [];
    }

    List<ICpuGameAction> actions = [];
    if (state.InitialBuy is not null)
    {
      GeneratePurchases(state, team, actions);
      AddIfLegal(state, new StopInitialBuyingAction(team), actions);
      return actions;
    }

    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team == team).OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      GenerateMoves(state, piece, actions);
      GenerateAttacks(state, piece, actions);
      GenerateAbilities(state, piece, actions);
    }

    GeneratePurchases(state, team, actions);
    AddIfLegal(state, new EndTurnAction(team), actions);
    return actions;
  }

  private static void GenerateMoves(CpuGameState state, NetworkPiece piece, List<ICpuGameAction> actions)
  {
    foreach ((int x, int y) destination in CpuGameRules.GetLegalMovementPaths(state, piece).Keys
      .OrderBy(position => position.y).ThenBy(position => position.x))
    {
      AddIfLegal(state, new MoveAction(piece.Team, piece.Id, destination.x, destination.y), actions);
    }
  }

  private static void GenerateAttacks(CpuGameState state, NetworkPiece attacker, List<ICpuGameAction> actions)
  {
    foreach (NetworkPiece target in state.Pieces.Where(target => target.Team != attacker.Team &&
      target.Team != NetworkTeam.Neutral && target.AttachedToId is null).OrderBy(target => target.Id, StringComparer.Ordinal))
    {
      foreach ((int x, int y) targetSquare in GetTargetSquares(target))
      {
        AddIfLegal(state, new AttackAction(attacker.Team, attacker.Id, target.Id, targetSquare.x, targetSquare.y), actions);
      }
    }
    foreach ((int x, int y) barricade in state.Barricades.Keys.OrderBy(position => position.y).ThenBy(position => position.x))
    {
      AddIfLegal(state, new AttackAction(attacker.Team, attacker.Id, null, barricade.x, barricade.y), actions);
    }
  }

  private static void GenerateAbilities(CpuGameState state, NetworkPiece actor, List<ICpuGameAction> actions)
  {
    if (actor.Type == "Mercenary")
    {
      AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Fire", null, actor.X, actor.Y), actions);
    }
    else if (actor.Type == "Spy")
    {
      foreach (NetworkPiece target in state.Pieces.Where(piece => piece.Team != actor.Team && piece.Team != NetworkTeam.Neutral)
        .OrderBy(piece => piece.Id, StringComparer.Ordinal))
      {
        foreach ((int x, int y) targetSquare in GetTargetSquares(target))
        {
          AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Mark", target.Id, targetSquare.x, targetSquare.y), actions);
        }
      }
    }
    else if (actor.Type == "Engineer")
    {
      foreach ((int x, int y) position in GetPotentialActionSquares(state, actor))
      {
        foreach (string ability in new[] { "Road", "Barrier", "Mine", "Demolish" })
        {
          AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, ability, null, position.x, position.y), actions);
        }
      }
    }
    else if (actor.Type is "Guard" or "Ox")
    {
      foreach (NetworkPiece target in state.Pieces.Where(piece => piece.Team == actor.Team && piece.Id != actor.Id)
        .OrderBy(piece => piece.Id, StringComparer.Ordinal))
      {
        foreach ((int x, int y) targetSquare in GetTargetSquares(target))
        {
          AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Attach", target.Id, targetSquare.x, targetSquare.y), actions);
        }
      }
    }

    if (state.TreasurePosition is (int x, int y) treasure)
    {
      AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "PickUpTreasure", null, treasure.x, treasure.y), actions);
    }
  }

  private static IEnumerable<(int x, int y)> GetPotentialActionSquares(CpuGameState state, NetworkPiece actor)
  {
    if (!UnitRules.TryGet(actor.Type, out UnitRule rule))
    {
      yield break;
    }

    int minimumX = actor.X - rule.AttackRange;
    int maximumX = actor.X + rule.Width - 1 + rule.AttackRange;
    int minimumY = actor.Y - rule.AttackRange;
    int maximumY = actor.Y + rule.Height - 1 + rule.AttackRange;
    for (int y = minimumY; y <= maximumY; y++)
    {
      for (int x = minimumX; x <= maximumX; x++)
      {
        if (BoardRules.Contains(state.Board, x, y) && CpuGameRules.CanUseActionSquare(actor, x, y))
        {
          yield return (x, y);
        }
      }
    }
  }

  private static IEnumerable<(int x, int y)> GetTargetSquares(NetworkPiece target)
  {
    if (!UnitRules.TryGet(target.Type, out UnitRule rule))
    {
      yield break;
    }

    for (int y = target.Y; y < target.Y + rule.Height; y++)
    {
      for (int x = target.X; x < target.X + rule.Width; x++)
      {
        yield return (x, y);
      }
    }
  }

  private static void GeneratePurchases(
    CpuGameState state,
    NetworkTeam team,
    List<ICpuGameAction> actions,
    int? placementLimit = null,
    bool avoidOccupiedPlacements = false
  )
  {
    int centreX = state.Board.MinX + state.Board.BoardArray.GetLength(1) / 2;
    int centreY = state.Board.MinY + state.Board.BoardArray.GetLength(0) / 2;
    bool openingFarmPlacement = state.InitialBuy?.IsFarmPlacementPhase == true;
    UnitRule farmRule = UnitRules.GetRequired("Farm");
    int furthestForwardProjection = openingFarmPlacement
      ? CpuPlacementHeuristics.GetFurthestForwardProjection(state, team)
      : 0;
    List<(int x, int y)> positions = openingFarmPlacement
      ? state.Board.Cells
        .Where(position => MatchRules.GetSquareOwner(state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount) == team)
        .Where(position => BoardRules.CanPlaceForTeam(state.Board, state.Configuration.GameMode, state.Configuration.PlayerCount, team, position.x, position.y, farmRule.Width, farmRule.Height))
        .OrderByDescending(position => CpuPlacementHeuristics.GetFarmProtectionScore(
          state, team, position.x, position.y, furthestForwardProjection))
        .ThenBy(position => position.y)
        .ThenBy(position => position.x)
        .ToList()
      : state.Board.Cells
        .OrderBy(position => MatchRules.GetSquareOwner(state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount) == team ? 0 : 1)
        .ThenBy(position => Math.Abs(position.x - centreX) + Math.Abs(position.y - centreY))
        .ThenBy(position => position.y)
        .ThenBy(position => position.x)
        .ToList();
    // During opening farm placement every non-farm action is illegal. Avoid constructing and
    // validating hundreds of guaranteed failures on the main search path.
    int availableMoney = state.Teams.GetValueOrDefault(team)?.Money ?? 0;
    IEnumerable<UnitRule> purchaseRules = openingFarmPlacement
      ? [farmRule]
      : UnitRules.Purchasable.Where(rule => rule.Type == "Mercenary" ||
        availableMoney >= GetPurchaseCost(state, rule));
    foreach (UnitRule rule in purchaseRules.OrderBy(rule => rule.Type, StringComparer.Ordinal))
    {
      int legalPlacements = 0;
      foreach ((int x, int y) position in positions)
      {
        // A normal unit can legally share a Farm tile in the game rules, but putting a newly
        // bought piece underneath any existing piece is unreadable and has repeatedly looked
        // like a stalled CPU turn. Search therefore avoids all occupied footprints while the
        // exhaustive public legal-action API still exposes every rule-legal action. Mercenary
        // buyouts remain the intentional exception.
        if (avoidOccupiedPlacements && rule.Type != "Mercenary" &&
            OverlapsExistingPiece(state, rule, position.x, position.y))
        {
          continue;
        }

        int actionCount = actions.Count;
        AddIfLegal(state, new PurchaseAction(team, rule.Type, position.x, position.y), actions);
        if (actions.Count == actionCount)
        {
          continue;
        }

        legalPlacements++;
        // Apply the search cap to legal placements, not to raw board squares. In the opening,
        // many high-scoring farm squares can overlap the first farm; truncating before legality
        // checks could leave the CPU with no second-farm action and make it re-plan forever.
        if (placementLimit is int limit && legalPlacements >= Math.Max(1, limit))
        {
          break;
        }
      }
    }
  }

  private static bool OverlapsExistingPiece(CpuGameState state, UnitRule rule, int x, int y) => state.Pieces.Any(piece =>
    UnitRules.TryGet(piece.Type, out UnitRule existingRule) &&
    UnitRules.FootprintsOverlap(x, y, rule.Width, rule.Height,
      piece.X, piece.Y, existingRule.Width, existingRule.Height));

  private static int GetPurchaseCost(CpuGameState state, UnitRule rule) => rule.Type == "Farm"
    ? rule.Cost
    : (int)Math.Ceiling(rule.Cost * state.Configuration.UnitPricePercent / 100d);

  private static void AddIfLegal(CpuGameState state, ICpuGameAction action, ICollection<ICpuGameAction> actions)
  {
    if (action.IsLegal(state))
    {
      actions.Add(action);
    }
  }
}
