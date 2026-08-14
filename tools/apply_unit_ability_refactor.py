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


# ---------------------------- CPU/local migration ----------------------------
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

replace_once("Game1.cs", "internal sealed class Game1 : Game\n{", "internal sealed partial class Game1 : Game\n{")

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

# -------------------------- authoritative server -----------------------------
replace_once(
    "MedivalChess.Server/MatchHub.cs",
    "public sealed class MatchStore\n{",
    "public sealed partial class MatchStore\n{"
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''            ResolvePieceDamage(foundMatch, piece, player, crossed.Id, 15);
''',
    '''            ResolvePieceDamage(foundMatch, piece, player, crossed.Id, AbilityRules.ElephantTrampleDamage);
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    if (rule.Type == "Ox")
    {
      NetworkPiece? cargo = match.Pieces.FirstOrDefault(other => other.AttachedToId == piece.Id &&
        other.AttachmentKind == NetworkAttachmentKind.Carried);
      if (cargo is not null && UnitRules.TryGet(cargo.Type, out UnitRule cargoRule))
      {
        rule = cargoRule with { MoveRange = cargoRule.MoveRange + 2 };
      }
    }
''',
    '''    int attachmentBonus = GetSharedServerAttachmentMovementBonus(match, piece);
    if (attachmentBonus != 0)
    {
      rule = rule with { MoveRange = rule.MoveRange + attachmentBonus };
    }
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    return AbilityRules.CanUseCavalierFollowUpMove(piece.Type, piece.CavalierFollowUpMoveAvailable)
      ? rule with { MoveRange = 2, MovePattern = RuleShape.Straight }
      : rule;
''',
    '''    return AbilityRules.CanUseCavalierFollowUpMove(piece.Type, piece.CavalierFollowUpMoveAvailable)
      ? rule with { MoveRange = AbilityRules.CavalierFollowUpMovement, MovePattern = RuleShape.Straight }
      : rule;
'''
)

# A unit may move through another only when the shared ability says so. No unit may finish
# overlapped with an ordinary unit, including Elephant/Sleipnir.
replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    if (match.Pieces.Any(other => !ignoredPieces.Contains(other.Id) &&
      (rule.Type == "Farm" || other.Type != "Farm") &&
      // An elephant may end its move on an enemy it tramples, but never on an ally.
      !(rule.Type == "Elephant" && other.Team != piece.Team) &&
      NetworkPieceRules.FootprintsOverlap(other, destination.x, destination.y, rule.Width, rule.Height))) return false;
''',
    '''    if (match.Pieces.Any(other => !ignoredPieces.Contains(other.Id) &&
      (rule.Type == "Farm" || other.Type != "Farm") &&
      NetworkPieceRules.FootprintsOverlap(other, destination.x, destination.y, rule.Width, rule.Height))) return false;
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''        bool ignoresTerrain = piece.Type == "Elephant" ||
          IsPalaceAssistedMovement(match, piece, rule, from, destination);
''',
    '''        bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) ||
          IsPalaceAssistedMovement(match, piece, rule, from, destination);
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''        if (blocker is not null && !(piece.Type == "Elephant" && blocker.Team != piece.Team)) return false;
''',
    '''        if (blocker is not null && !AbilityRules.CanTravelThroughUnit(rule, piece.Team, blocker.Team)) return false;
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    if (rule.Type == "Elephant") return 1;
    int cost = 0;
''',
    '''    int cost = 0;
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''      cost = Math.Max(cost, match.Terrain.IsForest(square) && !usesOwnedRoad && !ignoresTerrain
        ? 2
        : usesOwnedRoad && !match.Terrain.IsForest(square) ? 0 : 1);
''',
    '''      int ordinaryCost = match.Terrain.IsForest(square) && !usesOwnedRoad && !ignoresTerrain
        ? 2
        : usesOwnedRoad && !match.Terrain.IsForest(square) ? 0 : 1;
      cost = Math.Max(cost, AbilityRules.ApplyTerrainMovementCost(rule, ordinaryCost));
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    if (rule.Type == "Elephant" || IsPalaceAssistedMovement(match, piece, rule, from, to)) return false;
''',
    '''    if (AbilityRules.IgnoresRivers(rule) || IsPalaceAssistedMovement(match, piece, rule, from, to)) return false;
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''  private static int GetAttackDamage(Match match, NetworkPiece attacker, NetworkPiece target)
  {
    int baseDamage = NetworkAttackRules.GetDamage(attacker.Type);
    if (baseDamage <= 0) return 0;
    return CombatRules.CalculateDamage(
      baseDamage,
      HasAdjacentUnit(match, attacker, attacker.Team, "Baron"),
      match.Pieces.Any(piece => piece.Type == "Spy" && piece.MarkedTargetId == target.Id),
      false,
      false,
      0
    );
  }
''',
    '''  private static int GetAttackDamage(Match match, NetworkPiece attacker, NetworkPiece target) =>
    GetSharedServerAttackDamage(match, attacker, target);
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    NetworkPiece target = match.Pieces[index];
    NetworkPiece? guard = match.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard);
    NetworkPiece damagedPiece = guard ?? target;
    NetworkPiece? cargo = AbilityRules.SharesDamageWithCargo(target.Type)
      ? match.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
        piece.AttachmentKind == NetworkAttachmentKind.Carried)
      : null;
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(match, attacker, target);
    ApplyDamageToPiece(match, attacker, attackingPlayer, damagedPiece, unmitigatedDamage);
    if (cargo is not null && cargo.Id != damagedPiece.Id && match.Pieces.Any(piece => piece.Id == cargo.Id))
    {
      ApplyDamageToPiece(match, attacker, attackingPlayer, cargo, unmitigatedDamage);
    }
''',
    '''    NetworkPiece target = match.Pieces[index];
    if (!CanSharedServerDamage(attacker, target)) return;
    NetworkPiece? guard = match.Pieces.FirstOrDefault(piece => piece.AttachedToId == target.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Guard);
    NetworkPiece damagedPiece = guard ?? target;
    NetworkPiece? oxAttachment = match.Pieces.FirstOrDefault(piece =>
      piece.AttachedToId == target.Id && AbilityRules.SharesIncomingDamageWithHost(piece.Type));
    int unmitigatedDamage = damageOverride ?? GetAttackDamage(match, attacker, target);
    ApplyDamageToPiece(match, attacker, attackingPlayer, damagedPiece, unmitigatedDamage);
    if (oxAttachment is not null && oxAttachment.Id != damagedPiece.Id && match.Pieces.Any(piece => piece.Id == oxAttachment.Id))
    {
      ApplyDamageToPiece(match, attacker, attackingPlayer, oxAttachment, unmitigatedDamage);
    }
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    int damage = NetworkAttackRules.GetDamage(attacker.Type) +
      (HasAdjacentUnit(match, attacker, attacker.Team, "Baron") ? 5 : 0);
''',
    '''    UnitRule attackerRule = UnitRules.GetRequired(attacker.Type);
    int damage = AbilityRules.GetBaseAttack(attackerRule, attacker.Health) +
      (HasAdjacentUnit(match, attacker, attacker.Team, nameof(PieceType.Baron)) ? CombatRules.BaronDamageBonus : 0);
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    if (target.Health > 30)
    {
      match.Pieces[index] = target with { Health = target.Health - 30 };
''',
    '''    if (target.Health > AbilityRules.EngineerMineDamage)
    {
      match.Pieces[index] = target with { Health = target.Health - AbilityRules.EngineerMineDamage };
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''  private static void HandlePieceDestroyed(Match match, NetworkPiece defeatedPiece, PlayerSlot attackingPlayer)
  {
    if (match.TreasureCarrierId == defeatedPiece.Id)
''',
    '''  private static void HandlePieceDestroyed(Match match, NetworkPiece defeatedPiece, PlayerSlot attackingPlayer)
  {
    if (TryApplySharedServerLethalAbility(match, defeatedPiece))
    {
      return;
    }

    IReadOnlyList<AbilityDamageInstruction> deathExplosion = GetSharedServerDeathExplosion(match, defeatedPiece);
    PlayerSlot explosionSource = match.Players.FirstOrDefault(player => player.Team == defeatedPiece.Team) ?? attackingPlayer;
    if (match.TreasureCarrierId == defeatedPiece.Id)
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    RemovePiece(match, defeatedPiece.Id);
    if (!UnitRules.TryGet(defeatedPiece.Type, out UnitRule rule) || rule.Category != RuleCategory.Royal) return;
''',
    '''    RemovePiece(match, defeatedPiece.Id);
    ApplySharedServerDeathExplosion(match, defeatedPiece, explosionSource, deathExplosion);
    if (!UnitRules.TryGet(defeatedPiece.Type, out UnitRule rule) || rule.Category != RuleCategory.Royal) return;
    if (defeatedPiece.Type == nameof(PieceType.GoblinRoyalty) && match.Pieces.Any(piece =>
      piece.Team == defeatedPiece.Team && piece.Type == nameof(PieceType.GoblinRoyalty))) return;
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    if ((!demolition && engineer.EngineerBuildsThisTurn >= 2) || target is not null ||
''',
    '''    if ((!demolition && engineer.EngineerBuildsThisTurn >= AbilityRules.EngineerBuildsPerTurn) || target is not null ||
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''      match.Barricades[(targetX, targetY)] = 20;
''',
    '''      match.Barricades[(targetX, targetY)] = AbilityRules.EngineerBarrierHealth;
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''      HasAttackedThisTurn = buildsUsed >= 2
''',
    '''      HasAttackedThisTurn = buildsUsed >= AbilityRules.EngineerBuildsPerTurn
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''  private static bool TryAttachOxCargo(Match match, int actorIndex, int targetIndex)
  {
    if (targetIndex < 0 || !UnitRules.TryGet(match.Pieces[targetIndex].Type, out UnitRule targetRule)) return false;
    NetworkPiece ox = match.Pieces[actorIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    bool hasCargo = match.Pieces.Any(piece => piece.AttachedToId == ox.Id &&
      piece.AttachmentKind == NetworkAttachmentKind.Carried);
    if (!UnitRules.TryGet(ox.Type, out UnitRule oxRule) || target.Team != ox.Team || target.Id == ox.Id || target.Id == match.TreasureCarrierId ||
        !AbilityRules.CanOxAttach(oxRule, targetRule, target.AttachedToId is not null, hasCargo)) return false;

    match.Pieces[targetIndex] = target with
    {
      AttachedToId = ox.Id,
      AttachmentKind = NetworkAttachmentKind.Carried,
      X = ox.X,
      Y = ox.Y
    };
    return true;
  }
''',
    '''  private static bool TryAttachOxCargo(Match match, int actorIndex, int targetIndex)
  {
    if (targetIndex < 0 || !UnitRules.TryGet(match.Pieces[targetIndex].Type, out UnitRule targetRule)) return false;
    NetworkPiece ox = match.Pieces[actorIndex];
    NetworkPiece target = match.Pieces[targetIndex];
    bool targetAlreadyHasOx = match.Pieces.Any(piece =>
      piece.AttachedToId == target.Id && piece.Type == nameof(PieceType.Ox));
    if (!UnitRules.TryGet(ox.Type, out UnitRule oxRule) || target.Team != ox.Team || target.Id == ox.Id || target.Id == match.TreasureCarrierId ||
        !AbilityRules.CanOxAttach(oxRule, targetRule, ox.AttachedToId is not null, targetAlreadyHasOx)) return false;

    match.Pieces[actorIndex] = ox with
    {
      AttachedToId = target.Id,
      AttachmentKind = NetworkAttachmentKind.Carried,
      X = target.X,
      Y = target.Y
    };
    return true;
  }
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''  private static void ResetTurnActions(Match match, NetworkTeam team)
  {
    for (int index = 0; index < match.Pieces.Count; index++)
''',
    '''  private static void ResetTurnActions(Match match, NetworkTeam team)
  {
    ApplySharedServerStartOfTurnEffects(match, team);
    for (int index = 0; index < match.Pieces.Count; index++)
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''          HasAttackedThisTurn = false,
          CavalierFollowUpMoveAvailable = false,
''',
    '''          HasAttackedThisTurn = false,
          AttacksThisTurn = 0,
          CavalierFollowUpMoveAvailable = false,
'''
)

replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''    for (int index = 0; index < match.Pieces.Count; index++)
    {
      NetworkPiece mercenary = match.Pieces[index];
      if (mercenary.Team != team || mercenary.AttachedToId is not null || mercenary.Type != "Mercenary")
      {
        continue;
      }

      const int mercenaryPayroll = 10;
      if (player.Money < mercenaryPayroll)
      {
        match.Pieces[index] = mercenary with
        {
          Team = NetworkTeam.Neutral,
          HasMovedThisTurn = true,
          HasAttackedThisTurn = true
        };
        continue;
      }

      player.Money = ClampCurrency((long)player.Money - mercenaryPayroll);
    }

''',
    '''    if (!ApplySharedServerAbilityUpkeep(match, team, player))
    {
      return;
    }

'''
)

# Server attack execution: one shared attack state/target plan for ordinary attacks; Ballista's
# terrain-aware piercing ray remains adapter code but uses shared target geometry/damage.
replace_once(
    "MedivalChess.Server/MatchHub.cs",
    '''      foundMatch.Pieces[attackerIndex] = attacker with
      {
        HasAttackedThisTurn = true,
        CavalierFollowUpMoveAvailable = AbilityRules.GrantsCavalierFollowUpMove(
          attacker.Type, attacker.HasMovedThisTurn)
      };
      if (target is null)
      {
        DamageBarricade(foundMatch, attacker, targetPosition);
      }
      else if (attacker.Type == "Bombard")
      {
        ResolveBombardDamage(foundMatch, attacker, player, target);
      }
      else
      {
        ResolvePieceDamage(foundMatch, attacker, player, target.Id, null);
      }
''',
    '''      PrepareSharedServerAttack(foundMatch, attackerIndex, targetPosition, out attacker, out bool mayFire);
      if (!mayFire)
      {
        if (foundMatch.Winner is null) SpendAction(foundMatch, player);
        foundMatch.Version++;
        foundMatch.Touch();
        return new(true, null, foundMatch.State());
      }
      if (target is null)
      {
        DamageBarricade(foundMatch, attacker, targetPosition);
      }
      else if (attacker.Type == nameof(PieceType.Ballista))
      {
        ResolvePieceDamage(foundMatch, attacker, player, target.Id, null);
      }
      else
      {
        ResolveSharedServerAttack(foundMatch, attacker, player, target);
      }
'''
)
