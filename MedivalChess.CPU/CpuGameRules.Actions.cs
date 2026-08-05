using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>State-changing action application for CPU simulation; legality remains in the core facade.</summary>
public static partial class CpuGameRules
{
  private static void ApplyMove(CpuMutableGameState state, MoveAction action)
  {
    int index = FindPieceIndex(state.Pieces, action.PieceId);
    NetworkPiece piece = state.Pieces[index];
    if (piece.AttachedToId is not null)
    {
      piece = piece with { AttachedToId = null, AttachmentKind = NetworkAttachmentKind.None };
      state.Pieces[index] = piece;
    }

    IReadOnlyDictionary<(int x, int y), List<(int x, int y)>> paths = GetLegalMovementPaths(state.Source, state.Pieces, piece, UnitRules.GetRequired(piece.Type));
    List<(int x, int y)> path = paths[(action.DestinationX, action.DestinationY)];
    int oldX = piece.X;
    int oldY = piece.Y;
    bool elephantDamaged = false;
    if (piece.Type == "Elephant" && UnitRules.TryGet(piece.Type, out UnitRule elephantRule))
    {
      foreach (NetworkPiece crossed in state.Pieces.Where(other => other.Id != piece.Id && other.Team != piece.Team).ToArray())
      {
        if (UnitRules.TryGet(crossed.Type, out UnitRule crossedRule) &&
            AbilityRules.PathOverlapsUnit(elephantRule, path, crossedRule, crossed.X, crossed.Y))
        {
          ResolvePieceDamage(state, piece, action.Team, crossed.Id, 15);
          elephantDamaged = true;
        }
      }
    }

    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    piece = state.Pieces[index] with
    {
      X = action.DestinationX,
      Y = action.DestinationY,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = elephantDamaged || state.Pieces[index].HasAttackedThisTurn
    };
    state.Pieces[index] = piece;
    state.RecordMove(action.Team, piece.Id, oldX, oldY, action.DestinationX, action.DestinationY);
    MoveAttachedPieces(state, piece);
    MoveEmissaryCompanions(state, piece, oldX, oldY);
    TriggerMinesAlongMovement(state, piece, path);

    NetworkPiece? moved = FindPiece(state.Pieces, piece.Id);
    if (moved is not null)
    {
      TryDeliverTreasure(state, moved);
      if (state.Winner is null && IsEscortVictory(state, moved))
      {
        state.Winner = moved.Team;
      }
    }

    if (state.Winner is null)
    {
      SpendAction(state, action.Team);
    }
  }

  private static void ApplyAttack(CpuMutableGameState state, AttackAction action)
  {
    int attackerIndex = FindPieceIndex(state.Pieces, action.AttackerId);
    NetworkPiece attacker = state.Pieces[attackerIndex] with { HasAttackedThisTurn = true };
    state.Pieces[attackerIndex] = attacker;
    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    if (target is null)
    {
      DamageBarricade(state, attacker, (action.TargetX, action.TargetY));
    }
    else if (attacker.Type == "Bombard")
    {
      ResolveBombardDamage(state, attacker, action.Team, target);
    }
    else
    {
      ResolvePieceDamage(state, attacker, action.Team, target.Id, null);
    }

    if (target is not null && attacker.Type == "Ballista" && UnitRules.TryGet(attacker.Type, out UnitRule ballistaRule))
    {
      foreach ((int x, int y) position in AbilityRules.GetPiercingRay(ballistaRule, attacker.X, attacker.Y, target.X, target.Y))
      {
        if (!BoardRules.Contains(state.Source.Configuration, position.x, position.y) ||
            state.Source.Terrain.IsForest(position) || state.Barricades.ContainsKey(position))
        {
          break;
        }

        NetworkPiece? pierced = state.Pieces.FirstOrDefault(piece => piece.Id != attacker.Id && piece.Id != target.Id &&
          piece.Team != attacker.Team && piece.Type != "Farm" && piece.AttachedToId is null &&
          UnitRules.TryGet(piece.Type, out UnitRule rule) && Occupies(rule, piece, position));
        if (pierced is not null)
        {
          ResolvePieceDamage(state, attacker, action.Team, pierced.Id, null);
        }
      }
    }

    if (state.Winner is null)
    {
      SpendAction(state, action.Team);
    }
  }

