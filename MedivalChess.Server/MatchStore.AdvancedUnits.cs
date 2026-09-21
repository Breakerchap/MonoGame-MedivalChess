using MedivalChess.Shared;

namespace MedivalChess.Server;

public sealed partial class MatchStore
{
  private readonly record struct AdvancedSpecialResult(bool Handled, bool Applied, bool SpendAction)
  {
    internal static AdvancedSpecialResult NotHandled => new(false, false, false);
    internal static AdvancedSpecialResult Rejected => new(true, false, false);
    internal static AdvancedSpecialResult AppliedAction => new(true, true, true);
    internal static AdvancedSpecialResult AppliedWithoutAction => new(true, true, false);
  }

  private static AdvancedSpecialResult TryUseAdvancedServerAbility(
    Match match,
    int actorIndex,
    int targetIndex,
    SpecialActionRequest request,
    PlayerSlot player
  )
  {
    NetworkPiece actor = match.Pieces[actorIndex];
    NetworkPiece? target = targetIndex >= 0 ? match.Pieces[targetIndex] : null;
    string ability = request.Ability ?? string.Empty;

    // Reload is performed by the adjacent friendly unit, not by the Hwacha itself.
    if (string.Equals(ability, "ReloadHwacha", StringComparison.OrdinalIgnoreCase))
    {
      if (target is null || target.Team != actor.Team || target.Type != nameof(PieceType.Hwacha) ||
          actor.HasAttackedThisTurn || !UnitRules.TryGet(actor.Type, out UnitRule actorRule) ||
          !UnitRules.TryGet(target.Type, out UnitRule targetRule) ||
          !AbilityRules.AreAdjacent(actorRule, (actor.X, actor.Y), targetRule, (target.X, target.Y), includeDiagonal: true) ||
          !AdvancedAbilityRules.CanReloadHwacha(target.AbilityState))
      {
        return AdvancedSpecialResult.Rejected;
      }

      match.Pieces[targetIndex] = target with
      {
        AbilityState = AdvancedAbilityRules.ReloadHwacha(target.AbilityState)
      };
      match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
      return AdvancedSpecialResult.AppliedAction;
    }

    if (!AdvancedAbilityRules.CanUseSpecialAbility(actor.AbilityState))
    {
      return IsAdvancedSpecialUnit(actor.Type) ? AdvancedSpecialResult.Rejected : AdvancedSpecialResult.NotHandled;
    }

    switch (actor.Type)
    {
      case nameof(PieceType.Baron):
        if (!string.Equals(ability, "Select", StringComparison.OrdinalIgnoreCase) ||
            actor.AbilityState?.UsedThisTurn == true || target is null || target.Team != actor.Team ||
            target.Id == actor.Id || !CanUseActionSquare(actor, target.X, target.Y))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.SelectTarget(actor.AbilityState, target.Id)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.WarDrum):
        if (!string.Equals(ability, "Refresh", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || target is null || target.Team != actor.Team ||
            target.Id == actor.Id || !CanUseActionSquare(actor, target.X, target.Y) ||
            !AdvancedAbilityRules.CanBeRefreshedByWarDrum(target.AbilityState, target.HasMovedThisTurn))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[targetIndex] = target with
        {
          HasMovedThisTurn = false,
          AbilityState = AdvancedAbilityRules.RefreshByWarDrum(target.AbilityState)
        };
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Harvester):
        if (!string.Equals(ability, "Harvest", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || !CanUseActionSquare(actor, request.TargetX, request.TargetY) ||
            !TryDestroyTerrainTile(match, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        player.Money = ClampCurrency((long)player.Money + AdvancedAbilityRules.HarvesterGold);
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Mason):
        return TryServerBuilder(
          match, actorIndex, request, player,
          new Dictionary<string, (AbilityEntityKind kind, int count, int totalCost)>(StringComparer.OrdinalIgnoreCase)
          {
            ["StoneWall"] = (AbilityEntityKind.StoneWall, 2, 10),
            ["Gatehouse"] = (AbilityEntityKind.Gatehouse, 2, 0)
          });

      case nameof(PieceType.Carpenter):
        if (string.Equals(ability, "Watchtower", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerSingleBuild(match, actorIndex, request, player, AbilityEntityKind.Watchtower, 15);
        }
        if (string.Equals(ability, "Bridge", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerBuilder(
            match, actorIndex, request, player,
            new Dictionary<string, (AbilityEntityKind kind, int count, int totalCost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Bridge"] = (AbilityEntityKind.Bridge, 2, 0)
            },
            requireBuildableLand: false);
        }
        if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerDemolish(match, actorIndex, request);
        }
        return AdvancedSpecialResult.Rejected;

      case nameof(PieceType.Witch):
        if (!string.Equals(ability, "PoisonCloud", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || !CanUseActionSquare(actor, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.AbilityEntities.RemoveAll(entity =>
          entity.Kind == AbilityEntityKind.PoisonCloud && entity.SourcePieceId == actor.Id);
        match.AbilityEntities.Add(CreateEntity(
          AbilityEntityKind.PoisonCloud, actor.Team, request.TargetX, request.TargetY, actor.Id));
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Druid):
        if (!string.Equals(ability, "Bramble", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || !IsAdjacentSquare(actor.X, actor.Y, request.TargetX, request.TargetY) ||
            !CanPlaceAbilityEntity(match, AbilityEntityKind.Bramble, actor.Team, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.AbilityEntities.Add(CreateEntity(
          AbilityEntityKind.Bramble, actor.Team, request.TargetX, request.TargetY, actor.Id));
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Phoenix):
        if (!string.Equals(ability, "Fire", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
            actor.Health <= AdvancedAbilityRules.PhoenixFireHealthCost ||
            !CanUseActionSquare(actor, request.TargetX, request.TargetY) ||
            !CanPlaceAbilityEntity(match, AbilityEntityKind.Fire, actor.Team, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[actorIndex] = actor with
        {
          Health = actor.Health - AdvancedAbilityRules.PhoenixFireHealthCost,
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        match.AbilityEntities.Add(CreateEntity(
          AbilityEntityKind.Fire, actor.Team, request.TargetX, request.TargetY, actor.Id));
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.WillOWisp):
        if (string.Equals(ability, "Settle", StringComparison.OrdinalIgnoreCase))
        {
          if (!AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
              actor.AbilityState?.Settled == true)
          {
            return AdvancedSpecialResult.Rejected;
          }
          match.Pieces[actorIndex] = actor with
          {
            AbilityState = AdvancedAbilityRules.SetSettled(actor.AbilityState)
          };
          return AdvancedSpecialResult.AppliedAction;
        }
        if (string.Equals(ability, "SpawnWisp", StringComparison.OrdinalIgnoreCase))
        {
          if (actor.AbilityState?.Settled != true || actor.HasAttackedThisTurn ||
              !IsAdjacentSquare(actor.X, actor.Y, request.TargetX, request.TargetY) ||
              !CanPlaceNetworkPiece(match, nameof(PieceType.Wisp), actor.Team, request.TargetX, request.TargetY))
          {
            return AdvancedSpecialResult.Rejected;
          }
          SpawnNetworkPiece(match, nameof(PieceType.Wisp), actor.Team, request.TargetX, request.TargetY);
          match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          return AdvancedSpecialResult.AppliedAction;
        }
        return AdvancedSpecialResult.Rejected;

      case nameof(PieceType.Daedalus):
        if (string.Equals(ability, "Gate", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerBuilder(
            match, actorIndex, request, player,
            new Dictionary<string, (AbilityEntityKind kind, int count, int totalCost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Gate"] = (AbilityEntityKind.Gate, 3, 0)
            });
        }
        if (string.Equals(ability, "Snare", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerSingleBuild(match, actorIndex, request, player, AbilityEntityKind.Snare, 0);
        }
        if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerDemolish(match, actorIndex, request);
        }
        return AdvancedSpecialResult.Rejected;

      case nameof(PieceType.Shieldsman):
        if (!string.Equals(ability, "Attach", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || target is null || target.Team != actor.Team ||
            target.Id == actor.Id || UnitRules.GetRequired(target.Type).Category == RuleCategory.Royal ||
            target.AttachedToId is not null ||
            match.Pieces.Any(piece => piece.AttachedToId == target.Id &&
              piece.AttachmentKind == NetworkAttachmentKind.Shieldsman) ||
            !CanUseActionSquare(actor, target.X, target.Y))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[actorIndex] = actor with
        {
          AttachedToId = target.Id,
          AttachmentKind = NetworkAttachmentKind.Shieldsman,
          X = target.X,
          Y = target.Y,
          HasAttackedThisTurn = true
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Runesmith):
        if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerDemolish(match, actorIndex, request);
        }
        if (actor.HasAttackedThisTurn || !TryParseRune(ability, out AbilityEntityKind runeKind) ||
            !CanUseActionSquare(actor, request.TargetX, request.TargetY) ||
            !CanPlaceAbilityEntity(match, runeKind, actor.Team, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.AbilityEntities.Add(CreateEntity(runeKind, actor.Team, request.TargetX, request.TargetY, actor.Id));
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Fafnir):
        if (!string.Equals(ability, "Transform", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
            player.Money < AdvancedAbilityRules.FafnirTransformCost)
        {
          return AdvancedSpecialResult.Rejected;
        }
        UnitRule fafnirDragon = UnitRules.GetRequired(nameof(PieceType.FafnirDragon));
        player.Money = ClampCurrency((long)player.Money - AdvancedAbilityRules.FafnirTransformCost);
        match.Pieces[actorIndex] = actor with
        {
          Type = nameof(PieceType.FafnirDragon),
          Health = fafnirDragon.Health,
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        DestroyOverlappedTerrainAndEntities(match, match.Pieces[actorIndex], fafnirDragon);
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Odin):
        if (!string.Equals(ability, "Protect", StringComparison.OrdinalIgnoreCase) ||
            (actor.AbilityState?.CooldownOwnerTurns ?? 0) > 0 || target is null || target.Team != actor.Team ||
            target.Id == actor.Id || UnitRules.GetRequired(target.Type).Category == RuleCategory.Royal ||
            !IsWithinSquare(actor, target, 3))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[targetIndex] = target with
        {
          AbilityState = AdvancedAbilityRules.ProtectWithOdin(target.AbilityState, actor.Id)
        };
        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.StartCooldown(actor.AbilityState, AdvancedAbilityRules.OdinCooldownTurns)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Thor):
        if (!string.Equals(ability, "Thunderstorm", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
            !CanUseActionSquare(actor, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        List<AbilityEntity> storms = match.AbilityEntities
          .Where(entity => entity.Kind == AbilityEntityKind.Thunderstorm && entity.SourcePieceId == actor.Id)
          .ToList();
        AbilityEntity? existingStorm = storms.FirstOrDefault(entity =>
          entity.X == request.TargetX && entity.Y == request.TargetY);
        if (existingStorm is null && storms.Count >= 3)
        {
          // Moving an existing storm is explicit: target its entity ID through TargetId.
          AbilityEntity? chosen = match.AbilityEntities.FirstOrDefault(entity =>
            entity.Id == request.TargetId && entity.Kind == AbilityEntityKind.Thunderstorm &&
            entity.SourcePieceId == actor.Id);
          if (chosen is null)
          {
            return AdvancedSpecialResult.Rejected;
          }
          int chosenIndex = match.AbilityEntities.IndexOf(chosen);
          match.AbilityEntities[chosenIndex] = chosen with { X = request.TargetX, Y = request.TargetY };
        }
        else if (existingStorm is null)
        {
          match.AbilityEntities.Add(CreateEntity(
            AbilityEntityKind.Thunderstorm, actor.Team, request.TargetX, request.TargetY, actor.Id));
        }
        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Demolitionist):
        if (string.Equals(ability, "PlaceTnt", StringComparison.OrdinalIgnoreCase))
        {
          if (actor.HasAttackedThisTurn ||
              match.AbilityEntities.Any(entity => entity.Kind == AbilityEntityKind.Tnt && entity.SourcePieceId == actor.Id) ||
              !IsAdjacentSquare(actor.X, actor.Y, request.TargetX, request.TargetY) ||
              !CanPlaceAbilityEntity(match, AbilityEntityKind.Tnt, actor.Team, request.TargetX, request.TargetY))
          {
            return AdvancedSpecialResult.Rejected;
          }
          match.AbilityEntities.Add(CreateEntity(
            AbilityEntityKind.Tnt, actor.Team, request.TargetX, request.TargetY, actor.Id));
          match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
          return AdvancedSpecialResult.AppliedAction;
        }
        if (string.Equals(ability, "Detonate", StringComparison.OrdinalIgnoreCase))
        {
          AbilityEntity? tnt = match.AbilityEntities.FirstOrDefault(entity =>
            entity.Kind == AbilityEntityKind.Tnt && entity.SourcePieceId == actor.Id);
          if (tnt is null || !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState))
          {
            return AdvancedSpecialResult.Rejected;
          }
          DetonateServerTnt(match, actor, tnt, player);
          actorIndex = match.Pieces.FindIndex(piece => piece.Id == actor.Id);
          if (actorIndex >= 0)
          {
            actor = match.Pieces[actorIndex];
            match.Pieces[actorIndex] = actor with
            {
              AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
            };
          }
          return AdvancedSpecialResult.AppliedAction;
        }
        return AdvancedSpecialResult.Rejected;

      case nameof(PieceType.CommandCentre):
        if (!ability.StartsWith("Upgrade", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) || target is null ||
            target.Team != actor.Team || UnitRules.GetRequired(target.Type).Category == RuleCategory.Royal ||
            target.AbilityState?.Upgraded == true || !IsWithinSquare(actor, target, 2) ||
            player.Money < AdvancedAbilityRules.CommandCentreUpgradeCost)
        {
          return AdvancedSpecialResult.Rejected;
        }
        string upgrade = ability["Upgrade".Length..].TrimStart(':', ' ');
        UnitAbilityState upgraded;
        try { upgraded = AdvancedAbilityRules.ApplyCommandCentreUpgrade(target.AbilityState, upgrade); }
        catch (ArgumentOutOfRangeException) { return AdvancedSpecialResult.Rejected; }
        int heal = string.Equals(upgrade, "health", StringComparison.OrdinalIgnoreCase)
          ? AdvancedAbilityRules.CommandCentreHealthBonus
          : 0;
        player.Money = ClampCurrency((long)player.Money - AdvancedAbilityRules.CommandCentreUpgradeCost);
        match.Pieces[targetIndex] = target with
        {
          Health = Math.Min(
            UnitRules.GetRequired(target.Type).Health + AdvancedAbilityRules.GetEffectiveMaxHealthBonus(upgraded),
            target.Health + heal),
          AbilityState = upgraded
        };
        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Hacker):
        if (!string.Equals(ability, "Hack", StringComparison.OrdinalIgnoreCase) ||
            (actor.AbilityState?.CooldownOwnerTurns ?? 0) > 0 || target is null || target.Team == actor.Team ||
            !IsWithinCircle(actor, target, 5))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[targetIndex] = target with
        {
          AbilityState = AdvancedAbilityRules.DisableAbilities(target.AbilityState, 1)
        };
        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.StartCooldown(actor.AbilityState, AdvancedAbilityRules.HackerCooldownTurns)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Mashhit):
        if (!string.Equals(ability, "Destroy", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || !CanUseActionSquare(actor, request.TargetX, request.TargetY))
        {
          return AdvancedSpecialResult.Rejected;
        }
        bool destroyed = TryDestroyTerrainTile(match, request.TargetX, request.TargetY) ||
          TryDestroyAbilityEntity(match, request.TargetX, request.TargetY);
        if (!destroyed)
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Imp):
        if (!string.Equals(ability, "Attach", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) || target is null ||
            target.Team != actor.Team || target.Id == actor.Id ||
            match.Pieces.Any(piece => piece.AttachedToId == target.Id &&
              piece.AttachmentKind == NetworkAttachmentKind.Imp) ||
            !CanUseActionSquare(actor, target.X, target.Y))
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[actorIndex] = actor with
        {
          AttachedToId = target.Id,
          AttachmentKind = NetworkAttachmentKind.Imp,
          X = target.X,
          Y = target.Y,
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Gatekeeper):
        if (string.Equals(ability, "Portal", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerLinkedPortalBuild(match, actorIndex, request);
        }
        if (string.Equals(ability, "Seal", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerBuilder(
            match, actorIndex, request, player,
            new Dictionary<string, (AbilityEntityKind kind, int count, int totalCost)>(StringComparer.OrdinalIgnoreCase)
            {
              ["Seal"] = (AbilityEntityKind.Seal, 2, 0)
            });
        }
        if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
        {
          return TryServerDemolish(match, actorIndex, request);
        }
        return AdvancedSpecialResult.Rejected;

      default:
        return AdvancedSpecialResult.NotHandled;
    }
  }

  private static bool IsAdvancedSpecialUnit(string type) => type is
    nameof(PieceType.Baron) or nameof(PieceType.WarDrum) or nameof(PieceType.Harvester) or
    nameof(PieceType.Mason) or nameof(PieceType.Carpenter) or nameof(PieceType.Witch) or
    nameof(PieceType.Druid) or nameof(PieceType.Phoenix) or nameof(PieceType.WillOWisp) or
    nameof(PieceType.Daedalus) or nameof(PieceType.Shieldsman) or nameof(PieceType.Runesmith) or
    nameof(PieceType.Fafnir) or nameof(PieceType.Odin) or nameof(PieceType.Thor) or
    nameof(PieceType.Demolitionist) or nameof(PieceType.CommandCentre) or nameof(PieceType.Hacker) or
    nameof(PieceType.Mashhit) or nameof(PieceType.Imp) or nameof(PieceType.Gatekeeper);

  private static AdvancedSpecialResult TryServerSingleBuild(
    Match match,
    int actorIndex,
    SpecialActionRequest request,
    PlayerSlot player,
    AbilityEntityKind kind,
    int cost
  )
  {
    NetworkPiece actor = match.Pieces[actorIndex];
    if (actor.HasAttackedThisTurn || !CanUseActionSquare(actor, request.TargetX, request.TargetY) ||
        player.Money < cost || !CanPlaceAbilityEntity(match, kind, actor.Team, request.TargetX, request.TargetY))
    {
      return AdvancedSpecialResult.Rejected;
    }
    player.Money = ClampCurrency((long)player.Money - cost);
    match.AbilityEntities.Add(CreateEntity(kind, actor.Team, request.TargetX, request.TargetY, actor.Id));
    match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
    return AdvancedSpecialResult.AppliedAction;
  }

  private static AdvancedSpecialResult TryServerBuilder(
    Match match,
    int actorIndex,
    SpecialActionRequest request,
    PlayerSlot player,
    IReadOnlyDictionary<string, (AbilityEntityKind kind, int count, int totalCost)> options,
    bool requireBuildableLand = true
  )
  {
    NetworkPiece actor = match.Pieces[actorIndex];
    if (actor.HasAttackedThisTurn || !options.TryGetValue(request.Ability, out var option) ||
        !CanUseActionSquare(actor, request.TargetX, request.TargetY))
    {
      return AdvancedSpecialResult.Rejected;
    }
    if (requireBuildableLand && !CanPlaceAbilityEntity(
          match, option.kind, actor.Team, request.TargetX, request.TargetY))
    {
      return AdvancedSpecialResult.Rejected;
    }
    if (!requireBuildableLand && !NetworkBoardRules.Contains(match.Configuration, request.TargetX, request.TargetY))
    {
      return AdvancedSpecialResult.Rejected;
    }

    UnitAbilityState state = AdvancedAbilityRules.AddPendingSelection(
      actor.AbilityState, request.Ability, new AbilitySelection(null, request.TargetX, request.TargetY));
    if (state.PendingSelections.Select(selection => (selection.X, selection.Y)).Distinct().Count() !=
        state.PendingSelections.Count)
    {
      return AdvancedSpecialResult.Rejected;
    }

    if (state.PendingSelections.Count < option.count)
    {
      match.Pieces[actorIndex] = actor with { AbilityState = state };
      return AdvancedSpecialResult.AppliedWithoutAction;
    }
    if (state.PendingSelections.Count != option.count || player.Money < option.totalCost)
    {
      return AdvancedSpecialResult.Rejected;
    }

    foreach (AbilitySelection selection in state.PendingSelections)
    {
      if (requireBuildableLand &&
          !CanPlaceAbilityEntity(match, option.kind, actor.Team, selection.X, selection.Y))
      {
        return AdvancedSpecialResult.Rejected;
      }
    }

    player.Money = ClampCurrency((long)player.Money - option.totalCost);
    if (option.kind == AbilityEntityKind.Bridge)
    {
      foreach (AbilitySelection selection in state.PendingSelections)
      {
        match.AbilityEntities.Add(CreateEntity(option.kind, actor.Team, selection.X, selection.Y, actor.Id));
      }
    }
    else
    {
      foreach (AbilitySelection selection in state.PendingSelections)
      {
        match.AbilityEntities.Add(CreateEntity(option.kind, actor.Team, selection.X, selection.Y, actor.Id));
      }
    }
    match.Pieces[actorIndex] = actor with
    {
      HasAttackedThisTurn = true,
      AbilityState = AdvancedAbilityRules.ClearPendingSelections(state)
    };
    return AdvancedSpecialResult.AppliedAction;
  }

  private static AdvancedSpecialResult TryServerLinkedPortalBuild(
    Match match,
    int actorIndex,
    SpecialActionRequest request
  )
  {
    NetworkPiece actor = match.Pieces[actorIndex];
    if (actor.HasAttackedThisTurn || !CanUseActionSquare(actor, request.TargetX, request.TargetY) ||
        !CanPlaceAbilityEntity(match, AbilityEntityKind.Portal, actor.Team, request.TargetX, request.TargetY))
    {
      return AdvancedSpecialResult.Rejected;
    }

    UnitAbilityState state = AdvancedAbilityRules.AddPendingSelection(
      actor.AbilityState, "Portal", new AbilitySelection(null, request.TargetX, request.TargetY));
    if (state.PendingSelections.Count == 1)
    {
      match.Pieces[actorIndex] = actor with { AbilityState = state };
      return AdvancedSpecialResult.AppliedWithoutAction;
    }
    if (state.PendingSelections.Count != 2 ||
        state.PendingSelections[0].X == state.PendingSelections[1].X &&
        state.PendingSelections[0].Y == state.PendingSelections[1].Y)
    {
      return AdvancedSpecialResult.Rejected;
    }

    string firstId = Guid.NewGuid().ToString("N");
    string secondId = Guid.NewGuid().ToString("N");
    AbilitySelection first = state.PendingSelections[0];
    AbilitySelection second = state.PendingSelections[1];
    match.AbilityEntities.Add(new AbilityEntity(
      firstId, AbilityEntityKind.Portal, actor.Team, first.X, first.Y, 0, secondId, SourcePieceId: actor.Id));
    match.AbilityEntities.Add(new AbilityEntity(
      secondId, AbilityEntityKind.Portal, actor.Team, second.X, second.Y, 0, firstId, SourcePieceId: actor.Id));
    match.Pieces[actorIndex] = actor with
    {
      HasAttackedThisTurn = true,
      AbilityState = AdvancedAbilityRules.ClearPendingSelections(state)
    };
    return AdvancedSpecialResult.AppliedAction;
  }

  private static AdvancedSpecialResult TryServerDemolish(
    Match match,
    int actorIndex,
    SpecialActionRequest request
  )
  {
    NetworkPiece actor = match.Pieces[actorIndex];
    if (!CanUseActionSquare(actor, request.TargetX, request.TargetY))
    {
      return AdvancedSpecialResult.Rejected;
    }
    bool removed = TryDestroyAbilityEntity(match, request.TargetX, request.TargetY) ||
      match.Barricades.Remove((request.TargetX, request.TargetY)) ||
      match.Roads.Remove((request.TargetX, request.TargetY)) ||
      match.Mines.Remove((request.TargetX, request.TargetY));
    return removed ? AdvancedSpecialResult.AppliedAction : AdvancedSpecialResult.Rejected;
  }

  private static bool CanPlaceAbilityEntity(
    Match match,
    AbilityEntityKind kind,
    NetworkTeam owner,
    int x,
    int y
  )
  {
    if (!NetworkBoardRules.Contains(match.Configuration, x, y) ||
        match.AbilityEntities.Any(entity => entity.X == x && entity.Y == y) ||
        match.Barricades.ContainsKey((x, y)) ||
        match.Pieces.Any(piece => UnitRules.TryGet(piece.Type, out UnitRule rule) &&
          UnitRules.FootprintsOverlap(piece.X, piece.Y, rule.Width, rule.Height, x, y, 1, 1)))
    {
      return false;
    }

    return kind == AbilityEntityKind.Bridge || !match.Terrain.IsLake((x, y));
  }

  private static AbilityEntity CreateEntity(
    AbilityEntityKind kind,
    NetworkTeam owner,
    int x,
    int y,
    string? sourcePieceId = null
  )
  {
    AbilityEntityDefinition definition = AbilityEntityRules.GetRequired(kind);
    return new AbilityEntity(
      Guid.NewGuid().ToString("N"), kind, owner, x, y, definition.Health,
      SourcePieceId: sourcePieceId);
  }

  private static bool TryDestroyAbilityEntity(Match match, int x, int y)
  {
    int index = match.AbilityEntities.FindIndex(entity => entity.X == x && entity.Y == y);
    if (index < 0) return false;
    AbilityEntity entity = match.AbilityEntities[index];
    match.AbilityEntities.RemoveAt(index);
    if (entity.Kind == AbilityEntityKind.Portal && entity.LinkedEntityId is not null)
    {
      match.AbilityEntities.RemoveAll(candidate => candidate.Id == entity.LinkedEntityId);
    }
    return true;
  }

  private static bool TryDestroyTerrainTile(Match match, int x, int y)
  {
    var square = (x, y);
    if (!NetworkBoardRules.Contains(match.Configuration, x, y)) return false;
    if (match.Terrain.DestroyTile(square))
    {
      match.DestroyedTerrainTiles.Add(square);
      return true;
    }
    if (match.Roads.Remove(square)) return true;
    if (match.Mines.Remove(square)) return true;
    return false;
  }

  private static bool IsAdjacentSquare(int x, int y, int targetX, int targetY) =>
    Math.Max(Math.Abs(targetX - x), Math.Abs(targetY - y)) == 1;

  private static bool IsWithinSquare(NetworkPiece source, NetworkPiece target, int radius) =>
    Math.Max(Math.Abs(source.X - target.X), Math.Abs(source.Y - target.Y)) <= radius;

  private static bool IsWithinCircle(NetworkPiece source, NetworkPiece target, int radius)
  {
    int dx = source.X - target.X;
    int dy = source.Y - target.Y;
    return dx * dx + dy * dy <= radius * radius;
  }

  private static bool TryParseRune(string ability, out AbilityEntityKind kind)
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

  private static bool CanPlaceNetworkPiece(
    Match match,
    string type,
    NetworkTeam team,
    int x,
    int y
  )
  {
    UnitRule rule = UnitRules.GetRequired(type);
    if (!NetworkPieceRules.FootprintFitsBoard(match.Configuration, x, y, rule.Width, rule.Height))
    {
      return false;
    }
    for (int oy = 0; oy < rule.Height; oy++)
    for (int ox = 0; ox < rule.Width; ox++)
    {
      var square = (x + ox, y + oy);
      if (match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square) ||
          match.AbilityEntities.Any(entity => entity.X == square.Item1 && entity.Y == square.Item2 &&
            AbilityEntityRules.BlocksLandingFor(entity, team)))
      {
        return false;
      }
    }
    return !match.Pieces.Any(piece =>
      FootprintsOverlap(piece, x, y, rule.Width, rule.Height));
  }

  private static NetworkPiece SpawnNetworkPiece(
    Match match,
    string type,
    NetworkTeam team,
    int x,
    int y
  )
  {
    UnitRule rule = UnitRules.GetRequired(type);
    NetworkPiece piece = new(
      Guid.NewGuid().ToString("N"), type, team, x, y, rule.Health,
      FacingX: TeamRules.GetForwardDirection(team).x,
      FacingY: TeamRules.GetForwardDirection(team).y,
      AbilityState: new UnitAbilityState());
    match.Pieces.Add(piece);
    return piece;
  }

  private static void DestroyOverlappedTerrainAndEntities(Match match, NetworkPiece piece, UnitRule rule)
  {
    foreach ((int x, int y) square in OccupiedSquares(rule, (piece.X, piece.Y)).ToArray())
    {
      if (match.Terrain.DestroyTile(square))
      {
        match.DestroyedTerrainTiles.Add(square);
      }
      match.Barricades.Remove(square);
      match.Roads.Remove(square);
      match.Mines.Remove(square);
      match.AbilityEntities.RemoveAll(entity => entity.X == square.x && entity.Y == square.y);
    }
  }

  private static void DetonateServerTnt(
    Match match,
    NetworkPiece demolitionist,
    AbilityEntity tnt,
    PlayerSlot owner
  )
  {
    match.AbilityEntities.Remove(tnt);
    foreach (NetworkPiece victim in match.Pieces.ToArray())
    {
      if (!UnitRules.TryGet(victim.Type, out UnitRule victimRule)) continue;
      bool inBlast = OccupiedSquares(victimRule, (victim.X, victim.Y))
        .Any(square => Math.Max(Math.Abs(square.x - tnt.X), Math.Abs(square.y - tnt.Y)) <= 1);
      if (inBlast)
      {
        ResolvePieceDamage(match, demolitionist, owner, victim.Id, 30);
      }
    }
    for (int y = tnt.Y - 1; y <= tnt.Y + 1; y++)
    for (int x = tnt.X - 1; x <= tnt.X + 1; x++)
    {
      TryDestroyTerrainTile(match, x, y);
    }
  }
}
