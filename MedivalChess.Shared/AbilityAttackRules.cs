namespace MedivalChess.Shared;

public enum AbilityDamageMode
{
  NormalAttack,
  Fixed
}

public readonly record struct AbilityUnitSnapshot(
  string Id,
  string Type,
  NetworkTeam Team,
  int X,
  int Y,
  int Width,
  int Height
);

public readonly record struct AbilityDamageInstruction(
  string TargetId,
  AbilityDamageMode Mode,
  int FixedDamage = 0
);

public sealed record AbilityAttackPlan(
  IReadOnlyList<AbilityDamageInstruction> Damage,
  bool SelfDestructAfterAttack = false,
  int HealAttacker = 0
);

/// <summary>
/// Shared target selection for attack-triggered abilities. The runtimes remain responsible for
/// applying their normal damage/kill pipeline, but they do not decide which units an ability hits.
/// </summary>
public static class AbilityAttackRules
{
  public static AbilityUnitSnapshot Snapshot(NetworkPiece piece)
  {
    UnitRule rule = UnitRules.GetRequired(piece.Type);
    return new(piece.Id, piece.Type, piece.Team, piece.X, piece.Y, rule.Width, rule.Height);
  }

  public static AbilityAttackPlan BuildAttackPlan(
    AbilityUnitSnapshot attacker,
    AbilityUnitSnapshot selectedTarget,
    IReadOnlyList<AbilityUnitSnapshot> units
  )
  {
    List<AbilityDamageInstruction> damage = [];
    AddUnique(damage, new(selectedTarget.Id, AbilityDamageMode.NormalAttack));

    switch (attacker.Type)
    {
      case nameof(PieceType.Bombard):
        AddSquareSplash(
          damage,
          selectedTarget,
          units,
          attacker.Id,
          AbilityRules.BombardSplashDamage,
          includeSelectedTarget: false,
          enemiesOnly: false
        );
        break;

      case nameof(PieceType.Wizard):
        AddSquareAreaNormalDamage(
          damage,
          selectedTarget,
          units,
          attacker.Id,
          includeSelectedTarget: false,
          enemiesOnly: false
        );
        break;

      case nameof(PieceType.Dragon):
      case nameof(PieceType.Orc):
        AddAllUnitsInAttackRange(damage, attacker, units);
        break;

      case nameof(PieceType.Zeus):
        AddZeusChain(damage, selectedTarget, units, attacker.Team);
        break;
    }

    return new AbilityAttackPlan(
      damage,
      SelfDestructAfterAttack: attacker.Type == nameof(PieceType.Terrorist),
      HealAttacker: attacker.Type == nameof(PieceType.Vampire) ? AbilityRules.VampireHealing : 0
    );
  }

  /// <summary>Returns the Terrorist's death explosion. It damages every unit in its attack range.</summary>
  public static IReadOnlyList<AbilityDamageInstruction> BuildDeathExplosion(
    AbilityUnitSnapshot destroyed,
    IReadOnlyList<AbilityUnitSnapshot> units
  )
  {
    if (destroyed.Type != nameof(PieceType.Terrorist))
    {
      return Array.Empty<AbilityDamageInstruction>();
    }

    List<AbilityDamageInstruction> damage = [];
    if (!UnitRules.TryGet(destroyed.Type, out UnitRule rule))
    {
      return damage;
    }

    foreach (AbilityUnitSnapshot candidate in units)
    {
      if (candidate.Id == destroyed.Id || !UnitRules.TryGet(candidate.Type, out UnitRule candidateRule))
      {
        continue;
      }

      if (UnitRules.CanAttack(
        rule,
        destroyed.X,
        destroyed.Y,
        destroyed.Team,
        candidateRule,
        candidate.X,
        candidate.Y))
      {
        AddUnique(damage, new(candidate.Id, AbilityDamageMode.Fixed, rule.Attack));
      }
    }

    return damage;
  }

