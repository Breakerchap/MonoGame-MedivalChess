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


# Current Ox ability: Ox attaches to a friendly 1x1 host, gives the host +2 Move,
# and takes the same incoming damage as that host.
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

# Split the giant local game class so unit-specific runtime glue stays out of Game1.cs.
replace_once(
    "Game1.cs",
    "internal sealed class Game1 : Game\n{",
    "internal sealed partial class Game1 : Game\n{"
)

# Human/local attacks: Ballista keeps its terrain-aware pierce adapter; all other unit targets
# use the shared multi-target attack plan.
replace_once(
    "Game1.cs",
    '''              if (selectedPiece.Definition.Type == PieceType.Ballista)
              {
                PerformPiercingAttack(selectedPiece, targetPosition);
              }
              else if (selectedPiece.Definition.Type == PieceType.Bombard)
              {
                PerformBombardAttack(selectedPiece, hostilePieceAtTarget);
              }
              else if (_barricades.ContainsKey(targetPosition))
              {
                DamageBarricade(selectedPiece, targetPosition);
              }
              else
              {
                ResolveDamage(selectedPiece, hostilePieceAtTarget);
              }
''',
    '''              if (selectedPiece.Definition.Type == PieceType.Ballista)
              {
                PerformPiercingAttack(selectedPiece, targetPosition);
              }
              else if (_barricades.ContainsKey(targetPosition))
              {
                DamageBarricade(selectedPiece, targetPosition);
              }
              else
              {
                PerformSharedUnitAttack(selectedPiece, hostilePieceAtTarget);
              }
'''
)

# Local CPU execution must use exactly the same local/shared attack adapter as human input.
replace_once(
    "Game1.cs",
    '''    if (attacker.Definition.Type == PieceType.Ballista)
    {
      PerformPiercingAttack(attacker, targetPosition);
    }
    else if (attacker.Definition.Type == PieceType.Bombard && target is not null)
    {
      PerformBombardAttack(attacker, target);
    }
    else if (_barricades.ContainsKey(targetPosition))
    {
      DamageBarricade(attacker, targetPosition);
    }
    else
    {
      ResolveDamage(attacker, target);
    }
''',
    '''    if (attacker.Definition.Type == PieceType.Ballista)
    {
      PerformPiercingAttack(attacker, targetPosition);
    }
    else if (_barricades.ContainsKey(targetPosition))
    {
      DamageBarricade(attacker, targetPosition);
    }
    else
    {
      PerformSharedUnitAttack(attacker, target);
    }
'''
)

replace_once(
    "Game1.cs",
    '''  private int GetAttackDamage(Piece attacker, Piece target)
  {
    bool hasBaronBonus = HasAdjacentPieceOfType(attacker, PieceType.Baron, attacker.Team);
    bool isSpyMarked = pieceSetup.Pieces.Any(spy =>
      spy.Definition.Type == PieceType.Spy && spy.MarkedTarget == target);
    return CombatRules.CalculateDamage(attacker.Definition.Attack, hasBaronBonus, isSpyMarked, false, false, 0);
  }
''',
    '''  private int GetAttackDamage(Piece attacker, Piece target) =>
    GetSharedLocalAttackDamage(attacker, target);
'''
)

replace_once(
    "Game1.cs",
    '''  private void ResolveDamage(Piece attacker, Piece target, int? damageOverride = null)
  {
    Piece guard = pieceSetup.GetAttachedPiece(target, AttachmentKind.Guard);
''',
    '''  private void ResolveDamage(Piece attacker, Piece target, int? damageOverride = null)
  {
    if (target is null || !CanSharedAttackDamage(attacker, target))
    {
      return;
    }

    Piece guard = pieceSetup.GetAttachedPiece(target, AttachmentKind.Guard);
'''
)

replace_once(
    "Game1.cs",
    '''  private void ResolveMineDamage(Piece target, TeamName mineOwner)
  {
    target.CurrentHealth -= 30;
    Console.WriteLine($"Mine dealt 30 damage to {target.Definition.Type}.");
    HandlePieceDestroyed(target, mineOwner);
  }
''',
    '''  private void ResolveMineDamage(Piece target, TeamName mineOwner)
  {
    target.CurrentHealth -= AbilityRules.EngineerMineDamage;
    Console.WriteLine($"Mine dealt {AbilityRules.EngineerMineDamage} damage to {target.Definition.Type}.");
    HandlePieceDestroyed(target, mineOwner);
  }
'''
)

