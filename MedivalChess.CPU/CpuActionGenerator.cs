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
      return GenerateLegalActions(state, team);
    }

    List<ICpuGameAction> actions = [];
    foreach (NetworkPiece piece in state.Pieces.Where(piece => piece.Team == team).OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      GenerateMoves(state, piece, actions);
      GenerateAttacks(state, piece, actions);
      GenerateAbilities(state, piece, actions);
    }
    GeneratePurchases(state, team, actions, purchasePlacementLimit);
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
      AddIfLegal(state, new AttackAction(attacker.Team, attacker.Id, target.Id, target.X, target.Y), actions);
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
      return;
    }

    if (actor.Type == "Spy")
    {
      foreach (NetworkPiece target in state.Pieces.Where(piece => piece.Team != actor.Team && piece.Team != NetworkTeam.Neutral)
        .OrderBy(piece => piece.Id, StringComparer.Ordinal))
      {
        AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Mark", target.Id, target.X, target.Y), actions);
      }
    }
    else if (actor.Type == "Engineer")
    {
      foreach ((int x, int y) position in state.Board.Cells.OrderBy(position => position.y).ThenBy(position => position.x))
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
        AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Attach", target.Id, target.X, target.Y), actions);
      }
    }

    if (state.TreasurePosition is (int x, int y) treasure)
    {
      AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "PickUpTreasure", null, treasure.x, treasure.y), actions);
    }
  }

  private static void GeneratePurchases(
    CpuGameState state,
    NetworkTeam team,
    List<ICpuGameAction> actions,
    int? placementLimit = null
  )
  {
    int centreX = state.Board.MinX + state.Board.BoardArray.GetLength(1) / 2;
    int centreY = state.Board.MinY + state.Board.BoardArray.GetLength(0) / 2;
    IEnumerable<(int x, int y)> positions = state.Board.Cells
      .OrderBy(position => MatchRules.GetSquareOwner(state.Board, state.Configuration.GameMode, position, state.Configuration.PlayerCount) == team ? 0 : 1)
      .ThenBy(position => Math.Abs(position.x - centreX) + Math.Abs(position.y - centreY))
      .ThenBy(position => position.y)
      .ThenBy(position => position.x);
    if (placementLimit is int limit)
    {
      positions = positions.Take(Math.Max(1, limit));
    }
    foreach (UnitRule rule in UnitRules.Purchasable.OrderBy(rule => rule.Type, StringComparer.Ordinal))
    {
      foreach ((int x, int y) position in positions)
      {
        AddIfLegal(state, new PurchaseAction(team, rule.Type, position.x, position.y), actions);
      }
    }
  }

  private static void AddIfLegal(CpuGameState state, ICpuGameAction action, ICollection<ICpuGameAction> actions)
  {
    if (action.IsLegal(state))
    {
      actions.Add(action);
    }
  }
}
