from pathlib import Path


def edit(path: str, transform) -> None:
    file = Path(path)
    before = file.read_text(encoding="utf-8")
    after = transform(before)
    if before != after:
        file.write_text(after, encoding="utf-8")
        print(f"{path}: updated")
    else:
        print(f"{path}: no matching royal migration fragment")


def game(text: str) -> str:
    text = text.replace(
'''    bool isSpecialTarget = plunderTreasureTarget || actor.Definition.Type switch
    {
      PieceType.Spy => target is not null && target.Team != actor.Team,
      PieceType.Engineer => true,
      PieceType.Guard or PieceType.Ox => target is not null && target.Team == actor.Team,
      PieceType.Mercenary => targetPosition == actor.Position,
      _ => false
    };
''',
'''    bool isSpecialTarget = plunderTreasureTarget || actor.Definition.Type switch
    {
      PieceType.Spy => target is not null && target.Team != actor.Team,
      PieceType.Engineer => true,
      PieceType.Guard or PieceType.Ox => target is not null && target.Team == actor.Team,
      PieceType.Phantom => !string.IsNullOrEmpty(actor.PossessedUnitId)
        ? target == actor || target?.NetworkId == actor.PossessedUnitId
        : target is not null && target.Team == actor.Team,
      PieceType.Mercenary => targetPosition == actor.Position,
      _ => false
    };
''', 1)

    text = text.replace(
'''    string ability = plunderTreasureTarget
      ? "PickUpTreasure"
      : actor.Definition.Type == PieceType.Engineer
      ? _selectedEngineerAbility.ToString()
      : actor.Definition.Type == PieceType.Mercenary
        ? "Fire"
        : string.Empty;
''',
'''    string ability = plunderTreasureTarget
      ? "PickUpTreasure"
      : actor.Definition.Type == PieceType.Engineer
      ? _selectedEngineerAbility.ToString()
      : actor.Definition.Type == PieceType.Mercenary
        ? "Fire"
        : actor.Definition.Type == PieceType.Phantom
          ? string.IsNullOrEmpty(actor.PossessedUnitId) ? "Possess" : "Unpossess"
          : string.Empty;
''', 1)

    text = text.replace(
'''    if (actor.Definition.Type == PieceType.Spy && targetPiece != null && targetPiece.Team != actor.Team)
''',
'''    if (TryUseSharedRoyalAbility(actor, targetPiece))
    {
      return true;
    }

    if (actor.Definition.Type == PieceType.Spy && targetPiece != null && targetPiece.Team != actor.Team)
''', 1)

    text = text.replace(
'''    if (!CanPlacePiece(royal, position, placementTeam))
''',
'''    if (!CanPlaceSharedRoyalGroup(royal, position, placementTeam))
''', 1)

    text = text.replace(
''').Where(position => CanPlacePiece(royal, position, teamName)).ToList();
''',
''').Where(position => CanPlaceSharedRoyalGroup(royal, position, teamName)).ToList();
''', 1)

    text = text.replace(
'''    pieceSetup.AddPiece(new Piece(royal, position, teamName)
    {
      CurrentHealth = GetRoyalStartingHealth(royal)
    });
''',
'''    AddSharedRoyalGroup(royal, position, teamName);
''', 1)

    # Record Royal identity before removing the piece, then use the shared death rule instead of
    # checking the static category after removal.
    text = text.replace(
'''    pieceSetup.RemovePiece(damagedPiece);
    selectedPiece = null;
    InvalidateGameplayRenderCache();
    Console.WriteLine($"{damagedPiece.Definition.Type} was destroyed.");

    if (damagedPiece.Definition.Category != PieceCategory.Royal)
    {
      return;
    }
''',
'''    bool royalDeath = IsSharedRoyalDeath(damagedPiece);
    pieceSetup.RemovePiece(damagedPiece);
    selectedPiece = null;
    InvalidateGameplayRenderCache();
    Console.WriteLine($"{damagedPiece.Definition.Type} was destroyed.");

    if (!royalDeath)
    {
      return;
    }
''', 1)
    return text


def cpu_core(text: str) -> str:
    text = text.replace(
'''      "Mercenary" => string.Equals(action.Ability, "Fire", StringComparison.OrdinalIgnoreCase) &&
        actor.Team != NetworkTeam.Neutral && action.TargetPieceId is null &&
        action.TargetX == actor.X && action.TargetY == actor.Y,
      _ => false
''',
'''      "Mercenary" => string.Equals(action.Ability, "Fire", StringComparison.OrdinalIgnoreCase) &&
        actor.Team != NetworkTeam.Neutral && action.TargetPieceId is null &&
        action.TargetX == actor.X && action.TargetY == actor.Y,
      "Phantom" => string.Equals(action.Ability, "Unpossess", StringComparison.OrdinalIgnoreCase)
        ? !string.IsNullOrEmpty(actor.PossessedUnitId)
        : string.Equals(action.Ability, "Possess", StringComparison.OrdinalIgnoreCase) && target is not null &&
          RoyalAbilityRules.CanPhantomPossess(
            actor.Type, actor.Team, actor.PossessedUnitId,
            target.Id, target.Type, target.Team, target.IsRoyalProxy),
      _ => false
''', 1)
    return text


