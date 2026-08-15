namespace MedivalChess;

using System.Collections.Generic;
using MedivalChess.GameBoard;
using MedivalChess.Player;
using MedivalChess.Shared;

internal sealed class PieceSetup
{
  private readonly List<Piece> _pieces = new();
  private readonly Dictionary<(int x, int y), List<Piece>> _occupants = [];

  internal IReadOnlyList<Piece> Pieces => _pieces;

  internal void AddPieces()
  {
  }

  internal Piece GetPieceAt((int x, int y) position)
  {
    return FindOccupant(position, piece => piece.AttachedTo is null && piece.Definition.Type != PieceType.Farm)
      ?? FindOccupant(position, piece => piece.Definition.Type != PieceType.Farm)
      ?? FindOccupant(position, piece => piece.AttachedTo is null)
      ?? FindOccupant(position, _ => true);
  }

  internal Piece GetUnattachedPieceAt((int x, int y) position, TeamName? team = null) =>
    FindOccupant(position, piece => piece.AttachedTo is null && piece.Definition.Type != PieceType.Farm &&
      (!team.HasValue || piece.Team == team.Value))
    ?? FindOccupant(position, piece => piece.AttachedTo is null && (!team.HasValue || piece.Team == team.Value));

  internal Piece GetUnattachedHostilePieceAt((int x, int y) position, TeamName team) =>
    FindOccupant(position, piece => piece.AttachedTo is null && piece.Team != team && piece.Definition.Type != PieceType.Farm)
    ?? FindOccupant(position, piece => piece.AttachedTo is null && piece.Team != team);

  internal bool IsFootprintClear(
    PieceDefinition definition,
    (int x, int y) position,
    Piece ignoredPiece = null,
    TeamName? teamWhoseEnemiesMayBeOverlapped = null
  )
  {
    for (int y = 0; y < definition.Size.y; y++)
    {
      for (int x = 0; x < definition.Size.x; x++)
      {
        Piece occupiedPiece = GetPieceAt((position.x + x, position.y + y));
        if (occupiedPiece != null && occupiedPiece != ignoredPiece &&
            (!teamWhoseEnemiesMayBeOverlapped.HasValue || occupiedPiece.Team == teamWhoseEnemiesMayBeOverlapped.Value) &&
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

    bool removed = _pieces.Remove(piece);
    if (removed)
    {
      piece.DetachFromSetup(this);
      RebuildOccupancy();
    }
    return removed;
  }

  internal void AddPiece(Piece piece)
  {
    _pieces.Add(piece);
    piece.AttachToSetup(this);
    AddToOccupancy(piece);
  }

  internal void ClearPieces()
  {
    foreach (Piece piece in _pieces)
    {
      piece.DetachFromSetup(this);
    }
    _pieces.Clear();
    _occupants.Clear();
  }

  internal void MovePiece(Piece piece, (int x, int y) destination)
  {
    if (destination != piece.Position)
    {
      piece.Facing = AbilityRules.DirectionToward(piece.Position, destination);
    }

    piece.Position = destination;
    piece.HasMovedThisTurn = true;

    foreach (Piece attachedPiece in _pieces.FindAll(candidate => candidate.AttachedTo == piece))
    {
      attachedPiece.Position = destination;
      attachedPiece.Facing = piece.Facing;
      attachedPiece.HasMovedThisTurn = true;
    }
    RebuildOccupancy();
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
    attachment.Facing = host.Facing;
    RebuildOccupancy();
    return true;
  }

  internal void Detach(Piece piece)
  {
    piece.AttachedTo = null;
    piece.AttachmentKind = AttachmentKind.None;
    RebuildOccupancy();
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

    existingPiece.DetachFromSetup(this);
    replacement.AttachToSetup(this);
    _pieces[index] = replacement;
    RebuildOccupancy();
  }

  internal void RefreshOccupancy() => RebuildOccupancy();

  private Piece FindOccupant((int x, int y) position, System.Predicate<Piece> predicate)
  {
    return _occupants.TryGetValue(position, out List<Piece> occupants)
      ? occupants.Find(predicate)
      : null;
  }

  private void AddToOccupancy(Piece piece)
  {
    foreach ((int x, int y) square in piece.OccupiedSquares())
    {
      if (!_occupants.TryGetValue(square, out List<Piece> occupants))
      {
        occupants = [];
        _occupants[square] = occupants;
      }
      occupants.Add(piece);
    }
  }

  private void RebuildOccupancy()
  {
    _occupants.Clear();
    foreach (Piece piece in _pieces) AddToOccupancy(piece);
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
        if (piece.IsRoyal && piece.Team == currentTeam)
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
