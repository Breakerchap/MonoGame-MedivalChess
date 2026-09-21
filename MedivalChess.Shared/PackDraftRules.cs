namespace MedivalChess.Shared;

/// <summary>Shared validation and resolution rules for the configurable pre-match pack draft.</summary>
public static class PackDraftRules
{
  public const string DraftMode = "Draft";
  public const string ManualMode = "Manual";
  public const string BanningPhase = "Banning";
  public const string VotingPhase = "Voting";
  public const string CompletePhase = "Complete";

  public static bool IsDraft(string? mode) => string.Equals(mode, DraftMode, StringComparison.OrdinalIgnoreCase);

  public static bool IsKnownMode(string? mode) =>
    string.Equals(mode, DraftMode, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(mode, ManualMode, StringComparison.OrdinalIgnoreCase);

  public static string NormalizeMode(string? mode) => IsDraft(mode) ? DraftMode : ManualMode;

  public static bool TryValidateSettings(
    string? mode,
    int banCount,
    int voteCount,
    int playerCount,
    int candidateCount,
    out string? error
  )
  {
    error = null;
    if (!IsKnownMode(mode))
    {
      error = "Choose Manual or Draft pack selection.";
      return false;
    }

    if (!IsDraft(mode)) return true;
    if (banCount < 0 || voteCount < 1 || playerCount < 2 || candidateCount < 1 ||
        banCount * playerCount + voteCount > candidateCount)
    {
      error = "The draft needs enough candidate packs for every ban and vote.";
      return false;
    }

    return true;
  }

  public static bool TryNormaliseVote(
    IEnumerable<string>? names,
    IEnumerable<string> candidates,
    IEnumerable<string> banned,
    int voteCount,
    out string[] normalised,
    out string? error
  )
  {
    normalised = [];
    error = null;
    HashSet<string> candidateSet = candidates.ToHashSet(StringComparer.Ordinal);
    HashSet<string> bannedSet = banned.ToHashSet(StringComparer.Ordinal);
    string[] submitted = (names ?? [])
      .Select(name => candidates.FirstOrDefault(candidate =>
        string.Equals(candidate, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? name?.Trim() ?? string.Empty)
      .ToArray();
    if (submitted.Length != voteCount || submitted.Distinct(StringComparer.Ordinal).Count() != submitted.Length)
    {
      error = $"Vote for exactly {voteCount} different unbanned packs.";
      return false;
    }

    if (submitted.Any(pack => !candidateSet.Contains(pack) || bannedSet.Contains(pack)))
    {
      error = "Votes must be for unbanned packs in this draft.";
      return false;
    }

    normalised = submitted.OrderBy(pack => pack, StringComparer.Ordinal).ToArray();
    return true;
  }

  public static string[] ResolveCommonPacks(IEnumerable<IEnumerable<string>> votes) =>
    votes.Select(vote => vote.ToHashSet(StringComparer.Ordinal))
      .Aggregate((left, right) =>
      {
        left.IntersectWith(right);
        return left;
      })
      .OrderBy(pack => pack, StringComparer.Ordinal)
      .ToArray();
}
