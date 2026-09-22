namespace MedivalChess.Shared;

public sealed record UnitTurnSnapshot(int X, int Y, int Health);

public sealed record AbilitySelection(string? TargetId, int X, int Y);

/// <summary>
/// Persistent and per-owner-turn state used by codex-defined unit abilities. Keeping this state in
/// the shared network model prevents local play, authoritative online play, and CPU simulation from
/// silently implementing different versions of the same unit.
/// </summary>
public sealed record UnitAbilityState
{
  public int MovesThisTurn { get; init; }
  public bool UsedThisTurn { get; init; }
  public int CooldownOwnerTurns { get; init; }
  public bool ReloadRequired { get; init; }
  public bool ReloadedThisTurn { get; init; }
  public bool Consumed { get; init; }
  public bool Settled { get; init; }
  public bool RefreshedByWarDrumThisTurn { get; init; }
  public bool CannotMoveThisTurn { get; init; }
  public bool CannotActThisTurn { get; init; }
  public int DisabledOwnerTurnsRemaining { get; init; }
  public string? LinkedPieceId { get; init; }
  public string? SelectedTargetId { get; init; }
  public string? PetrifiedById { get; init; }
  public string? OdinProtectedById { get; init; }
  public bool OdinProtectionAvailable { get; init; }
  public string? BountyTargetId { get; init; }
  public IReadOnlyList<string> TargetIdsThisTurn { get; init; } = Array.Empty<string>();
  public string? PendingAbility { get; init; }
  public IReadOnlyList<AbilitySelection> PendingSelections { get; init; } = Array.Empty<AbilitySelection>();
  public bool Upgraded { get; init; }
  public int AttackBonus { get; init; }
  public int MaxHealthBonus { get; init; }
  public int MoveBonus { get; init; }
  public int AttackRangeBonus { get; init; }
  public UnitTurnSnapshot? CurrentOwnerTurnStart { get; init; }
  public UnitTurnSnapshot? PreviousOwnerTurnStart { get; init; }
  public bool PendingRespawn { get; init; }
  public int LinkedDeaths { get; init; }
  public int VariableCostValue { get; init; }
  public int SkipMovementOwnerTurns { get; init; }
}

public static class AdvancedAbilityRules
{
  public const int BaronAttackBonus = 10;
  public const int BaronDamageReduction = 10;
  public const int HarvesterGold = 15;
  public const int PalaceIncome = 10;
  public const int SummonedGolemUpkeep = 30;
  public const int HiredGunUpkeep = 20;
  public const int ContractDemonRoyalHealthUpkeep = 5;
  public const int PhoenixFireHealthCost = 5;
  public const int FireDamage = 15;
  public const int BrambleDamage = 10;
  public const int PickpocketGold = 20;
  public const int StagecoachTrampleDamage = 25;
  public const int CactusReflectionDivisor = 2;
  public const int FafnirTransformCost = 125;
  public const int ImpHealthDrain = 5;
  public const int ImpAttackBonus = 15;
  public const int ImpMoveBonus = 1;
  public const int RuneAttackBonus = 10;
  public const int RuneMoveBonus = 1;
  public const int RuneDamageReduction = 5;
  public const int RuneRangeBonus = 2;
  public const int ThunderstormDamage = 15;
  public const int CommandCentreUpgradeCost = 25;
  public const int CommandCentreAttackBonus = 10;
  public const int CommandCentreHealthBonus = 20;
  public const int CommandCentreMoveBonus = 1;
  public const int LichDeathDamage = 5;
  public const int GangLeaderCooldownTurns = 3;
  public const int HackerCooldownTurns = 3;
  public const int SatanCooldownTurns = 2;
  public const int OdinCooldownTurns = 4;
  public const int ChronosCooldownTurns = 5;
  public const int SniperCooldownTurns = 2;

  public static bool IsValidQilinCost(int cost) => cost is >= 40 and <= 160 && cost % 20 == 0;
  public static int GetQilinAttack(int cost) =>
    IsValidQilinCost(cost) ? 10 + cost / 4 : throw new ArgumentOutOfRangeException(nameof(cost));
  public static int GetQilinHealth(int cost) =>
    IsValidQilinCost(cost) ? 20 + cost / 2 : throw new ArgumentOutOfRangeException(nameof(cost));

