namespace MedivalChess.Shared;

/// <summary>
/// Data-only board entities created by unit abilities. Runtime layers own placement, rendering,
/// persistence and target selection; their rules use this common representation rather than
/// inventing a bespoke unit type for every temporary effect.
/// </summary>
public enum AbilityEntityKind
{
  PoisonCloud, Fire, Bramble, Portal, Seal, StoneWall, Gatehouse, Gate, Bridge, Watchtower,
  RuneAttack, RuneMovement, RuneHealth, RuneRange, Snare, Tnt, Prison, Thunderstorm, Road, Barricade
}

public sealed record AbilityEntity(
  string Id,
  AbilityEntityKind Kind,
  NetworkTeam Owner,
  int X,
  int Y,
  int Health = 0,
  string? LinkedEntityId = null,
  int ExpiresOnOwnerTurn = 0,
  string? SourcePieceId = null
);

public sealed record AbilityEntityDefinition(
  AbilityEntityKind Kind,
  int Health,
  bool BlocksMovement,
  bool BlocksAttacks,
  int EnterDamage = 0,
  int StartOfOwnerTurnDamage = 0,
  int Radius = 0,
  int LifetimeOwnerTurns = 0,
  bool BlocksLanding = false
);

public static class AbilityEntityRules
{
  private static readonly IReadOnlyDictionary<AbilityEntityKind, AbilityEntityDefinition> Definitions =
    new Dictionary<AbilityEntityKind, AbilityEntityDefinition>
    {
      [AbilityEntityKind.PoisonCloud] = new(AbilityEntityKind.PoisonCloud, 0, false, false, StartOfOwnerTurnDamage: 15, Radius: 1),
      [AbilityEntityKind.Fire] = new(AbilityEntityKind.Fire, 0, false, false, EnterDamage: 15),
      [AbilityEntityKind.Bramble] = new(AbilityEntityKind.Bramble, 30, false, false, EnterDamage: 10, BlocksLanding: true),
      [AbilityEntityKind.Portal] = new(AbilityEntityKind.Portal, 0, false, false),
      [AbilityEntityKind.Seal] = new(AbilityEntityKind.Seal, 0, true, true, LifetimeOwnerTurns: 1),
      [AbilityEntityKind.StoneWall] = new(AbilityEntityKind.StoneWall, 50, true, true),
      [AbilityEntityKind.Gatehouse] = new(AbilityEntityKind.Gatehouse, 15, true, false),
      [AbilityEntityKind.Gate] = new(AbilityEntityKind.Gate, 30, true, false),
      [AbilityEntityKind.Bridge] = new(AbilityEntityKind.Bridge, 0, false, false),
      [AbilityEntityKind.Watchtower] = new(AbilityEntityKind.Watchtower, 15, false, false),
      [AbilityEntityKind.RuneAttack] = new(AbilityEntityKind.RuneAttack, 5, false, false),
      [AbilityEntityKind.RuneMovement] = new(AbilityEntityKind.RuneMovement, 5, false, false),
      [AbilityEntityKind.RuneHealth] = new(AbilityEntityKind.RuneHealth, 5, false, false),
      [AbilityEntityKind.RuneRange] = new(AbilityEntityKind.RuneRange, 5, false, false),
      [AbilityEntityKind.Snare] = new(AbilityEntityKind.Snare, 0, false, false),
      [AbilityEntityKind.Tnt] = new(AbilityEntityKind.Tnt, 0, false, false),
      [AbilityEntityKind.Prison] = new(AbilityEntityKind.Prison, 65, true, true),
      [AbilityEntityKind.Thunderstorm] = new(AbilityEntityKind.Thunderstorm, 0, false, false, EnterDamage: AdvancedAbilityRules.ThunderstormDamage),
      [AbilityEntityKind.Road] = new(AbilityEntityKind.Road, 0, false, false),
      [AbilityEntityKind.Barricade] = new(AbilityEntityKind.Barricade, AbilityRules.EngineerBarrierHealth, true, true)
    };

  public static AbilityEntityDefinition GetRequired(AbilityEntityKind kind) => Definitions[kind];
  public static IReadOnlyCollection<AbilityEntityDefinition> All => Definitions.Values.ToArray();

  public static bool IsAt(AbilityEntity entity, int x, int y) => entity.X == x && entity.Y == y;

