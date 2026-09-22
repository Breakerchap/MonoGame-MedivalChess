using MedivalChess.Shared;

namespace MedivalChess.Server;

public sealed partial class MatchStore
{
  private static void TriggerServerAbilityEntitiesAlongMovement(
    Match match,
    string movingPieceId,
    IReadOnlyList<(int x, int y)> path)
  {
    HashSet<string> triggered = new(StringComparer.Ordinal);
    (int x, int y)? finalStep = path.Count > 0 ? path[^1] : null;
    foreach ((int x, int y) step in path)
    {
      int pieceIndex = match.Pieces.FindIndex(piece => piece.Id == movingPieceId);
      if (pieceIndex < 0 || !UnitRules.TryGet(match.Pieces[pieceIndex].Type, out UnitRule rule))
      {
        return;
      }

      NetworkPiece moving = match.Pieces[pieceIndex];
      string[] entityIds = match.AbilityEntities
        .Where(entity => !triggered.Contains(entity.Id) &&
          OccupiedSquares(rule, step).Any(square => entity.X == square.x && entity.Y == square.y))
        .Select(entity => entity.Id)
        .ToArray();

      foreach (string entityId in entityIds)
      {
        int entityIndex = match.AbilityEntities.FindIndex(entity => entity.Id == entityId);
        if (entityIndex < 0) continue;
        AbilityEntity entity = match.AbilityEntities[entityIndex];
        if (entity.Kind == AbilityEntityKind.Snare && finalStep != step)
        {
          continue;
        }
        AbilityEntityEntryEffect effect = AbilityEntityEffectRules.GetEntryEffect(entity, moving);
        triggered.Add(entity.Id);

        if (effect.EntityDamage > 0 && entity.Health > 0)
        {
          int remaining = entity.Health - effect.EntityDamage;
          if (remaining <= 0) match.AbilityEntities.RemoveAt(entityIndex);
          else match.AbilityEntities[entityIndex] = entity with { Health = remaining };
        }
        else if (effect.ConsumeEntity)
        {
          match.AbilityEntities.RemoveAt(entityIndex);
        }

        if (effect.LockMovementNextOwnerTurn)
        {
          pieceIndex = match.Pieces.FindIndex(piece => piece.Id == movingPieceId);
          if (pieceIndex < 0) return;
          moving = match.Pieces[pieceIndex];
          match.Pieces[pieceIndex] = moving with
          {
            AbilityState = (moving.AbilityState ?? new UnitAbilityState()) with
            {
              SkipMovementOwnerTurns = Math.Max(moving.AbilityState?.SkipMovementOwnerTurns ?? 0, 2)
            }
          };
          moving = match.Pieces[pieceIndex];
        }

        if (effect.UnitDamage > 0)
        {
          ApplyServerAbilityEntityDamage(match, movingPieceId, entity.Owner, effect.UnitDamage);
          pieceIndex = match.Pieces.FindIndex(piece => piece.Id == movingPieceId);
          if (pieceIndex < 0) return;
          moving = match.Pieces[pieceIndex];
        }
      }
    }
  }

  private static void TriggerServerPoisonCloudsAtOwnerTurnStart(Match match, NetworkTeam ownerTurn)
  {
    AbilityEntity[] clouds = match.AbilityEntities
      .Where(entity => entity.Kind == AbilityEntityKind.PoisonCloud && entity.Owner == ownerTurn)
      .ToArray();

    foreach (AbilityEntity cloud in clouds)
    {
      foreach (string pieceId in match.Pieces
        .Where(piece => piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
          AbilityEntityEffectRules.PoisonCloudAffects(cloud, piece, rule, ownerTurn))
        .Select(piece => piece.Id)
        .ToArray())
      {
        ApplyServerAbilityEntityDamage(
          match,
          pieceId,
          cloud.Owner,
          AbilityEntityRules.GetRequired(AbilityEntityKind.PoisonCloud).StartOfOwnerTurnDamage);
      }
    }
  }

  private static void ApplyServerAbilityEntityDamage(
    Match match,
    string targetId,
    NetworkTeam sourceTeam,
    int damage)
  {
    int index = match.Pieces.FindIndex(piece => piece.Id == targetId);
    if (index < 0 || !UnitRules.TryGet(match.Pieces[index].Type, out UnitRule targetRule))
    {
      return;
    }

    NetworkPiece target = match.Pieces[index];
    int applied = ApplyServerChessKingDeathRule(
      match,
      target,
      AbilityRules.LimitIncomingDamage(targetRule, damage));
    if (target.Health > applied)
    {
      match.Pieces[index] = target with { Health = target.Health - applied };
      return;
    }

    PlayerSlot? source = match.Players.FirstOrDefault(player => player.Team == sourceTeam);
    if (source is not null)
    {
      HandlePieceDestroyed(match, target, source);
    }
  }

  private static void RemoveSourceBoundServerAbilityEntities(Match match, string sourcePieceId)
  {
    match.AbilityEntities.RemoveAll(entity =>
      entity.SourcePieceId == sourcePieceId && AbilityEntityEffectRules.IsSourceBoundEffect(entity));
  }
}