  public static int MaximumMovesPerOwnerTurn(string unitType) =>
    unitType == nameof(PieceType.Hermes) ? 2 : 1;

  public static bool CanMove(string unitType, UnitAbilityState? state, bool legacyHasMoved)
  {
    state ??= new();
    if (state.PetrifiedById is not null || state.CannotMoveThisTurn || state.CannotActThisTurn || state.SkipMovementOwnerTurns > 0)
    {
      return false;
    }
    if ((unitType == nameof(PieceType.WillOWisp) && state.Settled) ||
        unitType == nameof(PieceType.Mimic))
    {
      return false;
    }

    int moves = Math.Max(state.MovesThisTurn, legacyHasMoved ? 1 : 0);
    return moves < MaximumMovesPerOwnerTurn(unitType);
  }

  public static bool CanUseMovementAbility(UnitAbilityState? state, bool legacyHasMoved)
  {
    state ??= new();
    if (state.PetrifiedById is not null || state.CannotMoveThisTurn ||
        state.CannotActThisTurn || state.SkipMovementOwnerTurns > 0)
    {
      return false;
    }

    return Math.Max(state.MovesThisTurn, legacyHasMoved ? 1 : 0) < 1;
  }

  public static bool IsLandingAttackUnit(string unitType) =>
    unitType is nameof(PieceType.Buffalo) or nameof(PieceType.ArmouredTruck);

  public static int GetLandingAttackPushDistance(string unitType) => unitType switch
  {
    nameof(PieceType.Buffalo) => 1,
    nameof(PieceType.ArmouredTruck) => 2,
    _ => 0
  };

  public static bool LandingAttackConsumesNormalAttack(string unitType) =>
    unitType == nameof(PieceType.Buffalo);

  public static bool CanUseSpecialAbility(UnitAbilityState? state)
  {
    state ??= new();
    return state.PetrifiedById is null && !state.CannotActThisTurn && state.DisabledOwnerTurnsRemaining <= 0;
  }

  public static bool CanAttack(string unitType, UnitAbilityState? state, bool legacyHasAttacked, string? targetId = null)
  {
    state ??= new();
    if (state.PetrifiedById is not null || state.CannotActThisTurn)
    {
      return false;
    }
    if (unitType == nameof(PieceType.Sniper) && state.CooldownOwnerTurns > 0)
    {
      return false;
    }
    if (unitType == nameof(PieceType.Hwacha) &&
        (state.ReloadRequired || state.ReloadedThisTurn))
    {
      return false;
    }
    if (unitType == nameof(PieceType.MissileSilo) && state.Consumed)
    {
      return false;
    }
    if (unitType == nameof(PieceType.BountyHunter) && state.BountyTargetId is not null &&
        targetId is not null && !string.Equals(state.BountyTargetId, targetId, StringComparison.Ordinal))
    {
      return false;
    }
    if (unitType == nameof(PieceType.Seraph))
    {
      if (targetId is not null && state.TargetIdsThisTurn.Contains(targetId, StringComparer.Ordinal))
      {
        return false;
      }
      return state.TargetIdsThisTurn.Count < 3;
    }

    return !legacyHasAttacked;
  }

  public static UnitAbilityState RecordMove(UnitAbilityState? state)
  {
    state ??= new();
    return state with { MovesThisTurn = state.MovesThisTurn + 1 };
  }

  public static UnitAbilityState RecordAttack(string unitType, UnitAbilityState? state, string? targetId)
  {
    state ??= new();
    IReadOnlyList<string> targets = state.TargetIdsThisTurn;
    if (unitType == nameof(PieceType.Seraph) && targetId is not null &&
        !targets.Contains(targetId, StringComparer.Ordinal))
    {
      targets = [.. targets, targetId];
    }

    return state with
    {
      TargetIdsThisTurn = targets,
      SelectedTargetId = unitType is nameof(PieceType.Duelist) or nameof(PieceType.Succubus) && targetId is not null
        ? targetId
        : state.SelectedTargetId,
      ReloadRequired = unitType == nameof(PieceType.Hwacha) || state.ReloadRequired,
      CooldownOwnerTurns = unitType == nameof(PieceType.Sniper)
        ? SniperCooldownTurns
        : state.CooldownOwnerTurns,
      Consumed = unitType == nameof(PieceType.MissileSilo) || state.Consumed
    };
  }

