namespace MedivalChess.GameBoard;

using System;
using System.Collections.Generic;
using System.Linq;
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
  internal global::MedivalChess.PieceSetup OwnerSetup => _ownerSetup;
  internal int CurrentHealth
  {
    get => _currentHealth;
    set
    {
      if (value <= 0)
      {
        LethalAbilityOutcome outcome = AbilityStateRules.ResolveLethalDamage(
          Definition.Type.ToString(),
          HasRevived
        );
        if (outcome.Kind != LethalAbilityOutcomeKind.Die)
        {
          HasRevived = outcome.HasRevived;
          if (outcome.Kind == LethalAbilityOutcomeKind.Transform)
          {
            Definition = ResolveDefinition(outcome.ResultingType);
            TurnsInCurrentForm = 0;
          }
          _currentHealth = outcome.ResultingHealth;
          RefreshBerserkerAttack();
          return;
        }
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
    get => _hasAttackedThisTurn;
    set
    {
      if (!_turnStateInitialised)
      {
        _hasAttackedThisTurn = value;
        AttacksThisTurn = value
          ? AbilityRules.MaximumAttacksPerTurn(Definition.Type.ToString())
          : 0;
        return;
      }

      if (!value)
      {
        _hasAttackedThisTurn = false;
        AttacksThisTurn = 0;
        return;
      }

      AttackTurnState attackState = AbilityStateRules.RecordAttack(
        Definition.Type.ToString(),
        AttacksThisTurn
      );
      AttacksThisTurn = attackState.AttacksThisTurn;
      _hasAttackedThisTurn = attackState.HasAttackedThisTurn;
    }
  }
  internal int AttacksThisTurn { get; set; }
  internal bool CavalierFollowUpMoveAvailable { get; set; }
  internal int EngineerBuildsThisTurn { get; set; }
  internal bool CannotContributeToConquestThisTurn { get; set; }

  internal int TurnsInCurrentForm { get; set; }
  internal bool HasRevived { get; set; }
  internal bool IsRoyalProxy { get; set; }
  internal string PossessedUnitId { get; set; }
  internal (int x, int y) Facing { get; set; }
  internal IReadOnlyList<NetworkPendingDamage> PendingDamage { get; set; } = Array.Empty<NetworkPendingDamage>();

  internal long NextMercenaryBid => (long)LastBid + 10;
  internal bool IsRoyal => RoyalAbilityRules.IsRoyal(
    Definition.Type.ToString(),
    IsRoyalProxy,
    PossessedUnitId
  );

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
    OwnerTurnState state = AbilityStateRules.AdvanceOwnerTurn(
      Definition.Type.ToString(),
      CurrentHealth,
      TurnsInCurrentForm
    );

    if (state.RemovePiece)
    {
      _ownerSetup?.RemovePiece(this);
      return;
    }

    TurnsInCurrentForm = state.TurnsInCurrentForm;
    if (!string.Equals(state.ResultingType, Definition.Type.ToString(), StringComparison.Ordinal))
    {
      Definition = ResolveDefinition(state.ResultingType);
      _currentHealth = state.ResultingHealth;
    }
  }

  private void RefreshBerserkerAttack()
  {
    if (Definition.Type != PieceType.Berserker) return;

    PieceDefinition source = PieceDefinitions.Berserker;
    int attack = AbilityRules.GetBaseAttack(UnitRules.FromPieceDefinition(source), _currentHealth);
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

  private static PieceDefinition ResolveDefinition(string type) =>
    PieceDefinitions.All.First(definition => string.Equals(
      definition.Type.ToString(),
      type,
      StringComparison.Ordinal
    ));

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
