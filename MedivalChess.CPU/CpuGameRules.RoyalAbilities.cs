using MedivalChess.Shared;

namespace MedivalChess.CPU;

/// <summary>CPU-state adapter for the shared Phantom Royal rules.</summary>
public static partial class CpuGameRules
{
  private static void ApplySharedPhantomAbility(
    CpuMutableGameState state,
    int actorIndex,
    NetworkPiece? target,
    string ability
  )
  {
    NetworkPiece phantom = state.Pieces[actorIndex];
    if (string.Equals(ability, "Unpossess", StringComparison.OrdinalIgnoreCase))
    {
      int possessedIndex = string.IsNullOrEmpty(phantom.PossessedUnitId)
        ? -1
        : FindPieceIndex(state.Pieces, phantom.PossessedUnitId);
      if (possessedIndex >= 0)
      {
        NetworkPiece possessed = state.Pieces[possessedIndex];
        state.Pieces[possessedIndex] = possessed with { IsRoyalProxy = false };
      }

      PhantomPossessionState unpossessed = RoyalAbilityRules.Unpossess();
      state.Pieces[actorIndex] = phantom with { PossessedUnitId = unpossessed.PhantomPossessedUnitId };
      return;
    }

    if (target is null)
    {
      return;
    }

    PhantomPossessionState possession = RoyalAbilityRules.Possess(target.Id);
    state.Pieces[actorIndex] = phantom with { PossessedUnitId = possession.PhantomPossessedUnitId };
    int targetIndex = FindPieceIndex(state.Pieces, target.Id);
    if (targetIndex >= 0)
    {
      state.Pieces[targetIndex] = target with { IsRoyalProxy = possession.TargetIsRoyalProxy };
    }
  }
}
