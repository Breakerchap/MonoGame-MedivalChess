using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess;

internal sealed partial class Game1
{
  private void TriggerLocalAbilityEntitiesAlongMovement(
    Piece movingPiece,
    IReadOnlyList<(int x, int y)> path)
  {
    HashSet<string> triggered = new(StringComparer.Ordinal);
    (int x, int y)? finalStep = path.Count > 0 ? path[^1] : null;
    foreach ((int x, int y) step in path)
    {
      if (!pieceSetup.Pieces.Contains(movingPiece)) return;
      UnitRule rule = UnitRules.FromPieceDefinition(movingPiece.Definition);
      NetworkPiece moving = new(
        movingPiece.NetworkId,
        movingPiece.Definition.Type.ToString(),
        movingPiece.Team.ToNetworkTeam(),
        step.x,
        step.y,
        movingPiece.CurrentHealth);

      string[] entityIds = _abilityEntities
        .Where(entity => !triggered.Contains(entity.Id) &&
          OccupiedSquares(movingPiece.Definition, step).Any(square =>
            entity.X == square.x && entity.Y == square.y))
        .Select(entity => entity.Id)
        .ToArray();

      foreach (string entityId in entityIds)
      {
        int entityIndex = _abilityEntities.FindIndex(entity => entity.Id == entityId);
        if (entityIndex < 0) continue;
        AbilityEntity entity = _abilityEntities[entityIndex];
        if (entity.Kind == AbilityEntityKind.Snare && finalStep != step)
        {
          continue;
        }
        if (entity.Kind == AbilityEntityKind.Portal && finalStep == step &&
            entity.Owner == moving.Team)
        {
          AbilityEntity linked = AbilityEntityRules.GetLinkedPortal(_abilityEntities, entity);
          if (linked is not null &&
              CanDisplaceLocalPieceTo(movingPiece, (linked.X, linked.Y)))
          {
            movingPiece.Position = (linked.X, linked.Y);
            ReleaseLocalPetrificationIfBroken(movingPiece);
            foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == movingPiece))
            {
              attachment.Position = movingPiece.Position;
            }
            pieceSetup.RefreshOccupancy();
          }
          triggered.Add(entity.Id);
          continue;
        }

        AbilityEntityEntryEffect effect = AbilityEntityEffectRules.GetEntryEffect(entity, moving);
        triggered.Add(entity.Id);

        if (effect.EntityDamage > 0 && entity.Health > 0)
        {
          int remaining = entity.Health - effect.EntityDamage;
          if (remaining <= 0) _abilityEntities.RemoveAt(entityIndex);
          else _abilityEntities[entityIndex] = entity with { Health = remaining };
        }
        else if (effect.ConsumeEntity)
        {
          _abilityEntities.RemoveAt(entityIndex);
        }

        if (effect.LockMovementNextOwnerTurn)
        {
          movingPiece.AbilityState = movingPiece.AbilityState with
          {
            SkipMovementOwnerTurns = Math.Max(movingPiece.AbilityState.SkipMovementOwnerTurns, 2)
          };
        }

        if (effect.UnitDamage > 0)
        {
          ApplyLocalAbilityEntityDamage(movingPiece, entity.Owner, effect.UnitDamage);
          if (!pieceSetup.Pieces.Contains(movingPiece)) return;
          moving = moving with { Health = movingPiece.CurrentHealth };
        }
      }
    }
  }

  private void TriggerLocalPoisonCloudsAtOwnerTurnStart(TeamName activeTeam)
  {
    NetworkTeam ownerTurn = activeTeam.ToNetworkTeam();
    AbilityEntity[] clouds = _abilityEntities
      .Where(entity => entity.Kind == AbilityEntityKind.PoisonCloud && entity.Owner == ownerTurn)
      .ToArray();

    foreach (AbilityEntity cloud in clouds)
    {
      foreach (Piece piece in pieceSetup.Pieces.Where(piece => piece.AttachedTo is null).ToArray())
      {
        UnitRule rule = UnitRules.FromPieceDefinition(piece.Definition);
        NetworkPiece snapshot = new(
          piece.NetworkId,
          piece.Definition.Type.ToString(),
          piece.Team.ToNetworkTeam(),
          piece.Position.x,
          piece.Position.y,
          piece.CurrentHealth);
        if (AbilityEntityEffectRules.PoisonCloudAffects(cloud, snapshot, rule, ownerTurn))
        {
          ApplyLocalAbilityEntityDamage(
            piece,
            cloud.Owner,
            AbilityEntityRules.GetRequired(AbilityEntityKind.PoisonCloud).StartOfOwnerTurnDamage);
        }
      }
    }
  }

  private void ApplyLocalAbilityEntityDamage(Piece target, NetworkTeam sourceTeam, int damage)
  {
    if (!pieceSetup.Pieces.Contains(target) || target.Definition.Type == PieceType.Helicopter) return;
    int applied = ApplyLocalChessKingDeathRule(
      target,
      AbilityRules.LimitIncomingDamage(UnitRules.FromPieceDefinition(target.Definition), damage));
    target.CurrentHealth -= applied;
    HandlePieceDestroyed(target, sourceTeam.ToTeamName());
  }

  private void RemoveSourceBoundLocalAbilityEntities(string sourcePieceId)
  {
    _abilityEntities.RemoveAll(entity =>
      entity.SourcePieceId == sourcePieceId && AbilityEntityEffectRules.IsSourceBoundEffect(entity));
  }

  private bool IsLocalDestructibleTerrainAt((int x, int y) position) =>
    _terrain.IsForest(position) || _terrain.IsLake(position);

  private bool TryDestroyLocalTerrainTile((int x, int y) position) =>
    _terrain.DestroyTile(position);

  private bool IsLocalStructureAt((int x, int y) position) =>
    _abilityEntities.Any(entity => entity.X == position.x && entity.Y == position.y) ||
    _barricades.ContainsKey(position) ||
    _roads.ContainsKey(position) ||
    _mines.ContainsKey(position) ||
    _restoredLakeTiles.Contains(position);

  private bool TryDestroyLocalStructure((int x, int y) position)
  {
    int entityIndex = _abilityEntities.FindIndex(entity =>
      entity.X == position.x && entity.Y == position.y);
    if (entityIndex >= 0)
    {
      AbilityEntity entity = _abilityEntities[entityIndex];
      _abilityEntities.RemoveAt(entityIndex);
      if (entity.Kind == AbilityEntityKind.Portal && entity.LinkedEntityId is not null)
      {
        _abilityEntities.RemoveAll(candidate => candidate.Id == entity.LinkedEntityId);
      }
      return true;
    }

    return _barricades.Remove(position) ||
      _roads.Remove(position) ||
      _mines.Remove(position) ||
      _restoredLakeTiles.Remove(position);
  }


  private bool TryInterceptLocalWatchtowerDamage(Piece target, int damage)
  {
    AbilityEntity tower = _abilityEntities.FirstOrDefault(entity =>
      entity.Kind == AbilityEntityKind.Watchtower &&
      entity.Owner == target.Team.ToNetworkTeam() &&
      target.OccupiedSquares().Any(square => entity.X == square.x && entity.Y == square.y));
    if (tower is null) return false;

    int index = _abilityEntities.FindIndex(entity => entity.Id == tower.Id);
    if (index < 0) return false;
    int remaining = tower.Health - Math.Max(0, damage);
    if (remaining <= 0) _abilityEntities.RemoveAt(index);
    else _abilityEntities[index] = tower with { Health = remaining };
    return true;
  }

  private void DetonateLocalTnt(Piece demolitionist, AbilityEntity tnt)
  {
    _abilityEntities.RemoveAll(entity => entity.Id == tnt.Id);

    foreach (Piece victim in pieceSetup.Pieces.ToArray())
    {
      bool inBlast = victim.OccupiedSquares().Any(square =>
        Math.Max(Math.Abs(square.x - tnt.X), Math.Abs(square.y - tnt.Y)) <= 1);
      if (inBlast)
      {
        ApplyLocalAbilityEntityDamage(victim, demolitionist.Team.ToNetworkTeam(), 30);
      }
    }

    for (int y = tnt.Y - 1; y <= tnt.Y + 1; y++)
    for (int x = tnt.X - 1; x <= tnt.X + 1; x++)
    {
      _terrain.DestroyTile((x, y));
    }
  }

  private bool CanPlaceLocalAbilityEntity((int x, int y) position, bool allowLake = false)
  {
    return IsBoardCell(position.x - _board.MinX, position.y - _board.MinY) &&
      (allowLake || !_terrain.IsLake(position)) &&
      !_barricades.ContainsKey(position) &&
      pieceSetup.GetPieceAt(position) is null &&
      !_abilityEntities.Any(entity => entity.X == position.x && entity.Y == position.y);
  }

  private bool HasLocalBridgeAt((int x, int y) position) =>
    _abilityEntities.Any(entity =>
      entity.Kind == AbilityEntityKind.Bridge &&
      entity.X == position.x && entity.Y == position.y);

  private AbilityEntity CreateLocalAbilityEntity(
    AbilityEntityKind kind,
    Piece source,
    (int x, int y) position)
  {
    AbilityEntityDefinition definition = AbilityEntityRules.GetRequired(kind);
    return new AbilityEntity(
      Guid.NewGuid().ToString("N"),
      kind,
      source.Team.ToNetworkTeam(),
      position.x,
      position.y,
      definition.Health,
      SourcePieceId: source.NetworkId);
  }

  private static bool IsCodexBuilder(PieceType type) =>
    type is PieceType.Mason or PieceType.Carpenter or PieceType.Daedalus or
      PieceType.Runesmith or PieceType.Gatekeeper;

  private static string[] GetCodexBuilderAbilities(PieceType type) => type switch
  {
    PieceType.Mason => ["StoneWall", "Gatehouse", "Demolish"],
    PieceType.Carpenter => ["Bridge", "Watchtower", "Demolish"],
    PieceType.Daedalus => ["Gate", "Snare", "Demolish"],
    PieceType.Runesmith => ["RuneAttack", "RuneMovement", "RuneHealth", "RuneRange", "Demolish"],
    PieceType.Gatekeeper => ["Portal", "Seal", "Demolish"],
    _ => Array.Empty<string>()
  };

  private string GetSelectedCodexBuilderAbility(Piece actor)
  {
    string[] abilities = GetCodexBuilderAbilities(actor.Definition.Type);
    if (abilities.Length == 0) return string.Empty;
    int index = ((_selectedCodexBuilderAbilityIndex % abilities.Length) + abilities.Length) % abilities.Length;
    return abilities[index];
  }

  private void CycleCodexBuilderAbility(Piece actor, int direction)
  {
    string[] abilities = GetCodexBuilderAbilities(actor.Definition.Type);
    if (abilities.Length == 0) return;
    _selectedCodexBuilderAbilityIndex =
      ((_selectedCodexBuilderAbilityIndex + direction) % abilities.Length + abilities.Length) % abilities.Length;
  }

  private static string GetCodexBuilderDisplayTitle(string ability) => ability switch
  {
    "StoneWall" => "STONE WALL",
    "RuneAttack" => "ATTACK RUNE",
    "RuneMovement" => "MOVE RUNE",
    "RuneHealth" => "WARD RUNE",
    "RuneRange" => "RANGE RUNE",
    _ => ability.ToUpperInvariant()
  };

  private static string GetCodexBuilderDetail(PieceType type, string ability) => (type, ability) switch
  {
    (PieceType.Mason, "StoneWall") => "Place 2 walls; costs 10 gold total.",
    (PieceType.Mason, "Gatehouse") => "Place 2 friendly-pass gatehouses.",
    (PieceType.Carpenter, "Bridge") => "Place 2 bridges over water/river crossings.",
    (PieceType.Carpenter, "Watchtower") => "15 HP tower; costs 15 gold and protects its occupant.",
    (PieceType.Daedalus, "Gate") => "Place 3 friendly-pass gates.",
    (PieceType.Daedalus, "Snare") => "Place 1 movement-locking snare.",
    (PieceType.Runesmith, "RuneAttack") => "5 HP rune; nearby allies gain +10 Attack.",
    (PieceType.Runesmith, "RuneMovement") => "5 HP rune; nearby allies gain +1 Move.",
    (PieceType.Runesmith, "RuneHealth") => "5 HP rune; nearby allies take 5 less damage.",
    (PieceType.Runesmith, "RuneRange") => "5 HP rune; nearby allies gain +2 Attack Range.",
    (PieceType.Gatekeeper, "Portal") => "Place 2 linked portals for friendly teleportation.",
    (PieceType.Gatekeeper, "Seal") => "Place 2 temporary impassable seals.",
    (_, "Demolish") => "Destroy an in-range structure for free.",
    _ => "RIGHT-CLICK a legal square to use."
  };

  private string GetCodexBuilderStatusLabel(Piece actor)
  {
    string ability = GetSelectedCodexBuilderAbility(actor);
    if (!TryGetCodexBuilderOption(actor.Definition.Type, ability, out _, out int requiredCount, out _))
    {
      return GetCodexBuilderDisplayTitle(ability);
    }

    int selected = string.Equals(actor.AbilityState.PendingAbility, ability, StringComparison.Ordinal)
      ? actor.AbilityState.PendingSelections.Count
      : 0;
    return requiredCount > 1
      ? $"{GetCodexBuilderDisplayTitle(ability)} ({Math.Max(0, requiredCount - selected)})"
      : GetCodexBuilderDisplayTitle(ability);
  }

  private bool CodexBuilderSelectionCompletesAction(Piece actor, string ability)
  {
    if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }
    if (!TryGetCodexBuilderOption(actor.Definition.Type, ability, out _, out int requiredCount, out _))
    {
      return true;
    }

    int selected = string.Equals(actor.AbilityState.PendingAbility, ability, StringComparison.Ordinal)
      ? actor.AbilityState.PendingSelections.Count
      : 0;
    return selected + 1 >= requiredCount;
  }

  private static bool TryGetCodexBuilderOption(
    PieceType type,
    string ability,
    out AbilityEntityKind kind,
    out int requiredCount,
    out int totalCost)
  {
    kind = default;
    requiredCount = 0;
    totalCost = 0;

    (AbilityEntityKind kind, int count, int cost)? option = (type, ability) switch
    {
      (PieceType.Mason, "StoneWall") => (AbilityEntityKind.StoneWall, 2, 10),
      (PieceType.Mason, "Gatehouse") => (AbilityEntityKind.Gatehouse, 2, 0),
      (PieceType.Carpenter, "Bridge") => (AbilityEntityKind.Bridge, 2, 0),
      (PieceType.Carpenter, "Watchtower") => (AbilityEntityKind.Watchtower, 1, 15),
      (PieceType.Daedalus, "Gate") => (AbilityEntityKind.Gate, 3, 0),
      (PieceType.Daedalus, "Snare") => (AbilityEntityKind.Snare, 1, 0),
      (PieceType.Runesmith, "RuneAttack") => (AbilityEntityKind.RuneAttack, 1, 0),
      (PieceType.Runesmith, "RuneMovement") => (AbilityEntityKind.RuneMovement, 1, 0),
      (PieceType.Runesmith, "RuneHealth") => (AbilityEntityKind.RuneHealth, 1, 0),
      (PieceType.Runesmith, "RuneRange") => (AbilityEntityKind.RuneRange, 1, 0),
      (PieceType.Gatekeeper, "Portal") => (AbilityEntityKind.Portal, 2, 0),
      (PieceType.Gatekeeper, "Seal") => (AbilityEntityKind.Seal, 2, 0),
      _ => null
    };
    if (option is null) return false;
    (kind, requiredCount, totalCost) = option.Value;
    return true;
  }

  private bool CanUseCodexBuilderAbilityAt(
    Piece actor,
    (int x, int y) position,
    Piece targetPiece)
  {
    if (!IsCodexBuilder(actor.Definition.Type) || actor.HasAttackedThisTurn ||
        targetPiece is not null || !CanAttackSquareWithAttachments(actor, position))
    {
      return false;
    }

    string ability = GetSelectedCodexBuilderAbility(actor);
    if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
    {
      return IsLocalStructureAt(position);
    }

    return TryGetCodexBuilderOption(actor.Definition.Type, ability, out AbilityEntityKind kind, out _, out _) &&
      CanPlaceLocalAbilityEntity(position, allowLake: kind == AbilityEntityKind.Bridge);
  }

  private bool TryUseCodexBuilderAbility(
    Piece actor,
    (int x, int y) position,
    Piece targetPiece)
  {
    if (!CanUseCodexBuilderAbilityAt(actor, position, targetPiece))
    {
      return false;
    }

    string ability = GetSelectedCodexBuilderAbility(actor);
    if (string.Equals(ability, "Demolish", StringComparison.OrdinalIgnoreCase))
    {
      if (!TryDestroyLocalStructure(position)) return false;
      actor.AbilityState = AdvancedAbilityRules.ClearPendingSelections(actor.AbilityState);
      actor.HasAttackedThisTurn = true;
      CompleteAction();
      return true;
    }

    if (!TryGetCodexBuilderOption(
          actor.Definition.Type, ability, out AbilityEntityKind kind,
          out int requiredCount, out int totalCost))
    {
      return false;
    }

    UnitAbilityState pending = AdvancedAbilityRules.AddPendingSelection(
      actor.AbilityState, ability, new AbilitySelection(null, position.x, position.y));
    if (pending.PendingSelections.Select(selection => (selection.X, selection.Y)).Distinct().Count() !=
        pending.PendingSelections.Count)
    {
      return false;
    }

    if (pending.PendingSelections.Count < requiredCount)
    {
      actor.AbilityState = pending;
      return false;
    }
    if (pending.PendingSelections.Count != requiredCount)
    {
      return false;
    }

    Team team = _teams.Find(candidate => candidate.TeamName == actor.Team);
    if (team is null || team.Money < totalCost ||
        pending.PendingSelections.Any(selection =>
          !CanPlaceLocalAbilityEntity(
            (selection.X, selection.Y), allowLake: kind == AbilityEntityKind.Bridge)))
    {
      return false;
    }

    team.Money = ClampCurrency((long)team.Money - totalCost);
    if (kind == AbilityEntityKind.Portal)
    {
      AbilitySelection first = pending.PendingSelections[0];
      AbilitySelection second = pending.PendingSelections[1];
      string firstId = Guid.NewGuid().ToString("N");
      string secondId = Guid.NewGuid().ToString("N");
      _abilityEntities.Add(new AbilityEntity(
        firstId, kind, actor.Team.ToNetworkTeam(), first.X, first.Y, 0,
        secondId, SourcePieceId: actor.NetworkId));
      _abilityEntities.Add(new AbilityEntity(
        secondId, kind, actor.Team.ToNetworkTeam(), second.X, second.Y, 0,
        firstId, SourcePieceId: actor.NetworkId));
    }
    else
    {
      foreach (AbilitySelection selection in pending.PendingSelections)
      {
        _abilityEntities.Add(CreateLocalAbilityEntity(
          kind, actor, (selection.X, selection.Y)));
      }
    }

    actor.AbilityState = AdvancedAbilityRules.ClearPendingSelections(pending);
    actor.HasAttackedThisTurn = true;
    CompleteAction();
    return true;
  }
}
