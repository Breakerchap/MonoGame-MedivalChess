from pathlib import Path


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


replace_once(
    "MedivalChess.CPU/CpuGameRules.Spatial.cs",
    '''    if (rule.Type == "Ox")
    {
      NetworkPiece? cargo = pieces.FirstOrDefault(other => other.AttachedToId == piece.Id && other.AttachmentKind == NetworkAttachmentKind.Carried);
      if (cargo is not null && UnitRules.TryGet(cargo.Type, out UnitRule cargoRule))
      {
        rule = cargoRule with { MoveRange = cargoRule.MoveRange + 2 };
      }
    }
''',
    '''    NetworkPiece? oxAttachment = pieces.FirstOrDefault(other =>
      other.AttachedToId == piece.Id && other.Type == nameof(PieceType.Ox));
    if (oxAttachment is not null)
    {
      rule = rule with
      {
        MoveRange = rule.MoveRange + AbilityRules.GetAttachmentMovementBonus(oxAttachment.Type)
      };
    }
'''
)

replace_once(
    "MedivalChess.CPU/CpuGameRules.Actions.cs",
    '''        case nameof(PieceType.Ox):
          int targetIndex = FindPieceIndex(state.Pieces, target!.Id);
          state.Pieces[targetIndex] = target with
          {
            AttachedToId = actor.Id,
            AttachmentKind = NetworkAttachmentKind.Carried,
            X = actor.X,
            Y = actor.Y
          };
          break;
''',
    '''        case nameof(PieceType.Ox):
          state.Pieces[actorIndex] = actor with
          {
            AttachedToId = target!.Id,
            AttachmentKind = NetworkAttachmentKind.Carried,
            X = target.X,
            Y = target.Y
          };
          break;
'''
)

replace_once(
    "MedivalChess.CPU/CpuGameRules.Abilities.cs",
    '''    NetworkPiece damaged = state.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard) ?? target;
    NetworkPiece? cargo = AbilityRules.SharesDamageWithCargo(target.Type)
      ? state.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
        piece.AttachmentKind == NetworkAttachmentKind.Carried)
      : null;
    int unmitigated = damageOverride ?? GetSharedAttackDamage(state, attacker, target);
    ApplySharedDamageToPiece(state, attacker, attackerTeam, damaged, unmitigated);
    if (cargo is not null && cargo.Id != damaged.Id && FindPiece(state.Pieces, cargo.Id) is not null)
    {
      ApplySharedDamageToPiece(state, attacker, attackerTeam, cargo, unmitigated);
    }
''',
    '''    NetworkPiece damaged = state.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard) ?? target;
    NetworkPiece? oxAttachment = state.Pieces.FirstOrDefault(piece =>
      piece.AttachedToId == target.Id && AbilityRules.SharesIncomingDamageWithHost(piece.Type));
    int unmitigated = damageOverride ?? GetSharedAttackDamage(state, attacker, target);
    ApplySharedDamageToPiece(state, attacker, attackerTeam, damaged, unmitigated);
    if (oxAttachment is not null && oxAttachment.Id != damaged.Id && FindPiece(state.Pieces, oxAttachment.Id) is not null)
    {
      ApplySharedDamageToPiece(state, attacker, attackerTeam, oxAttachment, unmitigated);
    }
'''
)

replace_once(
    "Game1.cs",
    '''    Piece cargo = piece.Definition.Type == PieceType.Ox
      ? pieceSetup.GetAttachedPiece(piece, AttachmentKind.Carried)
      : null;
    if (cargo is not null)
    {
      UnitRule cargoRule = UnitRules.FromPieceDefinition(cargo.Definition);
      rule = cargoRule with { MoveRange = cargoRule.MoveRange + 2 };
    }
''',
    '''    Piece oxAttachment = pieceSetup.Pieces.FirstOrDefault(candidate =>
      candidate.AttachedTo == piece && candidate.Definition.Type == PieceType.Ox);
    if (oxAttachment is not null)
    {
      rule = rule with
      {
        MoveRange = rule.MoveRange + AbilityRules.GetAttachmentMovementBonus(oxAttachment.Definition.Type.ToString())
      };
    }
'''
)

replace_once(
    "Game1.cs",
    '''    Piece guard = pieceSetup.GetAttachedPiece(target, AttachmentKind.Guard);
    Piece damagedPiece = guard ?? target;
    Piece cargo = AbilityRules.SharesDamageWithCargo(target.Definition.Type.ToString())
      ? pieceSetup.GetAttachedPiece(target, AttachmentKind.Carried)
      : null;
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(attacker, target);

    ApplyDamageToPiece(attacker, damagedPiece, unmitigatedDamage);
    if (cargo is not null && cargo != damagedPiece && pieceSetup.Pieces.Contains(cargo))
    {
      ApplyDamageToPiece(attacker, cargo, unmitigatedDamage);
    }
''',
    '''    Piece guard = pieceSetup.GetAttachedPiece(target, AttachmentKind.Guard);
    Piece damagedPiece = guard ?? target;
    Piece oxAttachment = pieceSetup.Pieces.FirstOrDefault(candidate =>
      candidate.AttachedTo == target && AbilityRules.SharesIncomingDamageWithHost(candidate.Definition.Type.ToString()));
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(attacker, target);

    ApplyDamageToPiece(attacker, damagedPiece, unmitigatedDamage);
    if (oxAttachment is not null && oxAttachment != damagedPiece && pieceSetup.Pieces.Contains(oxAttachment))
    {
      ApplyDamageToPiece(attacker, oxAttachment, unmitigatedDamage);
    }
'''
)
