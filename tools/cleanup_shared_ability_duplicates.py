from pathlib import Path

# Temporary migration helper. Every edit below is exact-match checked so cleanup fails loudly
# rather than silently mutating an unexpected source shape. This file is removed after migration.


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count == 0:
        if new in text:
            print(f"{path}: already transformed")
            return
        raise RuntimeError(f"{path}: expected source fragment not found")
    if count != 1:
        raise RuntimeError(f"{path}: expected one source fragment, found {count}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: transformed")


def remove_between(path: str, start: str, end: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    start_index = text.find(start)
    if start_index < 0:
        print(f"{path}: block already removed ({start.strip()})")
        return
    end_index = text.find(end, start_index)
    if end_index < 0:
        raise RuntimeError(f"{path}: end marker not found for {start.strip()}")
    file.write_text(text[:start_index] + text[end_index:], encoding="utf-8")
    print(f"{path}: removed duplicate block {start.strip()}")


# CPU legality uses the shared current Ox/Engineer rules.
replace_once(
    "MedivalChess.CPU/CpuGameRules.cs",
    '''      "Ox" => string.Equals(action.Ability, "Attach", StringComparison.OrdinalIgnoreCase) &&
        target is not null && target.Team == actor.Team && target.Id != actor.Id && target.Id != state.TreasureCarrierId &&
        UnitRules.TryGet(target.Type, out UnitRule cargoRule) && UnitRules.TryGet(actor.Type, out UnitRule oxRule) &&
        AbilityRules.CanOxAttach(oxRule, cargoRule, target.AttachedToId is not null,
          state.Pieces.Any(piece => piece.AttachedToId == actor.Id && piece.AttachmentKind == NetworkAttachmentKind.Carried)),
''',
    '''      "Ox" => string.Equals(action.Ability, "Attach", StringComparison.OrdinalIgnoreCase) &&
        target is not null && target.Team == actor.Team && target.Id != actor.Id && target.Id != state.TreasureCarrierId &&
        UnitRules.TryGet(target.Type, out UnitRule oxTargetRule) && UnitRules.TryGet(actor.Type, out UnitRule oxRule) &&
        AbilityRules.CanOxAttach(oxRule, oxTargetRule, actor.AttachedToId is not null,
          state.Pieces.Any(piece => piece.AttachedToId == target.Id && piece.Type == nameof(PieceType.Ox))),
'''
)

replace_once(
    "MedivalChess.CPU/CpuGameRules.cs",
    '''    if ((!demolition && actor.EngineerBuildsThisTurn >= 2) || target is not null ||
''',
    '''    if ((!demolition && actor.EngineerBuildsThisTurn >= AbilityRules.EngineerBuildsPerTurn) || target is not null ||
'''
)

# These CPU implementations are no longer called: actions now route through
# CpuGameRules.Abilities.cs, which applies shared plans/state transitions.
remove_between(
    "MedivalChess.CPU/CpuGameRules.cs",
    "  private static void ResolveBombardDamage(",
    "  private static void RemovePiece("
)
remove_between(
    "MedivalChess.CPU/CpuGameRules.cs",
    "  private static void DamageBarricade(",
    "  private static void MoveAttachedPieces("
)

# Local Ox attach validation follows the current host-attachment semantics.
replace_once(
    "Game1.cs",
    '''        AbilityRules.CanOxAttach(
          UnitRules.FromPieceDefinition(actor.Definition),
          UnitRules.FromPieceDefinition(targetPiece.Definition),
          targetPiece.AttachedTo != null,
          pieceSetup.GetAttachedPiece(actor, AttachmentKind.Carried) != null
        ) &&
''',
    '''        AbilityRules.CanOxAttach(
          UnitRules.FromPieceDefinition(actor.Definition),
          UnitRules.FromPieceDefinition(targetPiece.Definition),
          actor.AttachedTo != null,
          pieceSetup.Pieces.Any(candidate =>
            candidate.AttachedTo == targetPiece && candidate.Definition.Type == PieceType.Ox)
        ) &&
'''
)

replace_once(
    "Game1.cs",
    '''  private Piece GetOxCargo(Piece ox)
  {
    return pieceSetup.GetAttachedPiece(ox, AttachmentKind.Carried);
  }
''',
    '''  private Piece GetOxCargo(Piece ox)
  {
    return ox.AttachmentKind == AttachmentKind.Carried ? ox.AttachedTo : null;
  }
'''
)

replace_once(
    "Game1.cs",
    '''      PieceType.Ox => GetOxCargo(piece) == null
        ? "RIGHT-CLICK ally to carry"
        : "CARGO LINKED - USE CONTROL BELOW",
''',
    '''      PieceType.Ox => GetOxCargo(piece) == null
        ? "RIGHT-CLICK friendly 1 x 1 unit to attach"
        : "ATTACHED - HOST GAINS +2 MOVE",
'''
)

replace_once(
    "Game1.cs",
    '''      _ui.Text("CARGO: EMPTY", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.72f);
      _ui.TextWrapped(
        "RIGHT-CLICK a friendly 1 x 1 or Mechanical unit to carry.",
''',
    '''      _ui.Text("ATTACHMENT: NONE", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.72f);
      _ui.TextWrapped(
        "RIGHT-CLICK a friendly 1 x 1 unit to attach. That unit gains +2 Movement; when it is attacked, the Ox takes the same damage.",
'''
)

replace_once(
    "Game1.cs",
    '''    _ui.Text($"CARGO: CARRYING {cargo.Definition.Type.ToString().ToUpperInvariant()}", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.66f);
    _ui.TextWrapped(
      "The Ox moves both units using the cargo's movement pattern with +2 range. Select cargo to move it separately and dismount it. Either piece can attack.",
''',
    '''    _ui.Text($"ATTACHED TO: {cargo.Definition.Type.ToString().ToUpperInvariant()}", new Vector2(control.X + UiTheme.SpaceSm, control.Y + UiTheme.SpaceSm), UiTheme.Gold, 0.66f);
    _ui.TextWrapped(
      "The host gains +2 Movement. The Ox moves with the host and takes the same incoming damage. Select the host below.",
'''
)

replace_once(
    "Game1.cs",
    '''    DrawMenuButton(button, "SELECT CARGO", UiButtonTone.Accent);
''',
    '''    DrawMenuButton(button, "SELECT HOST", UiButtonTone.Accent);
'''
)

# Engineer/Barricade values are shared rather than repeated locally.
replace_once(
    "Game1.cs",
    '''    _barricades[targetPosition] = 20;
    Console.WriteLine("Engineer built a 20 HP barrier.");
''',
    '''    _barricades[targetPosition] = AbilityRules.EngineerBarrierHealth;
    Console.WriteLine($"Engineer built a {AbilityRules.EngineerBarrierHealth} HP barrier.");
'''
)

replace_once(
    "Game1.cs",
    '''    int damage = attacker.Definition.Attack;
    if (HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team))
    {
      damage += 5;
    }
''',
    '''    int damage = AbilityRules.GetBaseAttack(
      UnitRules.FromPieceDefinition(attacker.Definition),
      attacker.CurrentHealth
    );
    if (HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team))
    {
      damage += CombatRules.BaronDamageBonus;
    }
'''
)

replace_once(
    "Game1.cs",
    '''            int barrierHealthWidth = (cellBounds.Width - 16) * _barricades[boardPosition] / 20;
''',
    '''            int barrierHealthWidth = (cellBounds.Width - 16) * _barricades[boardPosition] / AbilityRules.EngineerBarrierHealth;
'''
)

# Old local/server Bombard methods are dead after all attacks route through the shared planner.
remove_between(
    "Game1.cs",
    "  private void PerformBombardAttack(",
    "  private void PerformPiercingAttack("
)
remove_between(
    "MedivalChess.Server/MatchHub.cs",
    "  private static void ResolveBombardDamage(",
    "  private static void DamageBarricade("
)
