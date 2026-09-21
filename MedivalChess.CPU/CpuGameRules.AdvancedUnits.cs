using MedivalChess.Shared;

namespace MedivalChess.CPU;

public static partial class CpuGameRules
{
  private static bool IsCodexDemolitionAbility(string unitType, string ability) =>
    string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase) &&
    unitType is nameof(PieceType.Mason) or nameof(PieceType.Carpenter) or
      nameof(PieceType.Daedalus) or nameof(PieceType.Runesmith) or nameof(PieceType.Gatekeeper);

  private static bool IsLegalCodexAdvancedAbility(
    CpuGameState state,
    NetworkPiece actor,
    NetworkPiece? target,
    UseAbilityAction action)
  {
    if (string.Equals(action.Ability, "ReloadHwacha", StringComparison.OrdinalIgnoreCase))
    {
      if (target is null || target.Team != actor.Team || target.Type != nameof(PieceType.Hwacha) ||
          actor.HasAttackedThisTurn ||
          !UnitRules.TryGet(actor.Type, out UnitRule actorRule) ||
          !UnitRules.TryGet(target.Type, out UnitRule targetRule))
      {
        return false;
      }
      return AbilityRules.AreAdjacent(
          actorRule, (actor.X, actor.Y), targetRule, (target.X, target.Y), includeDiagonal: true) &&
        AdvancedAbilityRules.CanReloadHwacha(target.AbilityState);
    }

    switch (actor.Type)
    {
      case nameof(PieceType.Mason):
        return IsLegalCpuBuilder(state, actor, action,
          new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
          {
            ["StoneWall"] = (AbilityEntityKind.StoneWall, 2, 10),
            ["Gatehouse"] = (AbilityEntityKind.Gatehouse, 2, 0)
          });

      case nameof(PieceType.Carpenter):
        if (string.Equals(action.Ability, "Watchtower", StringComparison.OrdinalIgnoreCase))
        {
          return target is null && GetMoney(state, actor.Team) >= 15 &&
            CanPlaceCpuAbilityEntity(state, AbilityEntityKind.Watchtower, actor.Team, action.TargetX, action.TargetY);
        }
        if (string.Equals(action.Ability, "Bridge", StringComparison.OrdinalIgnoreCase))
        {
          return IsLegalCpuBuilder(state, actor, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Bridge"] = (AbilityEntityKind.Bridge, 2, 0)
            });
        }
        return string.Equals(action.Ability, "Demolish", StringComparison.OrdinalIgnoreCase) &&
          HasCpuStructureAt(state, action.TargetX, action.TargetY);

      case nameof(PieceType.Daedalus):
        if (string.Equals(action.Ability, "Snare", StringComparison.OrdinalIgnoreCase))
        {
          return target is null &&
            CanPlaceCpuAbilityEntity(state, AbilityEntityKind.Snare, actor.Team, action.TargetX, action.TargetY);
        }
        if (string.Equals(action.Ability, "Gate", StringComparison.OrdinalIgnoreCase))
        {
          return IsLegalCpuBuilder(state, actor, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Gate"] = (AbilityEntityKind.Gate, 3, 0)
            });
        }
        return string.Equals(action.Ability, "Demolish", StringComparison.OrdinalIgnoreCase) &&
          HasCpuStructureAt(state, action.TargetX, action.TargetY);

      case nameof(PieceType.Runesmith):
        if (string.Equals(action.Ability, "Demolish", StringComparison.OrdinalIgnoreCase))
        {
          return HasCpuStructureAt(state, action.TargetX, action.TargetY);
        }
        return target is null && TryParseCpuRune(action.Ability, out AbilityEntityKind runeKind) &&
          CanPlaceCpuAbilityEntity(state, runeKind, actor.Team, action.TargetX, action.TargetY);

      case nameof(PieceType.Fafnir):
        return string.Equals(action.Ability, "Transform", StringComparison.OrdinalIgnoreCase) &&
          AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) &&
          GetMoney(state, actor.Team) >= AdvancedAbilityRules.FafnirTransformCost;

      case nameof(PieceType.Thor):
        if (!string.Equals(action.Ability, "Thunderstorm", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
            target is not null || !BoardRules.Contains(state.Board, action.TargetX, action.TargetY))
        {
          return false;
        }
        AbilityEntity[] storms = state.AbilityEntities
          .Where(entity => entity.Kind == AbilityEntityKind.Thunderstorm && entity.SourcePieceId == actor.Id)
          .ToArray();
        if (action.TargetPieceId is null)
        {
          return storms.Length < 3 &&
            !state.AbilityEntities.Any(entity => entity.X == action.TargetX && entity.Y == action.TargetY);
        }
        return storms.Any(entity => entity.Id == action.TargetPieceId) &&
          !state.AbilityEntities.Any(entity =>
            entity.Id != action.TargetPieceId && entity.X == action.TargetX && entity.Y == action.TargetY);

      case nameof(PieceType.Demolitionist):
        if (string.Equals(action.Ability, "PlaceTnt", StringComparison.OrdinalIgnoreCase))
        {
          return target is null && !actor.HasAttackedThisTurn &&
            !state.AbilityEntities.Any(entity =>
              entity.Kind == AbilityEntityKind.Tnt && entity.SourcePieceId == actor.Id) &&
            Math.Max(Math.Abs(action.TargetX - actor.X), Math.Abs(action.TargetY - actor.Y)) == 1 &&
            CanPlaceCpuAbilityEntity(state, AbilityEntityKind.Tnt, actor.Team, action.TargetX, action.TargetY);
        }
        return string.Equals(action.Ability, "Detonate", StringComparison.OrdinalIgnoreCase) &&
          AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) &&
          state.AbilityEntities.Any(entity =>
            entity.Kind == AbilityEntityKind.Tnt && entity.SourcePieceId == actor.Id);

      case nameof(PieceType.CommandCentre):
        if (!action.Ability.StartsWith("Upgrade", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
            target is null || target.Team != actor.Team ||
            !UnitRules.TryGet(target.Type, out UnitRule targetRule) ||
            targetRule.Category == RuleCategory.Royal ||
            target.AbilityState?.Upgraded == true ||
            !IsWithinCpuSquareRange(actor, target, 2) ||
            GetMoney(state, actor.Team) < AdvancedAbilityRules.CommandCentreUpgradeCost)
        {
          return false;
        }
        string upgrade = action.Ability["Upgrade".Length..].TrimStart(':', ' ');
        return upgrade.Equals("attack", StringComparison.OrdinalIgnoreCase) ||
          upgrade.Equals("health", StringComparison.OrdinalIgnoreCase) ||
          upgrade.Equals("move", StringComparison.OrdinalIgnoreCase);

      case nameof(PieceType.Mashhit):
        return string.Equals(action.Ability, "Destroy", StringComparison.OrdinalIgnoreCase) &&
          (state.Terrain.IsForest((action.TargetX, action.TargetY)) ||
           state.Terrain.IsLake((action.TargetX, action.TargetY)) ||
           HasCpuStructureAt(state, action.TargetX, action.TargetY));

      case nameof(PieceType.Gatekeeper):
        if (string.Equals(action.Ability, "Portal", StringComparison.OrdinalIgnoreCase))
        {
          return IsLegalCpuBuilder(state, actor, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Portal"] = (AbilityEntityKind.Portal, 2, 0)
            });
        }
        if (string.Equals(action.Ability, "Seal", StringComparison.OrdinalIgnoreCase))
        {
          return IsLegalCpuBuilder(state, actor, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Seal"] = (AbilityEntityKind.Seal, 2, 0)
            });
        }
        return string.Equals(action.Ability, "Demolish", StringComparison.OrdinalIgnoreCase) &&
          HasCpuStructureAt(state, action.TargetX, action.TargetY);

      default:
        return false;
    }
  }

  private static bool ApplyCodexAdvancedAbility(
    CpuMutableGameState state,
    int actorIndex,
    NetworkPiece? target,
    UseAbilityAction action)
  {
    NetworkPiece actor = state.Pieces[actorIndex];

    if (string.Equals(action.Ability, "ReloadHwacha", StringComparison.OrdinalIgnoreCase))
    {
      int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
      NetworkPiece hwacha = state.Pieces[targetIndex];
      state.Pieces[targetIndex] = hwacha with
      {
        AbilityState = AdvancedAbilityRules.ReloadHwacha(hwacha.AbilityState)
      };
      state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
      return true;
    }

    switch (actor.Type)
    {
      case nameof(PieceType.Mason):
        return ApplyCpuBuilder(state, actorIndex, action,
          new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
          {
            ["StoneWall"] = (AbilityEntityKind.StoneWall, 2, 10),
            ["Gatehouse"] = (AbilityEntityKind.Gatehouse, 2, 0)
          });

      case nameof(PieceType.Carpenter):
        if (string.Equals(action.Ability, "Watchtower", StringComparison.OrdinalIgnoreCase))
        {
          SpendMoney(state, actor.Team, 15);
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.Watchtower, actor.Team, action.TargetX, action.TargetY, actor.Id));
          state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          return true;
        }
        if (string.Equals(action.Ability, "Bridge", StringComparison.OrdinalIgnoreCase))
        {
          return ApplyCpuBuilder(state, actorIndex, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Bridge"] = (AbilityEntityKind.Bridge, 2, 0)
            });
        }
        DestroyCpuStructure(state, action.TargetX, action.TargetY);
        state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return true;

      case nameof(PieceType.Daedalus):
        if (string.Equals(action.Ability, "Snare", StringComparison.OrdinalIgnoreCase))
        {
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.Snare, actor.Team, action.TargetX, action.TargetY, actor.Id));
          state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          return true;
        }
        if (string.Equals(action.Ability, "Gate", StringComparison.OrdinalIgnoreCase))
        {
          return ApplyCpuBuilder(state, actorIndex, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Gate"] = (AbilityEntityKind.Gate, 3, 0)
            });
        }
        DestroyCpuStructure(state, action.TargetX, action.TargetY);
        state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return true;

      case nameof(PieceType.Runesmith):
        if (string.Equals(action.Ability, "Demolish", StringComparison.OrdinalIgnoreCase))
        {
          DestroyCpuStructure(state, action.TargetX, action.TargetY);
        }
        else
        {
          TryParseCpuRune(action.Ability, out AbilityEntityKind runeKind);
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, runeKind, actor.Team, action.TargetX, action.TargetY, actor.Id));
        }
        state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return true;

      case nameof(PieceType.Fafnir):
        UnitRule dragon = UnitRules.GetRequired(nameof(PieceType.FafnirDragon));
        SpendMoney(state, actor.Team, AdvancedAbilityRules.FafnirTransformCost);
        state.Pieces[actorIndex] = actor with
        {
          Type = nameof(PieceType.FafnirDragon),
          Health = dragon.Health,
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        DestroyCpuOverlappedTerrainAndEntities(state, state.Pieces[actorIndex], dragon);
        return true;

      case nameof(PieceType.Thor):
        AbilityEntity? movingStorm = action.TargetPieceId is null
          ? null
          : state.AbilityEntities.FirstOrDefault(entity =>
            entity.Id == action.TargetPieceId &&
            entity.Kind == AbilityEntityKind.Thunderstorm &&
            entity.SourcePieceId == actor.Id);
        if (movingStorm is null)
        {
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.Thunderstorm, actor.Team, action.TargetX, action.TargetY, actor.Id));
        }
        else
        {
          int stormIndex = state.AbilityEntities.FindIndex(entity => entity.Id == movingStorm.Id);
          state.AbilityEntities[stormIndex] = movingStorm with { X = action.TargetX, Y = action.TargetY };
        }
        state.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        return true;

      case nameof(PieceType.Demolitionist):
        if (string.Equals(action.Ability, "PlaceTnt", StringComparison.OrdinalIgnoreCase))
        {
          state.AbilityEntities.Add(CreateCpuAbilityEntity(
            state, AbilityEntityKind.Tnt, actor.Team, action.TargetX, action.TargetY, actor.Id));
          state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          return true;
        }
        AbilityEntity tnt = state.AbilityEntities.First(entity =>
          entity.Kind == AbilityEntityKind.Tnt && entity.SourcePieceId == actor.Id);
        DetonateCpuTnt(state, actor, tnt);
        actorIndex = FindPieceIndex(state.Pieces, actor.Id);
        if (actorIndex >= 0)
        {
          NetworkPiece live = state.Pieces[actorIndex];
          state.Pieces[actorIndex] = live with
          {
            AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(live.AbilityState)
          };
        }
        return true;

      case nameof(PieceType.CommandCentre):
        string upgrade = action.Ability["Upgrade".Length..].TrimStart(':', ' ');
        int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
        NetworkPiece upgradedTarget = state.Pieces[targetIndex];
        UnitAbilityState upgraded = AdvancedAbilityRules.ApplyCommandCentreUpgrade(
          upgradedTarget.AbilityState, upgrade);
        SpendMoney(state, actor.Team, AdvancedAbilityRules.CommandCentreUpgradeCost);
        int heal = upgrade.Equals("health", StringComparison.OrdinalIgnoreCase)
          ? AdvancedAbilityRules.CommandCentreHealthBonus
          : 0;
        state.Pieces[targetIndex] = upgradedTarget with
        {
          Health = Math.Min(
            UnitRules.GetRequired(upgradedTarget.Type).Health +
              AdvancedAbilityRules.GetEffectiveMaxHealthBonus(upgraded),
            upgradedTarget.Health + heal),
          AbilityState = upgraded
        };
        state.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        return true;

      case nameof(PieceType.Mashhit):
        _ = state.Terrain.DestroyTile((action.TargetX, action.TargetY)) ||
          DestroyCpuStructure(state, action.TargetX, action.TargetY);
        state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return true;

      case nameof(PieceType.Gatekeeper):
        if (string.Equals(action.Ability, "Portal", StringComparison.OrdinalIgnoreCase))
        {
          return ApplyCpuBuilder(state, actorIndex, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Portal"] = (AbilityEntityKind.Portal, 2, 0)
            });
        }
        if (string.Equals(action.Ability, "Seal", StringComparison.OrdinalIgnoreCase))
        {
          return ApplyCpuBuilder(state, actorIndex, action,
            new Dictionary<string, (AbilityEntityKind kind, int count, int cost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Seal"] = (AbilityEntityKind.Seal, 2, 0)
            });
        }
        DestroyCpuStructure(state, action.TargetX, action.TargetY);
        state.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return true;

      default:
        return true;
    }
  }

  private static bool IsLegalCpuBuilder(
    CpuGameState state,
    NetworkPiece actor,
    UseAbilityAction action,
    IReadOnlyDictionary<string, (AbilityEntityKind kind, int count, int cost)> options)
  {
    if (actor.HasAttackedThisTurn || action.TargetPieceId is not null ||
        !options.TryGetValue(action.Ability, out var option) ||
        GetMoney(state, actor.Team) < option.cost ||
        !CanPlaceCpuAbilityEntity(state, option.kind, actor.Team, action.TargetX, action.TargetY))
    {
      return false;
    }

    IReadOnlyList<AbilitySelection> pending =
      string.Equals(actor.AbilityState?.PendingAbility, action.Ability, StringComparison.OrdinalIgnoreCase)
        ? actor.AbilityState?.PendingSelections ?? Array.Empty<AbilitySelection>()
        : Array.Empty<AbilitySelection>();
    return pending.Count < option.count &&
      !pending.Any(selection => selection.X == action.TargetX && selection.Y == action.TargetY);
  }

  private static bool ApplyCpuBuilder(
    CpuMutableGameState state,
    int actorIndex,
    UseAbilityAction action,
    IReadOnlyDictionary<string, (AbilityEntityKind kind, int count, int cost)> options)
  {
    NetworkPiece actor = state.Pieces[actorIndex];
    var option = options[action.Ability];
    UnitAbilityState pending = AdvancedAbilityRules.AddPendingSelection(
      actor.AbilityState, action.Ability, new AbilitySelection(null, action.TargetX, action.TargetY));

    if (pending.PendingSelections.Count < option.count)
    {
      state.Pieces[actorIndex] = actor with { AbilityState = pending };
      return false;
    }

    if (option.cost > 0)
    {
      SpendMoney(state, actor.Team, option.cost);
    }

    if (option.kind == AbilityEntityKind.Portal)
    {
      AbilitySelection first = pending.PendingSelections[0];
      AbilitySelection second = pending.PendingSelections[1];
      string firstId = $"cpu-Portal-{state.Source.TurnNumber}-{state.AbilityEntities.Count}-{first.X}-{first.Y}";
      string secondId = $"cpu-Portal-{state.Source.TurnNumber}-{state.AbilityEntities.Count + 1}-{second.X}-{second.Y}";
      state.AbilityEntities.Add(new AbilityEntity(
        firstId, AbilityEntityKind.Portal, actor.Team, first.X, first.Y, 0, secondId, SourcePieceId: actor.Id));
      state.AbilityEntities.Add(new AbilityEntity(
        secondId, AbilityEntityKind.Portal, actor.Team, second.X, second.Y, 0, firstId, SourcePieceId: actor.Id));
    }
    else
    {
      foreach (AbilitySelection selection in pending.PendingSelections)
      {
        state.AbilityEntities.Add(CreateCpuAbilityEntity(
          state, option.kind, actor.Team, selection.X, selection.Y, actor.Id));
      }
    }

    state.Pieces[actorIndex] = actor with
    {
      HasAttackedThisTurn = true,
      AbilityState = AdvancedAbilityRules.ClearPendingSelections(pending)
    };
    return true;
  }

  private static int GetMoney(CpuGameState state, NetworkTeam team) =>
    state.Teams.TryGetValue(team, out CpuTeamState? teamState) ? teamState.Money : 0;

  private static bool HasCpuStructureAt(CpuGameState state, int x, int y) =>
    state.AbilityEntities.Any(entity => entity.X == x && entity.Y == y) ||
    state.Barricades.ContainsKey((x, y)) || state.Roads.ContainsKey((x, y)) ||
    state.Mines.ContainsKey((x, y));

  private static bool DestroyCpuStructure(CpuMutableGameState state, int x, int y)
  {
    int entityIndex = state.AbilityEntities.FindIndex(entity => entity.X == x && entity.Y == y);
    if (entityIndex >= 0)
    {
      AbilityEntity entity = state.AbilityEntities[entityIndex];
      state.AbilityEntities.RemoveAt(entityIndex);
      if (entity.Kind == AbilityEntityKind.Portal && entity.LinkedEntityId is not null)
      {
        state.AbilityEntities.RemoveAll(candidate => candidate.Id == entity.LinkedEntityId);
      }
      return true;
    }
    return state.Barricades.Remove((x, y)) || state.Roads.Remove((x, y)) || state.Mines.Remove((x, y));
  }

  private static bool TryParseCpuRune(string ability, out AbilityEntityKind kind)
  {
    kind = ability.ToLowerInvariant() switch
    {
      "runeattack" => AbilityEntityKind.RuneAttack,
      "runemovement" => AbilityEntityKind.RuneMovement,
      "runehealth" => AbilityEntityKind.RuneHealth,
      "runerange" => AbilityEntityKind.RuneRange,
      _ => default
    };
    return ability.Equals("RuneAttack", StringComparison.OrdinalIgnoreCase) ||
      ability.Equals("RuneMovement", StringComparison.OrdinalIgnoreCase) ||
      ability.Equals("RuneHealth", StringComparison.OrdinalIgnoreCase) ||
      ability.Equals("RuneRange", StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsWithinCpuSquareRange(NetworkPiece source, NetworkPiece target, int radius)
  {
    if (!UnitRules.TryGet(source.Type, out UnitRule sourceRule) ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule))
    {
      return false;
    }
    foreach ((int x, int y) first in OccupiedSquares(sourceRule, (source.X, source.Y)))
    foreach ((int x, int y) second in OccupiedSquares(targetRule, (target.X, target.Y)))
    {
      if (Math.Max(Math.Abs(first.x - second.x), Math.Abs(first.y - second.y)) <= radius)
      {
        return true;
      }
    }
    return false;
  }

  private static void DestroyCpuOverlappedTerrainAndEntities(
    CpuMutableGameState state,
    NetworkPiece piece,
    UnitRule rule)
  {
    foreach ((int x, int y) square in OccupiedSquares(rule, (piece.X, piece.Y)).ToArray())
    {
      state.Terrain.DestroyTile(square);
      state.Barricades.Remove(square);
      state.Roads.Remove(square);
      state.Mines.Remove(square);
      state.AbilityEntities.RemoveAll(entity => entity.X == square.x && entity.Y == square.y);
    }
  }

  private static void DetonateCpuTnt(CpuMutableGameState state, NetworkPiece demolitionist, AbilityEntity tnt)
  {
    state.AbilityEntities.RemoveAll(entity => entity.Id == tnt.Id);
    foreach (string victimId in state.Pieces
      .Where(victim => UnitRules.TryGet(victim.Type, out UnitRule victimRule) &&
        OccupiedSquares(victimRule, (victim.X, victim.Y)).Any(square =>
          Math.Max(Math.Abs(square.x - tnt.X), Math.Abs(square.y - tnt.Y)) <= 1))
      .Select(victim => victim.Id)
      .ToArray())
    {
      ApplySharedFixedDamage(state, victimId, demolitionist.Team, 30, applyCombatMitigation: false);
    }

    for (int y = tnt.Y - 1; y <= tnt.Y + 1; y++)
    for (int x = tnt.X - 1; x <= tnt.X + 1; x++)
    {
      state.Terrain.DestroyTile((x, y));
    }
  }

  private static void GenerateHwachaReloadAbilities(
    CpuGameState state,
    NetworkPiece actor,
    List<ICpuGameAction> actions)
  {
    foreach (NetworkPiece hwacha in state.Pieces
      .Where(piece => piece.Team == actor.Team && piece.Type == nameof(PieceType.Hwacha))
      .OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      AddIfLegal(state, new UseAbilityAction(
        actor.Team, actor.Id, "ReloadHwacha", hwacha.Id, hwacha.X, hwacha.Y), actions);
    }
  }

  private static bool GenerateCodexAdvancedAbilities(
    CpuGameState state,
    NetworkPiece actor,
    List<ICpuGameAction> actions)
  {
    string[]? squareAbilities = actor.Type switch
    {
      nameof(PieceType.Mason) => ["StoneWall", "Gatehouse", "Demolish"],
      nameof(PieceType.Carpenter) => ["Bridge", "Watchtower", "Demolish"],
      nameof(PieceType.Daedalus) => ["Gate", "Snare", "Demolish"],
      nameof(PieceType.Runesmith) => ["RuneAttack", "RuneMovement", "RuneHealth", "RuneRange", "Demolish"],
      nameof(PieceType.Thor) => ["Thunderstorm"],
      nameof(PieceType.Demolitionist) => ["PlaceTnt"],
      nameof(PieceType.Mashhit) => ["Destroy"],
      nameof(PieceType.Gatekeeper) => ["Portal", "Seal", "Demolish"],
      _ => null
    };

    if (squareAbilities is not null)
    {
      foreach ((int x, int y) position in GetPotentialActionSquares(state, actor))
      foreach (string ability in squareAbilities)
      {
        AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, ability, null, position.x, position.y), actions);
      }

      if (actor.Type == nameof(PieceType.Thor))
      {
        foreach (AbilityEntity storm in state.AbilityEntities
          .Where(entity => entity.Kind == AbilityEntityKind.Thunderstorm && entity.SourcePieceId == actor.Id)
          .OrderBy(entity => entity.Id, StringComparer.Ordinal))
        foreach ((int x, int y) position in GetPotentialActionSquares(state, actor))
        {
          AddIfLegal(state, new UseAbilityAction(
            actor.Team, actor.Id, "Thunderstorm", storm.Id, position.x, position.y), actions);
        }
      }
      if (actor.Type == nameof(PieceType.Demolitionist))
      {
        AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Detonate", null, actor.X, actor.Y), actions);
      }
      return true;
    }

    if (actor.Type == nameof(PieceType.Fafnir))
    {
      AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Transform", null, actor.X, actor.Y), actions);
      return true;
    }

    if (actor.Type == nameof(PieceType.CommandCentre))
    {
      foreach (NetworkPiece target in state.Pieces
        .Where(piece => piece.Team == actor.Team && piece.Id != actor.Id)
        .OrderBy(piece => piece.Id, StringComparer.Ordinal))
      foreach (string ability in new[] { "UpgradeAttack", "UpgradeHealth", "UpgradeMove" })
      {
        AddIfLegal(state, new UseAbilityAction(
          actor.Team, actor.Id, ability, target.Id, target.X, target.Y), actions);
      }
      return true;
    }

    return false;
  }
}
