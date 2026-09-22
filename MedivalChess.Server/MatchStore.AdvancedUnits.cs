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
          actor.HasAttackedThisTurn || target.HasAttackedThisTurn ||
          !AdvancedAbilityRules.CanUseSpecialAbility(actor.AbilityState) ||
          !UnitRules.TryGet(actor.Type, out UnitRule actorRule) ||
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

    if (actor.Type == nameof(PieceType.Mimic))
    {
      if (!string.Equals(ability, "Swap", StringComparison.OrdinalIgnoreCase) ||
          target is null || !TryServerMimicSwap(match, actorIndex, targetIndex))
      {
        return AdvancedSpecialResult.Rejected;
      }
      return AdvancedSpecialResult.AppliedAction;
    }

    if (actor.Type == nameof(PieceType.Succubus))
    {
      if (!AdvancedAbilityRules.CanUseSpecialAbility(actor.AbilityState))
      {
        return AdvancedSpecialResult.Rejected;
      }

      if (string.Equals(ability, "Attach", StringComparison.OrdinalIgnoreCase))
      {
        if (actor.AttachedToId is not null || target is null ||
            target.Team == actor.Team || target.AttachedToId is not null ||
            !AdvancedAbilityRules.CanSuccubusAttach(actor.AbilityState, target.Id))
        {
          return AdvancedSpecialResult.Rejected;
        }

        match.Pieces[actorIndex] = actor with
        {
          AttachedToId = target.Id,
          AttachmentKind = NetworkAttachmentKind.Succubus,
          X = target.X,
          Y = target.Y
        };
        return AdvancedSpecialResult.AppliedWithoutAction;
      }

      if (string.Equals(ability, "Detach", StringComparison.OrdinalIgnoreCase))
      {
        if (actor.AttachmentKind != NetworkAttachmentKind.Succubus ||
            string.IsNullOrWhiteSpace(actor.AttachedToId) ||
            target is not null ||
            !UnitRules.TryGet(actor.Type, out UnitRule actorRule))
        {
          return AdvancedSpecialResult.Rejected;
        }

        NetworkPiece? host = match.Pieces.FirstOrDefault(piece => piece.Id == actor.AttachedToId);
        if (host is null || !UnitRules.TryGet(host.Type, out UnitRule hostRule) ||
            !AbilityRules.AreAdjacent(
              actorRule, (request.TargetX, request.TargetY),
              hostRule, (host.X, host.Y), includeDiagonal: true) ||
            !CanDisplaceServerPieceTo(
              match, actor, actorRule, (request.TargetX, request.TargetY)))
        {
          return AdvancedSpecialResult.Rejected;
        }

        match.Pieces[actorIndex] = actor with
        {
          AttachedToId = null,
          AttachmentKind = NetworkAttachmentKind.None,
          X = request.TargetX,
          Y = request.TargetY
        };
        return AdvancedSpecialResult.AppliedWithoutAction;
      }

      return AdvancedSpecialResult.Rejected;
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

      case nameof(PieceType.Medusa):
        if (!string.Equals(ability, "Petrify", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || target is null || target.Id == actor.Id ||
            RoyalAbilityRules.IsRoyal(target.Type, target.IsRoyalProxy, target.PossessedUnitId) ||
            !AdvancedAbilityRules.CanMedusaPetrify(target.Type) ||
            !CanUseActionTarget(match, actor, target))
        {
          return AdvancedSpecialResult.Rejected;
        }
        ClearServerPetrificationBy(match, actor.Id);
        target = match.Pieces[targetIndex];
        match.Pieces[targetIndex] = target with
        {
          AbilityState = AdvancedAbilityRules.SetPetrified(target.AbilityState, actor.Id)
        };
        match.Pieces[actorIndex] = actor with { HasAttackedThisTurn = true };
        return AdvancedSpecialResult.AppliedAction;

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

      case nameof(PieceType.Muse):
        if (!string.Equals(ability, "Attach", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn || target is null || target.Team != actor.Team ||
            target.Id == actor.Id || target.AttachedToId is not null)
        {
          return AdvancedSpecialResult.Rejected;
        }
        match.Pieces[actorIndex] = actor with
        {
          AttachedToId = target.Id,
          AttachmentKind = NetworkAttachmentKind.Muse,
          X = target.X,
          Y = target.Y,
          HasAttackedThisTurn = true
        };
        return AdvancedSpecialResult.AppliedAction;

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
        UnitRule fafnirDragon = UnitRules.GetRequired(nameof(PieceType.FafnirDragon));
        if (!string.Equals(ability, "Transform", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState) ||
            player.Money < AdvancedAbilityRules.FafnirTransformCost ||
            !NetworkPieceRules.FootprintFitsBoard(
              match.Configuration, actor.X, actor.Y, fafnirDragon.Width, fafnirDragon.Height))
        {
          return AdvancedSpecialResult.Rejected;
        }
        player.Money = ClampCurrency((long)player.Money - AdvancedAbilityRules.FafnirTransformCost);
        match.Pieces[actorIndex] = actor with
        {
          Type = nameof(PieceType.FafnirDragon),
          Health = fafnirDragon.Health,
          AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
        };
        DestroyOverlappedTerrainAndEntities(match, match.Pieces[actorIndex], fafnirDragon);
        PushServerUnitsOutOfFafnirFootprint(match, match.Pieces[actorIndex], fafnirDragon, player);
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

      case nameof(PieceType.Chronos):
        if (!string.Equals(ability, "Rewind", StringComparison.OrdinalIgnoreCase) ||
            target is null || target.Id == actor.Id || target.Team != actor.Team ||
            (actor.AbilityState?.CooldownOwnerTurns ?? 0) > 0 ||
            actor.AbilityState?.PreviousOwnerTurnStart is not UnitTurnSnapshot chronosSnapshot ||
            target.AbilityState?.PreviousOwnerTurnStart is not UnitTurnSnapshot targetSnapshot ||
            !IsWithinCircle(actor, target, 3) ||
            !UnitRules.TryGet(actor.Type, out UnitRule chronosRule) ||
            !UnitRules.TryGet(target.Type, out UnitRule targetRule))
        {
          return AdvancedSpecialResult.Rejected;
        }

        (int x, int y) chronosDestination = (chronosSnapshot.X, chronosSnapshot.Y);
        (int x, int y) targetDestination = (targetSnapshot.X, targetSnapshot.Y);
        if (!CanSwapServerPieceTo(match, actor, chronosRule, target, chronosDestination) ||
            !CanSwapServerPieceTo(match, target, targetRule, actor, targetDestination) ||
            UnitRules.FootprintsOverlap(
              chronosDestination.x, chronosDestination.y, chronosRule.Width, chronosRule.Height,
              targetDestination.x, targetDestination.y, targetRule.Width, targetRule.Height))
        {
          return AdvancedSpecialResult.Rejected;
        }

        match.Pieces[actorIndex] = actor with
        {
          X = chronosDestination.x,
          Y = chronosDestination.y,
          Health = Math.Max(1, chronosSnapshot.Health),
          AbilityState = AdvancedAbilityRules.StartCooldown(
            actor.AbilityState, AdvancedAbilityRules.ChronosCooldownTurns)
        };
        match.Pieces[targetIndex] = target with
        {
          X = targetDestination.x,
          Y = targetDestination.y,
          Health = Math.Max(1, targetSnapshot.Health)
        };
        for (int index = 0; index < match.Pieces.Count; index++)
        {
          NetworkPiece attachment = match.Pieces[index];
          if (attachment.AttachedToId == actor.Id)
          {
            match.Pieces[index] = attachment with
            {
              X = chronosDestination.x,
              Y = chronosDestination.y
            };
          }
          else if (attachment.AttachedToId == target.Id)
          {
            match.Pieces[index] = attachment with
            {
              X = targetDestination.x,
              Y = targetDestination.y
            };
          }
        }
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
        AbilityEntity? chosenStorm = string.IsNullOrWhiteSpace(request.TargetId)
          ? null
          : storms.FirstOrDefault(entity => entity.Id == request.TargetId);
        bool destinationOccupiedByOtherStorm = storms.Any(entity =>
          entity.Id != chosenStorm?.Id &&
          entity.X == request.TargetX && entity.Y == request.TargetY);
        if (destinationOccupiedByOtherStorm)
        {
          return AdvancedSpecialResult.Rejected;
        }
        if (chosenStorm is not null)
        {
          int chosenIndex = match.AbilityEntities.FindIndex(entity => entity.Id == chosenStorm.Id);
          match.AbilityEntities[chosenIndex] = chosenStorm with
          {
            X = request.TargetX,
            Y = request.TargetY
          };
        }
        else
        {
          if (storms.Count >= 3)
          {
            return AdvancedSpecialResult.Rejected;
          }
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
          match.Pieces[actorIndex] = actor with
          {
            HasAttackedThisTurn = true,
            AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(actor.AbilityState)
          };
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
          DetonateServerTnt(match, actor, tnt);
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

      case nameof(PieceType.Atlas):
        if (!string.Equals(ability, "AtlasMove", StringComparison.OrdinalIgnoreCase) ||
            !AdvancedAbilityRules.CanUseOncePerOwnerTurn(actor.AbilityState))
        {
          return AdvancedSpecialResult.Rejected;
        }
        {
          UnitAbilityState atlasState = actor.AbilityState ?? new UnitAbilityState();
          bool atlasPending = string.Equals(
            atlasState.PendingAbility, "AtlasMove", StringComparison.Ordinal);
          IReadOnlyList<AbilitySelection> moved = atlasPending
            ? atlasState.PendingSelections
            : Array.Empty<AbilitySelection>();

          if (string.IsNullOrWhiteSpace(atlasState.SelectedTargetId))
          {
            if (target?.Id == actor.Id && moved.Count > 0)
            {
              match.Pieces[actorIndex] = actor with
              {
                AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(
                  AdvancedAbilityRules.ClearPendingSelections(
                    atlasState with { SelectedTargetId = null }))
              };
              return AdvancedSpecialResult.AppliedAction;
            }

            if (target is null || target.Id == actor.Id ||
                target.Team != actor.Team || target.AttachedToId is not null ||
                !UnitRules.TryGet(target.Type, out UnitRule atlasTargetRule) ||
                atlasTargetRule.MoveRange <= 0 ||
                moved.Any(selection =>
                  string.Equals(selection.TargetId, target.Id, StringComparison.Ordinal)))
            {
              return AdvancedSpecialResult.Rejected;
            }

            match.Pieces[actorIndex] = actor with
            {
              AbilityState = atlasState with
              {
                PendingAbility = "AtlasMove",
                PendingSelections = moved,
                SelectedTargetId = target.Id
              }
            };
            return AdvancedSpecialResult.AppliedWithoutAction;
          }

          int movingIndex = match.Pieces.FindIndex(piece =>
            string.Equals(piece.Id, atlasState.SelectedTargetId, StringComparison.Ordinal));
          if (movingIndex < 0)
          {
            match.Pieces[actorIndex] = actor with
            {
              AbilityState = atlasState with { SelectedTargetId = null }
            };
            return AdvancedSpecialResult.Rejected;
          }

          NetworkPiece moving = match.Pieces[movingIndex];
          if (moving.Team != actor.Team || moving.AttachedToId is not null ||
              !UnitRules.TryGet(moving.Type, out UnitRule movingRule) ||
              movingRule.MoveRange <= 0)
          {
            return AdvancedSpecialResult.Rejected;
          }

          int dx = Math.Abs(request.TargetX - moving.X);
          int dy = Math.Abs(request.TargetY - moving.Y);
          if (target is not null || (dx == 0 && dy == 0) ||
              dx > 1 || dy > 1 ||
              !CanLandAt(
                match, moving, movingRule,
                (request.TargetX, request.TargetY),
                mayUsePalaceSupport: false))
          {
            return AdvancedSpecialResult.Rejected;
          }

          int oldX = moving.X;
          int oldY = moving.Y;
          NetworkPiece movedPiece = moving with
          {
            X = request.TargetX,
            Y = request.TargetY,
            HasMovedThisTurn = true,
            AbilityState = AdvancedAbilityRules.RecordMove(moving.AbilityState)
          };
          match.Pieces[movingIndex] = movedPiece;
          MoveAttachedPieces(match, movedPiece, oldX, oldY);
          ReleaseServerPetrificationIfBroken(match, movedPiece);

          UnitAbilityState updated = atlasState with { SelectedTargetId = null };
          updated = AdvancedAbilityRules.AddPendingSelection(
            updated,
            "AtlasMove",
            new AbilitySelection(moving.Id, request.TargetX, request.TargetY));

          if (updated.PendingSelections.Count >= 3)
          {
            match.Pieces[actorIndex] = match.Pieces[actorIndex] with
            {
              AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(
                AdvancedAbilityRules.ClearPendingSelections(updated))
            };
            return AdvancedSpecialResult.AppliedAction;
          }

          match.Pieces[actorIndex] = match.Pieces[actorIndex] with
          {
            AbilityState = updated
          };
          return AdvancedSpecialResult.AppliedWithoutAction;
        }

      case nameof(PieceType.Herald):
        if (!string.Equals(ability, "ToggleCompanion", StringComparison.OrdinalIgnoreCase) ||
            target is null || target.Id == actor.Id ||
            target.Team != actor.Team || target.AttachedToId is not null ||
            target.Id == match.TreasureCarrierId ||
            !AdvancedAbilityRules.CanMove(actor.Type, actor.AbilityState, actor.HasMovedThisTurn) ||
            !UnitRules.TryGet(target.Type, out UnitRule heraldTargetRule) ||
            !AbilityRules.IsHeraldCompanion(
              heraldTargetRule, (actor.X, actor.Y), (target.X, target.Y)))
        {
          return AdvancedSpecialResult.Rejected;
        }

        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.TogglePendingTarget(
            actor.AbilityState,
            "HeraldCompanions",
            target.Id,
            3)
        };
        return AdvancedSpecialResult.AppliedWithoutAction;

      case nameof(PieceType.BountyHunter):
        if (!string.Equals(ability, "SetBounty", StringComparison.OrdinalIgnoreCase) ||
            actor.AbilityState?.BountySelectionAvailable != true ||
            target is null ||
            !IsValidServerBountyTarget(actor, target))
        {
          return AdvancedSpecialResult.Rejected;
        }

        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.SetBountyTarget(
            actor.AbilityState, target.Id)
        };
        return AdvancedSpecialResult.AppliedWithoutAction;

      case nameof(PieceType.GangLeader):
        if (!string.Equals(ability, "Recruit", StringComparison.OrdinalIgnoreCase) ||
            target is null || target.Id == actor.Id ||
            target.Team == actor.Team || target.Team == NetworkTeam.Neutral ||
            target.AttachedToId is not null ||
            RoyalAbilityRules.IsRoyal(target.Type, target.IsRoyalProxy, target.PossessedUnitId) ||
            (actor.AbilityState?.CooldownOwnerTurns ?? 0) > 0 ||
            !CanUseActionTarget(match, actor, target) ||
            !UnitRules.TryGet(target.Type, out UnitRule recruitedRule))
        {
          return AdvancedSpecialResult.Rejected;
        }

        int recruitedBaseCost = target.Type == nameof(PieceType.Qilin) &&
          AdvancedAbilityRules.IsValidQilinCost(target.AbilityState?.VariableCostValue ?? 0)
            ? target.AbilityState!.VariableCostValue
            : recruitedRule.Cost;
        int recruitCost = Math.Max(0, recruitedBaseCost) * 2;
        if (player.Money < recruitCost)
        {
          return AdvancedSpecialResult.Rejected;
        }

        player.Money = ClampCurrency((long)player.Money - recruitCost);
        match.Pieces[targetIndex] = target with
        {
          Team = actor.Team,
          HasMovedThisTurn = true,
          HasAttackedThisTurn = true,
          AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(target.Type),
          AbilityState = (target.AbilityState ?? new UnitAbilityState()) with
          {
            CannotActThisTurn = true,
            CannotMoveThisTurn = true
          }
        };
        match.Pieces[actorIndex] = actor with
        {
          AbilityState = AdvancedAbilityRules.StartCooldown(
            actor.AbilityState, AdvancedAbilityRules.GangLeaderCooldownTurns)
        };
        return AdvancedSpecialResult.AppliedAction;

      case nameof(PieceType.Fylgja):
        if (!string.Equals(ability, "ForceMove", StringComparison.OrdinalIgnoreCase) ||
            actor.HasAttackedThisTurn)
        {
          return AdvancedSpecialResult.Rejected;
        }

        UnitAbilityState fylgjaState = actor.AbilityState ?? new UnitAbilityState();
        if (!string.Equals(fylgjaState.PendingAbility, "ForceMove", StringComparison.Ordinal) ||
            fylgjaState.PendingSelections.Count == 0)
        {
          if (target is null || target.Id == actor.Id || target.AttachedToId is not null ||
              !UnitRules.TryGet(target.Type, out UnitRule selectedRule) ||
              selectedRule.Category == RuleCategory.Structure ||
              !CanUseActionTarget(match, actor, target))
          {
            return AdvancedSpecialResult.Rejected;
          }

          match.Pieces[actorIndex] = actor with
          {
            AbilityState = AdvancedAbilityRules.AddPendingSelection(
              fylgjaState,
              "ForceMove",
              new AbilitySelection(target.Id, target.X, target.Y))
          };
          return AdvancedSpecialResult.AppliedWithoutAction;
        }

        string? forcedId = fylgjaState.PendingSelections[0].TargetId;
        int forcedIndex = string.IsNullOrWhiteSpace(forcedId)
          ? -1
          : match.Pieces.FindIndex(piece => piece.Id == forcedId);
        if (forcedIndex < 0)
        {
          match.Pieces[actorIndex] = actor with
          {
            AbilityState = AdvancedAbilityRules.ClearPendingSelections(fylgjaState)
          };
          return AdvancedSpecialResult.Rejected;
        }

        NetworkPiece forced = match.Pieces[forcedIndex];
        if (forced.AttachedToId is not null ||
            !UnitRules.TryGet(forced.Type, out UnitRule forcedBaseRule) ||
            forcedBaseRule.Category == RuleCategory.Structure ||
            !TryGetServerFylgjaForcedMovementPath(
              match, forced, request.TargetX, request.TargetY,
              out List<(int x, int y)> forcedPath))
        {
          return AdvancedSpecialResult.Rejected;
        }

        AttackTurnState fylgjaAttackState = AbilityStateRules.RecordAttack(
          actor.Type, actor.AttacksThisTurn);
        match.Pieces[actorIndex] = actor with
        {
          AttacksThisTurn = fylgjaAttackState.AttacksThisTurn,
          HasAttackedThisTurn = fylgjaAttackState.HasAttackedThisTurn,
          AbilityState = AdvancedAbilityRules.RecordAttack(
            actor.Type,
            AdvancedAbilityRules.ClearPendingSelections(fylgjaState),
            forced.Id)
        };

        NetworkPiece movedForced = forced with
        {
          X = request.TargetX,
          Y = request.TargetY,
          HasMovedThisTurn = true,
          AbilityState = AdvancedAbilityRules.RecordMove(forced.AbilityState)
        };
        match.Pieces[forcedIndex] = movedForced;
        for (int attachmentIndex = 0; attachmentIndex < match.Pieces.Count; attachmentIndex++)
        {
          NetworkPiece attachment = match.Pieces[attachmentIndex];
          if (attachment.AttachedToId == forced.Id)
          {
            match.Pieces[attachmentIndex] = attachment with
            {
              X = request.TargetX,
              Y = request.TargetY
            };
          }
        }

        ReleaseServerPetrificationIfBroken(match, movedForced);
        TriggerMinesAlongMovement(match, movedForced, forcedPath);
        TriggerServerAbilityEntitiesAlongMovement(match, movedForced.Id, forcedPath);
        int liveForcedIndex = match.Pieces.FindIndex(piece => piece.Id == movedForced.Id);
        if (liveForcedIndex >= 0)
        {
          NetworkPiece liveForced = match.Pieces[liveForcedIndex];
          TryDeliverTreasure(match, liveForced);
          if (IsEscortVictory(match, liveForced, liveForced.X, liveForced.Y))
          {
            match.Winner = liveForced.Team;
          }
        }
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
          TryDestroyAbilityEntity(match, request.TargetX, request.TargetY) ||
          match.Barricades.Remove((request.TargetX, request.TargetY));
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
    nameof(PieceType.Mimic) or
    nameof(PieceType.Mason) or nameof(PieceType.Carpenter) or nameof(PieceType.Witch) or
    nameof(PieceType.Druid) or nameof(PieceType.Phoenix) or nameof(PieceType.WillOWisp) or
    nameof(PieceType.Medusa) or nameof(PieceType.Daedalus) or nameof(PieceType.Muse) or
    nameof(PieceType.Shieldsman) or nameof(PieceType.Runesmith) or
    nameof(PieceType.Fafnir) or nameof(PieceType.Odin) or nameof(PieceType.Thor) or
    nameof(PieceType.Atlas) or
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
    if (!CanPlaceAbilityEntity(
          match, option.kind, actor.Team, request.TargetX, request.TargetY,
          allowLake: !requireBuildableLand))
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
      if (!CanPlaceAbilityEntity(
            match, option.kind, actor.Team, selection.X, selection.Y,
            allowLake: !requireBuildableLand))
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
    int y,
    bool allowLake = false
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

    return allowLake || !match.Terrain.IsLake((x, y));
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
    AbilityEntity tnt
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
        ApplyServerAbilityEntityDamage(match, victim.Id, demolitionist.Team, 30);
      }
    }
    for (int y = tnt.Y - 1; y <= tnt.Y + 1; y++)
    for (int x = tnt.X - 1; x <= tnt.X + 1; x++)
    {
      TryDestroyTerrainTile(match, x, y);
    }
  }

  private static bool TryGetServerFylgjaForcedMovementPath(
    Match match,
    NetworkPiece forced,
    int targetX,
    int targetY,
    out List<(int x, int y)> path)
  {
    path = null!;
    if (!UnitRules.TryGet(forced.Type, out UnitRule baseRule))
    {
      return false;
    }

    baseRule = GetEffectiveMovementRule(match, forced, baseRule);
    UnitRule forcedRule = baseRule with
    {
      Type = "FylgjaForcedMovement",
      MoveRange = 3,
      MinimumMoveRange = 1,
      MovePattern = RuleShape.Any
    };

    bool CanLand((int x, int y) destination)
    {
      if (match.Pieces.Any(other =>
        other.Id != forced.Id &&
        other.AttachedToId is null &&
        other.Type != nameof(PieceType.Farm) &&
        UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
        UnitRules.FootprintsOverlap(
          other.X, other.Y, otherRule.Width, otherRule.Height,
          destination.x, destination.y, baseRule.Width, baseRule.Height)))
      {
        return false;
      }
      return CanLandAt(match, forced, baseRule, destination);
    }

    Dictionary<(int x, int y), List<(int x, int y)>> paths =
      MovementRules.FindPaths(
        forcedRule,
        (forced.X, forced.Y),
        forced.Team,
        CanLand,
        (from, destination) =>
          CanTravelThrough(match, forced, baseRule, from, destination),
        destination => GetMovementCost(match, forced, baseRule, destination),
        (from, to) => CrossesRiver(match, forced, baseRule, from, to),
        (from, destination) =>
          GetMovementCost(match, forced, baseRule, from, destination),
        _ => 3,
        3,
        _ => true);

    return paths.TryGetValue((targetX, targetY), out path!);
  }


}
