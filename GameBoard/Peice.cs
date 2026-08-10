namespace MedivalChess.GameBoard;

using System.Collections.Generic;
using MedivalChess.Player;
using MedivalChess.Shared;

internal enum AttachmentKind
{
  None,
  Guard,
  Carried
}

internal sealed class Piece
{
  internal PieceDefinition Definition { get; }
  internal int CurrentHealth { get; set; }
  internal (int x, int y) Position { get; set; }
  internal TeamName Team { get; set; }
  internal int LastBid { get; set; }
  internal Piece MarkedTarget { get; set; }
  internal Piece AttachedTo { get; set; }
  internal AttachmentKind AttachmentKind { get; set; }
  internal string NetworkId { get; set; } = System.Guid.NewGuid().ToString("N");
  internal bool HasMovedThisTurn { get; set; }
  internal bool HasAttackedThisTurn { get; set; }
  internal bool CavalierFollowUpMoveAvailable { get; set; }
  internal int EngineerBuildsThisTurn { get; set; }
  internal bool CannotContributeToConquestThisTurn { get; set; }
  internal long NextMercenaryBid => (long)LastBid + 10;

  internal Piece(PieceDefinition definition, (int x, int y) position, TeamName team)
  {
    Definition = definition;
    CurrentHealth = Definition.Health;
    Position = position;
    Team = team;
    LastBid = definition.Cost;
  }

  internal bool Occupies((int x, int y) position)
  {
    return
      position.x >= Position.x &&
      position.x < Position.x + Definition.Size.x &&
      position.y >= Position.y &&
      position.y < Position.y + Definition.Size.y;
  }

  internal IEnumerable<(int x, int y)> OccupiedSquares()
  {
    for (int y = 0; y < Definition.Size.y; y++)
    {
      for (int x = 0; x < Definition.Size.x; x++)
      {
        yield return (Position.x + x, Position.y + y);
      }
    }
  }
}
