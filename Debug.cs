namespace MedivalChess;

using System.Collections.Generic;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

internal sealed class PieceSetup
{
  private readonly List<Piece> _pieces = new();

  internal IReadOnlyList<Piece> Pieces => _pieces;

  internal void AddPieces()
  {
  }

  internal Piece GetPieceAt((int x, int y) position)
  {
    return _pieces.Find(piece => piece.AttachedTo == null && piece.Definition.Type != PieceType.Farm && piece.Occupies(position))
      ?? _pieces.Find(piece => piece.Definition.Type != PieceType.Farm && piece.Occupies(position))
      ?? _pieces.Find(piece => piece.AttachedTo == null && piece.Occupies(position))
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
        if (occupiedPiece != null && occupiedPiece != ignoredPiece &&
            (definition.Type == PieceType.Farm || occupiedPiece.Definition.Type != PieceType.Farm))
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

  internal void ClearPieces()
  {
    _pieces.Clear();
  }

  internal void MovePiece(Piece piece, (int x, int y) destination)
  {
    piece.Position = destination;
    piece.HasMovedThisTurn = true;

    foreach (Piece attachedPiece in _pieces.FindAll(candidate => candidate.AttachedTo == piece))
    {
      attachedPiece.Position = destination;
      attachedPiece.HasMovedThisTurn = true;
    }
  }

  internal bool Attach(Piece attachment, Piece host, AttachmentKind kind)
  {
    if (attachment == host)
    {
      return false;
    }

    bool isCargo = kind == AttachmentKind.Carried;
    if (isCargo && _pieces.Exists(candidate =>
      candidate.AttachedTo == host &&
      candidate.AttachmentKind == AttachmentKind.Carried))
    {
      return false;
    }

    if (kind == AttachmentKind.Guard && _pieces.Exists(candidate =>
      candidate.AttachedTo == host && candidate.AttachmentKind == AttachmentKind.Guard))
    {
      return false;
    }

    Detach(attachment);
    attachment.AttachedTo = host;
    attachment.AttachmentKind = kind;
    attachment.Position = host.Position;

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

  internal List<Team> CreateTeams(int playerCount = 2)
  {
    List<Team> teams = new();
    foreach (NetworkTeam networkTeam in TeamRules.GetActiveTeams(playerCount))
    {
      TeamName currentTeam = networkTeam.ToTeamName();
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
    }
    return teams;
  }
}
