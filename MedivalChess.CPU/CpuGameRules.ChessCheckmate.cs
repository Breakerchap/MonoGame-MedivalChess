using MedivalChess.Shared;

namespace MedivalChess.CPU;

public static partial class CpuGameRules
{
  private static int ApplyCpuChessKingDeathRule(CpuMutableGameState state, NetworkPiece target, int damage)
  {
    if (!ChessAbilityRules.IsChessKing(target.Type) || damage < target.Health) return damage;
    return ChessAbilityRules.CanChessKingDie(IsCpuChessKingCheckmated(state, target)) ? damage : Math.Max(0, target.Health - 1);
  }

  private static bool IsCpuChessKingCheckmated(CpuMutableGameState state, NetworkPiece king)
  {
    UnitRule kingRule = UnitRules.GetRequired(king.Type);
    IEnumerable<(int x, int y)> escapes = ShapeGeometryRules.GetStepDirections(RuleShape.Any, king.Team)
      .Select(offset => (king.X + offset.x, king.Y + offset.y));
    return ChessAbilityRules.IsCheckmated((king.X, king.Y), escapes,
      square => CanCpuChessKingOccupy(state, king, kingRule, square),
      square => IsCpuChessKingSquareThreatened(state, king, square));
  }

  private static bool CanCpuChessKingOccupy(CpuMutableGameState state, NetworkPiece king, UnitRule kingRule, (int x, int y) destination)
  {
    if (!BoardRules.FootprintFitsBoard(state.Source.Board, destination.x, destination.y, kingRule.Width, kingRule.Height) ||
        OccupiedSquares(kingRule, destination).Any(square => state.Source.Terrain.IsLake(square) || state.Barricades.ContainsKey(square))) return false;
    NetworkPiece? occupant = state.Pieces.FirstOrDefault(other => other.Id != king.Id && other.AttachedToId is null &&
      other.Type != nameof(PieceType.Farm) && UnitRules.TryGet(other.Type, out UnitRule otherRule) &&
      UnitRules.FootprintsOverlap(destination.x, destination.y, kingRule.Width, kingRule.Height,
        other.X, other.Y, otherRule.Width, otherRule.Height));
    return occupant is null || ChessAbilityRules.CanCaptureByLanding(kingRule, king.Team, (king.X, king.Y), destination, occupant.Team);
  }

  private static bool IsCpuChessKingSquareThreatened(CpuMutableGameState state, NetworkPiece king, (int x, int y) square)
  {
    NetworkPiece? capturedAtDestination = square == (king.X, king.Y) ? null : state.Pieces.FirstOrDefault(other =>
      other.Id != king.Id && other.AttachedToId is null && other.Type != nameof(PieceType.Farm) && other.Team != king.Team &&
      UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square));
    foreach (NetworkPiece attacker in state.Pieces.Where(piece => piece.Id != king.Id && piece.Id != capturedAtDestination?.Id &&
      piece.AttachedToId is null && piece.Team != king.Team && piece.Team != NetworkTeam.Neutral))
    {
      UnitRule attackerRule = UnitRules.GetRequired(attacker.Type);
      if (attackerRule.Attack <= 0 || !ChessAbilityRules.ThreatensSquareGeometry(attackerRule, attacker.Team,
        (attacker.X, attacker.Y), square, king.Team)) continue;
      if (ChessAbilityRules.IsLandingCaptureUnit(attacker.Type))
      {
        if (ChessAbilityRules.HasClearLandingCapturePath(attackerRule, (attacker.X, attacker.Y), square,
          intermediate => CpuChessThreatBlocker(state, intermediate, attacker, king, capturedAtDestination))) return true;
        continue;
      }
      if (LineOfSightRules.HasClearAttackPath(attackerRule, OccupiedSquares(attackerRule, (attacker.X, attacker.Y)), square,
        state.Source.Terrain.IsForest, state.Barricades.ContainsKey,
        intermediate => state.Pieces.Any(other => other.Id != attacker.Id && other.Id != king.Id &&
          other.Id != capturedAtDestination?.Id && other.AttachedToId is null && other.Type != nameof(PieceType.Farm) &&
          UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, intermediate)))) return true;
    }
    return false;
  }

  private static bool CpuChessThreatBlocker(CpuMutableGameState state, (int x, int y) square,
    NetworkPiece attacker, NetworkPiece king, NetworkPiece? capturedAtDestination) =>
    state.Source.Terrain.IsLake(square) || state.Barricades.ContainsKey(square) || state.Pieces.Any(other =>
      other.Id != attacker.Id && other.Id != king.Id && other.Id != capturedAtDestination?.Id && other.AttachedToId is null &&
      other.Type != nameof(PieceType.Farm) && UnitRules.TryGet(other.Type, out UnitRule otherRule) && Occupies(otherRule, other, square));
}
