using MedivalChess.Shared;

namespace MedivalChess.Server;

/// <summary>Authoritative state adapter for shared Phantom and Goblin Royalty rules.</summary>
public sealed partial class MatchStore
{
  private static bool TryUseSharedServerPhantomAbility(
    Match match,
    int actorIndex,
    int targetIndex,
    string ability
  )
  {
    NetworkPiece phantom = match.Pieces[actorIndex];
    if (phantom.Type != nameof(PieceType.Phantom))
    {
      return false;
    }

    if (string.Equals(ability, "Unpossess", StringComparison.OrdinalIgnoreCase))
    {
      if (string.IsNullOrEmpty(phantom.PossessedUnitId))
      {
        return false;
      }

      int possessedIndex = match.Pieces.FindIndex(piece => piece.Id == phantom.PossessedUnitId);
      if (possessedIndex >= 0)
      {
        NetworkPiece possessed = match.Pieces[possessedIndex];
        match.Pieces[possessedIndex] = possessed with { IsRoyalProxy = false };
      }

      PhantomPossessionState state = RoyalAbilityRules.Unpossess();
      match.Pieces[actorIndex] = phantom with { PossessedUnitId = state.PhantomPossessedUnitId };
      return true;
    }

    if (!string.Equals(ability, "Possess", StringComparison.OrdinalIgnoreCase) || targetIndex < 0)
    {
      return false;
    }

    NetworkPiece target = match.Pieces[targetIndex];
    if (!RoyalAbilityRules.CanPhantomPossess(
      phantom.Type,
      phantom.Team,
      phantom.PossessedUnitId,
      target.Id,
      target.Type,
      target.Team,
      target.IsRoyalProxy))
    {
      return false;
    }

    PhantomPossessionState possession = RoyalAbilityRules.Possess(target.Id);
    match.Pieces[actorIndex] = phantom with { PossessedUnitId = possession.PhantomPossessedUnitId };
    match.Pieces[targetIndex] = target with { IsRoyalProxy = possession.TargetIsRoyalProxy };
    return true;
  }

  private static bool IsSharedServerRoyalDeath(Match match, NetworkPiece defeatedPiece)
  {
    bool sameTeamGoblinRemains = match.Pieces.Any(piece =>
      piece.Id != defeatedPiece.Id &&
      piece.Team == defeatedPiece.Team &&
      piece.Type == nameof(PieceType.GoblinRoyalty));
    bool wasRoyal = RoyalAbilityRules.IsRoyal(
      defeatedPiece.Type,
      defeatedPiece.IsRoyalProxy,
      defeatedPiece.PossessedUnitId
    );
    return RoyalAbilityRules.IsRoyalDeath(
      defeatedPiece.Type,
      wasRoyal,
      sameTeamGoblinRemains
    );
  }

  private static bool CanPlaceSharedServerRoyalGroup(
    Match match,
    NetworkTeam team,
    string royalType,
    int anchorX,
    int anchorY
  )
  {
    UnitRule rule = UnitRules.GetRequired(royalType);
    return RoyalAbilityRules.GetRoyalSpawnOffsets(royalType).All(offset =>
      CanPlaceRoyal(
        match,
        team,
        anchorX + offset.x,
        anchorY + offset.y,
        rule.Width,
        rule.Height));
  }

  private static void AddSharedServerRoyalGroup(
    Match match,
    NetworkTeam team,
    string royalType,
    int anchorX,
    int anchorY,
    int health
  )
  {
    foreach ((int x, int y) offset in RoyalAbilityRules.GetRoyalSpawnOffsets(royalType))
    {
      match.Pieces.Add(new NetworkPiece(
        Guid.NewGuid().ToString("N"),
        royalType,
        team,
        anchorX + offset.x,
        anchorY + offset.y,
        health
      ));
    }
  }
}
