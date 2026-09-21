using MedivalChess.Shared;

namespace MedivalChess.CPU;

public static partial class CpuGameRules
{
  private static void TriggerAbilityEntitiesAlongMovement(
    CpuMutableGameState state,
    string movingPieceId,
    IReadOnlyList<(int x, int y)> path)
  {
    HashSet<string> triggered = new(StringComparer.Ordinal);
    foreach ((int x, int y) step in path)
    {
      int pieceIndex = FindPieceIndex(state.Pieces, movingPieceId);
      if (pieceIndex < 0 || !UnitRules.TryGet(state.Pieces[pieceIndex].Type, out UnitRule rule))
      {
        return;
      }

      NetworkPiece moving = state.Pieces[pieceIndex];
      string[] entityIds = state.AbilityEntities
        .Where(entity => !triggered.Contains(entity.Id) &&
          OccupiedSquares(rule, step).Any(square => entity.X == square.x && entity.Y == square.y))
        .Select(entity => entity.Id)
        .ToArray();

      foreach (string entityId in entityIds)
      {
        int entityIndex = state.AbilityEntities.FindIndex(entity => entity.Id == entityId);
        if (entityIndex < 0) continue;
        AbilityEntity entity = state.AbilityEntities[entityIndex];
        AbilityEntityEntryEffect effect = AbilityEntityEffectRules.GetEntryEffect(entity, moving);
        triggered.Add(entity.Id);

        if (effect.EntityDamage > 0 && entity.Health > 0)
        {
          int remaining = entity.Health - effect.EntityDamage;
          if (remaining <= 0) state.AbilityEntities.RemoveAt(entityIndex);
          else state.AbilityEntities[entityIndex] = entity with { Health = remaining };
        }
        else if (effect.ConsumeEntity)
        {
          state.AbilityEntities.RemoveAt(entityIndex);
        }

        if (effect.UnitDamage > 0)
        {
          ApplySharedFixedDamage(
            state,
            movingPieceId,
            entity.Owner,
            effect.UnitDamage,
            applyCombatMitigation: false);
          pieceIndex = FindPieceIndex(state.Pieces, movingPieceId);
          if (pieceIndex < 0) return;
          moving = state.Pieces[pieceIndex];
        }
      }
    }
  }

  private static void TriggerPoisonCloudsAtOwnerTurnStart(CpuMutableGameState state, NetworkTeam ownerTurn)
  {
    AbilityEntity[] clouds = state.AbilityEntities
      .Where(entity => entity.Kind == AbilityEntityKind.PoisonCloud && entity.Owner == ownerTurn)
      .ToArray();

    foreach (AbilityEntity cloud in clouds)
    {
      foreach (string pieceId in state.Pieces
        .Where(piece => piece.AttachedToId is null && UnitRules.TryGet(piece.Type, out UnitRule rule) &&
          AbilityEntityEffectRules.PoisonCloudAffects(cloud, piece, rule, ownerTurn))
        .Select(piece => piece.Id)
        .ToArray())
      {
        ApplySharedFixedDamage(
          state,
          pieceId,
          cloud.Owner,
          AbilityEntityRules.GetRequired(AbilityEntityKind.PoisonCloud).StartOfOwnerTurnDamage,
          applyCombatMitigation: false);
      }
    }
  }

  private static void RemoveSourceBoundAbilityEntities(CpuMutableGameState state, string sourcePieceId)
  {
    state.AbilityEntities.RemoveAll(entity =>
      entity.SourcePieceId == sourcePieceId && AbilityEntityEffectRules.IsSourceBoundEffect(entity));
  }

  private static bool CanPlaceCpuAbilityEntity(
    CpuGameState state,
    AbilityEntityKind kind,
    NetworkTeam owner,
    int x,
    int y)
  {
    if (!BoardRules.Contains(state.Board, x, y) || state.Terrain.IsLake((x, y)) ||
        PieceOccupies(state.Pieces, (x, y)) ||
        state.AbilityEntities.Any(entity => entity.X == x && entity.Y == y))
    {
      return false;
    }

    return true;
  }

  private static AbilityEntity CreateCpuAbilityEntity(
    CpuMutableGameState state,
    AbilityEntityKind kind,
    NetworkTeam owner,
    int x,
    int y,
    string sourcePieceId)
  {
    AbilityEntityDefinition definition = AbilityEntityRules.GetRequired(kind);
    return new AbilityEntity(
      $"cpu-{kind}-{state.Source.TurnNumber}-{state.AbilityEntities.Count}-{x}-{y}",
      kind,
      owner,
      x,
      y,
      definition.Health,
      SourcePieceId: sourcePieceId);
  }
}
