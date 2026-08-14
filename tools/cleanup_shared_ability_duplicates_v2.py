from pathlib import Path
import re


def edit(path: str, transform) -> None:
    file = Path(path)
    before = file.read_text(encoding="utf-8")
    after = transform(before)
    if after != before:
        file.write_text(after, encoding="utf-8")
        print(f"{path}: updated")
    else:
        print(f"{path}: no matching stale code")


def replace(text: str, old: str, new: str) -> str:
    return text.replace(old, new, 1) if old in text else text


def remove_method_range(text: str, start_signature: str, next_signature: str) -> str:
    start = text.find(start_signature)
    if start < 0:
        return text
    end = text.find(next_signature, start)
    if end < 0:
        raise RuntimeError(f"Missing end marker for {start_signature}")
    return text[:start] + text[end:]


def cpu(text: str) -> str:
    text = text.replace(
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
''', 1)
    text = text.replace(
        'if ((!demolition && actor.EngineerBuildsThisTurn >= 2) || target is not null ||',
        'if ((!demolition && actor.EngineerBuildsThisTurn >= AbilityRules.EngineerBuildsPerTurn) || target is not null ||',
        1)
    text = remove_method_range(text, '  private static void ResolveBombardDamage(', '  private static void RemovePiece(')
    text = remove_method_range(text, '  private static void DamageBarricade(', '  private static void MoveAttachedPieces(')
    return text


def game(text: str) -> str:
    text = text.replace(
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
''', 1)
    text = text.replace(
'''  private Piece GetOxCargo(Piece ox)
  {
    return pieceSetup.GetAttachedPiece(ox, AttachmentKind.Carried);
  }
''',
'''  private Piece GetOxCargo(Piece ox)
  {
    return ox.AttachmentKind == AttachmentKind.Carried ? ox.AttachedTo : null;
  }
''', 1)
    text = text.replace('"RIGHT-CLICK ally to carry"', '"RIGHT-CLICK friendly 1 x 1 unit to attach"')
    text = text.replace('"CARGO LINKED - USE CONTROL BELOW"', '"ATTACHED - HOST GAINS +2 MOVE"')
    text = text.replace('"CARGO: EMPTY"', '"ATTACHMENT: NONE"')
    text = text.replace(
        '"RIGHT-CLICK a friendly 1 x 1 or Mechanical unit to carry."',
        '"RIGHT-CLICK a friendly 1 x 1 unit to attach. That unit gains +2 Movement; when it is attacked, the Ox takes the same damage."')
    text = text.replace('CARGO: CARRYING ', 'ATTACHED TO: ')
    text = text.replace(
        '"The Ox moves both units using the cargo\'s movement pattern with +2 range. Select cargo to move it separately and dismount it. Either piece can attack."',
        '"The host gains +2 Movement. The Ox moves with the host and takes the same incoming damage. Select the host below."')
    text = text.replace('"SELECT CARGO"', '"SELECT HOST"')
    text = text.replace(
'''    _barricades[targetPosition] = 20;
    Console.WriteLine("Engineer built a 20 HP barrier.");
''',
'''    _barricades[targetPosition] = AbilityRules.EngineerBarrierHealth;
    Console.WriteLine($"Engineer built a {AbilityRules.EngineerBarrierHealth} HP barrier.");
''', 1)
    text = text.replace(
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
''', 1)
    text = text.replace(
        'int barrierHealthWidth = (cellBounds.Width - 16) * _barricades[boardPosition] / 20;',
        'int barrierHealthWidth = (cellBounds.Width - 16) * _barricades[boardPosition] / AbilityRules.EngineerBarrierHealth;')
    text = remove_method_range(text, '  private void PerformBombardAttack(', '  private void PerformPiercingAttack(')
    return text


def server(text: str) -> str:
    return remove_method_range(text, '  private static void ResolveBombardDamage(', '  private static void DamageBarricade(')


edit('MedivalChess.CPU/CpuGameRules.cs', cpu)
edit('Game1.cs', game)
edit('MedivalChess.Server/MatchHub.cs', server)
