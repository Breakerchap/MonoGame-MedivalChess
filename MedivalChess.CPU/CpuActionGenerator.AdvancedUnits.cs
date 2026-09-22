using MedivalChess.Shared;

namespace MedivalChess.CPU;

public sealed partial class CpuActionGenerator
{
  private static void GenerateHwachaReloadAbilities(
    CpuGameState state,
    NetworkPiece actor,
    List<ICpuGameAction> actions)
  {
    foreach (NetworkPiece hwacha in state.Pieces
      .Where(piece => piece.Team == actor.Team && piece.Type == nameof(PieceType.Hwacha))
      .OrderBy(piece => piece.Id, StringComparer.Ordinal))
    {
      AddIfLegal(state, new UseAbilityAction(
        actor.Team, actor.Id, "ReloadHwacha", hwacha.Id, hwacha.X, hwacha.Y), actions);
    }
  }

  private static bool GenerateCodexAdvancedAbilities(
    CpuGameState state,
    NetworkPiece actor,
    List<ICpuGameAction> actions)
  {
    string[]? squareAbilities = actor.Type switch
    {
      nameof(PieceType.Mason) => ["StoneWall", "Gatehouse", "Demolish"],
      nameof(PieceType.Carpenter) => ["Bridge", "Watchtower", "Demolish"],
      nameof(PieceType.Daedalus) => ["Gate", "Snare", "Demolish"],
      nameof(PieceType.Runesmith) => ["RuneAttack", "RuneMovement", "RuneHealth", "RuneRange", "Demolish"],
      nameof(PieceType.Thor) => ["Thunderstorm"],
      nameof(PieceType.Demolitionist) => ["PlaceTnt"],
      nameof(PieceType.Mashhit) => ["Destroy"],
      nameof(PieceType.Gatekeeper) => ["Portal", "Seal", "Demolish"],
      _ => null
    };

    if (squareAbilities is not null)
    {
      foreach ((int x, int y) position in GetPotentialActionSquares(state, actor))
      foreach (string ability in squareAbilities)
      {
        AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, ability, null, position.x, position.y), actions);
      }

      if (actor.Type == nameof(PieceType.Thor))
      {
        foreach (AbilityEntity storm in state.AbilityEntities
          .Where(entity => entity.Kind == AbilityEntityKind.Thunderstorm && entity.SourcePieceId == actor.Id)
          .OrderBy(entity => entity.Id, StringComparer.Ordinal))
        foreach ((int x, int y) position in GetPotentialActionSquares(state, actor))
        {
          AddIfLegal(state, new UseAbilityAction(
            actor.Team, actor.Id, "Thunderstorm", storm.Id, position.x, position.y), actions);
        }
      }
      if (actor.Type == nameof(PieceType.Demolitionist))
      {
        AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Detonate", null, actor.X, actor.Y), actions);
      }
      return true;
    }

    if (actor.Type == nameof(PieceType.Sheriff))
    {
      foreach (NetworkPiece target in state.Pieces
        .Where(piece =>
          piece.Team != actor.Team &&
          piece.Team != NetworkTeam.Neutral &&
          piece.AttachedToId is null)
        .OrderBy(piece => piece.Id, StringComparer.Ordinal))
      {
        foreach ((int x, int y) targetSquare in GetTargetSquares(target))
        {
          AddIfLegal(state, new UseAbilityAction(
            actor.Team, actor.Id, "Arrest",
            target.Id, targetSquare.x, targetSquare.y), actions);
        }
      }
      return true;
    }

    if (actor.Type == nameof(PieceType.Fafnir))
    {
      AddIfLegal(state, new UseAbilityAction(actor.Team, actor.Id, "Transform", null, actor.X, actor.Y), actions);
      return true;
    }

    if (actor.Type == nameof(PieceType.CommandCentre))
    {
      foreach (NetworkPiece target in state.Pieces
        .Where(piece => piece.Team == actor.Team && piece.Id != actor.Id)
        .OrderBy(piece => piece.Id, StringComparer.Ordinal))
      foreach (string ability in new[] { "UpgradeAttack", "UpgradeHealth", "UpgradeMove" })
      {
        AddIfLegal(state, new UseAbilityAction(
          actor.Team, actor.Id, ability, target.Id, target.X, target.Y), actions);
      }
      return true;
    }

    return false;
  }

}