  public static bool IsWithinRadius(AbilityEntity entity, int x, int y) =>
    Math.Max(Math.Abs(entity.X - x), Math.Abs(entity.Y - y)) <= GetRequired(entity.Kind).Radius;

  public static bool BlocksMovementFor(AbilityEntity entity, NetworkTeam mover) =>
    entity.Kind is AbilityEntityKind.Gatehouse or AbilityEntityKind.Gate
      ? entity.Owner != mover
      : GetRequired(entity.Kind).BlocksMovement;

  public static bool BlocksLandingFor(AbilityEntity entity, NetworkTeam mover) =>
    BlocksMovementFor(entity, mover) || GetRequired(entity.Kind).BlocksLanding;

  public static bool BlocksAttackFor(AbilityEntity entity, NetworkTeam attacker) =>
    entity.Kind is AbilityEntityKind.Gatehouse or AbilityEntityKind.Gate
      ? entity.Owner != attacker
      : GetRequired(entity.Kind).BlocksAttacks;

  public static bool IsRune(AbilityEntityKind kind) =>
    kind is AbilityEntityKind.RuneAttack or AbilityEntityKind.RuneMovement or
      AbilityEntityKind.RuneHealth or AbilityEntityKind.RuneRange;

  private static bool IsWithinAura(AbilityEntity entity, NetworkPiece unit, int radius)
  {
    UnitRule rule = UnitRules.GetRequired(unit.Type);
    for (int y = 0; y < rule.Height; y++)
    for (int x = 0; x < rule.Width; x++)
    {
      if (Math.Max(Math.Abs(entity.X - (unit.X + x)), Math.Abs(entity.Y - (unit.Y + y))) <= radius)
      {
        return true;
      }
    }
    return false;
  }

  private static bool OccupiesEntity(AbilityEntity entity, NetworkPiece unit) =>
    IsWithinAura(entity, unit, 0);

  public static int GetAttackBonus(IEnumerable<AbilityEntity> entities, NetworkPiece unit)
  {
    bool rune = entities.Any(entity => entity.Owner == unit.Team &&
      entity.Kind == AbilityEntityKind.RuneAttack && IsWithinAura(entity, unit, 1));
    return rune ? AdvancedAbilityRules.RuneAttackBonus : 0;
  }

  public static int GetMoveBonus(IEnumerable<AbilityEntity> entities, NetworkPiece unit)
  {
    bool rune = entities.Any(entity => entity.Owner == unit.Team &&
      entity.Kind == AbilityEntityKind.RuneMovement && IsWithinAura(entity, unit, 1));
    return rune ? AdvancedAbilityRules.RuneMoveBonus : 0;
  }

  public static int GetDamageReduction(IEnumerable<AbilityEntity> entities, NetworkPiece unit)
  {
    bool rune = entities.Any(entity => entity.Owner == unit.Team &&
      entity.Kind == AbilityEntityKind.RuneHealth && IsWithinAura(entity, unit, 1));
    return rune ? AdvancedAbilityRules.RuneDamageReduction : 0;
  }

  public static int GetAttackRangeBonus(IEnumerable<AbilityEntity> entities, NetworkPiece unit)
  {
    int bonus = 0;
    if (entities.Any(entity => entity.Owner == unit.Team &&
        entity.Kind == AbilityEntityKind.RuneRange && IsWithinAura(entity, unit, 1)))
    {
      bonus += AdvancedAbilityRules.RuneRangeBonus;
    }
    if (entities.Any(entity => entity.Owner == unit.Team &&
        entity.Kind == AbilityEntityKind.Watchtower && OccupiesEntity(entity, unit)))
    {
      bonus += 2;
    }
    return bonus;
  }

  public static UnitRule ApplyAuraBonuses(
    UnitRule rule,
    IEnumerable<AbilityEntity> entities,
    NetworkPiece unit)
  {
    return rule with
    {
      Attack = rule.Attack + GetAttackBonus(entities, unit),
      MoveRange = rule.MoveRange + GetMoveBonus(entities, unit),
      AttackRange = rule.AttackRange + GetAttackRangeBonus(entities, unit)
    };
  }

  public static AbilityEntity? GetLinkedPortal(IEnumerable<AbilityEntity> entities, AbilityEntity portal) =>
    portal.Kind == AbilityEntityKind.Portal && portal.LinkedEntityId is not null
      ? entities.FirstOrDefault(entity => entity.Id == portal.LinkedEntityId && entity.Kind == AbilityEntityKind.Portal)
      : null;
}
