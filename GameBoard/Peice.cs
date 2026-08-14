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
  private bool _hasAttackedThisTurn;
  private int _currentHealth;

  internal PieceDefinition Definition { get; private set; }
  internal int CurrentHealth
  {
    get => _currentHealth;
    set
    {
      if (value <= 0 && !HasRevived && Definition.Type == PieceType.Shieldbearer)
      {
        HasRevived = true;
        _currentHealth = AbilityRules.ShieldbearerReviveHealth;
        return;
      }

      if (value <= 0 && !HasRevived && Definition.Type == PieceType.Emperor)
      {
        HasRevived = true;
        Definition = PieceDefinitions.TerracottaWarrior;
        _currentHealth = Definition.Health;
        return;
      }

      if (value <= 0 && Definition.Type == PieceType.Zombie)
      {
        TransformTo(PieceDefinitions.Flesh);
        return;
      }

      _currentHealth = value;
    }
  }
  internal (int x, int y) Position { get; set; }
  internal TeamName Team { get; set; }
  internal int LastBid { get; set; }
  internal Piece MarkedTarget { get; set; }
  internal Piece AttachedTo { get; set; }
  internal AttachmentKind AttachmentKind { get; set; }
  internal string NetworkId { get; set; } = System.Guid.NewGuid().ToString("N");
  internal bool HasMovedThisTurn { get; set; }
  internal bool HasAttackedThisTurn
  {
    get => Definition.Type == PieceType.Ninja
      ? AttacksThisTurn >= AbilityRules.MaximumAttacksPerTurn(Definition.Type.ToString())
      : _hasAttackedThisTurn;
    set
    {
      if (!value)
      {
        _hasAttackedThisTurn = false;
        AttacksThisTurn = 0;
        return;
      }

      if (Definition.Type == PieceType.Ninja)
      {
        AttacksThisTurn = Math.Min(
          AttacksThisTurn + 1,
          AbilityRules.MaximumAttacksPerTurn(Definition.Type.ToString()));
        _hasAttackedThisTurn = AttacksThisTurn >= AbilityRules.MaximumAttacksPerTurn(Definition.Type.ToString());
        return;
      }

      _hasAttackedThisTurn = true;
      AttacksThisTurn = 1;
      if (Definition.Type == PieceType.Vampire && CurrentHealth > 0)
      {
        CurrentHealth = Math.Min(Definition.Health, CurrentHealth + AbilityRules.VampireHealing);
      }
    }
  }
  internal int AttacksThisTurn { get; set; }
  internal bool CavalierFollowUpMoveAvailable { get; set; }
  internal int EngineerBuildsThisTurn { get; set; }
  internal bool CannotContributeToConquestThisTurn { get; set; }

  internal int TurnsInCurrentForm { get; set; }
  internal bool HasRevived { get; set; }
  internal bool IsRoyalProxy { get; set; }
  internal (int x, int y) Facing { get; set; }
  internal int PendingDamage { get; set; }
  internal TeamName? PendingDamageSourceTeam { get; set; }

  internal long NextMercenaryBid => (long)LastBid + 10;
  internal bool IsRoyal => Definition.Category == PieceCategory.Royal || IsRoyalProxy;

  internal Piece(PieceDefinition definition, (int x, int y) position, TeamName team)
  {
    Definition = definition;
    _currentHealth = Definition.Health;
    Position = position;
    Team = team;
    LastBid = definition.Cost;
    Facing = TeamRules.GetForwardDirection(team.ToNetworkTeam());
  }

  internal void TransformTo(PieceDefinition definition)
  {
    Definition = definition;
    _currentHealth = definition.Health;
    TurnsInCurrentForm = 0;
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
