namespace MedivalChess.GameBoard;

using System;
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
  private bool _hasMovedThisTurn;
  private int _currentHealth;
  private global::MedivalChess.PieceSetup _ownerSetup;
  private bool _turnStateInitialised;

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
      RefreshBerserkerAttack();
    }
  }
  internal (int x, int y) Position { get; set; }
  internal TeamName Team { get; set; }
  internal int LastBid { get; set; }
  internal Piece MarkedTarget { get; set; }
  internal Piece AttachedTo { get; set; }
  internal AttachmentKind AttachmentKind { get; set; }
  internal string NetworkId { get; set; } = System.Guid.NewGuid().ToString("N");
  internal bool HasMovedThisTurn
  {
    get => _hasMovedThisTurn;
    set
    {
      if (!value && _turnStateInitialised)
      {
        OnOwnerTurnStart();
      }
      _hasMovedThisTurn = value;
    }
  }
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

  internal void AttachToSetup(global::MedivalChess.PieceSetup setup)
  {
    _ownerSetup = setup;
    _turnStateInitialised = true;
  }

  internal void DetachFromSetup(global::MedivalChess.PieceSetup setup)
  {
    if (_ownerSetup == setup)
    {
      _ownerSetup = null;
      _turnStateInitialised = false;
    }
  }

  internal void TransformTo(PieceDefinition definition)
  {
    Definition = definition;
    _currentHealth = definition.Health;
    TurnsInCurrentForm = 0;
  }

  private void OnOwnerTurnStart()
  {
    if (Definition.Type == PieceType.Flesh)
    {
      TransformTo(PieceDefinitions.Zombie);
      return;
    }

    if (Definition.Type == PieceType.Ghoul)
    {
      TurnsInCurrentForm++;
      if (TurnsInCurrentForm >= AbilityRules.GhoulLifetimeTurns)
      {
        _ownerSetup?.RemovePiece(this);
      }
      return;
    }

    if (Definition.Type == PieceType.Tumbleweed)
    {
      TurnsInCurrentForm++;
      if (TurnsInCurrentForm >= AbilityRules.TumbleweedLifetimeRounds)
      {
        _ownerSetup?.RemovePiece(this);
      }
    }
  }

  private void RefreshBerserkerAttack()
  {
    if (Definition.Type != PieceType.Berserker) return;

    PieceDefinition source = PieceDefinitions.Berserker;
    int attack = _currentHealth <= 20 ? 40 : source.Attack;
    if (Definition.Attack == attack) return;

    Definition = new PieceDefinition(
      source.Type,
      source.Abbreviation ?? string.Empty,
      source.Pack,
      source.Movement,
      attack,
      source.Health,
      source.Size,
      source.AttackRange,
      source.AttackPattern,
      source.Cost,
      source.AbilityDescription,
      source.Identifier,
      source.DisplayName
    );
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
