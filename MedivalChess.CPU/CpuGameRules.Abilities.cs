using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>
/// CPU-state adapter for shared ability decisions. Unit mechanics are defined in MedivalChess.Shared;
/// this file only applies those decisions to <see cref="CpuMutableGameState"/>.
/// </summary>
public static partial class CpuGameRules
{
  private static void ResolveSharedPieceDamage(
    CpuMutableGameState state,
    NetworkPiece attacker,
    NetworkTeam attackerTeam,
    string targetId,
    int? damageOverride
  )
  {
    NetworkPiece? target = FindPiece(state.Pieces, targetId);
    if (target is null || !UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) ||
        !UnitRules.TryGet(target.Type, out UnitRule targetRule) ||
        !AbilityRules.CanDamageTarget(attackerRule, targetRule))
    {
      return;
    }

    NetworkPiece damaged = state.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard) ?? target;
    NetworkPiece? oxAttachment = state.Pieces.FirstOrDefault(piece =>
      piece.AttachedToId == target.Id && AbilityRules.SharesIncomingDamageWithHost(piece.Type));
    int unmitigated = damageOverride ?? GetSharedAttackDamage(state, attacker, target);
    ApplySharedDamageToPiece(state, attacker, attackerTeam, damaged, unmitigated);
    if (oxAttachment is not null && oxAttachment.Id != damaged.Id && FindPiece(state.Pieces, oxAttachment.Id) is not null)
    {
      ApplySharedDamageToPiece(state, attacker, attackerTeam, oxAttachment, unmitigated);
    }

    for (int index = 0; index < state.Pieces.Count; index++)
    {
      if (state.Pieces[index].MarkedTargetId == target.Id)
      {
        state.Pieces[index] = state.Pieces[index] with { MarkedTargetId = null };
      }
    }
  }

  private static int GetSharedAttackDamage(CpuMutableGameState state, NetworkPiece attacker, NetworkPiece target)
  {
    UnitRule attackerRule = UnitRules.GetRequired(attacker.Type);
    UnitRule targetRule = UnitRules.GetRequired(target.Type);
    int baseDamage = AbilityRules.GetBaseAttack(attackerRule, attacker.Health);
    (int x, int y) targetFacing = AbilityStateRules.GetFacing(target.Team, target.FacingX, target.FacingY);
    baseDamage += AbilityRules.GetAttackAbilityBonus(
      attackerRule,
      targetRule,
      IsInForest(state, target),
      targetFacing,
      (attacker.X, attacker.Y),
      (target.X, target.Y)
    );

    return CombatRules.CalculateDamage(
      baseDamage,
      HasAdjacentUnit(state, attacker, attacker.Team, nameof(PieceType.Baron)),
      state.Pieces.Any(piece => piece.Type == nameof(PieceType.Spy) && piece.MarkedTargetId == target.Id),
      false,
      false,
      0
    );
  }

  private static void ApplySharedDamageToPiece(
    CpuMutableGameState state,
    NetworkPiece attacker,
    NetworkTeam attackerTeam,
    NetworkPiece damaged,
    int unmitigatedDamage
  )
  {
    if (!UnitRules.TryGet(attacker.Type, out UnitRule attackerRule) ||
        !UnitRules.TryGet(damaged.Type, out UnitRule damagedRule) ||
        !AbilityRules.CanDamageTarget(attackerRule, damagedRule))
    {
      return;
    }

    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(state, damaged, damaged.Team, nameof(PieceType.Baron)),
      IsInForest(state, damaged),
      state.Source.Terrain.ForestDamageReduction
    );
    int damagedIndex = FindPieceIndex(state.Pieces, damaged.Id);
    if (damagedIndex < 0)
    {
      return;
    }

    NetworkPiece live = state.Pieces[damagedIndex];
    if (live.Health > damage)
    {
      state.Pieces[damagedIndex] = live with { Health = live.Health - damage };
      return;
    }

    HandleSharedPieceDestroyed(state, live, attackerTeam);
  }

  private static void ApplySharedFixedDamage(
    CpuMutableGameState state,
    string targetId,
    NetworkTeam sourceTeam,
    int damage,
    bool applyCombatMitigation
  )
  {
    NetworkPiece? target = FindPiece(state.Pieces, targetId);
    if (target is null)
    {
      return;
    }

    int appliedDamage = applyCombatMitigation
      ? CombatRules.CalculateDamage(
        damage,
        false,
        false,
        HasAdjacentUnit(state, target, target.Team, nameof(PieceType.Baron)),
        IsInForest(state, target),
        state.Source.Terrain.ForestDamageReduction)
      : damage;

    int index = FindPieceIndex(state.Pieces, target.Id);
    if (index < 0) return;
    NetworkPiece live = state.Pieces[index];
    if (live.Health > appliedDamage)
    {
      state.Pieces[index] = live with { Health = live.Health - appliedDamage };
    }
    else
    {
      HandleSharedPieceDestroyed(state, live, sourceTeam);
    }
  }

  private static void HandleSharedPieceDestroyed(
    CpuMutableGameState state,
    NetworkPiece piece,
    NetworkTeam? attackingTeam
  )
  {
    int liveIndex = FindPieceIndex(state.Pieces, piece.Id);
    if (liveIndex < 0)
    {
      return;
    }
    piece = state.Pieces[liveIndex];

    LethalAbilityOutcome lethal = AbilityStateRules.ResolveLethalDamage(piece.Type, piece.HasRevived);
    if (lethal.Kind != LethalAbilityOutcomeKind.Die)
    {
      state.Pieces[liveIndex] = piece with
      {
        Type = lethal.ResultingType,
        Health = lethal.ResultingHealth,
        HasRevived = lethal.HasRevived,
        TurnsInCurrentForm = 0,
        AttacksThisTurn = 0,
        HasAttackedThisTurn = false
      };
      return;
    }

    AbilityUnitSnapshot[] deathSnapshots = state.Pieces
      .Where(candidate => candidate.AttachedToId is null)
      .Select(AbilityAttackRules.Snapshot)
      .ToArray();
    IReadOnlyList<AbilityDamageInstruction> deathExplosion = AbilityAttackRules.BuildDeathExplosion(
      AbilityAttackRules.Snapshot(piece),
      deathSnapshots
    );

    if (state.TreasureCarrierId == piece.Id)
    {
      state.TreasureCarrierId = null;
      state.TreasurePosition = (piece.X, piece.Y);
    }

    if (piece.Team != NetworkTeam.Neutral && attackingTeam is NetworkTeam attacker && attacker != piece.Team)
    {
      int unitPrice = UnitRules.TryGet(piece.Type, out UnitRule rule) ? GetUnitPrice(state.Source.Configuration, rule) : 0;
      AddMoney(state, attacker, CombatRules.RoundCurrencyToNearestFive(unitPrice * state.Source.Configuration.KillerRefundMultiplier));
      AddMoney(state, piece.Team, CombatRules.RoundCurrencyToNearestFive(unitPrice * state.Source.Configuration.DefeatedTeamRefundMultiplier));
    }

    bool royalDeath = IsSharedCpuRoyalDeath(state, piece);
    UnitRules.TryGet(piece.Type, out UnitRule destroyedRule);
    RemovePiece(state, piece.Id);

    foreach (AbilityDamageInstruction instruction in deathExplosion)
    {
      ApplySharedFixedDamage(state, instruction.TargetId, piece.Team, instruction.FixedDamage, applyCombatMitigation: true);
    }

    if (!royalDeath || state.Winner is not null)
    {
      return;
    }

    if (state.Source.Configuration.GameMode == "Regicide" && attackingTeam is NetworkTeam winner && winner != piece.Team)
    {
      state.Winner = winner;
    }
    else if (state.Source.Configuration.GameMode == "Escort" && destroyedRule is not null)
    {
      RespawnEscortRoyal(state, piece, destroyedRule);
    }
    else if (state.Source.Configuration.GameMode == "Plunder" && attackingTeam is NetworkTeam plunderAttacker && plunderAttacker != piece.Team)
    {
      state.ModeScores[plunderAttacker] = Math.Max(0,
        state.ModeScores.GetValueOrDefault(plunderAttacker) - state.Source.Configuration.PlunderRoyalKillPenalty);
    }
  }

  private static void TriggerSharedMinesAlongMovement(
    CpuMutableGameState state,
    NetworkPiece movingPiece,
    IReadOnlyList<(int x, int y)> path
  )
  {
    if (movingPiece.Type == nameof(PieceType.Engineer) || !UnitRules.TryGet(movingPiece.Type, out UnitRule rule))
    {
      return;
    }

    List<((int x, int y) position, NetworkTeam owner)> triggered = [];
    foreach ((int x, int y) step in path)
    {
      foreach ((int x, int y) square in OccupiedSquares(rule, step))
      {
        if (state.Mines.TryGetValue(square, out NetworkTeam owner) && owner != movingPiece.Team)
        {
          triggered.Add((square, owner));
        }
      }
    }

    foreach (((int x, int y) position, NetworkTeam owner) mine in triggered.Distinct())
    {
      state.Mines.Remove(mine.position);
      HashSet<string> affectedIds = state.Pieces.Where(piece => UnitRules.TryGet(piece.Type, out UnitRule affectedRule) &&
        OccupiedSquares(affectedRule, (piece.X, piece.Y)).Any(square =>
          Math.Abs(square.x - mine.position.x) <= 1 && Math.Abs(square.y - mine.position.y) <= 1))
        .Select(piece => piece.Id)
        .ToHashSet(StringComparer.Ordinal);
      affectedIds.Add(movingPiece.Id);
      foreach (string affectedId in affectedIds)
      {
        ApplySharedFixedDamage(
          state,
          affectedId,
          mine.owner,
          AbilityRules.EngineerMineDamage,
          applyCombatMitigation: false
        );
      }
    }
  }

  private static void DamageSharedBarricade(
    CpuMutableGameState state,
    NetworkPiece attacker,
    (int x, int y) position
  )
  {
    if (!state.Barricades.TryGetValue(position, out int health) ||
        !UnitRules.TryGet(attacker.Type, out UnitRule attackerRule))
    {
      return;
    }

    int damage = AbilityRules.GetBaseAttack(attackerRule, attacker.Health) +
      (HasAdjacentUnit(state, attacker, attacker.Team, nameof(PieceType.Baron))
        ? CombatRules.BaronDamageBonus
        : 0);
    if (health <= damage)
    {
      state.Barricades.Remove(position);
    }
    else
    {
      state.Barricades[position] = health - damage;
    }
  }

  private static void ApplySharedTurnEconomy(CpuMutableGameState state, NetworkTeam team)
  {
    if (!state.Teams.TryGetValue(team, out CpuTeamState? current))
    {
      return;
    }

    int money = current.Money;
    if (state.Source.Configuration.InterestEnabled && state.Source.Configuration.InterestPercent != 0)
    {
      money = ClampCurrency((long)money + EconomyRules.GetInterest(money, state.Source.Configuration.InterestPercent));
    }
    int farms = state.Pieces.Count(piece => piece.Team == team && piece.AttachedToId is null && piece.Type == nameof(PieceType.Farm));
    money = ClampCurrency((long)money + farms * (long)state.Source.Configuration.FarmIncomePerTurn);

    UnitUpkeepSequenceResult abilityUpkeep = EconomyRules.ResolveAbilityUpkeepSequence(
      money,
      state.Pieces
        .Where(piece => piece.Team == team && piece.AttachedToId is null)
        .Select(piece => new UnitUpkeepRequest(piece.Id, piece.Type))
    );
    money = abilityUpkeep.RemainingMoney;
    foreach (UnitUpkeepDecision decision in abilityUpkeep.Decisions)
    {
      if (decision.Paid) continue;
      if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.LoseMatch)
      {
        state.Winner = TeamRules.GetActiveTeams(state.Source.Configuration.PlayerCount)
          .First(candidate => candidate != team);
        state.Teams[team] = current with { Money = money };
        return;
      }
      if (decision.UnpaidEffect == UnpaidUnitUpkeepEffect.FireUnit)
      {
        int index = FindPieceIndex(state.Pieces, decision.UnitId);
        if (index >= 0)
        {
          NetworkPiece mercenary = state.Pieces[index];
          state.Pieces[index] = mercenary with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true,
            AttacksThisTurn = AbilityRules.MaximumAttacksPerTurn(mercenary.Type)
          };
        }
      }
    }

    if (state.Source.Configuration.UnitMaintenanceEnabled && state.Source.Configuration.UnitMaintenancePercent > 0)
    {
      long upkeep = state.Pieces.Where(piece => piece.Team == team && piece.AttachedToId is null)
        .Sum(piece => UnitRules.TryGet(piece.Type, out UnitRule rule)
          ? (long)GetUnitMaintenance(state.Source.Configuration, rule)
          : 0L);
      money = ClampCurrency((long)money - upkeep);
    }
    state.Teams[team] = current with { Money = money };
  }

  private static void ResetSharedTurnActions(CpuMutableGameState state, NetworkTeam team)
  {
    // Delayed effects trigger at the start of the source team's next turn, even if the target is
    // an enemy piece. Split them before refreshing the active team's own pieces.
    foreach (string pieceId in state.Pieces.Select(piece => piece.Id).ToArray())
    {
      int index = FindPieceIndex(state.Pieces, pieceId);
      if (index < 0) continue;
      NetworkPiece piece = state.Pieces[index];
      var pending = AbilityStateRules.SplitPendingDamageForTurn(piece.PendingDamage, team);
      state.Pieces[index] = piece with { PendingDamage = pending.Remaining };
      foreach (NetworkPendingDamage effect in pending.Triggered)
      {
        ApplySharedFixedDamage(state, pieceId, effect.SourceTeam, effect.Damage, applyCombatMitigation: true);
        if (FindPieceIndex(state.Pieces, pieceId) < 0) break;
      }
    }

    foreach (string pieceId in state.Pieces.Where(piece => piece.Team == team).Select(piece => piece.Id).ToArray())
    {
      int index = FindPieceIndex(state.Pieces, pieceId);
      if (index < 0) continue;
      NetworkPiece piece = state.Pieces[index];
      OwnerTurnState ownerTurn = AbilityStateRules.AdvanceOwnerTurn(
        piece.Type,
        piece.Health,
        piece.TurnsInCurrentForm
      );
      if (ownerTurn.RemovePiece)
      {
        HandleSharedPieceDestroyed(state, piece, null);
        continue;
      }

      state.Pieces[index] = piece with
      {
        Type = ownerTurn.ResultingType,
        Health = ownerTurn.ResultingHealth,
        TurnsInCurrentForm = ownerTurn.TurnsInCurrentForm,
        HasMovedThisTurn = false,
        HasAttackedThisTurn = false,
        AttacksThisTurn = 0,
        CavalierFollowUpMoveAvailable = false,
        EngineerBuildsThisTurn = 0,
        CannotContributeToConquestThisTurn = false
      };
    }
  }

  private static void SpendSharedAction(CpuMutableGameState state, NetworkTeam team)
  {
    if (!Globals.ActionLimitsEnabled)
    {
      return;
    }

    CpuTeamState current = state.Teams[team];
    state.Teams[team] = current with { ActionsRemaining = current.ActionsRemaining - 1 };
    if (state.Teams[team].ActionsRemaining <= 0)
    {
      CompleteSharedTurn(state, team);
    }
  }

  private static void CompleteSharedTurn(CpuMutableGameState state, NetworkTeam team)
  {
    ApplyEndOfTurnObjectives(state, team);
    if (state.Winner is not null)
    {
      return;
    }

    state.Teams[team] = state.Teams[team] with { ActionsRemaining = state.Teams[team].ActionLimit };
    state.CurrentTurn = TeamRules.GetNextTeam(team, state.Source.Configuration.PlayerCount);
    state.TurnNumber++;
    ApplyScenarioReinforcements(state);
    ApplyScenarioTurnLimit(state);
    if (state.Winner is not null || state.Source.Scenario?.IsTerminal(state.Freeze()) == true)
    {
      return;
    }

    ApplySharedTurnEconomy(state, state.CurrentTurn);
    if (state.Winner is null)
    {
      ResetSharedTurnActions(state, state.CurrentTurn);
    }
  }
}
