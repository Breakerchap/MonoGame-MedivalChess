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

  private Piece GetLocalRoyalHealthPayer(TeamName team)
  {
    return pieceSetup.Pieces
      .Where(piece =>
        piece.Team == team &&
        piece.AttachedTo is null &&
        piece.IsRoyal)
      .OrderBy(piece => piece.Position.y)
      .ThenBy(piece => piece.Position.x)
      .ThenBy(piece => piece.NetworkId, StringComparer.Ordinal)
      .FirstOrDefault();
  }

  private bool CanPayLocalRoyalHealth(TeamName team, int amount)
  {
    Piece royal = GetLocalRoyalHealthPayer(team);
    return royal is not null && royal.CurrentHealth > Math.Max(0, amount);
  }

  private bool TryPayLocalRoyalHealth(TeamName team, int amount)
  {
    Piece royal = GetLocalRoyalHealthPayer(team);
    int cost = Math.Max(0, amount);
    if (royal is null || royal.CurrentHealth <= cost)
    {
      return false;
    }

    royal.CurrentHealth -= cost;
    return true;
  }

  private void ApplyLocalContractDemonUpkeep(TeamName team)
  {
    foreach (Piece demon in pieceSetup.Pieces
      .Where(piece =>
        piece.Team == team &&
        piece.AttachedTo is null &&
        piece.Definition.Type == PieceType.ContractDemon)
      .OrderBy(piece => piece.Position.y)
      .ThenBy(piece => piece.Position.x)
      .ThenBy(piece => piece.NetworkId, StringComparer.Ordinal)
      .ToArray())
    {
      if (TryPayLocalRoyalHealth(team, AdvancedAbilityRules.ContractDemonRoyalHealthUpkeep))
      {
        continue;
      }

      demon.Team = TeamName.Neutral;
      demon.HasMovedThisTurn = true;
      demon.HasAttackedThisTurn = true;
      demon.AbilityState = demon.AbilityState with
      {
        CannotActThisTurn = true,
        CannotMoveThisTurn = true
      };
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

    Piece sacrificePiece = sacrifice;
    (int x, int y) resolvedPlacement = placement;
    return !pieceSetup.Pieces.Any(piece =>
      piece != sacrificePiece &&
      piece.AttachedTo is null &&
      piece.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
        resolvedPlacement.x, resolvedPlacement.y, archdemon.Size.x, archdemon.Size.y));
  }

  private bool TryGetLocalHelicopterDeployment(
    PieceDefinition definition,
    (int x, int y) clickedPosition,
    out Piece helicopter,
    out (int x, int y) placement)
  {
    helicopter = GetUnattachedPieceAt(clickedPosition, Team.CurrentTurn);
    placement = helicopter?.Position ?? clickedPosition;
    if (helicopter?.Definition.Type != PieceType.Helicopter ||
        definition.Type is PieceType.Farm or PieceType.Helicopter or PieceType.Shadow or PieceType.Archdemon ||
        !IsFootprintOnBoard(definition, placement))
    {
      return false;
    }

    if (OccupiedSquares(definition, placement).Any(_terrain.IsLake))
    {
      return false;
    }

    if (RoyalAbilityRules.RequiresAdjacentRoyalPlacement(definition.Type.ToString()))
    {
      UnitRule placingRule = UnitRules.FromPieceDefinition(definition);
      Piece deploymentHelicopter = helicopter;
      (int x, int y) deploymentPlacement = placement;
      bool hasAdjacentRoyal = pieceSetup.Pieces.Any(piece =>
        piece.Team == Team.CurrentTurn &&
        piece != deploymentHelicopter &&
        piece.IsRoyal &&
        AbilityRules.AreAdjacent(
          placingRule, deploymentPlacement,
          UnitRules.FromPieceDefinition(piece.Definition), piece.Position,
          includeDiagonal: true));
      if (!RoyalAbilityRules.MeetsAdjacentRoyalPlacementRequirement(
            definition.Type.ToString(), hasAdjacentRoyal))
      {
        return false;
      }
    }

    Piece ignoredHelicopter = helicopter;
    (int x, int y) resolvedDeployment = placement;
    return !pieceSetup.Pieces.Any(piece =>
      piece != ignoredHelicopter &&
      piece.AttachedTo is null &&
      piece.Definition.Type != PieceType.Farm &&
      UnitRules.FootprintsOverlap(
        piece.Position.x, piece.Position.y, piece.Definition.Size.x, piece.Definition.Size.y,
        resolvedDeployment.x, resolvedDeployment.y, definition.Size.x, definition.Size.y));
  }

  private bool CanPlaceLocalHelicopter(
    PieceDefinition definition,
    (int x, int y) position)
  {
    if (definition.Type != PieceType.Helicopter || !IsFootprintOnBoard(definition, position))
    {
      return false;
    }
    TeamName? owner = GetSquareOwner(position);
    if (owner.HasValue && owner.Value != Team.CurrentTurn)
    {
      return false;
    }
    return pieceSetup.IsFootprintClear(definition, position);
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
      _ => TryGetLocalHelicopterDeployment(
        definition, clickedPosition, out _, out _)
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



  private Piece GetOwnedAttachedActionUnitAt(
    (int x, int y) position,
    TeamName team)
  {
    return pieceSetup.Pieces.FirstOrDefault(piece =>
      piece.Team == team &&
      piece.AttachedTo is not null &&
      piece.Position == position &&
      AdvancedAbilityRules.CanAttachedUnitAttack(piece.Definition.Type.ToString()));
  }

  private Piece GetAttackableAttachedSuccubusAt(
    (int x, int y) position,
    Piece attacker)
  {
    return pieceSetup.Pieces.FirstOrDefault(piece =>
      piece.Definition.Type == PieceType.Succubus &&
      piece.AttachmentKind == AttachmentKind.Succubus &&
      piece.AttachedTo is not null &&
      piece.Position == position &&
      piece.Team != attacker.Team &&
      piece.AttachedTo != attacker);
  }

  private bool TryUseLocalSuccubusSpecial(
    Piece succubus,
    (int x, int y) targetPosition,
    Piece target)
  {
    if (succubus.Definition.Type != PieceType.Succubus)
    {
      return false;
    }

    if (succubus.AttachedTo is not null)
    {
      if (succubus.AttachmentKind != AttachmentKind.Succubus ||
          target is not null)
      {
        return false;
      }

      Piece host = succubus.AttachedTo;
      if (!AbilityRules.AreAdjacent(
            UnitRules.FromPieceDefinition(succubus.Definition), targetPosition,
            UnitRules.FromPieceDefinition(host.Definition), host.Position,
            includeDiagonal: true) ||
          !CanDisplaceLocalPieceTo(succubus, targetPosition))
      {
        return false;
      }

      pieceSetup.Detach(succubus);
      succubus.Position = targetPosition;
      pieceSetup.RefreshOccupancy();
      return true;
    }

    if (target is null || target == succubus ||
        target.Team == succubus.Team ||
        target.AttachedTo is not null ||
        !AdvancedAbilityRules.CanSuccubusAttach(
          succubus.AbilityState, target.NetworkId))
    {
      return false;
    }

    return pieceSetup.Attach(succubus, target, AttachmentKind.Succubus);
  }



  private bool TryUseLocalChronosRewind(Piece chronos, Piece target)
  {
    if (chronos.Definition.Type != PieceType.Chronos ||
        target is null || target == chronos ||
        target.Team != chronos.Team ||
        chronos.AbilityState.CooldownOwnerTurns > 0 ||
        chronos.AbilityState.PreviousOwnerTurnStart is not UnitTurnSnapshot chronosSnapshot ||
        target.AbilityState.PreviousOwnerTurnStart is not UnitTurnSnapshot targetSnapshot ||
        !IsWithinLocalCircleRange(chronos, target, 3))
    {
      return false;
    }

    (int x, int y) chronosDestination = (chronosSnapshot.X, chronosSnapshot.Y);
    (int x, int y) targetDestination = (targetSnapshot.X, targetSnapshot.Y);
    if (!CanSwapLocalPieceTo(chronos, target, chronosDestination) ||
        !CanSwapLocalPieceTo(target, chronos, targetDestination) ||
        UnitRules.FootprintsOverlap(
          chronosDestination.x, chronosDestination.y,
          chronos.Definition.Size.x, chronos.Definition.Size.y,
          targetDestination.x, targetDestination.y,
          target.Definition.Size.x, target.Definition.Size.y))
    {
      return false;
    }

    chronos.Position = chronosDestination;
    target.Position = targetDestination;
    foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == chronos))
    {
      attachment.Position = chronosDestination;
    }
    foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == target))
    {
      attachment.Position = targetDestination;
    }

    chronos.CurrentHealth = Math.Min(
      AdvancedAbilityRules.GetEffectiveMaximumHealth(
        UnitRules.FromPieceDefinition(chronos.Definition), chronos.AbilityState),
      chronosSnapshot.Health);
    target.CurrentHealth = Math.Min(
      AdvancedAbilityRules.GetEffectiveMaximumHealth(
        UnitRules.FromPieceDefinition(target.Definition), target.AbilityState),
      targetSnapshot.Health);
    chronos.AbilityState = chronos.AbilityState with
    {
      CooldownOwnerTurns = AdvancedAbilityRules.ChronosCooldownTurns
    };
    pieceSetup.RefreshOccupancy();
    CompleteAction();
    return true;
  }



  private Piece GetLocalLinkedPhylactery(Piece lich)
  {
    string phylacteryId = lich.AbilityState.LinkedPieceId;
    return string.IsNullOrWhiteSpace(phylacteryId)
      ? null
      : pieceSetup.Pieces.FirstOrDefault(piece =>
          piece.NetworkId == phylacteryId &&
          piece.Definition.Type == PieceType.Phylactery &&
          piece.Team == lich.Team);
  }

  private bool IsLocalLichDestinationWithinLink(Piece piece, (int x, int y) destination)
  {
    if (piece.Definition.Type != PieceType.Lich)
    {
      return true;
    }

    Piece phylactery = GetLocalLinkedPhylactery(piece);
    return phylactery is not null &&
      Math.Abs(destination.x - phylactery.Position.x) +
      Math.Abs(destination.y - phylactery.Position.y) <= 4;
  }

  private void SpawnLocalLinkedLichesAtOwnerTurnStart(TeamName team)
  {
    PieceDefinition lichDefinition = PieceDefinitions.All.First(definition =>
      definition.Type == PieceType.Lich);
    (int x, int y)[] offsets =
    [
      (0, -1), (1, 0), (0, 1), (-1, 0),
      (1, -1), (1, 1), (-1, 1), (-1, -1)
    ];

    foreach (Piece phylactery in pieceSetup.Pieces
      .Where(piece =>
        piece.Team == team &&
        piece.Definition.Type == PieceType.Phylactery)
      .OrderBy(piece => piece.Position.y)
      .ThenBy(piece => piece.Position.x)
      .ThenBy(piece => piece.NetworkId, StringComparer.Ordinal)
      .ToArray())
    {
      Piece linked = string.IsNullOrWhiteSpace(phylactery.AbilityState.LinkedPieceId)
        ? null
        : pieceSetup.Pieces.FirstOrDefault(piece =>
            piece.NetworkId == phylactery.AbilityState.LinkedPieceId &&
            piece.Definition.Type == PieceType.Lich &&
            piece.Team == team);
      if (linked is not null)
      {
        continue;
      }

      foreach ((int x, int y) offset in offsets)
      {
        (int x, int y) destination = (
          phylactery.Position.x + offset.x,
          phylactery.Position.y + offset.y);
        if (!CanPlacePiece(lichDefinition, destination, null) ||
            _barricades.ContainsKey(destination) ||
            _abilityEntities.Any(entity =>
              entity.X == destination.x && entity.Y == destination.y &&
              AbilityEntityRules.BlocksLandingFor(entity, team.ToNetworkTeam())))
        {
          continue;
        }

        Piece lich = new(lichDefinition, destination, team)
        {
          AbilityState = AdvancedAbilityRules.SetLinkedPiece(
            new UnitAbilityState(), phylactery.NetworkId)
        };
        pieceSetup.AddPiece(lich);
        phylactery.AbilityState = AdvancedAbilityRules.SetLinkedPiece(
          phylactery.AbilityState, lich.NetworkId);
        break;
      }
    }
  }

  private void ApplyLocalLichDeathLink(Piece lich, TeamName? attackingTeam)
  {
    if (lich.Definition.Type != PieceType.Lich)
    {
      return;
    }

    Piece phylactery = GetLocalLinkedPhylactery(lich);
    if (phylactery is null || !pieceSetup.Pieces.Contains(phylactery))
    {
      return;
    }

    int linkedDeaths = phylactery.AbilityState.LinkedDeaths + 1;
    phylactery.AbilityState = (AdvancedAbilityRules.SetLinkedPiece(
      phylactery.AbilityState, null)) with
    {
      LinkedDeaths = linkedDeaths,
      OdinProtectionAvailable = linkedDeaths >= 4
        ? false
        : phylactery.AbilityState.OdinProtectionAvailable
    };
    phylactery.CurrentHealth = linkedDeaths >= 4
      ? 0
      : phylactery.CurrentHealth - AdvancedAbilityRules.LichDeathDamage;
    HandlePieceDestroyed(phylactery, attackingTeam);
  }



  private int GetLocalGangLeaderRecruitCost(Piece target)
  {
    int baseCost = target.Definition.Type == PieceType.Qilin &&
      AdvancedAbilityRules.IsValidQilinCost(target.AbilityState.VariableCostValue)
        ? target.AbilityState.VariableCostValue
        : target.Definition.Cost;
    return Math.Max(0, baseCost) * 2;
  }

  private bool TryUseLocalGangLeaderRecruit(Piece gangLeader, Piece target)
  {
    if (gangLeader.Definition.Type != PieceType.GangLeader ||
        target is null || target == gangLeader ||
        target.Team == gangLeader.Team || target.Team == TeamName.Neutral ||
        target.AttachedTo is not null || target.IsRoyal ||
        gangLeader.AbilityState.CooldownOwnerTurns > 0 ||
        !target.OccupiedSquares().Any(square =>
          CanAttackSquareWithAttachments(gangLeader, square)))
    {
      return false;
    }

    Team team = _teams.Find(candidate => candidate.TeamName == gangLeader.Team);
    int cost = GetLocalGangLeaderRecruitCost(target);
    if (team is null || team.Money < cost)
    {
      return false;
    }

    team.Money = ClampCurrency((long)team.Money - cost);
    target.Team = gangLeader.Team;
    target.HasMovedThisTurn = true;
    target.HasAttackedThisTurn = true;
    target.AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(
      target.Definition.Type.ToString());
    target.AbilityState = target.AbilityState with
    {
      CannotActThisTurn = true,
      CannotMoveThisTurn = true
    };
    gangLeader.AbilityState = AdvancedAbilityRules.StartCooldown(
      gangLeader.AbilityState, AdvancedAbilityRules.GangLeaderCooldownTurns);
    CompleteAction();
    return true;
  }



  private Dictionary<(int x, int y), List<(int x, int y)>>
    GetLocalFylgjaForcedMovementPaths(Piece target)
  {
    UnitRule baseRule = GetEffectiveMovementRule(target);
    UnitRule forcedRule = baseRule with
    {
      Type = "FylgjaForcedMovement",
      MoveRange = 3,
      MinimumMoveRange = 1,
      MovePattern = RuleShape.Any
    };

    return MovementPathfinder.FindPaths(
      target,
      destination =>
        pieceSetup.IsFootprintClear(target.Definition, destination, target) &&
        CanLandPieceAt(target, destination, mayUsePalaceSupport: false),
      (from, destination) => CanTravelThroughPosition(target, from, destination),
      destination => GetMovementCost(target, destination),
      (from, to) => CrossesRiver(target, from, to),
      forcedRule,
      (from, destination) => GetMovementCost(target, from, destination),
      _ => 3,
      3,
      _ => true);
  }

  private bool TryUseLocalFylgjaAbility(
    Piece fylgja,
    (int x, int y) targetPosition,
    Piece targetPiece)
  {
    if (fylgja.Definition.Type != PieceType.Fylgja ||
        fylgja.HasAttackedThisTurn)
    {
      return false;
    }

    UnitAbilityState state = fylgja.AbilityState;
    if (!string.Equals(state.PendingAbility, "ForceMove", StringComparison.Ordinal) ||
        state.PendingSelections.Count == 0)
    {
      if (targetPiece is null || targetPiece == fylgja ||
          targetPiece.AttachedTo is not null ||
          targetPiece.Definition.Category == PieceCategory.Structure ||
          !targetPiece.OccupiedSquares().Any(square =>
            CanAttackSquareWithAttachments(fylgja, square)))
      {
        return false;
      }

      fylgja.AbilityState = AdvancedAbilityRules.AddPendingSelection(
        state,
        "ForceMove",
        new AbilitySelection(targetPiece.NetworkId, targetPiece.Position.x, targetPiece.Position.y));
      return true;
    }

    string forcedId = state.PendingSelections[0].TargetId;
    Piece forced = string.IsNullOrWhiteSpace(forcedId)
      ? null
      : pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == forcedId);
    if (forced is null || forced.AttachedTo is not null ||
        forced.Definition.Category == PieceCategory.Structure)
    {
      fylgja.AbilityState = AdvancedAbilityRules.ClearPendingSelections(state);
      return false;
    }

    Dictionary<(int x, int y), List<(int x, int y)>> paths =
      GetLocalFylgjaForcedMovementPaths(forced);
    if (!paths.TryGetValue(targetPosition, out List<(int x, int y)> path))
    {
      return false;
    }

    fylgja.AbilityState = AdvancedAbilityRules.ClearPendingSelections(
      fylgja.AbilityState);
    AttackTurnState attackState = AbilityStateRules.RecordAttack(
      fylgja.Definition.Type.ToString(), fylgja.AttacksThisTurn);
    fylgja.AttacksThisTurn = attackState.AttacksThisTurn;
    fylgja.HasAttackedThisTurn = attackState.HasAttackedThisTurn;
    fylgja.AbilityState = AdvancedAbilityRules.RecordAttack(
      fylgja.Definition.Type.ToString(), fylgja.AbilityState, forced.NetworkId);

    pieceSetup.MovePiece(forced, targetPosition);
    ReleaseLocalPetrificationIfBroken(forced);
    TriggerMinesAlongMovement(forced, path);
    TriggerLocalAbilityEntitiesAlongMovement(forced, path);
    if (pieceSetup.Pieces.Contains(forced))
    {
      TryDeliverTreasure(forced);
      if (HasEscortVictory(forced))
      {
        _winningTeam = forced.Team;
        _screen = Screen.GameOver;
      }
    }

    CompleteAction();
    return true;
  }



  private bool IsValidLocalBountyTarget(Piece hunter, Piece target) =>
    hunter is not null &&
    target is not null &&
    target != hunter &&
    target.Team != hunter.Team &&
    target.Team != TeamName.Neutral &&
    target.AttachedTo is null &&
    !target.IsRoyal;

  private bool TrySelectLocalBountyTarget(Piece hunter, Piece target)
  {
    if (hunter.Definition.Type != PieceType.BountyHunter ||
        !hunter.AbilityState.BountySelectionAvailable ||
        !IsValidLocalBountyTarget(hunter, target))
    {
      return false;
    }

    hunter.AbilityState = AdvancedAbilityRules.SetBountyTarget(
      hunter.AbilityState, target.NetworkId);
    return true;
  }

  private void ClearLocalBountyTargetsFor(string defeatedId)
  {
    foreach (Piece hunter in pieceSetup.Pieces.Where(piece =>
      piece.Definition.Type == PieceType.BountyHunter &&
      string.Equals(
        piece.AbilityState.BountyTargetId, defeatedId, StringComparison.Ordinal)))
    {
      hunter.AbilityState = AdvancedAbilityRules.ClearBountyTarget(
        hunter.AbilityState);
    }
  }

  private void RefreshLocalBountySelectionAtOwnerTurnStart(TeamName team)
  {
    foreach (Piece hunter in pieceSetup.Pieces.Where(piece =>
      piece.Team == team && piece.Definition.Type == PieceType.BountyHunter))
    {
      Piece currentTarget = string.IsNullOrWhiteSpace(hunter.AbilityState.BountyTargetId)
        ? null
        : pieceSetup.Pieces.FirstOrDefault(piece =>
            piece.NetworkId == hunter.AbilityState.BountyTargetId);

      if (currentTarget is not null && IsValidLocalBountyTarget(hunter, currentTarget))
      {
        continue;
      }

      hunter.AbilityState = AdvancedAbilityRules.EnableBountySelection(
        AdvancedAbilityRules.ClearBountyTarget(hunter.AbilityState));
    }
  }



  private bool CanToggleLocalHeraldCompanion(Piece herald, Piece target)
  {
    if (herald.Definition.Type != PieceType.Herald ||
        target is null || target == herald ||
        target.Team != herald.Team ||
        target.AttachedTo is not null ||
        target.Definition.Size != (1, 1) ||
        IsTreasureCarrier(target) ||
        !CanMoveThisTurn(herald))
    {
      return false;
    }

    return AbilityRules.IsHeraldCompanion(
      UnitRules.FromPieceDefinition(target.Definition),
      herald.Position,
      target.Position);
  }

  private bool TryToggleLocalHeraldCompanion(Piece herald, Piece target)
  {
    if (!CanToggleLocalHeraldCompanion(herald, target))
    {
      return false;
    }

    herald.AbilityState = AdvancedAbilityRules.TogglePendingTarget(
      herald.AbilityState,
      "HeraldCompanions",
      target.NetworkId,
      3);
    return true;
  }



  private void TransformLocalSkinwalkerAfterKill(Piece skinwalker, Piece defeated)
  {
    if (skinwalker.Definition.Type != PieceType.Skinwalker ||
        defeated.Definition.Type == PieceType.Farm ||
        !pieceSetup.Pieces.Contains(skinwalker))
    {
      return;
    }

    PieceDefinition copiedDefinition = defeated.Definition;
    skinwalker.TransformTo(copiedDefinition);
    skinwalker.AbilityState = skinwalker.AbilityState with
    {
      CannotMoveThisTurn = true,
      CannotActThisTurn = true
    };
    skinwalker.HasMovedThisTurn = true;
    skinwalker.HasAttackedThisTurn = true;
    skinwalker.AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(copiedDefinition.Type.ToString());

    if (!CanDisplaceLocalPieceTo(skinwalker, skinwalker.Position))
    {
      (int x, int y)? relocation = FindNearestLocalLegalDisplacement(skinwalker);
      if (relocation is not null)
      {
        skinwalker.Position = relocation.Value;
        foreach (Piece attachment in pieceSetup.Pieces.Where(piece => piece.AttachedTo == skinwalker))
        {
          attachment.Position = relocation.Value;
        }
        pieceSetup.RefreshOccupancy();
      }
    }
  }



  private Piece GetLocalLongboatBoardTarget(Piece rider, (int x, int y) destination)
  {
    if (rider.AttachedTo is not null ||
        rider.Definition.Size != (1, 1) ||
        rider.Definition.Type == PieceType.FlyingLongboat)
    {
      return null;
    }

    Piece longboat = GetUnattachedPieceAt(destination, rider.Team);
    if (longboat?.Definition.Type != PieceType.FlyingLongboat ||
        pieceSetup.Pieces.Count(piece =>
          piece.AttachedTo == longboat &&
          piece.AttachmentKind == AttachmentKind.Passenger) >= 3)
    {
      return null;
    }

    return longboat;
  }

  private bool TryBoardLocalLongboat(Piece rider, (int x, int y) destination)
  {
    Piece longboat = GetLocalLongboatBoardTarget(rider, destination);
    if (longboat is null)
    {
      return false;
    }

    if (!pieceSetup.Attach(rider, longboat, AttachmentKind.Passenger))
    {
      return false;
    }

    rider.HasMovedThisTurn = true;
    rider.AbilityState = AdvancedAbilityRules.RecordMove(rider.AbilityState);
    rider.AbilityState = rider.AbilityState with
    {
      CannotActThisTurn = true,
      CannotMoveThisTurn = true
    };
    longboat.AbilityState = AdvancedAbilityRules.RecordLongboatBoarding(
      longboat.AbilityState, rider.NetworkId);
    return true;
  }

  private Piece GetNextLocalLongboatPassenger(Piece longboat, string afterPassengerId = null)
  {
    string[] orderedIds = longboat.AbilityState.PassengerIds
      .Where(id => pieceSetup.Pieces.Any(piece =>
        piece.NetworkId == id &&
        piece.AttachedTo == longboat &&
        piece.AttachmentKind == AttachmentKind.Passenger))
      .ToArray();
    if (orderedIds.Length == 0)
    {
      return null;
    }

    int index = string.IsNullOrWhiteSpace(afterPassengerId)
      ? 0
      : Array.FindIndex(orderedIds, id => id == afterPassengerId) + 1;
    if (index < 0 || index >= orderedIds.Length)
    {
      index = 0;
    }

    return pieceSetup.Pieces.FirstOrDefault(piece => piece.NetworkId == orderedIds[index]);
  }

  private bool CanLocalLongboatPassengerDisembark(Piece passenger, (int x, int y) destination)
  {
    Piece longboat = passenger.AttachedTo;
    if (passenger.AttachmentKind != AttachmentKind.Passenger ||
        longboat?.Definition.Type != PieceType.FlyingLongboat ||
        !AbilityRules.AreAdjacent(
          UnitRules.FromPieceDefinition(longboat.Definition), longboat.Position,
          UnitRules.FromPieceDefinition(passenger.Definition), destination,
          includeDiagonal: true))
    {
      return false;
    }

    return CanDisplaceLocalPieceTo(passenger, destination);
  }

  private bool TryDisembarkLocalLongboatPassenger(
    Piece passenger,
    (int x, int y) destination,
    Piece targetPiece)
  {
    if (targetPiece is not null ||
        !CanLocalLongboatPassengerDisembark(passenger, destination))
    {
      return false;
    }

    Piece longboat = passenger.AttachedTo;
    longboat.AbilityState = AdvancedAbilityRules.RecordLongboatDisembark(
      longboat.AbilityState, passenger.NetworkId);
    pieceSetup.Detach(passenger);
    passenger.Position = destination;
    passenger.HasMovedThisTurn = true;
    passenger.HasAttackedThisTurn = true;
    passenger.AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(
      passenger.Definition.Type.ToString());
    passenger.AbilityState = passenger.AbilityState with
    {
      CannotActThisTurn = true,
      CannotMoveThisTurn = true
    };
    pieceSetup.RefreshOccupancy();
    CompleteAction();
    return true;
  }


  private void RemoveLocalLongboatPassengerReference(Piece passenger)
  {
    if (passenger.AttachmentKind != AttachmentKind.Passenger ||
        passenger.AttachedTo?.Definition.Type != PieceType.FlyingLongboat)
    {
      return;
    }

    Piece longboat = passenger.AttachedTo;
    longboat.AbilityState = AdvancedAbilityRules.RecordLongboatDisembark(
      longboat.AbilityState, passenger.NetworkId);
  }

  private void ReleaseLocalLongboatPassengers(Piece longboat)
  {
    if (longboat.Definition.Type != PieceType.FlyingLongboat)
    {
      return;
    }

    string[] boardingOrder = longboat.AbilityState.PassengerIds.ToArray();
    foreach (string passengerId in boardingOrder)
    {
      Piece passenger = pieceSetup.Pieces.FirstOrDefault(piece =>
        piece.NetworkId == passengerId &&
        piece.AttachedTo == longboat &&
        piece.AttachmentKind == AttachmentKind.Passenger);
      if (passenger is null) continue;

      pieceSetup.Detach(passenger);
      (int x, int y)? destination = null;
      foreach ((int x, int y) candidate in _board.Cells
        .OrderBy(position => Math.Max(
          Math.Abs(position.x - longboat.Position.x),
          Math.Abs(position.y - longboat.Position.y)))
        .ThenBy(position => position.y)
        .ThenBy(position => position.x))
      {
        if (CanDisplaceLocalPieceTo(passenger, candidate))
        {
          destination = candidate;
          break;
        }
      }

      if (destination is not null)
      {
        passenger.Position = destination.Value;
        passenger.AbilityState = passenger.AbilityState with
        {
          CannotActThisTurn = true,
          CannotMoveThisTurn = true
        };
        passenger.HasMovedThisTurn = true;
        passenger.HasAttackedThisTurn = true;
      }
      pieceSetup.RefreshOccupancy();
    }

    longboat.AbilityState = longboat.AbilityState with
    {
      PassengerIds = Array.Empty<string>()
    };
  }



  private Piece GetLocalLinkedNecromancer(Piece skeleton)
  {
    string necromancerId = skeleton.AbilityState.LinkedPieceId;
    return string.IsNullOrWhiteSpace(necromancerId)
      ? null
      : pieceSetup.Pieces.FirstOrDefault(piece =>
          piece.NetworkId == necromancerId &&
          piece.Definition.Type == PieceType.Necromancer &&
          piece.Team == skeleton.Team);
  }

  private bool IsLocalSkeletonDestinationWithinLink(
    Piece piece,
    (int x, int y) destination)
  {
    if (piece.Definition.Type != PieceType.SkeletonMinion)
    {
      return true;
    }

    Piece necromancer = GetLocalLinkedNecromancer(piece);
    return necromancer is not null &&
      Math.Max(
        Math.Abs(destination.x - necromancer.Position.x),
        Math.Abs(destination.y - necromancer.Position.y)) <= 4;
  }

  private bool SpawnLocalSkeletonForNecromancer(
    Piece necromancer,
    bool initialPlacement)
  {
    if (necromancer.Definition.Type != PieceType.Necromancer ||
        !pieceSetup.Pieces.Contains(necromancer))
    {
      return false;
    }

    PieceDefinition skeletonDefinition = PieceDefinitions.All.First(definition =>
      definition.Type == PieceType.SkeletonMinion);

    IEnumerable<(int x, int y)> candidates = _board.Cells
      .Where(position =>
      {
        int dx = Math.Abs(position.x - necromancer.Position.x);
        int dy = Math.Abs(position.y - necromancer.Position.y);
        int distance = Math.Max(dx, dy);
        return distance >= 1 && (initialPlacement ? distance <= 4 : distance == 1);
      })
      .OrderBy(position => Math.Max(
        Math.Abs(position.x - necromancer.Position.x),
        Math.Abs(position.y - necromancer.Position.y)))
      .ThenBy(position => position.y)
      .ThenBy(position => position.x);

    foreach ((int x, int y) destination in candidates)
    {
      if (!CanPlacePiece(skeletonDefinition, destination, null) ||
          _barricades.ContainsKey(destination) ||
          _abilityEntities.Any(entity =>
            entity.X == destination.x && entity.Y == destination.y &&
            AbilityEntityRules.BlocksLandingFor(entity, necromancer.Team.ToNetworkTeam())))
      {
        continue;
      }

      Piece skeleton = new(skeletonDefinition, destination, necromancer.Team)
      {
        AbilityState = AdvancedAbilityRules.SetLinkedPiece(
          new UnitAbilityState(), necromancer.NetworkId)
      };
      pieceSetup.AddPiece(skeleton);
      necromancer.AbilityState = (AdvancedAbilityRules.SetLinkedPiece(
        necromancer.AbilityState, skeleton.NetworkId)) with
      {
        PendingRespawn = false
      };
      return true;
    }

    necromancer.AbilityState = necromancer.AbilityState with
    {
      LinkedPieceId = null,
      PendingRespawn = true
    };
    return false;
  }

  private void RespawnLocalSkeletonsAtOwnerTurnStart(TeamName team)
  {
    foreach (Piece necromancer in pieceSetup.Pieces
      .Where(piece =>
        piece.Team == team &&
        piece.Definition.Type == PieceType.Necromancer)
      .OrderBy(piece => piece.Position.y)
      .ThenBy(piece => piece.Position.x)
      .ThenBy(piece => piece.NetworkId, StringComparer.Ordinal)
      .ToArray())
    {
      Piece linked = string.IsNullOrWhiteSpace(necromancer.AbilityState.LinkedPieceId)
        ? null
        : pieceSetup.Pieces.FirstOrDefault(piece =>
            piece.NetworkId == necromancer.AbilityState.LinkedPieceId &&
            piece.Definition.Type == PieceType.SkeletonMinion &&
            piece.Team == team);
      if (linked is not null)
      {
        continue;
      }

      if (necromancer.AbilityState.PendingRespawn)
      {
        SpawnLocalSkeletonForNecromancer(necromancer, initialPlacement: false);
      }
    }
  }

  private void ApplyLocalSkeletonDeathLink(Piece skeleton)
  {
    if (skeleton.Definition.Type != PieceType.SkeletonMinion)
    {
      return;
    }

    Piece necromancer = GetLocalLinkedNecromancer(skeleton);
    if (necromancer is null || !pieceSetup.Pieces.Contains(necromancer))
    {
      return;
    }

    necromancer.AbilityState = necromancer.AbilityState with
    {
      LinkedPieceId = null,
      PendingRespawn = true
    };
  }

  private void RemoveLocalSkeletonForNecromancerDeath(Piece necromancer)
  {
    if (necromancer.Definition.Type != PieceType.Necromancer)
    {
      return;
    }

    string skeletonId = necromancer.AbilityState.LinkedPieceId;
    if (string.IsNullOrWhiteSpace(skeletonId))
    {
      return;
    }

    Piece skeleton = pieceSetup.Pieces.FirstOrDefault(piece =>
      piece.NetworkId == skeletonId &&
      piece.Definition.Type == PieceType.SkeletonMinion &&
      piece.Team == necromancer.Team);
    if (skeleton is not null)
    {
      pieceSetup.RemovePiece(skeleton);
    }
  }


}