  private static void ApplyAbility(CpuMutableGameState state, UseAbilityAction action)
  {
    int actorIndex = FindPieceIndex(state.Pieces, action.ActorId);
    NetworkPiece actor = state.Pieces[actorIndex];
    NetworkPiece? target = action.TargetPieceId is null ? null : FindPiece(state.Pieces, action.TargetPieceId);
    bool plunderPickup = state.Source.Configuration.GameMode == "Plunder" &&
      string.Equals(action.Ability, "PickUpTreasure", StringComparison.OrdinalIgnoreCase);

    if (plunderPickup)
    {
      state.TreasureCarrierId = actor.Id;
      state.TreasurePosition = null;
      state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
    }
    else
    {
      switch (actor.Type)
      {
        case "Spy":
          state.Pieces[actorIndex] = actor with { MarkedTargetId = target!.Id };
          break;
        case "Engineer":
          ApplyEngineerAbility(state, actorIndex, action);
          break;
        case "Guard":
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Guard,
            X = target.X,
            Y = target.Y
          };
          break;
        case "Ox":
          int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
          state.Pieces[targetIndex] = target with
          {
            AttachedToId = actor.Id,
            AttachmentKind = NetworkAttachmentKind.Carried,
            X = actor.X,
            Y = actor.Y
          };
          break;
        case "Mercenary":
          state.Pieces[actorIndex] = actor with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true
          };
          break;
      }
    }

    SpendAction(state, action.Team);
  }

  private static void ApplyEngineerAbility(CpuMutableGameState state, int actorIndex, UseAbilityAction action)
  {
    NetworkPiece engineer = state.Pieces[actorIndex];
    (int x, int y) position = (action.TargetX, action.TargetY);
    if (AbilityRules.IsEngineerDemolition(action.Ability))
    {
      _ = state.Roads.Remove(position) || state.Barricades.Remove(position) || state.Mines.Remove(position) ||
        state.RiverBridges.Remove(TileEdge.Between((engineer.X, engineer.Y), position));
      return;
    }

    if (string.Equals(action.Ability, "Road", StringComparison.OrdinalIgnoreCase))
    {
      state.Roads.Add(position);
    }
    else if (string.Equals(action.Ability, "Barrier", StringComparison.OrdinalIgnoreCase))
    {
      state.Barricades[position] = 20;
    }
    else if (string.Equals(action.Ability, "Mine", StringComparison.OrdinalIgnoreCase))
    {
      state.Mines[position] = engineer.Team;
    }

    int buildsUsed = engineer.EngineerBuildsThisTurn + 1;
    state.Pieces[actorIndex] = engineer with
    {
      EngineerBuildsThisTurn = buildsUsed,
      HasAttackedThisTurn = buildsUsed >= 2
    };
  }

  private static void ApplyPurchase(CpuMutableGameState state, PurchaseAction action)
  {
    NetworkPiece? mercenary = state.Pieces.FirstOrDefault(piece => piece.Type == "Mercenary" &&
      piece.Team != action.Team && piece.X == action.X && piece.Y == action.Y);
    if (mercenary is not null)
    {
      int index = FindPieceIndex(state.Pieces, mercenary.Id);
      int cost = mercenary.Team == NetworkTeam.Neutral
        ? Math.Max(1, mercenary.LastBid)
        : Math.Max(mercenary.LastBid, GetUnitPrice(state.Source.Configuration, UnitRules.GetRequired("Mercenary"))) + 10;
      SpendMoney(state, action.Team, cost);
      if (mercenary.Team != NetworkTeam.Neutral)
      {
        AddMoney(state, mercenary.Team, cost);
      }
      state.Pieces[index] = mercenary with
      {
        Team = action.Team,
        LastBid = cost,
        HasMovedThisTurn = true,
        HasAttackedThisTurn = true,
        CannotContributeToConquestThisTurn = true
      };
      SpendAction(state, action.Team);
      return;
    }

    UnitRule rule = UnitRules.GetRequired(action.UnitType);
    bool openingFarmPlacement = state.InitialBuy?.IsFarmPlacementPhase == true && rule.Type == "Farm";
    if (!openingFarmPlacement)
    {
      SpendMoney(state, action.Team, GetUnitPrice(state.Source.Configuration, rule));
    }

    state.Pieces.Add(new NetworkPiece(
      CreatePieceId(state, rule.Type),
      rule.Type,
      action.Team,
      action.X,
      action.Y,
      rule.Health,
      HasMovedThisTurn: state.InitialBuy is null,
      HasAttackedThisTurn: state.InitialBuy is null,
      LastBid: GetUnitPrice(state.Source.Configuration, rule),
      CannotContributeToConquestThisTurn: state.InitialBuy is null
    ));

    if (state.InitialBuy is null)
    {
      SpendAction(state, action.Team);
    }
    else
    {
      RecordInitialPurchase(state, action.Team);
    }
  }

  private static void ApplyEndTurn(CpuMutableGameState state, NetworkTeam team)
  {
    CpuTeamState current = state.Teams[team];
    state.Teams[team] = current with { ActionsRemaining = 1 };
    SpendAction(state, team);
  }

  private static void ApplyStopInitialBuying(CpuMutableGameState state, NetworkTeam team)
  {
    NetworkInitialBuyState current = state.InitialBuy!;
    Dictionary<NetworkTeam, (int turnsUsed, bool stopped, int farmsPlaced)> records = GetInitialBuyRecords(current, state.Source.Configuration.PlayerCount);
    (int turnsUsed, bool _, int farmsPlaced) = records[team];
    records[team] = (turnsUsed, true, farmsPlaced);
    AdvanceInitialBuyer(state, records, current.PurchasesThisTurn, current.IsFarmPlacementPhase);
  }
}