def cpu_actions(text: str) -> str:
    text = text.replace(
'''        case nameof(PieceType.Mercenary):
          state.Pieces[actorIndex] = actor with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true
          };
          break;
''',
'''        case nameof(PieceType.Mercenary):
          state.Pieces[actorIndex] = actor with
          {
            Team = NetworkTeam.Neutral,
            HasMovedThisTurn = true,
            HasAttackedThisTurn = true
          };
          break;
        case nameof(PieceType.Phantom):
          ApplySharedPhantomAbility(state, actorIndex, target, action.Ability);
          break;
''', 1)
    return text


def cpu_generator(text: str) -> str:
    marker = '''    else if (actor.Type is "Guard" or "Ox")
    {
'''
    addition = '''    else if (actor.Type == nameof(PieceType.Phantom))
    {
      if (!string.IsNullOrEmpty(actor.PossessedUnitId))
      {
        AddIfLegal(state, new UseAbilityAction(
          actor.Team, actor.Id, "Unpossess", actor.PossessedUnitId, actor.X, actor.Y), actions);
      }
      else
      {
        foreach (NetworkPiece target in state.Pieces.Where(piece => piece.Team == actor.Team && piece.Id != actor.Id)
          .OrderBy(piece => piece.Id, StringComparer.Ordinal))
        {
          foreach ((int x, int y) targetSquare in GetTargetSquares(target))
          {
            AddIfLegal(state, new UseAbilityAction(
              actor.Team, actor.Id, "Possess", target.Id, targetSquare.x, targetSquare.y), actions);
          }
        }
      }
    }
    else if (actor.Type is "Guard" or "Ox")
    {
'''
    return text.replace(marker, addition, 1)


def server(text: str) -> str:
    text = text.replace(
'''          "Ox" => TryAttachOxCargo(foundMatch, actorIndex, targetIndex),
          "Mercenary" => TryFireMercenary(foundMatch, actorIndex, request.Ability),
''',
'''          "Ox" => TryAttachOxCargo(foundMatch, actorIndex, targetIndex),
          "Phantom" => TryUseSharedServerPhantomAbility(foundMatch, actorIndex, targetIndex, request.Ability),
          "Mercenary" => TryFireMercenary(foundMatch, actorIndex, request.Ability),
''', 1)

    text = text.replace(
'''      (int width, int height, int health) = GetRoyalStats(foundMatch.Configuration, request.RoyalType);
      (int x, int y) position = request.X is int requestedX && request.Y is int requestedY
        ? (requestedX, requestedY)
        // Keep the hub compatible with older clients while the current client always provides a placement.
        : NetworkBoardRules.GetRoyalSpawn(foundMatch.Configuration, player.Team, width, height);
      if (!CanPlaceRoyal(foundMatch, player.Team, position.x, position.y, width, height))
''',
'''      (int width, int height, int health) = GetRoyalStats(foundMatch.Configuration, request.RoyalType);
      (int spawnWidth, int spawnHeight) = RoyalAbilityRules.GetRoyalSpawnFootprint(request.RoyalType);
      (int x, int y) position = request.X is int requestedX && request.Y is int requestedY
        ? (requestedX, requestedY)
        // Keep the hub compatible with older clients while the current client always provides a placement.
        : NetworkBoardRules.GetRoyalSpawn(foundMatch.Configuration, player.Team, spawnWidth, spawnHeight);
      if (!CanPlaceSharedServerRoyalGroup(foundMatch, player.Team, request.RoyalType, position.x, position.y))
''', 1)

    text = text.replace(
'''      foundMatch.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), request.RoyalType, player.Team, position.x, position.y, health));
''',
'''      AddSharedServerRoyalGroup(foundMatch, player.Team, request.RoyalType, position.x, position.y, health);
''', 1)

    text = text.replace(
'''    RemovePiece(match, defeatedPiece.Id);
    ApplySharedServerDeathExplosion(match, defeatedPiece, explosionSource, deathExplosion);
    if (!UnitRules.TryGet(defeatedPiece.Type, out UnitRule rule) || rule.Category != RuleCategory.Royal) return;
    if (defeatedPiece.Type == nameof(PieceType.GoblinRoyalty) && match.Pieces.Any(piece =>
      piece.Team == defeatedPiece.Team && piece.Type == nameof(PieceType.GoblinRoyalty))) return;
''',
'''    bool royalDeath = IsSharedServerRoyalDeath(match, defeatedPiece);
    RemovePiece(match, defeatedPiece.Id);
    ApplySharedServerDeathExplosion(match, defeatedPiece, explosionSource, deathExplosion);
    if (!royalDeath || !UnitRules.TryGet(defeatedPiece.Type, out UnitRule rule)) return;
''', 1)
    return text


edit('Game1.cs', game)
edit('MedivalChess.CPU/CpuGameRules.cs', cpu_core)
edit('MedivalChess.CPU/CpuGameRules.Actions.cs', cpu_actions)
edit('MedivalChess.CPU/CpuActionGenerator.cs', cpu_generator)
edit('MedivalChess.Server/MatchHub.cs', server)
