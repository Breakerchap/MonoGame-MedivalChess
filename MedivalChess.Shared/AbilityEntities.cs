namespace MedivalChess.Shared;

/// <summary>
/// Data-only board entities created by unit abilities. Runtime layers own placement, rendering,
/// persistence and target selection; their rules use this common representation rather than
/// inventing a bespoke unit type for every temporary effect.
/// </summary>
public enum AbilityEntityKind
{
  PoisonCloud, Fire, Bramble, Portal, Seal, StoneWall, Gatehouse, Bridge, Watchtower,
  RuneAttack, RuneMovement, RuneHealth, RuneRange, Snare, Tnt, Prison, Road, Barricade
}

public sealed record AbilityEntity(
  string Id,
  AbilityEntityKind Kind,
  NetworkTeam Owner,
  int X,
  int Y,
  int Health = 0,
  string? LinkedEntityId = null,
  int ExpiresOnOwnerTurn = 0
);

public sealed record AbilityEntityDefinition(
  AbilityEntityKind Kind,
  int Health,
  bool BlocksMovement,
  bool BlocksAttacks,
  int EnterDamage = 0,
  int StartOfOwnerTurnDamage = 0,
  int Radius = 0,
  int LifetimeOwnerTurns = 0
);

public static class AbilityEntityRules
{
  private static readonly IReadOnlyDictionary<AbilityEntityKind, AbilityEntityDefinition> Definitions =
    new Dictionary<AbilityEntityKind, AbilityEntityDefinition>
    {
      [AbilityEntityKind.PoisonCloud] = new(AbilityEntityKind.PoisonCloud, 0, false, false, StartOfOwnerTurnDamage: 15, Radius: 1),
      [AbilityEntityKind.Fire] = new(AbilityEntityKind.Fire, 0, false, false, EnterDamage: 15, LifetimeOwnerTurns: 1),
      [AbilityEntityKind.Bramble] = new(AbilityEntityKind.Bramble, 30, true, false, EnterDamage: 10),
      [AbilityEntityKind.Portal] = new(AbilityEntityKind.Portal, 0, false, false),
      [AbilityEntityKind.Seal] = new(AbilityEntityKind.Seal, 0, true, true, LifetimeOwnerTurns: 1),
      [AbilityEntityKind.StoneWall] = new(AbilityEntityKind.StoneWall, 50, true, true),
      [AbilityEntityKind.Gatehouse] = new(AbilityEntityKind.Gatehouse, 15, true, false),
      [AbilityEntityKind.Bridge] = new(AbilityEntityKind.Bridge, 0, false, false),
      [AbilityEntityKind.Watchtower] = new(AbilityEntityKind.Watchtower, 15, false, false),
      [AbilityEntityKind.RuneAttack] = new(AbilityEntityKind.RuneAttack, 0, false, false),
      [AbilityEntityKind.RuneMovement] = new(AbilityEntityKind.RuneMovement, 0, false, false),
      [AbilityEntityKind.RuneHealth] = new(AbilityEntityKind.RuneHealth, 0, false, false),
      [AbilityEntityKind.RuneRange] = new(AbilityEntityKind.RuneRange, 0, false, false),
      [AbilityEntityKind.Snare] = new(AbilityEntityKind.Snare, 0, false, false),
      [AbilityEntityKind.Tnt] = new(AbilityEntityKind.Tnt, 0, false, false),
      [AbilityEntityKind.Prison] = new(AbilityEntityKind.Prison, 65, true, true),
      [AbilityEntityKind.Road] = new(AbilityEntityKind.Road, 0, false, false),
      [AbilityEntityKind.Barricade] = new(AbilityEntityKind.Barricade, AbilityRules.EngineerBarrierHealth, true, true)
    };

  public static AbilityEntityDefinition GetRequired(AbilityEntityKind kind) => Definitions[kind];
  public static IReadOnlyCollection<AbilityEntityDefinition> All => Definitions.Values.ToArray();
}
