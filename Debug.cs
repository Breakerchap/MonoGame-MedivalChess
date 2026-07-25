namespace MedivalChess;

using System.Collections.Generic;
using MedivalChess.GameBoard;
using MedivalChess.Player;

internal sealed class PieceSetup
{
  private readonly List<Piece> _pieces = new();

  internal IReadOnlyList<Piece> Pieces => _pieces;

  internal void AddPieces()
  {
  }

  internal Piece GetPieceAt((int x, int y) position)
  {
    return _pieces.Find(piece => piece.AttachedTo == null && piece.Occupies(position))
      ?? _pieces.Find(piece => piece.Occupies(position));
  }

  internal bool IsFootprintClear(
    PieceDefinition definition,
    (int x, int y) position,
    Piece ignoredPiece = null
  )
  {
    for (int y = 0; y < definition.Size.y; y++)
    {
      for (int x = 0; x < definition.Size.x; x++)
      {
        Piece occupiedPiece = GetPieceAt((position.x + x, position.y + y));
        if (occupiedPiece != null && occupiedPiece != ignoredPiece)
        {
          return false;
        }
      }
    }

    return true;
  }

  internal bool RemovePiece(Piece piece)
  {
    foreach (Piece attachedPiece in _pieces.FindAll(candidate => candidate.AttachedTo == piece))
    {
      attachedPiece.AttachedTo = null;
      attachedPiece.AttachmentKind = AttachmentKind.None;
    }

    if (piece.AttachedTo != null)
    {
      piece.AttachedTo = null;
      piece.AttachmentKind = AttachmentKind.None;
    }

    foreach (Piece candidate in _pieces)
    {
      if (candidate.MarkedTarget == piece)
      {
        candidate.MarkedTarget = null;
      }
    }

    return _pieces.Remove(piece);
  }

  internal void AddPiece(Piece piece)
  {
    _pieces.Add(piece);
  }

  internal void MovePiece(Piece piece, (int x, int y) destination)
  {
    var displacement = (
      x: destination.x - piece.Position.x,
      y: destination.y - piece.Position.y
    );
    piece.Position = destination;

    foreach (Piece attachedPiece in _pieces.FindAll(candidate => candidate.AttachedTo == piece))
    {
      attachedPiece.Position = attachedPiece.AttachmentKind == AttachmentKind.Towed
        ? (attachedPiece.Position.x + displacement.x, attachedPiece.Position.y + displacement.y)
        : destination;
    }
  }

  internal bool Attach(Piece attachment, Piece host, AttachmentKind kind)
  {
    bool isCargo = kind is AttachmentKind.Carried or AttachmentKind.Towed;
    if (isCargo && _pieces.Exists(candidate =>
      candidate.AttachedTo == host &&
      candidate.AttachmentKind is AttachmentKind.Carried or AttachmentKind.Towed))
    {
      return false;
    }

    Detach(attachment);
    attachment.AttachedTo = host;
    attachment.AttachmentKind = kind;
    if (kind != AttachmentKind.Towed)
    {
      attachment.Position = host.Position;
    }

    return true;
  }

  internal void Detach(Piece piece)
  {
    piece.AttachedTo = null;
    piece.AttachmentKind = AttachmentKind.None;
  }

  internal Piece GetAttachedPiece(Piece host, AttachmentKind kind)
  {
    return _pieces.Find(piece => piece.AttachedTo == host && piece.AttachmentKind == kind);
  }

  internal void ReplacePiece(Piece existingPiece, Piece replacement)
  {
    int index = _pieces.IndexOf(existingPiece);
    if (index < 0)
    {
      return;
    }

    replacement.AttachedTo = existingPiece.AttachedTo;
    replacement.AttachmentKind = existingPiece.AttachmentKind;

    foreach (Piece attachedPiece in _pieces.FindAll(candidate => candidate.AttachedTo == existingPiece))
    {
      attachedPiece.AttachedTo = replacement;
    }

    _pieces[index] = replacement;
  }

  internal List<Team> CreateTeams()
  {
    List<Team> teams = new();
    TeamName currentTeam = TeamName.Red;

    while (teams.Count < 2)
    {
      PieceType? chosenRoyal = null;

      foreach (Piece piece in _pieces)
      {
        if (piece.Definition.Category == PieceCategory.Royal && piece.Team == currentTeam)
        {
          chosenRoyal = piece.Definition.Type;
          break;
        }
      }
      teams.Add(new Team(currentTeam, chosenRoyal));
      currentTeam = TeamName.Blue;

    }
    return teams;
  }
}
