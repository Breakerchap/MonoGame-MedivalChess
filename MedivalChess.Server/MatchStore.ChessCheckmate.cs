using MedivalChess.Shared;

namespace MedivalChess.Server;

public sealed partial class MatchStore
{
  private static int ApplyServerChessKingDeathRule(Match match, NetworkPiece target, int damage)
  {
    if (!ChessAbilityRules.IsChessKing(target.Type) || damage < target.Health)
    {
      return damage;
    }

    return ChessAbilityRules.CanChessKingDie(IsServerChessKingCheckmated(match, target))
      ? damage
      : Math.Max(0, target.Health - 1);
  }

  private static bool IsServerChessKingCheckmated(Match match, NetworkPiece king)
  {
    UnitRule kingRule = UnitRules.GetRequired(king.Type);
    IEnumerable<(int x, int y)> escapes = ShapeGeometryRules
      .GetStepDirections(RuleShape.Any, king.Team)
      .Select(offset => (king.X + offset.x, king.Y + offset.y));
    return ChessAbilityRules.IsCheckmated(
      (king.X, king.Y),
      escapes,
      square => CanServerChessKingOccupy(match, king, kingRule, square),
      square => IsServerChessKingSquareThreatened(match, king, square)
    );
  }

  private static bool CanServerChessKingOccupy(
    Match match,
    NetworkPiece king,
    UnitRule kingRule,
    (int x, int y) destination
  )
  {
    if (!NetworkPieceRules.FootprintFitsBoard(
          match.Configuration, destination.x, destination.y, kingRule.Width, kingRule.Height) ||
        OccupiedSquares(kingRule, destination).Any(square =>
          match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square)))
    {
      return false;
    }

    NetworkPiece? occupant = match.Pieces.FirstOrDefault(other =>
      other.Id != king.Id && other.AttachedToId is null && other.Type != nameof(PieceType.Farm) &&
      UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(
        destination.x, destination.y, kingRule.Width, kingRule.Height,
        other.X, other.Y, otherRule.Width, otherRule.Height));
    return occupant is null || ChessAbilityRules.CanCaptureByLanding(
      kingRule, king.Team, (king.X, king.Y), destination, occupant.Team);
  }

  private static bool IsServerChessKingSquareThreatened(
    Match match,
    NetworkPiece king,
    (int x, int y) square
  )
  {
    NetworkPiece? capturedAtDestination = square == (king.X, king.Y)
      ? null
      : match.Pieces.FirstOrDefault(other => other.Id != king.Id && other.AttachedToId is null &&
        other.Type != nameof(PieceType.Farm) && other.Team != king.Team &&
        UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square));

    foreach (NetworkPiece attacker in match.Pieces.Where(piece =>
      piece.Id != king.Id && piece.Id != capturedAtDestination?.Id && piece.AttachedToId is null &&
      piece.Team != king.Team && piece.Team != NetworkTeam.Neutral))
    {
      UnitRule attackerRule = UnitRules.GetRequired(attacker.Type);
      if (attackerRule.Attack <= 0 || !ChessAbilityRules.ThreatensSquareGeometry(
        attackerRule, attacker.Team, (attacker.X, attacker.Y), square, king.Team)) continue;

      if (ChessAbilityRules.IsLandingCaptureUnit(attacker.Type))
      {
        if (ChessAbilityRules.HasClearLandingCapturePath(
          attackerRule,
          (attacker.X, attacker.Y),
          square,
          intermediate => ServerChessThreatBlocker(match, intermediate, attacker, king, capturedAtDestination)))
        {
          return true;
        }
        continue;
      }

      if (LineOfSightRules.HasClearAttackPath(
        attackerRule,
        OccupiedSquares(attackerRule, (attacker.X, attacker.Y)),
        square,
        match.Terrain.IsForest,
        match.Barricades.ContainsKey,
        intermediate => match.Pieces.Any(other =>
          other.Id != attacker.Id && other.Id != king.Id && other.Id != capturedAtDestination?.Id &&
          other.AttachedToId is null && other.Type != nameof(PieceType.Farm) &&
          UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, intermediate))))
      {
        return true;
      }
    }
    return false;
  }

  private static bool ServerChessThreatBlocker(
    Match match,
    (int x, int y) square,
    NetworkPiece attacker,
    NetworkPiece king,
    NetworkPiece? capturedAtDestination
  ) => match.Terrain.IsLake(square) || match.Barricades.ContainsKey(square) ||
       match.Pieces.Any(other =>
         other.Id != attacker.Id && other.Id != king.Id && other.Id != capturedAtDestination?.Id &&
         other.AttachedToId is null && other.Type != nameof(PieceType.Farm) &&
         UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square));
}