  public static UnitAbilityState StartOwnerTurn(UnitAbilityState? state, int x, int y, int health)
  {
    state ??= new();
    return state with
    {
      MovesThisTurn = 0,
      UsedThisTurn = false,
      ReloadedThisTurn = false,
      RefreshedByWarDrumThisTurn = false,
      CannotMoveThisTurn = false,
      CannotActThisTurn = false,
      TargetIdsThisTurn = Array.Empty<string>(),
      PendingAbility = null,
      PendingSelections = Array.Empty<AbilitySelection>(),
      CooldownOwnerTurns = Math.Max(0, state.CooldownOwnerTurns - 1),
      OdinProtectedById = null,
      OdinProtectionAvailable = false,
      PreviousOwnerTurnStart = state.CurrentOwnerTurnStart,
      CurrentOwnerTurnStart = new UnitTurnSnapshot(x, y, health),
      SkipMovementOwnerTurns = Math.Max(0, state.SkipMovementOwnerTurns - 1)
    };
  }

  public static UnitAbilityState EndOwnerTurn(UnitAbilityState? state)
  {
    state ??= new();
    return state with
    {
      DisabledOwnerTurnsRemaining = Math.Max(0, state.DisabledOwnerTurnsRemaining - 1)
    };
  }

  public static bool ShouldDieAtEndOwnerTurn(string unitType, int attacksThisTurn) =>
    unitType == nameof(PieceType.Wendigo) && attacksThisTurn == 0;

  public static bool CanTargetFriendlyWithNormalAttack(string unitType) =>
    unitType == nameof(PieceType.Wendigo);

  public static bool CanTakeDirectDamage(string unitType, UnitAbilityState? state) =>
    unitType is not (nameof(PieceType.Phylactery) or nameof(PieceType.Helicopter) or nameof(PieceType.Shadow)) &&
    state?.PetrifiedById is null;

  public static bool CanMedusaPetrify(string targetType) =>
    UnitRules.TryGet(targetType, out UnitRule rule) && rule.Category != RuleCategory.Royal;

  public static bool IsPetrificationMaintained(
    UnitRule medusa,
    (int x, int y) medusaPosition,
    UnitRule target,
    (int x, int y) targetPosition) =>
    AbilityRules.IsWithinSquareRadius(medusa, medusaPosition, target, targetPosition, 5);

  public static bool MayAlsoPlaceInNoMansLand(string unitType) =>
    unitType is nameof(PieceType.ContractDemon) or nameof(PieceType.Helicopter);

  public static bool IsAttachedUnitUntargetable(string unitType) =>
    unitType == nameof(PieceType.Shadow);

  public static bool CanAttachedUnitAttack(string unitType) =>
    unitType is nameof(PieceType.Shadow) or nameof(PieceType.Succubus);

  public static bool CanSuccubusAttach(UnitAbilityState? state, string targetId) =>
    !string.IsNullOrWhiteSpace(targetId) &&
    string.Equals(state?.SelectedTargetId, targetId, StringComparison.Ordinal);

  public static bool IsBaronSelectedTarget(
    IEnumerable<(string type, NetworkTeam team, string? selectedTargetId)> pieces,
    string targetId,
    NetworkTeam team
  ) => pieces.Any(piece =>
    piece.team == team && piece.type == nameof(PieceType.Baron) &&
    string.Equals(piece.selectedTargetId, targetId, StringComparison.Ordinal));

  public static int ApplyBaronOutgoingBonus(int damage, bool selected) =>
    selected ? damage + BaronAttackBonus : damage;

  public static int ApplyBaronIncomingReduction(int damage, bool selected) =>
    selected ? Math.Max(0, damage - BaronDamageReduction) : damage;

  public static RuleShape ImproveMusePattern(RuleShape pattern) => pattern switch
  {
    RuleShape.Line or RuleShape.Diagonal => RuleShape.Straight,
    RuleShape.Straight => RuleShape.Circle,
    RuleShape.Circle => RuleShape.Any,
    _ => pattern
  };

