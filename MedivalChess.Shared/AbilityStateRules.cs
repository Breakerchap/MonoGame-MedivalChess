namespace MedivalChess.Shared;

public enum LethalAbilityOutcomeKind
{
  Die,
  Survive,
  Transform
}

public readonly record struct LethalAbilityOutcome(
  LethalAbilityOutcomeKind Kind,
  string ResultingType,
  int ResultingHealth,
  bool HasRevived
);

public readonly record struct AttackTurnState(int AttacksThisTurn, bool HasAttackedThisTurn);

public readonly record struct OwnerTurnState(
  string ResultingType,
  int ResultingHealth,
  int TurnsInCurrentForm,
  bool RemovePiece
);

public readonly record struct TankAttackDecision(
  bool MayFire,
  int FacingX,
  int FacingY
);

/// <summary>
/// Pure state transitions for unit abilities. These rules intentionally know nothing about MonoGame,
/// CPU search, SignalR, rendering, or board storage, so every runtime can apply the same result.
/// </summary>
public static class AbilityStateRules
{
  public static LethalAbilityOutcome ResolveLethalDamage(string unitType, bool hasRevived)
  {
    if ((unitType is nameof(PieceType.Shieldbearer) or nameof(PieceType.Spartan)) && !hasRevived)
    {
      return new(
        LethalAbilityOutcomeKind.Survive,
        unitType,
        AbilityRules.ShieldbearerReviveHealth,
        true
      );
    }

    if (unitType == nameof(PieceType.Emperor) && !hasRevived)
    {
      UnitRule terracotta = UnitRules.GetRequired(nameof(PieceType.TerracottaWarrior));
      return new(
        LethalAbilityOutcomeKind.Transform,
        terracotta.Type,
        terracotta.Health,
        true
      );
    }

    if (unitType == nameof(PieceType.Zombie))
    {
      UnitRule flesh = UnitRules.GetRequired(nameof(PieceType.Flesh));
      return new(
        LethalAbilityOutcomeKind.Transform,
        flesh.Type,
        flesh.Health,
        hasRevived
      );
    }

    return new(LethalAbilityOutcomeKind.Die, unitType, 0, hasRevived);
  }

  public static AttackTurnState RecordAttack(string unitType, int previousAttacksThisTurn)
  {
    int maximum = AbilityRules.MaximumAttacksPerTurn(unitType);
    int attacks = Math.Min(maximum, Math.Max(0, previousAttacksThisTurn) + 1);
    return new(attacks, attacks >= maximum);
  }

  public static OwnerTurnState AdvanceOwnerTurn(
    string unitType,
    int currentHealth,
    int turnsInCurrentForm
  )
  {
    if (unitType == nameof(PieceType.Flesh))
    {
      UnitRule zombie = UnitRules.GetRequired(nameof(PieceType.Zombie));
      return new(zombie.Type, zombie.Health, 0, false);
    }

    int nextTurns = Math.Max(0, turnsInCurrentForm) + 1;
    if (unitType == nameof(PieceType.Ghoul))
    {
      return new(unitType, currentHealth, nextTurns, nextTurns >= AbilityRules.GhoulLifetimeTurns);
    }

    if (unitType == nameof(PieceType.Tumbleweed))
    {
      return new(unitType, currentHealth, nextTurns, nextTurns >= AbilityRules.TumbleweedLifetimeRounds);
    }

    return new(unitType, currentHealth, turnsInCurrentForm, false);
  }

  public static (int x, int y) GetFacing(NetworkTeam team, int facingX, int facingY)
  {
    if (facingX != 0 || facingY != 0)
    {
      return (Math.Sign(facingX), Math.Sign(facingY));
    }

    return TeamRules.GetForwardDirection(team);
  }

  public static TankAttackDecision ResolveTankAttackAttempt(
    NetworkTeam team,
    int facingX,
    int facingY,
    (int x, int y) attackerPosition,
    (int x, int y) targetPosition
  )
  {
    (int x, int y) facing = GetFacing(team, facingX, facingY);
    (int x, int y) desired = AbilityRules.DirectionToward(attackerPosition, targetPosition);
    if (desired == (0, 0))
    {
      return new(false, facing.x, facing.y);
    }

    return desired == facing
      ? new(true, facing.x, facing.y)
      : new(false, desired.x, desired.y);
  }

  public static IReadOnlyList<NetworkPendingDamage> AddDragonbornBurn(
    IReadOnlyList<NetworkPendingDamage>? existing,
    NetworkTeam triggerTeam,
    NetworkTeam sourceTeam
  )
  {
    List<NetworkPendingDamage> result = existing is null ? [] : [.. existing];
    result.Add(new NetworkPendingDamage(triggerTeam, sourceTeam, AbilityRules.DragonbornBurnDamage));
    return result;
  }

  public static (IReadOnlyList<NetworkPendingDamage> Triggered, IReadOnlyList<NetworkPendingDamage> Remaining)
    SplitPendingDamageForTurn(IReadOnlyList<NetworkPendingDamage>? pending, NetworkTeam activeTeam)
  {
    if (pending is null || pending.Count == 0)
    {
      return (Array.Empty<NetworkPendingDamage>(), Array.Empty<NetworkPendingDamage>());
    }

    NetworkPendingDamage[] triggered = pending.Where(effect => effect.TriggerTeam == activeTeam).ToArray();
    NetworkPendingDamage[] remaining = pending.Where(effect => effect.TriggerTeam != activeTeam).ToArray();
    return (triggered, remaining);
  }
}