replace_once(
    "Game1.cs",
    '''  private void HandlePieceDestroyed(Piece damagedPiece, TeamName? attackingTeamName)
  {
    if (damagedPiece.CurrentHealth > 0)
    {
      return;
    }

    DropTreasure(damagedPiece);
''',
    '''  private void HandlePieceDestroyed(Piece damagedPiece, TeamName? attackingTeamName)
  {
    if (damagedPiece.CurrentHealth > 0)
    {
      return;
    }

    ApplySharedDeathExplosion(damagedPiece);
    DropTreasure(damagedPiece);
'''
)

replace_once(
    "Game1.cs",
    '''  private void ResetPieceTurnActions(TeamName teamName)
  {
    foreach (Piece piece in pieceSetup.Pieces.OrderBy(piece => piece.Definition.Type == PieceType.Farm ? 0 : 1))
''',
    '''  private void ResetPieceTurnActions(TeamName teamName)
  {
    ApplySharedStartOfTurnEffects(teamName);
    foreach (Piece piece in pieceSetup.Pieces.OrderBy(piece => piece.Definition.Type == PieceType.Farm ? 0 : 1).ToArray())
'''
)

replace_once(
    "Game1.cs",
    '''    int paidMercenaries = 0;
    int firedMercenaries = 0;
    foreach (Piece mercenary in pieceSetup.Pieces.Where(piece =>
      piece.Team == teamName && piece.AttachedTo is null && piece.Definition.Type == PieceType.Mercenary).ToList())
    {
      const int mercenaryPayroll = 10;
      if (team.Money < mercenaryPayroll)
      {
        mercenary.Team = TeamName.Neutral;
        mercenary.HasMovedThisTurn = true;
        mercenary.HasAttackedThisTurn = true;
        firedMercenaries++;
        continue;
      }

      team.Money = ClampCurrency((long)team.Money - mercenaryPayroll);
      paidMercenaries++;
    }
    if (paidMercenaries > 0)
    {
      Console.WriteLine($"{UiText.GetTeamDisplayName(teamName)} paid {paidMercenaries * 10} gold to {paidMercenaries} Mercenary unit(s).");
    }
    if (firedMercenaries > 0)
    {
      Console.WriteLine($"{UiText.GetTeamDisplayName(teamName)} could not afford {firedMercenaries} Mercenary unit(s); they were fired and left neutral.");
    }

''',
    '''    ApplySharedAbilityUpkeep(teamName, team);
    if (_screen == Screen.GameOver)
    {
      return;
    }

'''
)

# Keep all shared ability state when mirroring local games into CPU search snapshots.
replace_once(
    "Game1.cs",
    '''        piece.EngineerBuildsThisTurn,
        piece.CannotContributeToConquestThisTurn,
        piece.CavalierFollowUpMoveAvailable
      )),
''',
    '''        piece.EngineerBuildsThisTurn,
        piece.CannotContributeToConquestThisTurn,
        piece.CavalierFollowUpMoveAvailable,
        piece.AttacksThisTurn,
        piece.HasRevived,
        piece.TurnsInCurrentForm,
        piece.IsRoyalProxy,
        piece.PossessedUnitId,
        piece.Facing.x,
        piece.Facing.y,
        piece.PendingDamage
      )),
'''
)

# Restore all shared ability state from authoritative online snapshots.
replace_once(
    "Game1.cs",
    '''        LastBid = networkPiece.LastBid,
        EngineerBuildsThisTurn = networkPiece.EngineerBuildsThisTurn,
        CannotContributeToConquestThisTurn = networkPiece.CannotContributeToConquestThisTurn
      };
''',
    '''        LastBid = networkPiece.LastBid,
        EngineerBuildsThisTurn = networkPiece.EngineerBuildsThisTurn,
        CannotContributeToConquestThisTurn = networkPiece.CannotContributeToConquestThisTurn,
        AttacksThisTurn = networkPiece.AttacksThisTurn,
        HasRevived = networkPiece.HasRevived,
        TurnsInCurrentForm = networkPiece.TurnsInCurrentForm,
        IsRoyalProxy = networkPiece.IsRoyalProxy,
        PossessedUnitId = networkPiece.PossessedUnitId,
        Facing = AbilityStateRules.GetFacing(
          networkPiece.Team,
          networkPiece.FacingX,
          networkPiece.FacingY
        ),
        PendingDamage = networkPiece.PendingDamage ?? Array.Empty<NetworkPendingDamage>()
      };
'''
)
