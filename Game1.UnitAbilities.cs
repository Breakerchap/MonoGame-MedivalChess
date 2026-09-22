using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess;

/// <summary>
/// Local-game adapter for shared unit ability rules. Target selection, state transitions and
/// ability values live in MedivalChess.Shared; this partial only maps them onto local Piece state.
/// </summary>
internal sealed partial class Game1
{
  private static NetworkPiece SnapshotRuntimePiece(Piece piece) => new(
    piece.NetworkId,
    piece.Definition.Type.ToString(),
    piece.Team.ToNetworkTeam(),
    piece.Position.x,
    piece.Position.y,
    piece.CurrentHealth,
    AbilityState: piece.AbilityState
  );

  private UnitRule ApplyLocalAttachmentBonuses(Piece host, UnitRule rule)
  {
    bool hasImp = pieceSetup.Pieces.Any(piece =>
      piece.AttachedTo == host && piece.AttachmentKind == AttachmentKind.Imp);
    int museCount = pieceSetup.Pieces.Count(piece =>
      piece.AttachedTo == host && piece.AttachmentKind == AttachmentKind.Muse);
    rule = AdvancedAbilityRules.ApplyAttachmentBonuses(rule, hasImp, museCount);
    rule = AdvancedAbilityRules.ApplyPersistentBonuses(rule, host.AbilityState);
    return AbilityEntityRules.ApplyAuraBonuses(rule, _abilityEntities, SnapshotRuntimePiece(host));
  }

  private AbilityUnitSnapshot SnapshotAbilityUnit(Piece piece) => new(
    piece.NetworkId,
    piece.Definition.Type.ToString(),
    piece.Team.ToNetworkTeam(),
    piece.Position.x,
    piece.Position.y,
    piece.Definition.Size.x,
    piece.Definition.Size.y
  );

  private void PerformSharedUnitAttack(Piece attacker, Piece selectedTarget)
  {
    if (attacker.Definition.Type == PieceType.Tank)
    {
      TankAttackDecision tank = AbilityStateRules.ResolveTankAttackAttempt(
        attacker.Team.ToNetworkTeam(),
        attacker.Facing.x,
        attacker.Facing.y,
        attacker.Position,
        selectedTarget.Position
      );
      attacker.Facing = (tank.FacingX, tank.FacingY);
      if (!tank.MayFire)
      {
        return;
      }
    }

    AbilityUnitSnapshot[] snapshots = pieceSetup.Pieces
      .Where(piece => piece.AttachedTo is null)
      .Select(SnapshotAbilityUnit)
      .ToArray();
    AbilityAttackPlan plan = AbilityAttackRules.BuildAttackPlan(
      SnapshotAbilityUnit(attacker),
      SnapshotAbilityUnit(selectedTarget),
      snapshots
    );

    foreach (AbilityDamageInstruction instruction in plan.Damage)
    {
      Piece target = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == instruction.TargetId);
      if (target is null) continue;
      int? fixedDamage = instruction.Mode == AbilityDamageMode.Fixed
        ? instruction.FixedDamage
        : null;
      ResolveDamage(attacker, target, fixedDamage);
    }

    ApplySharedLocalDisplacements(plan);

    if (plan.HealAttacker > 0 && pieceSetup.Pieces.Contains(attacker) && attacker.CurrentHealth > 0)
    {
      attacker.CurrentHealth = Math.Min(
        AdvancedAbilityRules.GetEffectiveMaximumHealth(
          UnitRules.FromPieceDefinition(attacker.Definition),
          attacker.AbilityState),
        attacker.CurrentHealth + plan.HealAttacker
      );
    }