  private static void AddSquareSplash(
    List<AbilityDamageInstruction> damage,
    AbilityUnitSnapshot centre,
    IReadOnlyList<AbilityUnitSnapshot> units,
    string attackerId,
    int fixedDamage,
    bool includeSelectedTarget,
    bool enemiesOnly
  )
  {
    UnitRule centreRule = UnitRules.GetRequired(centre.Type);
    foreach (AbilityUnitSnapshot candidate in units)
    {
      if (candidate.Id == attackerId || (!includeSelectedTarget && candidate.Id == centre.Id) ||
          (enemiesOnly && candidate.Team == centre.Team) ||
          !UnitRules.TryGet(candidate.Type, out UnitRule candidateRule))
      {
        continue;
      }

      if (AbilityRules.IsWithinSquareRadius(
        centreRule,
        (centre.X, centre.Y),
        candidateRule,
        (candidate.X, candidate.Y),
        1))
      {
        AddUnique(damage, new(candidate.Id, AbilityDamageMode.Fixed, fixedDamage));
      }
    }
  }

  private static void AddSquareAreaNormalDamage(
    List<AbilityDamageInstruction> damage,
    AbilityUnitSnapshot centre,
    IReadOnlyList<AbilityUnitSnapshot> units,
    string attackerId,
    bool includeSelectedTarget,
    bool enemiesOnly
  )
  {
    UnitRule centreRule = UnitRules.GetRequired(centre.Type);
    foreach (AbilityUnitSnapshot candidate in units)
    {
      if (candidate.Id == attackerId || (!includeSelectedTarget && candidate.Id == centre.Id) ||
          (enemiesOnly && candidate.Team == centre.Team) ||
          !UnitRules.TryGet(candidate.Type, out UnitRule candidateRule))
      {
        continue;
      }

      if (AbilityRules.IsWithinSquareRadius(
        centreRule,
        (centre.X, centre.Y),
        candidateRule,
        (candidate.X, candidate.Y),
        1))
      {
        AddUnique(damage, new(candidate.Id, AbilityDamageMode.NormalAttack));
      }
    }
  }

  private static void AddAllUnitsInAttackRange(
    List<AbilityDamageInstruction> damage,
    AbilityUnitSnapshot attacker,
    IReadOnlyList<AbilityUnitSnapshot> units
  )
  {
    UnitRule attackerRule = UnitRules.GetRequired(attacker.Type);
    foreach (AbilityUnitSnapshot candidate in units)
    {
      if (candidate.Id == attacker.Id || !UnitRules.TryGet(candidate.Type, out UnitRule candidateRule))
      {
        continue;
      }

      if (UnitRules.CanAttack(
        attackerRule,
        attacker.X,
        attacker.Y,
        attacker.Team,
        candidateRule,
        candidate.X,
        candidate.Y))
      {
        AddUnique(damage, new(candidate.Id, AbilityDamageMode.NormalAttack));
      }
    }
  }

  private static void AddZeusChain(
    List<AbilityDamageInstruction> damage,
    AbilityUnitSnapshot selectedTarget,
    IReadOnlyList<AbilityUnitSnapshot> units,
    NetworkTeam attackerTeam
  )
  {
    Dictionary<string, AbilityUnitSnapshot> eligible = units
      .Where(unit => unit.Team != attackerTeam && unit.Id != selectedTarget.Id)
      .ToDictionary(unit => unit.Id, StringComparer.Ordinal);
    Queue<AbilityUnitSnapshot> frontier = new();
    HashSet<string> visited = new(StringComparer.Ordinal) { selectedTarget.Id };
    frontier.Enqueue(selectedTarget);

    while (frontier.TryDequeue(out AbilityUnitSnapshot current))
    {
      UnitRule currentRule = UnitRules.GetRequired(current.Type);
      foreach (AbilityUnitSnapshot candidate in eligible.Values)
      {
        if (visited.Contains(candidate.Id)) continue;
        UnitRule candidateRule = UnitRules.GetRequired(candidate.Type);
        if (!AbilityRules.AreAdjacent(
          currentRule,
          (current.X, current.Y),
          candidateRule,
          (candidate.X, candidate.Y)))
        {
          continue;
        }

        visited.Add(candidate.Id);
        frontier.Enqueue(candidate);
        AddUnique(damage, new(candidate.Id, AbilityDamageMode.Fixed, AbilityRules.ZeusChainDamage));
      }
    }
  }

  private static void AddUnique(List<AbilityDamageInstruction> damage, AbilityDamageInstruction instruction)
  {
    if (damage.Any(existing => string.Equals(existing.TargetId, instruction.TargetId, StringComparison.Ordinal)))
    {
      return;
    }
    damage.Add(instruction);
  }
}
