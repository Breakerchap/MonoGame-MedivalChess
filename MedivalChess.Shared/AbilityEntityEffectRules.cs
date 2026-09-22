namespace MedivalChess.Shared;

public readonly record struct AbilityEntityEntryEffect(
  int UnitDamage,
  int EntityDamage,
  bool ConsumeEntity,
  bool LockMovementNextOwnerTurn = false
);

public static class AbilityEntityEffectRules
{
  public static bool IgnoresFire(string unitType) =>
    unitType is nameof(PieceType.Dragon) or nameof(PieceType.Phoenix);

  public static AbilityEntityEntryEffect GetEntryEffect(AbilityEntity entity, NetworkPiece unit)
  {
    return entity.Kind switch
    {
      AbilityEntityKind.Fire when !IgnoresFire(unit.Type) =>
        new(AbilityEntityRules.GetRequired(AbilityEntityKind.Fire).EnterDamage, 0, true),
      AbilityEntityKind.Bramble =>
        new(
          AbilityEntityRules.GetRequired(AbilityEntityKind.Bramble).EnterDamage,
          AbilityEntityRules.GetRequired(AbilityEntityKind.Bramble).EnterDamage,
          false),
      AbilityEntityKind.Thunderstorm when entity.Owner != unit.Team =>
        new(AbilityEntityRules.GetRequired(AbilityEntityKind.Thunderstorm).EnterDamage, 0, false),
      AbilityEntityKind.Snare => new(0, 0, true, LockMovementNextOwnerTurn: true),
      _ => default
    };
  }

  public static bool PoisonCloudAffects(
    AbilityEntity cloud,
    NetworkPiece unit,
    UnitRule unitRule,
    NetworkTeam ownerTurn)
  {
    if (cloud.Kind != AbilityEntityKind.PoisonCloud || cloud.Owner != ownerTurn)
    {
      return false;
    }

    for (int y = 0; y < unitRule.Height; y++)
    {
      for (int x = 0; x < unitRule.Width; x++)
      {
        if (AbilityEntityRules.IsWithinRadius(cloud, unit.X + x, unit.Y + y))
        {
          return true;
        }
      }
    }
    return false;
  }

  public static bool ShouldExpireAtOwnerTurnStart(AbilityEntity entity, NetworkTeam ownerTurn) =>
    entity.Kind == AbilityEntityKind.Seal && entity.Owner == ownerTurn;

  public static bool IsSourceBoundEffect(AbilityEntity entity) =>
    entity.Kind == AbilityEntityKind.PoisonCloud;
}
