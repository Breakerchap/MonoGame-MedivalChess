namespace MedivalChess.Shared;

public readonly record struct PhantomPossessionState(
  string? PhantomPossessedUnitId,
  bool TargetIsRoyalProxy
);

/// <summary>Shared deterministic rules for Royal abilities and Royal identity.</summary>
public static class RoyalAbilityRules
{
  private static readonly (int x, int y)[] SingleSpawn = [(0, 0)];
  private static readonly (int x, int y)[] GoblinRoyaltySpawn =
  [
    (0, 0),
    (1, 0),
    (0, 1),
    (1, 1)
  ];

  /// <summary>
  /// Goblin Royalty is represented by four separate 1x1 units arranged as a 2x2 group around
  /// the chosen anchor square. Other Royals spawn once at the anchor.
  /// </summary>
  public static IReadOnlyList<(int x, int y)> GetRoyalSpawnOffsets(string royalType) =>
    royalType == nameof(PieceType.GoblinRoyalty) ? GoblinRoyaltySpawn : SingleSpawn;

  public static (int width, int height) GetRoyalSpawnFootprint(string royalType)
  {
    IReadOnlyList<(int x, int y)> offsets = GetRoyalSpawnOffsets(royalType);
    UnitRule rule = UnitRules.GetRequired(royalType);
    return (
      offsets.Max(offset => offset.x) + rule.Width,
      offsets.Max(offset => offset.y) + rule.Height
    );
  }

  public static bool IsRoyal(string unitType, bool isRoyalProxy, string? possessedUnitId)
  {
    if (isRoyalProxy) return true;
    if (unitType == nameof(PieceType.Phantom) && !string.IsNullOrEmpty(possessedUnitId)) return false;
    return UnitRules.TryGet(unitType, out UnitRule rule) && rule.Category == RuleCategory.Royal;
  }

  public static bool CanPhantomPossess(
    string phantomType,
    NetworkTeam phantomTeam,
    string? currentPossessedUnitId,
    string targetId,
    string targetType,
    NetworkTeam targetTeam,
    bool targetIsRoyalProxy
  ) =>
    phantomType == nameof(PieceType.Phantom) &&
    string.IsNullOrEmpty(currentPossessedUnitId) &&
    targetTeam == phantomTeam &&
    targetType != nameof(PieceType.Phantom) &&
    !targetIsRoyalProxy &&
    !IsRoyal(targetType, targetIsRoyalProxy, null) &&
    !string.IsNullOrWhiteSpace(targetId);

  public static PhantomPossessionState Possess(string targetId) => new(targetId, true);

  public static PhantomPossessionState Unpossess() => new(null, false);

  /// <summary>
  /// Goblin Royalty only dies as a Royal when the final Goblin Royalty unit for that team dies.
  /// </summary>
  public static bool IsRoyalDeath(
    string defeatedType,
    bool defeatedWasRoyal,
    bool sameTeamGoblinRoyaltyRemains
  ) => defeatedWasRoyal &&
    !(defeatedType == nameof(PieceType.GoblinRoyalty) && sameTeamGoblinRoyaltyRemains);
}
