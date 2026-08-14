using MedivalChess.GameBoard;
using MedivalChess.Shared;

namespace MedivalChess;

internal sealed partial class Game1
{
  private int ApplyLocalChessKingDeathRule(Piece target, int damage)
  {
    if (target.Definition.Type != PieceType.ChessKing || damage < target.CurrentHealth)
    {
      return damage;
    }

    return ChessAbilityRules.CanChessKingDie(IsLocalChessKingCheckmated(target))
      ? damage
      : Math.Max(0, target.CurrentHealth - 1);
  }

  private bool IsLocalChessKingCheckmated(Piece king)
  {
    UnitRule kingRule = UnitRules.FromPieceDefinition(king.Definition);
    IEnumerable<(int x, int y)> escapes = ShapeGeometryRules
      .GetStepDirections(RuleShape.Any, king.Team.ToNetworkTeam())
      .Select(offset => (king.Position.x + offset.x, king.Position.y + offset.y));
    return ChessAbilityRules.IsCheckmated(
      king.Position,
      escapes,
      square => CanLocalChessKingOccupy(king, kingRule, square),
      square => IsLocalChessKingSquareThreatened(king, square)
    );
  }

  private bool CanLocalChessKingOccupy(Piece king, UnitRule kingRule, (int x, int y) destination)
  {
    if (!IsFootprintOnBoard(king.Definition, destination) ||
        OccupiedSquares(king.Definition, destination).Any(square => _terrain.IsLake(square) || _barricades.ContainsKey(square)))
    {
      return false;
    }

    Piece occupant = pieceSetup.Pieces.FirstOrDefault(other =>
      other != king && other.AttachedTo is null && other.Definition.Type != PieceType.Farm &&
      FootprintsOverlap(king.Definition, destination, other.Definition, other.Position));
    if (occupant is null) return true;
    return ChessAbilityRules.CanCaptureByLanding(
      kingRule,
      king.Team.ToNetworkTeam(),
      king.Position,
      destination,
      occupant.Team.ToNetworkTeam()
    );
  }

  private bool IsLocalChessKingSquareThreatened(Piece king, (int x, int y) square)
  {
    Piece capturedAtDestination = square == king.Position
      ? null
      : pieceSetup.Pieces.FirstOrDefault(other =>
        other != king && other.AttachedTo is null && other.Definition.Type != PieceType.Farm &&
        other.Team != king.Team && other.Occupies(square));

    foreach (Piece attacker in pieceSetup.Pieces.Where(piece =>
      piece != king && piece != capturedAtDestination && piece.AttachedTo is null &&
      piece.Team != king.Team && piece.Team != TeamName.Neutral))
    {
      UnitRule attackerRule = UnitRules.FromPieceDefinition(attacker.Definition);
      if (attackerRule.Attack <= 0 || !ChessAbilityRules.ThreatensSquareGeometry(
        attackerRule,
        attacker.Team.ToNetworkTeam(),
        attacker.Position,
        square,
        king.Team.ToNetworkTeam()))
      {
        continue;
      }

      if (ChessAbilityRules.IsLandingCaptureUnit(attackerRule.Type))
      {
        if (ChessAbilityRules.HasClearLandingCapturePath(
          attackerRule,
          attacker.Position,
          square,
          intermediate => IsLocalChessThreatBlocker(intermediate, attacker, king, capturedAtDestination)))
        {
          return true;
        }
        continue;
      }

      if (LineOfSightRules.HasClearAttackPath(
        attackerRule,
        attacker.OccupiedSquares(),
        square,
        _terrain.IsForest,
        _barricades.ContainsKey,
        intermediate => pieceSetup.Pieces.Any(other =>
          other != attacker && other != king && other != capturedAtDestination &&
          other.AttachedTo is null && other.Definition.Type != PieceType.Farm && other.Occupies(intermediate))))
      {
        return true;
      }
    }
    return false;
  }

  private bool IsLocalChessThreatBlocker(
    (int x, int y) square,
    Piece attacker,
    Piece king,
    Piece capturedAtDestination
  ) => _terrain.IsLake(square) || _barricades.ContainsKey(square) ||
       pieceSetup.Pieces.Any(other =>
         other != attacker && other != king && other != capturedAtDestination &&
         other.AttachedTo is null && other.Definition.Type != PieceType.Farm && other.Occupies(square));
}