  public static UnitRule ApplyAttachmentBonuses(
    UnitRule rule,
    bool hasImp,
    int museCount
  )
  {
    if (hasImp)
    {
      rule = rule with
      {
        MoveRange = rule.MoveRange + ImpMoveBonus,
        Attack = rule.Attack + ImpAttackBonus
      };
    }

    for (int i = 0; i < Math.Max(0, museCount); i++)
    {
      rule = rule with
      {
        MovePattern = ImproveMusePattern(rule.MovePattern),
        AttackPattern = ImproveMusePattern(rule.AttackPattern)
      };
    }
    return rule;
  }

  public static bool IsUpkeepFireUnit(string unitType) =>
    unitType is nameof(PieceType.Mercenary) or nameof(PieceType.SummonedGolem) or
      nameof(PieceType.HiredGun) or nameof(PieceType.ContractDemon);

  public static int GetGoldUpkeep(string unitType) => unitType switch
  {
    nameof(PieceType.Mercenary) => AbilityRules.MercenaryPayroll,
    nameof(PieceType.SummonedGolem) => SummonedGolemUpkeep,
    nameof(PieceType.HiredGun) => HiredGunUpkeep,
    nameof(PieceType.President) => AbilityRules.PresidentPayroll,
    _ => 0
  };

  public static bool IsImmediateGoldUpkeepUnit(string unitType) =>
    unitType is nameof(PieceType.SummonedGolem) or nameof(PieceType.HiredGun);

  public static int GetImmediateGoldUpkeep(string unitType) => unitType switch
  {
    nameof(PieceType.SummonedGolem) => SummonedGolemUpkeep,
    nameof(PieceType.HiredGun) => HiredGunUpkeep,
    _ => 0
  };

  public static bool CanUseOncePerOwnerTurn(UnitAbilityState? state) =>
    CanUseSpecialAbility(state) && !(state?.UsedThisTurn ?? false);

  public static UnitAbilityState RecordOncePerOwnerTurnUse(UnitAbilityState? state)
  {
    state ??= new();
    return state with { UsedThisTurn = true };
  }

  public static UnitAbilityState StartCooldown(UnitAbilityState? state, int ownerTurns)
  {
    state ??= new();
    return state with
    {
      UsedThisTurn = true,
      CooldownOwnerTurns = Math.Max(state.CooldownOwnerTurns, Math.Max(0, ownerTurns))
    };
  }

  public static UnitAbilityState ReloadHwacha(UnitAbilityState? state)
  {
    state ??= new();
    return state with { ReloadRequired = false, ReloadedThisTurn = true };
  }

  public static bool CanReloadHwacha(UnitAbilityState? state) =>
    CanUseSpecialAbility(state) && (state?.ReloadRequired ?? false) && !(state?.ReloadedThisTurn ?? false);

  public static UnitAbilityState RefreshByWarDrum(UnitAbilityState? state)
  {
    state ??= new();
    return state with
    {
      MovesThisTurn = 0,
      RefreshedByWarDrumThisTurn = true,
      CannotMoveThisTurn = false
    };
  }

  public static bool CanBeRefreshedByWarDrum(UnitAbilityState? state, bool hasMoved) =>
    hasMoved && !(state?.RefreshedByWarDrumThisTurn ?? false) && !(state?.CannotActThisTurn ?? false);

  public static UnitAbilityState SelectTarget(UnitAbilityState? state, string? targetId)
  {
    state ??= new();
    return state with { SelectedTargetId = targetId, UsedThisTurn = true };
  }

  public static UnitAbilityState SetSettled(UnitAbilityState? state)
  {
    state ??= new();
    return state with { Settled = true, UsedThisTurn = true };
  }

  public static UnitAbilityState SetBountyTarget(UnitAbilityState? state, string? targetId)
  {
    state ??= new();
    return state with { BountyTargetId = targetId };
  }

  public static UnitAbilityState DisableAbilities(UnitAbilityState? state, int ownerTurns)
  {
    state ??= new();
    return state with { DisabledOwnerTurnsRemaining = Math.Max(state.DisabledOwnerTurnsRemaining, ownerTurns) };
  }

