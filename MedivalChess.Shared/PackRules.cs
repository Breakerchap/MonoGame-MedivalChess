namespace MedivalChess.Shared;

/// <summary>Shared parsing and filtering for the packs enabled in a match or campaign level.</summary>
public static class PackRules
{
  private static readonly Pack[] Packs = Enum.GetValues<Pack>();

  public static IReadOnlyList<Pack> All => Packs;
  public static IReadOnlyList<string> AllNames { get; } = Packs.Select(pack => pack.ToString()).ToArray();

  /// <summary>Null means every pack for backwards-compatible network messages.</summary>
  public static IReadOnlySet<Pack> GetAllowedPacks(IEnumerable<string>? names)
  {
    if (names is null)
    {
      return Packs.ToHashSet();
    }

    HashSet<Pack> allowed = [];
    foreach (string? name in names)
    {
      if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse(name.Trim(), true, out Pack pack) && Enum.IsDefined(pack))
      {
        allowed.Add(pack);
      }
    }
    return allowed;
  }

  public static bool TryNormaliseAllowedPacks(IEnumerable<string>? names, out string[] normalised)
  {
    if (names is null)
    {
      normalised = [.. AllNames];
      return true;
    }

    HashSet<Pack> parsed = [];
    foreach (string? name in names)
    {
      if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse(name.Trim(), true, out Pack pack) || !Enum.IsDefined(pack))
      {
        normalised = [];
        return false;
      }
      parsed.Add(pack);
    }

    normalised = Packs.Where(parsed.Contains).Select(pack => pack.ToString()).ToArray();
    return normalised.Length > 0;
  }

  public static bool IsAllowed(Pack pack, IEnumerable<string>? names) =>
    names is null || GetAllowedPacks(names).Contains(pack);

  public static bool IsAllowed(PieceDefinition definition, IEnumerable<string>? names) =>
    IsAllowed(definition.Pack, names);

  public static bool IsAllowed(string identifier, IEnumerable<string>? names)
  {
    PieceDefinition? definition = PieceDefinitions.All.FirstOrDefault(candidate =>
      string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
    return definition is not null && IsAllowed(definition, names);
  }
}