    if (plan.SelfDestructAfterAttack && pieceSetup.Pieces.Contains(attacker))
    {
      attacker.CurrentHealth = 0;
      HandlePieceDestroyed(attacker, null);
    }
  }

  private void ApplySharedLocalDisplacements(AbilityAttackPlan plan)
  {
    foreach (AbilityDisplacementInstruction instruction in plan.Displacements ?? Array.Empty<AbilityDisplacementInstruction>())
    {
      Piece moving = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == instruction.UnitId);
      if (moving is null || moving.AttachedTo is not null ||
          !DisplacementRules.CanBePushed(moving.Definition.Type.ToString()))
      {
        continue;
      }

      (int x, int y) start = moving.Position;
      (int x, int y) destination = DisplacementRules.GetFurthestLegalPosition(
        start,
        instruction.DirectionX,
        instruction.DirectionY,
        instruction.MaximumDistance,
        candidate => CanDisplaceLocalPieceTo(moving, candidate),
        instruction.RequireFullDistance);
      if (destination == start) continue;

      moving.Position = destination;
      foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == moving))
      {
        attachment.Position = destination;
      }
      pieceSetup.RefreshOccupancy();
    }
  }

  private bool CanDisplaceLocalPieceTo(Piece moving, (int x, int y) destination)
  {
    if (!IsFootprintOnBoard(moving.Definition, destination))
    {
      return false;
    }

    foreach ((int x, int y) square in OccupiedSquares(moving.Definition, destination))
    {
      if (_terrain.IsLake(square) || _barricades.ContainsKey(square) ||
          _abilityEntities.Any(entity =>
            entity.X == square.x && entity.Y == square.y &&
            AbilityEntityRules.BlocksLandingFor(entity, moving.Team.ToNetworkTeam())))
      {
        return false;
      }
    }

    HashSet<Piece> ignored = pieceSetup.Pieces
      .Where(piece => piece == moving || piece.AttachedTo == moving)
      .ToHashSet();
    return !pieceSetup.Pieces.Any(piece =>
      !ignored.Contains(piece) && piece.AttachedTo is null && piece.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
        destination.x, destination.y, moving.Definition.Size.x, moving.Definition.Size.y));
  }

  private int GetSharedLocalAttackDamage(Piece attacker, Piece target)
  {
    UnitRule attackerRule = ApplyLocalAttachmentBonuses(
      attacker, UnitRules.FromPieceDefinition(attacker.Definition));
    UnitRule targetRule = UnitRules.FromPieceDefinition(target.Definition);
    int baseDamage = AbilityRules.GetBaseAttack(attackerRule, attacker.CurrentHealth);
    baseDamage += AbilityRules.GetAttackAbilityBonus(
      attackerRule,
      targetRule,
      IsPieceInForest(target),
      target.Facing,
      attacker.Position,
      target.Position
    );

    bool hasBaronBonus = AdvancedAbilityRules.IsBaronSelectedTarget(
      pieceSetup.Pieces.Select(piece => (piece.Definition.Type.ToString(), piece.Team.ToNetworkTeam(), piece.AbilityState.SelectedTargetId)),
      attacker.NetworkId,
      attacker.Team.ToNetworkTeam());
    bool isSpyMarked = pieceSetup.Pieces.Any(spy =>
      spy.Definition.Type == PieceType.Spy && spy.MarkedTarget == target);
    baseDamage = AdvancedAbilityRules.ApplyBaronOutgoingBonus(baseDamage, hasBaronBonus);
    return CombatRules.CalculateDamage(
      baseDamage,
      false,
      isSpyMarked,
      false,
      false,
      0
    );
  }

  private bool CanSharedAttackDamage(Piece attacker, Piece target) =>
    AdvancedAbilityRules.CanTakeDirectDamage(target.Definition.Type.ToString(), target.AbilityState) &&
    AbilityRules.CanDamageTarget(
      ApplyLocalAttachmentBonuses(attacker, UnitRules.FromPieceDefinition(attacker.Definition)),
      UnitRules.FromPieceDefinition(target.Definition)
    );

  private void ApplySharedDeathExplosion(Piece destroyedPiece)
  {
    AbilityUnitSnapshot[] snapshots = pieceSetup.Pieces
      .Where(piece => piece.AttachedTo is null)
      .Select(SnapshotAbilityUnit)
      .ToArray();
    IReadOnlyList<AbilityDamageInstruction> explosion = AbilityAttackRules.BuildDeathExplosion(
      SnapshotAbilityUnit(destroyedPiece),
      snapshots
    );

    foreach (AbilityDamageInstruction instruction in explosion)
    {
      Piece target = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == instruction.TargetId);
      if (target is null) continue;
      ResolveDamage(destroyedPiece, target, instruction.FixedDamage);
    }
  }

  private void ApplySharedStartOfTurnEffects(TeamName activeTeam)
  {
    NetworkTeam networkTeam = activeTeam.ToNetworkTeam();
    foreach (Piece target in pieceSetup.Pieces.ToArray())
    {
      var pending = AbilityStateRules.SplitPendingDamageForTurn(target.PendingDamage, networkTeam);
      target.PendingDamage = pending.Remaining;
      foreach (NetworkPendingDamage effect in pending.Triggered)
      {
        if (!pieceSetup.Pieces.Contains(target)) break;
        int damage = CombatRules.CalculateDamage(
          effect.Damage,
          false,
          false,
          false,
          IsPieceInForest(target),
          _terrain.ForestDamageReduction
        );
        bool protectedByBaron = AdvancedAbilityRules.IsBaronSelectedTarget(
          pieceSetup.Pieces.Select(piece => (
            piece.Definition.Type.ToString(),
            piece.Team.ToNetworkTeam(),
            piece.AbilityState.SelectedTargetId)),
          target.NetworkId,
          target.Team.ToNetworkTeam());
        damage = AdvancedAbilityRules.ApplyBaronIncomingReduction(damage, protectedByBaron);
        target.CurrentHealth -= damage;
        HandlePieceDestroyed(target, effect.SourceTeam.ToTeamName());
      }
    }

    foreach (Piece imp in pieceSetup.Pieces
      .Where(piece => piece.Team == activeTeam && piece.AttachedTo is not null &&
        piece.AttachmentKind == AttachmentKind.Imp)
      .ToArray())
    {
      Piece host = imp.AttachedTo;
      if (host is null || !pieceSetup.Pieces.Contains(host)) continue;
      host.CurrentHealth -= AdvancedAbilityRules.ImpHealthDrain;
      HandlePieceDestroyed(host, null);
    }
  }

  private void ApplySharedAbilityUpkeep(TeamName teamName, Team team)
  {
    UnitUpkeepSequenceResult result = EconomyRules.ResolveAbilityUpkeepSequence(
      team.Money,
      pieceSetup.Pieces
        .Where(piece => piece.Team == teamName && piece.AttachedTo is null)
        .Select(piece => new UnitUpkeepRequest(piece.NetworkId, piece.Definition.Type.ToString()))
    );
    team.Money = result.RemainingMoney;

    foreach (UnitUpkeepDecision decision in result.Decisions)
    {
      if (decision.Paid) continue;
      Piece piece = pieceSetup.Pieces.FirstOrDefault(candidate => candidate.NetworkId == decision.UnitId);
      if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.FireUnit && piece is not null)
      {
        piece.Team = TeamName.Neutral;
        piece.HasMovedThisTurn = true;
        piece.HasAttackedThisTurn = true;
        piece.AbilityState = piece.AbilityState with { CannotActThisTurn = true, CannotMoveThisTurn = true };
      }
      else if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.LoseMatch)
      {
        _winningTeam = Team.ActiveTeams.First(candidate => candidate != teamName);
        _screen = Screen.GameOver;
        selectedPiece = null;
        return;
      }
    }
  }

  private bool TryUseSharedRoyalAbility(Piece actor, Piece target)
  {
    if (actor.Definition.Type != PieceType.Phantom)
    {
      return false;
    }

    if (!string.IsNullOrEmpty(actor.PossessedUnitId))
    {
      Piece possessed = pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == actor.PossessedUnitId);
      if (target != actor && target != possessed)
      {
        return false;
      }

      if (possessed is not null)
      {
        possessed.IsRoyalProxy = false;
      }
      PhantomPossessionState state = RoyalAbilityRules.Unpossess();
      actor.PossessedUnitId = state.PhantomPossessedUnitId;
      actor.AbilityState = actor.AbilityState with
      {
        CannotMoveThisTurn = true,
        CannotActThisTurn = true
      };
      CompleteAction();
      return true;
    }

    if (target is null || !RoyalAbilityRules.CanPhantomPossess(
      actor.Definition.Type.ToString(),
      actor.Team.ToNetworkTeam(),
      actor.PossessedUnitId,
      target.NetworkId,
      target.Definition.Type.ToString(),
      target.Team.ToNetworkTeam(),
      target.IsRoyalProxy))
    {
      return false;
    }

    PhantomPossessionState possession = RoyalAbilityRules.Possess(target.NetworkId);
    actor.PossessedUnitId = possession.PhantomPossessedUnitId;
    target.IsRoyalProxy = possession.TargetIsRoyalProxy;
    CompleteAction();
    return true;
  }

  private bool CanPlaceSharedRoyalGroup(PieceDefinition royal, (int x, int y) anchor, TeamName team) =>
    RoyalAbilityRules.GetRoyalSpawnOffsets(royal.Type.ToString()).All(offset =>
      CanPlacePiece(royal, (anchor.x + offset.x, anchor.y + offset.y), team));

  private void AddSharedRoyalGroup(PieceDefinition royal, (int x, int y) anchor, TeamName team)
  {
    foreach ((int x, int y) offset in RoyalAbilityRules.GetRoyalSpawnOffsets(royal.Type.ToString()))
    {
      pieceSetup.AddPiece(new Piece(royal, (anchor.x + offset.x, anchor.y + offset.y), team)
      {
        CurrentHealth = GetRoyalStartingHealth(royal)
      });
    }
  }

  private bool IsSharedRoyalDeath(Piece defeatedPiece)
  {
    bool sameTeamGoblinRemains = pieceSetup.Pieces.Any(piece =>
      piece != defeatedPiece &&
      piece.Team == defeatedPiece.Team &&
      piece.Definition.Type == PieceType.GoblinRoyalty);
    return RoyalAbilityRules.IsRoyalDeath(
      defeatedPiece.Definition.Type.ToString(),
      defeatedPiece.IsRoyal,
      sameTeamGoblinRemains
    );
  }

  private bool IsLocalDuelistAttackLegal(Piece attacker, Piece? intendedTarget)
  {
    Piece[] forcingDuelists = pieceSetup.Pieces
      .Where(piece =>
        piece.Definition.Type == PieceType.Duelist &&
        piece.Team != attacker.Team &&
        string.Equals(piece.AbilityState.SelectedTargetId, attacker.NetworkId, StringComparison.Ordinal) &&
        CanAttackSquareWithAttachments(piece, attacker.Position))
      .ToArray();
    return forcingDuelists.Length == 0 ||
      (intendedTarget is not null && forcingDuelists.Contains(intendedTarget));
  }

  private void ClearLocalPetrificationBy(string medusaId)
  {
    foreach (Piece piece in pieceSetup.Pieces.Where(piece =>
      string.Equals(piece.AbilityState.PetrifiedById, medusaId, StringComparison.Ordinal)))
    {
      piece.AbilityState = AdvancedAbilityRules.SetPetrified(piece.AbilityState, null);
    }
  }

  private void ReleaseLocalPetrificationIfBroken(Piece medusa)
  {
    if (medusa.Definition.Type != PieceType.Medusa) return;
    UnitRule medusaRule = UnitRules.FromPieceDefinition(medusa.Definition);
    foreach (Piece target in pieceSetup.Pieces.Where(piece =>
      string.Equals(piece.AbilityState.PetrifiedById, medusa.NetworkId, StringComparison.Ordinal)).ToArray())
    {
      UnitRule targetRule = UnitRules.FromPieceDefinition(target.Definition);
      if (!AdvancedAbilityRules.IsPetrificationMaintained(
            medusaRule, medusa.Position, targetRule, target.Position))
      {
        target.AbilityState = AdvancedAbilityRules.SetPetrified(target.AbilityState, null);
      }
    }
  }

  private bool TryPetrifyLocalTarget(Piece medusa, Piece? target)
  {
    if (target is null || target == medusa || target.IsRoyal || medusa.HasAttackedThisTurn ||
        !AdvancedAbilityRules.CanMedusaPetrify(target.Definition.Type.ToString()) ||
        !CanAttackSquareWithAttachments(medusa, target.Position))
    {
      return false;
    }

    ClearLocalPetrificationBy(medusa.NetworkId);
    target.AbilityState = AdvancedAbilityRules.SetPetrified(target.AbilityState, medusa.NetworkId);
    medusa.HasAttackedThisTurn = true;
    CompleteAction();
    return true;
  }

  private void PerformLocalMissileSiloAttack(Piece attacker, (int x, int y) centre)
  {
    int damage = UnitRules.FromPieceDefinition(attacker.Definition).Attack;
    foreach (Piece victim in pieceSetup.Pieces.ToArray())
    {
      if (!victim.OccupiedSquares().Any(square =>
            Math.Max(Math.Abs(square.x - centre.x), Math.Abs(square.y - centre.y)) <= 2))
      {
        continue;
      }
      if (!CanSharedAttackDamage(attacker, victim)) continue;
      ApplyDamageToPiece(attacker, victim, damage);
    }

    HashSet<(int x, int y)> structurePositions = new();
    foreach (AbilityEntity entity in _abilityEntities)
    {
      if (Math.Max(Math.Abs(entity.X - centre.x), Math.Abs(entity.Y - centre.y)) <= 2)
      {
        structurePositions.Add((entity.X, entity.Y));
      }
    }
    foreach ((int x, int y) position in _barricades.Keys.Concat(_roads.Keys).Concat(_mines.Keys).Concat(_restoredLakeTiles))
    {
      if (Math.Max(Math.Abs(position.x - centre.x), Math.Abs(position.y - centre.y)) <= 2)
      {
        structurePositions.Add(position);
      }
    }
    foreach ((int x, int y) position in structurePositions)
    {
      TryDestroyLocalStructure(position);
    }
  }


  private bool TryTransformLocalFafnir(Piece fafnir)
  {
    PieceDefinition dragon = PieceDefinitions.All.First(definition =>
      definition.Type == PieceType.FafnirDragon);
    Team team = _teams.Find(candidate => candidate.TeamName == fafnir.Team);
    if (team is null ||
        team.Money < AdvancedAbilityRules.FafnirTransformCost ||
        !AdvancedAbilityRules.CanUseOncePerOwnerTurn(fafnir.AbilityState) ||
        !IsFootprintOnBoard(dragon, fafnir.Position))
    {
      return false;
    }

    team.Money = ClampCurrency((long)team.Money - AdvancedAbilityRules.FafnirTransformCost);
    fafnir.TransformTo(dragon);
    fafnir.AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(fafnir.AbilityState);

    foreach ((int x, int y) square in fafnir.OccupiedSquares().ToArray())
    {
      _terrain.DestroyTile(square);
      TryDestroyLocalStructure(square);
    }

    Piece[] overlappingFarms = pieceSetup.Pieces
      .Where(piece => piece != fafnir && piece.AttachedTo is null &&
        piece.Definition.Type == PieceType.Farm &&
        UnitRules.FootprintsOverlap(
          piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
          fafnir.Position.x, fafnir.Position.y, fafnir.Definition.Size.x, fafnir.Definition.Size.y))
      .ToArray();
    foreach (Piece farm in overlappingFarms)
    {
      farm.CurrentHealth = 0;
      HandlePieceDestroyed(farm, fafnir.Team);
    }

    Piece[] overlapping = pieceSetup.Pieces
      .Where(piece => piece != fafnir && piece.AttachedTo is null &&
        piece.Definition.Type != PieceType.Farm &&
        UnitRules.FootprintsOverlap(
          piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
          fafnir.Position.x, fafnir.Position.y, fafnir.Definition.Size.x, fafnir.Definition.Size.y))
      .ToArray();

    foreach (Piece moving in overlapping)
    {
      (int x, int y)? destination = FindNearestLocalLegalDisplacement(moving);
      if (destination is null) continue;
      moving.Position = destination.Value;
      foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == moving))
      {
        attachment.Position = destination.Value;
      }
      pieceSetup.RefreshOccupancy();
    }

    CompleteAction();
    return true;
  }

  private (int x, int y)? FindNearestLocalLegalDisplacement(Piece moving)
  {
    List<(int x, int y)> cells = [];
    for (int arrayY = 0; arrayY < _board.BoardArray.GetLength(0); arrayY++)
    for (int arrayX = 0; arrayX < _board.BoardArray.GetLength(1); arrayX++)
    {
      if (_board.BoardArray[arrayY, arrayX] != 1) continue;
      cells.Add((_board.MinX + arrayX, _board.MinY + arrayY));
    }

    foreach ((int x, int y) candidate in cells
      .OrderBy(position => Math.Max(
        Math.Abs(position.x - moving.Position.x),
        Math.Abs(position.y - moving.Position.y)))
      .ThenBy(position => position.y)
      .ThenBy(position => position.x))
    {
      if (CanDisplaceLocalPieceTo(moving, candidate))
      {
        return candidate;
      }
    }
    return null;
  }

  private string? GetSelectedLocalThorStormId(Piece thor)
  {
    AbilityEntity[] storms = _abilityEntities
      .Where(entity => entity.Kind == AbilityEntityKind.Thunderstorm &&
        entity.SourcePieceId == thor.NetworkId)
      .OrderBy(entity => entity.Id, StringComparer.Ordinal)
      .ToArray();
    int optionCount = storms.Length < 3 ? storms.Length + 1 : storms.Length;
    if (optionCount == 0) return null;
    int index = ((_selectedThorStormIndex % optionCount) + optionCount) % optionCount;
    if (storms.Length < 3)
    {
      if (index == 0) return null;
      return storms[index - 1].Id;
    }
    return storms[index].Id;
  }

  private string GetSelectedThorStormLabel(Piece thor)
  {
    AbilityEntity[] storms = _abilityEntities
      .Where(entity => entity.Kind == AbilityEntityKind.Thunderstorm &&
        entity.SourcePieceId == thor.NetworkId)
      .OrderBy(entity => entity.Id, StringComparer.Ordinal)
      .ToArray();
    int optionCount = storms.Length < 3 ? storms.Length + 1 : storms.Length;
    if (optionCount == 0) return "NEW";
    int index = ((_selectedThorStormIndex % optionCount) + optionCount) % optionCount;
    if (storms.Length < 3 && index == 0) return "NEW";
    int stormIndex = storms.Length < 3 ? index - 1 : index;
    return $"STORM {stormIndex + 1}";
  }

  private void CycleThorStorm(Piece thor, int direction)
  {
    int stormCount = _abilityEntities.Count(entity =>
      entity.Kind == AbilityEntityKind.Thunderstorm &&
      entity.SourcePieceId == thor.NetworkId);
    int optionCount = stormCount < 3 ? stormCount + 1 : stormCount;
    if (optionCount <= 0) return;
    _selectedThorStormIndex =
      ((_selectedThorStormIndex + direction) % optionCount + optionCount) % optionCount;
  }

  private bool TryUseLocalThorAbility(Piece thor, (int x, int y) targetPosition)
  {
    if (!AdvancedAbilityRules.CanUseOncePerOwnerTurn(thor.AbilityState) ||
        !CanAttackSquareWithAttachments(thor, targetPosition))
    {
      return false;
    }

    string? selectedStormId = GetSelectedLocalThorStormId(thor);
    AbilityEntity selectedStorm = selectedStormId is null
      ? null
      : _abilityEntities.FirstOrDefault(entity => entity.Id == selectedStormId);
    if (selectedStorm is null)
    {
      int count = _abilityEntities.Count(entity =>
        entity.Kind == AbilityEntityKind.Thunderstorm &&
        entity.SourcePieceId == thor.NetworkId);
      if (count >= 3) return false;
      _abilityEntities.Add(CreateLocalAbilityEntity(
        AbilityEntityKind.Thunderstorm, thor, targetPosition));
    }
    else
    {
      int index = _abilityEntities.FindIndex(entity => entity.Id == selectedStorm.Id);
      if (index < 0) return false;
      if (_abilityEntities.Any(entity =>
        entity.Id != selectedStorm.Id &&
        entity.X == targetPosition.x && entity.Y == targetPosition.y))
      {
        return false;
      }
      _abilityEntities[index] = selectedStorm with
      {
        X = targetPosition.x,
        Y = targetPosition.y
      };
    }

    thor.AbilityState = AdvancedAbilityRules.RecordOncePerOwnerTurnUse(thor.AbilityState);
    CompleteAction();
    return true;
  }



  private Piece GetLocalLandingAttackTarget(Piece mover, (int x, int y) destination)
  {
    if (!AdvancedAbilityRules.IsLandingAttackUnit(mover.Definition.Type.ToString()))
    {
      return null;
    }

    Piece[] targets = pieceSetup.Pieces
      .Where(piece =>
        piece != mover &&
        piece.AttachedTo is null &&
        piece.Definition.Type != PieceType.Farm &&
        piece.Team != mover.Team &&
        AdvancedAbilityRules.CanTakeDirectDamage(
          piece.Definition.Type.ToString(), piece.AbilityState) &&
        UnitRules.FootprintsOverlap(
          destination.x, destination.y, mover.Definition.Size.x, mover.Definition.Size.y,
          piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y))
      .ToArray();
    return targets.Length == 1 ? targets[0] : null;
  }

  private bool CanLocalLandingAttackLand(Piece mover, (int x, int y) destination) =>
    GetLocalLandingAttackTarget(mover, destination) is not null;

  private bool CanContinueLocalSpecialLandingPath(Piece mover, (int x, int y) position) =>
    GetLocalLandingAttackTarget(mover, position) is null;

  private (int x, int y) ResolveLocalLandingAttack(
    Piece mover,
    IReadOnlyList<(int x, int y)> path,
    (int x, int y) requestedDestination)
  {
    Piece target = GetLocalLandingAttackTarget(mover, requestedDestination);
    if (target is null)
    {
      return requestedDestination;
    }

    (int x, int y) origin = mover.Position;
    ResolveDamage(mover, target);
    bool targetSurvived = pieceSetup.Pieces.Contains(target);

    if (targetSurvived)
    {
      int sourceCentreX2 = origin.x * 2 + mover.Definition.Size.x - 1;
      int sourceCentreY2 = origin.y * 2 + mover.Definition.Size.y - 1;
      int targetCentreX2 = target.Position.x * 2 + target.Definition.Size.x - 1;
      int targetCentreY2 = target.Position.y * 2 + target.Definition.Size.y - 1;
      int directionX = Math.Sign(targetCentreX2 - sourceCentreX2);
      int directionY = Math.Sign(targetCentreY2 - sourceCentreY2);
      int pushDistance = AdvancedAbilityRules.GetLandingAttackPushDistance(
        mover.Definition.Type.ToString());

      if ((directionX != 0 || directionY != 0) && pushDistance > 0)
      {
        (int x, int y) pushed = DisplacementRules.GetFurthestLegalPosition(
          target.Position,
          directionX,
          directionY,
          pushDistance,
          candidate => CanDisplaceLocalPieceTo(target, candidate));
        if (pushed != target.Position)
        {
          target.Position = pushed;
          foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == target))
          {
            attachment.Position = pushed;
          }
          pieceSetup.RefreshOccupancy();
        }
      }
    }

    if (AdvancedAbilityRules.LandingAttackConsumesNormalAttack(mover.Definition.Type.ToString()))
    {
      AttackTurnState attackState = AbilityStateRules.RecordAttack(
        mover.Definition.Type.ToString(), mover.AttacksThisTurn);
      mover.AttacksThisTurn = attackState.AttacksThisTurn;
      mover.HasAttackedThisTurn = attackState.HasAttackedThisTurn;
      mover.AbilityState = AdvancedAbilityRules.RecordAttack(
        mover.Definition.Type.ToString(), mover.AbilityState, target.NetworkId);
    }

    return targetSurvived
      ? ChessAbilityRules.GetFailedCaptureFallback(origin, path)
      : requestedDestination;
  }

  private bool CanUseLocalMimicSwap(Piece mimic, Piece target)
  {
    if (mimic.Definition.Type != PieceType.Mimic ||
        target is null || target == mimic || target.AttachedTo is not null ||
        mimic.Definition.Size != (1, 1) || target.Definition.Size != (1, 1) ||
        !AdvancedAbilityRules.CanUseMovementAbility(mimic.AbilityState, mimic.HasMovedThisTurn))
    {
      return false;
    }

    UnitRule mimicRule = GetEffectiveMovementRule(mimic);
    return UnitRules.CanMove(
        mimicRule,
        mimic.Position.x,
        mimic.Position.y,
        target.Position.x,
        target.Position.y) &&
      CanSwapLocalPieceTo(mimic, target, target.Position) &&
      CanSwapLocalPieceTo(target, mimic, mimic.Position);
  }

  private bool CanSwapLocalPieceTo(Piece moving, Piece ignoredOther, (int x, int y) destination)
  {
    UnitRule rule = GetEffectiveMovementRule(moving);
    if (!IsFootprintOnBoard(moving.Definition, destination))
    {
      return false;
    }

    foreach ((int x, int y) square in OccupiedSquares(moving.Definition, destination))
    {
      if ((!AbilityRules.IgnoresImpassableTerrain(rule) &&
           _terrain.IsLake(square) && !HasLocalBridgeAt(square)) ||
          (!AbilityRules.IgnoresStructures(rule) && _barricades.ContainsKey(square)) ||
          _abilityEntities.Any(entity =>
            entity.X == square.x && entity.Y == square.y &&
            AbilityEntityRules.BlocksLandingFor(entity, moving.Team.ToNetworkTeam()) &&
            (entity.Kind == AbilityEntityKind.Bramble || !AbilityRules.IgnoresStructures(rule))))
      {
        return false;
      }
    }

    return !pieceSetup.Pieces.Any(piece =>
      piece != moving && piece != ignoredOther &&
      piece.AttachedTo is null && piece.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
        destination.x, destination.y, moving.Definition.Size.x, moving.Definition.Size.y));
  }

  private bool TryUseLocalMimicSwap(Piece mimic, Piece target)
  {
    if (!CanUseLocalMimicSwap(mimic, target))
    {
      return false;
    }

    (int x, int y) mimicOrigin = mimic.Position;
    (int x, int y) targetOrigin = target.Position;
    mimic.Position = targetOrigin;
    target.Position = mimicOrigin;

    foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == mimic))
    {
      attachment.Position = mimic.Position;
    }
    foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == target))
    {
      attachment.Position = target.Position;
    }

    mimic.HasMovedThisTurn = true;
    mimic.AbilityState = AdvancedAbilityRules.RecordMove(mimic.AbilityState);
    pieceSetup.RefreshOccupancy();
    ReleaseLocalPetrificationIfBroken(mimic);
    CompleteAction();
    return true;
  }



  private bool TryGetLocalShadowHost((int x, int y) position, out Piece host)
  {
    host = GetUnattachedPieceAt(position, Team.CurrentTurn);
    return host is not null &&
      host.Definition.Type != PieceType.Farm &&
      !host.IsRoyal;
  }

  private bool TryGetLocalArchdemonSacrifice(
    PieceDefinition archdemon,
    (int x, int y) clickedPosition,
    out Piece sacrifice,
    out (int x, int y) placement)
  {
    sacrifice = GetUnattachedPieceAt(clickedPosition, Team.CurrentTurn);
    placement = sacrifice?.Position ?? clickedPosition;
    if (sacrifice is null || sacrifice.Definition.Type == PieceType.Farm)
    {
      return false;
    }

    if (!IsFootprintOnBoard(archdemon, placement))
    {
      return false;
    }

    foreach ((int x, int y) square in OccupiedSquares(archdemon, placement))
    {
      if (!IsTraversableTerrainSquare(square))
      {
        return false;
      }
    }

    return !pieceSetup.Pieces.Any(piece =>
      piece != sacrifice &&
      piece.AttachedTo is null &&
      piece.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
        placement.x, placement.y, archdemon.Size.x, archdemon.Size.y));
  }

  private bool CanPlaceSpecialPurchase(
    PieceDefinition definition,
    (int x, int y) clickedPosition)
  {
    return definition.Type switch
    {
      PieceType.Shadow => TryGetLocalShadowHost(clickedPosition, out _),
      PieceType.Archdemon => TryGetLocalArchdemonSacrifice(
        definition, clickedPosition, out _, out _),
      _ => false
    };
  }

  private void RemoveLocalShadowsAttachedTo(Piece host)
  {
    foreach (Piece shadow in pieceSetup.Pieces.Where(piece =>
      piece.AttachedTo == host && piece.AttachmentKind == AttachmentKind.Shadow).ToArray())
    {
      pieceSetup.RemovePiece(shadow);
    }
  }


}