  public static UnitAbilityState ApplyCommandCentreUpgrade(UnitAbilityState? state, string upgrade)
  {
    state ??= new();
    if (state.Upgraded)
    {
      return state;
    }

    return upgrade.ToLowerInvariant() switch
    {
      "attack" => state with { Upgraded = true, AttackBonus = state.AttackBonus + CommandCentreAttackBonus },
      "health" => state with { Upgraded = true, MaxHealthBonus = state.MaxHealthBonus + CommandCentreHealthBonus },
      "move" => state with { Upgraded = true, MoveBonus = state.MoveBonus + CommandCentreMoveBonus },
      _ => throw new ArgumentOutOfRangeException(nameof(upgrade), upgrade, "Unknown Command Centre upgrade.")
    };
  }

  public static UnitAbilityState SetPetrified(UnitAbilityState? state, string? medusaId)
  {
    state ??= new();
    return state with { PetrifiedById = medusaId };
  }

  public static UnitAbilityState ProtectWithOdin(UnitAbilityState? state, string odinId)
  {
    state ??= new();
    return state with { OdinProtectedById = odinId, OdinProtectionAvailable = true };
  }

  public static UnitAbilityState ConsumeOdinProtection(UnitAbilityState? state)
  {
    state ??= new();
    return state with { OdinProtectionAvailable = false };
  }

  public static UnitAbilityState AddPendingSelection(
    UnitAbilityState? state,
    string ability,
    AbilitySelection selection
  )
  {
    state ??= new();
    IReadOnlyList<AbilitySelection> existing = string.Equals(state.PendingAbility, ability, StringComparison.Ordinal)
      ? state.PendingSelections
      : Array.Empty<AbilitySelection>();
    return state with
    {
      PendingAbility = ability,
      PendingSelections = [.. existing, selection]
    };
  }

  public static UnitAbilityState ClearPendingSelections(UnitAbilityState? state)
  {
    state ??= new();
    return state with { PendingAbility = null, PendingSelections = Array.Empty<AbilitySelection>() };
  }

  public static UnitAbilityState SetLinkedPiece(UnitAbilityState? state, string? pieceId)
  {
    state ??= new();
    return state with { LinkedPieceId = pieceId };
  }

  public static int ReflectCactusDamage(int incomingDamage) => Math.Max(0, incomingDamage / CactusReflectionDivisor);

  public static int GetRaiderKillReward(int defeatedBaseCost) =>
    CombatRules.RoundCurrencyToNearestFive(Math.Max(0, defeatedBaseCost) * 0.5f);

  public static UnitRule ApplyPersistentBonuses(UnitRule rule, UnitAbilityState? state)
  {
    state ??= new();
    int baseAttack = rule.Type == nameof(PieceType.Qilin) && IsValidQilinCost(state.VariableCostValue)
      ? GetQilinAttack(state.VariableCostValue)
      : rule.Attack;
    int baseHealth = rule.Type == nameof(PieceType.Qilin) && IsValidQilinCost(state.VariableCostValue)
      ? GetQilinHealth(state.VariableCostValue)
      : rule.Health;
    return rule with
    {
      Attack = baseAttack + GetEffectiveAttackBonus(state),
      Health = baseHealth,
      MoveRange = rule.MoveRange + GetEffectiveMoveBonus(state),
      AttackRange = rule.AttackRange + GetEffectiveRangeBonus(state)
    };
  }

  public static int GetEffectiveMaximumHealth(UnitRule rule, UnitAbilityState? state)
  {
    state ??= new();
    int baseHealth = rule.Type == nameof(PieceType.Qilin) && IsValidQilinCost(state.VariableCostValue)
      ? GetQilinHealth(state.VariableCostValue)
      : rule.Health;
    return baseHealth + GetEffectiveMaxHealthBonus(state);
  }

  public static int GetEffectiveAttackBonus(UnitAbilityState? state) => Math.Max(0, state?.AttackBonus ?? 0);
  public static int GetEffectiveMoveBonus(UnitAbilityState? state) => Math.Max(0, state?.MoveBonus ?? 0);
  public static int GetEffectiveRangeBonus(UnitAbilityState? state) => Math.Max(0, state?.AttackRangeBonus ?? 0);
  public static int GetEffectiveMaxHealthBonus(UnitAbilityState? state) => Math.Max(0, state?.MaxHealthBonus ?? 0);
}
