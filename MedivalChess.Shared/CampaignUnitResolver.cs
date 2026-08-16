#nullable enable

namespace MedivalChess.Shared;

/// <summary>Resolves native, custom, and per-placement campaign unit data into the game's runtime definition.</summary>
public static class CampaignUnitResolver
{
  public static bool TryResolve(
    CampaignLevelDefinition level,
    string? identifier,
    CampaignUnitStatOverrides? placementOverrides,
    out PieceDefinition definition
  )
  {
    ArgumentNullException.ThrowIfNull(level);
    PieceDefinition? native = PieceDefinitions.All.FirstOrDefault(candidate =>
      string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
    if (native is not null)
    {
      CampaignUnitTemplateOverrideDefinition? template = (level.UnitOverrides ?? []).FirstOrDefault(candidate =>
        string.Equals(candidate.UnitType, native.Identifier, StringComparison.Ordinal));
      if (template is null)
      {
        return TryApplyOverrides(native, placementOverrides, native.Identifier, native.DisplayName, native.AbilityDescription, native.Abbreviation, out definition);
      }
      if (!TryGetAbilitySource(template.AbilitySourceUnitType, native, out PieceDefinition nativeAbilitySource))
      {
        definition = null!;
        return false;
      }
      if (!TryApplyOverrides(
        new PieceDefinition(nativeAbilitySource.Type, template.Abbreviation ?? native.Abbreviation ?? string.Empty, native.Pack, native.Movement, native.Attack, native.Health, native.Size,
          native.AttackRange, native.AttackPattern, native.Cost, nativeAbilitySource.AbilityDescription, native.Identifier, template.Name),
        template.StatOverrides, native.Identifier, template.Name, nativeAbilitySource.AbilityDescription, template.Abbreviation, out PieceDefinition templatedDefinition))
      {
        definition = null!;
        return false;
      }
      return TryApplyOverrides(templatedDefinition, placementOverrides, native.Identifier, template.Name, nativeAbilitySource.AbilityDescription, template.Abbreviation, out definition);
    }

    CampaignCustomUnitDefinition? custom = (level.CustomUnits ?? []).FirstOrDefault(candidate =>
      string.Equals(candidate.Id, identifier, StringComparison.Ordinal));
    if (custom is null ||
        !TryGetNative(custom.BaseUnitType, out PieceDefinition baseDefinition) ||
        !TryGetAbilitySource(custom.AbilitySourceUnitType, baseDefinition, out PieceDefinition abilitySource))
    {
      definition = null!;
      return false;
    }

    if (!TryApplyOverrides(
      new PieceDefinition(
        abilitySource.Type,
        custom.Abbreviation ?? baseDefinition.Abbreviation ?? string.Empty,
        baseDefinition.Pack,
        baseDefinition.Movement,
        baseDefinition.Attack,
        baseDefinition.Health,
        baseDefinition.Size,
        baseDefinition.AttackRange,
        baseDefinition.AttackPattern,
        baseDefinition.Cost,
        abilitySource.AbilityDescription,
        custom.Id,
        custom.Name
      ),
      custom.StatOverrides,
      custom.Id,
      custom.Name,
      abilitySource.AbilityDescription,
      custom.Abbreviation,
      out PieceDefinition customDefinition))
    {
      definition = null!;
      return false;
    }

    return TryApplyOverrides(
      customDefinition,
      placementOverrides,
      custom.Id,
      custom.Name,
      abilitySource.AbilityDescription,
      custom.Abbreviation,
      out definition);
  }

  public static bool IsKnownIdentifier(CampaignLevelDefinition level, string? identifier) =>
    TryResolve(level, identifier, null, out _);

  public static IReadOnlyList<string> GetPurchasableIdentifiers(CampaignLevelDefinition level) =>
    PieceDefinitions.Purchasable.Where(definition => !((level.UnitOverrides ?? [])
        .FirstOrDefault(template => string.Equals(template.UnitType, definition.Identifier, StringComparison.Ordinal))?.Purchasable == false))
      .Select(definition => definition.Identifier)
      .Concat((level.CustomUnits ?? []).Where(unit => unit.Purchasable).Select(unit => unit.Id))
      .Distinct(StringComparer.Ordinal)
      .ToArray();

  private static bool TryGetNative(string? identifier, out PieceDefinition definition)
  {
    definition = PieceDefinitions.All.FirstOrDefault(candidate =>
      string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal))!;
    return definition is not null;
  }

  private static bool TryGetAbilitySource(string? identifier, PieceDefinition fallback, out PieceDefinition definition)
  {
    if (string.Equals(identifier, "None", StringComparison.OrdinalIgnoreCase))
    {
      definition = PieceDefinitions.Swordsman;
      return true;
    }
    if (string.IsNullOrWhiteSpace(identifier))
    {
      definition = fallback;
      return true;
    }
    return TryGetNative(identifier, out definition);
  }

  private static bool TryApplyOverrides(
    PieceDefinition source,
    CampaignUnitStatOverrides? overrides,
    string identifier,
    string displayName,
    string abilityDescription,
    string? abbreviation,
    out PieceDefinition definition
  )
  {
    try
    {
      int minimumAttackRange = overrides?.MinimumAttackRange ?? source.AttackRange.Minimum;
      int maximumAttackRange = overrides?.MaximumAttackRange ?? source.AttackRange.Maximum;
      int maximumMoveRange = overrides?.MoveRange ?? source.Movement.Maximum;
      int minimumMoveRange = Math.Min(source.Movement.Minimum, maximumMoveRange);
      definition = new PieceDefinition(
        source.Type,
        abbreviation ?? source.Abbreviation ?? string.Empty,
        source.Pack,
        new MovementDefinition(minimumMoveRange, maximumMoveRange, overrides?.MovePattern ?? source.Movement.Shape),
        overrides?.Attack ?? source.Attack,
        overrides?.Health ?? source.Health,
        (overrides?.Width ?? source.Size.x, overrides?.Height ?? source.Size.y),
        new AttackRange(minimumAttackRange, maximumAttackRange),
        overrides?.AttackPattern ?? source.AttackPattern,
        overrides?.Cost ?? source.Cost,
        abilityDescription,
        identifier,
        displayName
      );
      return true;
    }
    catch (ArgumentOutOfRangeException)
    {
      definition = null!;
      return false;
    }
  }
}
