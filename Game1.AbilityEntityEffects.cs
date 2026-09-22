using System;
using System.Collections.Generic;
using System.Linq;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

namespace MedivalChess;

internal sealed partial class Game1
{
  private void TriggerLocalAbilityEntitiesAlongMovement(
    Piece movingPiece,
    IReadOnlyList<(int x, int y)> path)
  {
    HashSet<string> triggered = new(StringComparer.Ordinal);
    (int x, int y)? finalStep = path.Count > 0 ? path[^1] : null;
    foreach ((int x, int y) step in path)
    {
      if (!pieceSetup.Pieces.Contains(movingPiece)) return;
      UnitRule rule = UnitRules.FromPieceDefinition(movingPiece.Definition);
      NetworkPiece moving = new(
        movingPiece.NetworkId,
        movingPiece.Definition.Type.ToString(),
        movingPiece.Team.ToNetworkTeam(),
        step.x,
        step.y,
        movingPiece.CurrentHealth);

      string[] entityIds = _abilityEntities
        .Where(entity => !triggered.Contains(entity.Id) &&
          OccupiedSquares(movingPiece.Definition, step).Any(square =>
            entity.X == square.x && entity.Y == square.y))
        .Select(entity => entity.Id)
        .ToArray();

      foreach (string entityId in entityIds)
      {
        int entityIndex = _abilityEntities.FindIndex(entity => entity.Id == entityId);
        if (entityIndex < 0) continue;
        AbilityEntity entity = _abilityEntities[entityIndex];
        if (entity.Kind == AbilityEntityKind.Snare && finalStep != step)
        {
          continue;
        }
        AbilityEntityEntryEffect effect = AbilityEntityEffectRules.GetEntryEffect(entity, moving);
        triggered.Add(entity.Id);

        if (effect.EntityDamage > 0 && entity.Health > 0)
        {
          int remaining = entity.Health - effect.EntityDamage;
          if (remaining <= 0) _abilityEntities.RemoveAt(entityIndex);
          else _abilityEntities[entityIndex] = entity with { Health = remaining };
        }
        else if (effect.ConsumeEntity)
        {
          _abilityEntities.RemoveAt(entityIndex);
        }

        if (effect.LockMovementNextOwnerTurn)
        {
          movingPiece.AbilityState = movingPiece.AbilityState with
          {
            SkipMovementOwnerTurns = Math.Max(movingPiece.AbilityState.SkipMovementOwnerTurns, 2)
          };
        }

        if (effect.UnitDamage > 0)
        {
          ApplyLocalAbilityEntityDamage(movingPiece, entity.Owner, effect.UnitDamage);
          if (!pieceSetup.Pieces.Contains(movingPiece)) return;
          moving = moving with { Health = movingPiece.CurrentHealth };
        }
      }
    }
  }

  private void TriggerLocalPoisonCloudsAtOwnerTurnStart(TeamName activeTeam)
  {
    NetworkTeam ownerTurn = activeTeam.ToNetworkTeam();
    AbilityEntity[] clouds = _abilityEntities
      .Where(entity => entity.Kind == AbilityEntityKind.PoisonCloud && entity.Owner == ownerTurn)
      .ToArray();

    foreach (AbilityEntity cloud in clouds)
    {
      foreach (Piece piece in pieceSetup.Pieces.Where(piece => piece.AttachedTo is null).ToArray())
      {
        UnitRule rule = UnitRules.FromPieceDefinition(piece.Definition);
        NetworkPiece snapshot = new(
          piece.NetworkId,
          piece.Definition.Type.ToString(),
          piece.Team.ToNetworkTeam(),
          piece.Position.x,
          piece.Position.y,
          piece.CurrentHealth);
        if (AbilityEntityEffectRules.PoisonCloudAffects(cloud, snapshot, rule, ownerTurn))
        {
          ApplyLocalAbilityEntityDamage(
            piece,
            cloud.Owner,
            AbilityEntityRules.GetRequired(AbilityEntityKind.PoisonCloud).StartOfOwnerTurnDamage);
        }
      }
    }
  }

  private void ApplyLocalAbilityEntityDamage(Piece target, NetworkTeam sourceTeam, int damage)
  {
    if (!pieceSetup.Pieces.Contains(target)) return;
    int applied = ApplyLocalChessKingDeathRule(
      target,
      AbilityRules.LimitIncomingDamage(UnitRules.FromPieceDefinition(target.Definition), damage));
    target.CurrentHealth -= applied;
    HandlePieceDestroyed(target, sourceTeam.ToTeamName());
  }

  private void RemoveSourceBoundLocalAbilityEntities(string sourcePieceId)
  {
    _abilityEntities.RemoveAll(entity =>
      entity.SourcePieceId == sourcePieceId && AbilityEntityEffectRules.IsSourceBoundEffect(entity));
  }

  private bool CanPlaceLocalAbilityEntity((int x, int y) position)
  {
    return IsBoardCell(position.x - _board.MinX, position.y - _board.MinY) &&
      !_terrain.IsLake(position) &&
      !_barricades.ContainsKey(position) &&
      pieceSetup.GetPieceAt(position) is null &&
      !_abilityEntities.Any(entity => entity.X == position.x && entity.Y == position.y);
  }

  private AbilityEntity CreateLocalAbilityEntity(
    AbilityEntityKind kind,
    Piece source,
    (int x, int y) position)
  {
    AbilityEntityDefinition definition = AbilityEntityRules.GetRequired(kind);
    return new AbilityEntity(
      Guid.NewGuid().ToString("N"),
      kind,
      source.Team.ToNetworkTeam(),
      position.x,
      position.y,
      definition.Health,
      SourcePieceId: source.NetworkId);
  }
}
