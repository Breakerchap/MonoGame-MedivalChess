from pathlib import Path


def edit(path: str, transform) -> None:
    file = Path(path)
    before = file.read_text(encoding="utf-8")
    after = transform(before)
    if after != before:
        file.write_text(after, encoding="utf-8")
        print(f"{path}: updated")
    else:
        print(f"{path}: no matching finalisation fragment")


def piece_defs(text: str) -> str:
    text = text.replace('// Chess -- unchanged', '// Chess')
    replacements = {
        'public static readonly PieceDefinition Pawn = new(PieceType.Pawn, "Pwn", Pack.Chess, (2, Shape.Forward), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,':
        'public static readonly PieceDefinition Pawn = new(PieceType.Pawn, "Pwn", Pack.Chess, (2, Shape.Forward), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 20,',
        'public static readonly PieceDefinition ChessKnight = new(PieceType.ChessKnight, "KnC", Pack.Chess, (3, Shape.ChessKnight), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,':
        'public static readonly PieceDefinition ChessKnight = new(PieceType.ChessKnight, "KnC", Pack.Chess, (3, Shape.ChessKnight), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 60,',
        'public static readonly PieceDefinition Bishop = new(PieceType.Bishop, "Bsh", Pack.Chess, (8, Shape.Diagonal), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,':
        'public static readonly PieceDefinition Bishop = new(PieceType.Bishop, "Bsh", Pack.Chess, (8, Shape.Diagonal), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 60,',
        'public static readonly PieceDefinition Rook = new(PieceType.Rook, "Rok", Pack.Chess, (8, Shape.Line), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,':
        'public static readonly PieceDefinition Rook = new(PieceType.Rook, "Rok", Pack.Chess, (8, Shape.Line), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 100,',
        'public static readonly PieceDefinition Queen = new(PieceType.Queen, "Qun", Pack.Chess, (8, Shape.LineOrDiagonal), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,':
        'public static readonly PieceDefinition Queen = new(PieceType.Queen, "Qun", Pack.Chess, (8, Shape.LineOrDiagonal), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 180,',
        'public static readonly PieceDefinition ChessKing = new(PieceType.ChessKing, "KIC", Pack.Chess, (1, Shape.Any), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,':
        'public static readonly PieceDefinition ChessKing = new(PieceType.ChessKing, "KIC", Pack.Chess, (1, Shape.Any), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0,'
    }
    for old, new in replacements.items():
        text = text.replace(old, new, 1)
    return text


def game(text: str) -> str:
    # Offline Phantom uses the same shared possession rule as CPU/server/online.
    text = text.replace(
'''    if (TryPickUpTreasure(actor, targetPosition, targetPiece))
    {
      return true;
    }

    if (actor.Definition.Type == PieceType.Spy &&
''',
'''    if (TryPickUpTreasure(actor, targetPosition, targetPiece))
    {
      return true;
    }

    if (TryUseSharedRoyalAbility(actor, targetPiece))
    {
      return true;
    }

    if (actor.Definition.Type == PieceType.Spy &&
''', 1)

    # Shared Royal identity decides whether a death is actually a Royal death (Phantom proxy,
    # final Goblin Royalty, etc.).
    text = text.replace(
'''    pieceSetup.RemovePiece(damagedPiece);
    if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Regicide)
''',
'''    bool royalDeath = IsSharedRoyalDeath(damagedPiece);
    pieceSetup.RemovePiece(damagedPiece);
    if (royalDeath && _gameMode == GameMode.Regicide)
''', 1)
    text = text.replace(
'else if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Escort)',
'else if (royalDeath && _gameMode == GameMode.Escort)', 1)
    text = text.replace(
'else if (damagedPiece.Definition.Category == PieceCategory.Royal && _gameMode == GameMode.Plunder &&',
'else if (royalDeath && _gameMode == GameMode.Plunder &&', 1)

    # Shared pathfinder + Chess stop-at-capture behaviour.
    old_paths = '''    return MovementPathfinder.FindPaths(
      piece,
      destination => CanLandPieceAt(piece, destination, hasPalaceSupport),
      (from, destination) => CanTravelThroughPosition(piece, from, destination),
      destination => GetMovementCost(piece, destination),
      (from, to) => CrossesRiver(piece, from, to),
      movementRule,
      (from, destination) => GetMovementCost(piece, from, destination),
      destination => GetMovementRangeAt(piece, movementRule, destination),
      movementRule.MoveRange + (hasPalaceSupport ? 1 : 0)
    );
'''
    new_paths = '''    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementPathfinder.FindPaths(
      piece,
      destination => CanLandPieceAt(piece, destination, hasPalaceSupport),
      (from, destination) => CanTravelThroughPosition(piece, from, destination),
      destination => GetMovementCost(piece, destination),
      (from, to) => CrossesRiver(piece, from, to),
      movementRule,
      (from, destination) => GetMovementCost(piece, from, destination),
      destination => GetMovementRangeAt(piece, movementRule, destination),
      movementRule.MoveRange + (hasPalaceSupport ? 1 : 0),
      position => CanContinueLocalChessPath(piece, movementRule, position)
    );
    AddLocalPawnCapturePaths(piece, movementRule, paths);
    return paths;
'''
    text = text.replace(old_paths, new_paths, 1)

    start = text.find('  private bool CanLandPieceAt(Piece piece, (int x, int y) destination, bool mayUsePalaceSupport)')
    end = text.find('  private bool CanTravelThroughPosition(', start)
    if start >= 0 and end >= 0:
        text = text[:start] + '''  private bool CanLandPieceAt(Piece piece, (int x, int y) destination, bool mayUsePalaceSupport)
  {
    UnitRule rule = GetEffectiveMovementRule(piece);
    if (CanLocalChessCaptureLand(piece, rule, destination))
    {
      return true;
    }

    bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) ||
      (mayUsePalaceSupport && IsPalaceAssistedMovement(piece, piece.Position, destination));
    if (!IsFootprintOnBoard(piece.Definition, destination) ||
        OccupiedSquares(piece.Definition, destination).Any(_barricades.ContainsKey) ||
        (!ignoresTerrain && OccupiedSquares(piece.Definition, destination).Any(_terrain.IsLake)))
    {
      return false;
    }
    return pieceSetup.IsFootprintClear(piece.Definition, destination, piece);
  }

''' + text[end:]

    start = text.find('  private bool CanTravelThroughPosition(')
    end = text.find('  private int GetMovementCost(Piece piece, (int x, int y) destination)', start)
    if start >= 0 and end >= 0:
        text = text[:start] + '''  private bool CanTravelThroughPosition(
    Piece piece,
    (int x, int y) from,
    (int x, int y) destination
  )
  {
    UnitRule rule = GetEffectiveMovementRule(piece);
    foreach ((int x, int y) position in PositionsBetween(from, destination))
    {
      foreach ((int x, int y) occupiedSquare in OccupiedSquares(piece.Definition, position))
      {
        bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) ||
          IsPalaceAssistedMovement(piece, from, destination);
        if ((!ignoresTerrain && _terrain.IsLake(occupiedSquare)) || _barricades.ContainsKey(occupiedSquare) ||
            !IsBoardCell(occupiedSquare.x - _board.MinX, occupiedSquare.y - _board.MinY))
        {
          return false;
        }

        Piece blockingPiece = pieceSetup.GetPieceAt(occupiedSquare);
        if (blockingPiece is null || blockingPiece == piece || blockingPiece.Definition.Type == PieceType.Farm)
        {
          continue;
        }
        if (AbilityRules.CanTravelThroughUnit(rule, piece.Team.ToNetworkTeam(), blockingPiece.Team.ToNetworkTeam()))
        {
          continue;
        }
        if (GetLocalChessCaptureTarget(piece, rule, position) == blockingPiece)
        {
          continue;
        }
        return false;
      }
    }
    return true;
  }

''' + text[end:]

    # Terrain-immune movement is expressed once in shared AbilityRules.
    text = text.replace(
'''  private int GetMovementCost(Piece piece, (int x, int y) from, (int x, int y) destination)
  {
    if (piece.Definition.Type == PieceType.Elephant)
    {
      return 1;
    }
    int cost = 0;
    bool ignoresTerrain = IsPalaceAssistedMovement(piece, from, destination);
''',
'''  private int GetMovementCost(Piece piece, (int x, int y) from, (int x, int y) destination)
  {
    UnitRule rule = GetEffectiveMovementRule(piece);
    int cost = 0;
    bool ignoresTerrain = IsPalaceAssistedMovement(piece, from, destination);
''', 1)
    text = text.replace(
'''      if (_terrain.IsForest(occupiedSquare) && !usesOwnedRoad && !ignoresTerrain)
      {
        cost = Math.Max(cost, 2);
      }
      else if (usesOwnedRoad && !_terrain.IsForest(occupiedSquare))
      {
        // A road built along open ground costs no movement points.
        cost = Math.Max(cost, 0);
      }
      else
      {
        cost = Math.Max(cost, 1);
      }
''',
'''      int ordinaryCost = _terrain.IsForest(occupiedSquare) && !usesOwnedRoad && !ignoresTerrain
        ? 2
        : usesOwnedRoad && !_terrain.IsForest(occupiedSquare) ? 0 : 1;
      cost = Math.Max(cost, AbilityRules.ApplyTerrainMovementCost(rule, ordinaryCost));
''', 1)
    text = text.replace(
'''    if (piece.Definition.Type == PieceType.Elephant || IsPalaceAssistedMovement(piece, from, to))
''',
'''    if (AbilityRules.IgnoresRivers(GetEffectiveMovementRule(piece)) || IsPalaceAssistedMovement(piece, from, to))
''', 1)

    # Resolve Chess capture at movement completion; a surviving target returns the mover to the
    # previous path square as specified by the workbook.
    text = text.replace(
'''    MovePieceWithCompanions(movedPiece, destination);
''',
'''    destination = ResolveLocalChessLandingCapture(movedPiece, completedAnimation.Path, destination);
    MovePieceWithCompanions(movedPiece, destination);
''', 1)
    return text


def cpu_spatial(text: str) -> str:
    old = '''    return MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLand(source, pieces, piece, rule, destination, hasPalaceSupport),
      (from, destination) => CanTravelThrough(source, pieces, piece, rule, from, destination),
      destination => GetMovementCost(source, piece, rule, destination),
      (from, destination) => CrossesRiver(source, pieces, piece, rule, from, destination),
      (from, destination) => GetMovementCost(source, pieces, piece, rule, from, destination),
      destination => rule.MoveRange + (IsPalaceAssistedMovement(
        pieces, piece, rule, (piece.X, piece.Y), destination) ? 1 : 0),
      rule.MoveRange + (hasPalaceSupport ? 1 : 0)
    );
'''
    new = '''    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLand(source, pieces, piece, rule, destination, hasPalaceSupport),
      (from, destination) => CanTravelThrough(source, pieces, piece, rule, from, destination),
      destination => GetMovementCost(source, piece, rule, destination),
      (from, destination) => CrossesRiver(source, pieces, piece, rule, from, destination),
      (from, destination) => GetMovementCost(source, pieces, piece, rule, from, destination),
      destination => rule.MoveRange + (IsPalaceAssistedMovement(
        pieces, piece, rule, (piece.X, piece.Y), destination) ? 1 : 0),
      rule.MoveRange + (hasPalaceSupport ? 1 : 0),
      position => CanContinueChessPath(pieces, piece, rule, position)
    );
    AddPawnCapturePaths(source, pieces, piece, rule, paths);
    return paths;
'''
    text = text.replace(old, new, 1)
    text = text.replace(
'''  ) =>
    CanPlace(
      state,
''',
'''  )
  {
    if (CanChessCaptureLand(state, pieces, piece, rule, destination)) return true;
    return CanPlace(
      state,
''', 1)
    # close expression-bodied CanLand after CanPlace call.
    text = text.replace(
'''      null
    );

  private static bool CanPlace(
''',
'''      null
    );
  }

  private static bool CanPlace(
''', 1)
    text = text.replace(
'''        if (blocker is not null && !AbilityRules.CanTravelThroughUnit(rule, piece.Team, blocker.Team))
        {
          return false;
        }
''',
'''        if (blocker is not null && !AbilityRules.CanTravelThroughUnit(rule, piece.Team, blocker.Team) &&
            GetChessCaptureTarget(pieces, piece, rule, position) != blocker)
        {
          return false;
        }
''', 1)
    return text


def cpu_actions(text: str) -> str:
    text = text.replace(
'''    List<(int x, int y)> path = paths[(action.DestinationX, action.DestinationY)];
    int oldX = piece.X;
    int oldY = piece.Y;
''',
'''    List<(int x, int y)> path = paths[(action.DestinationX, action.DestinationY)];
    NetworkPiece? chessCaptureTarget = GetChessCaptureTarget(
      state.Pieces, piece, UnitRules.GetRequired(piece.Type), (action.DestinationX, action.DestinationY));
    int oldX = piece.X;
    int oldY = piece.Y;
''', 1)
    text = text.replace(
'''    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    piece = state.Pieces[index] with
    {
      X = action.DestinationX,
      Y = action.DestinationY,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = elephantDamaged || state.Pieces[index].HasAttackedThisTurn,
''',
'''    bool chessCaptureSurvived = false;
    if (chessCaptureTarget is not null)
    {
      ResolveSharedPieceDamage(state, piece, action.Team, chessCaptureTarget.Id, null);
      chessCaptureSurvived = FindPiece(state.Pieces, chessCaptureTarget.Id) is not null;
    }

    index = FindPieceIndex(state.Pieces, action.PieceId);
    if (index < 0)
    {
      return;
    }
    (int finalX, int finalY) = chessCaptureSurvived
      ? ChessAbilityRules.GetFailedCaptureFallback((oldX, oldY), path)
      : (action.DestinationX, action.DestinationY);
    List<(int x, int y)> actualPath = chessCaptureSurvived && path.Count > 0 ? path[..^1] : path;
    piece = state.Pieces[index] with
    {
      X = finalX,
      Y = finalY,
      HasMovedThisTurn = true,
      HasAttackedThisTurn = chessCaptureTarget is not null || elephantDamaged || state.Pieces[index].HasAttackedThisTurn,
''', 1)
    text = text.replace(
'''    state.RecordMove(action.Team, piece.Id, oldX, oldY, action.DestinationX, action.DestinationY);
    MoveAttachedPieces(state, piece);
    MoveEmissaryCompanions(state, piece, oldX, oldY);
    TriggerSharedMinesAlongMovement(state, piece, path);
''',
'''    state.RecordMove(action.Team, piece.Id, oldX, oldY, finalX, finalY);
    MoveAttachedPieces(state, piece);
    MoveEmissaryCompanions(state, piece, oldX, oldY);
    TriggerSharedMinesAlongMovement(state, piece, actualPath);
''', 1)
    return text


def cpu_core(text: str) -> str:
    text = text.replace(
'''    if (!plunderPickup && actor.Type != "Mercenary" && !CanUseActionSquare(actor, action.TargetX, action.TargetY))
''',
'''    if (!plunderPickup && actor.Type is not ("Mercenary" or "Phantom") &&
        !CanUseActionSquare(actor, action.TargetX, action.TargetY))
''', 1)
    return text


def cpu_abilities(text: str) -> str:
    old = '''    bool wasRoyal = (UnitRules.TryGet(piece.Type, out UnitRule destroyedRule) && destroyedRule.Category == RuleCategory.Royal) ||
      piece.IsRoyalProxy;
    RemovePiece(state, piece.Id);

    foreach (AbilityDamageInstruction instruction in deathExplosion)
'''
    new = '''    bool royalDeath = IsSharedCpuRoyalDeath(state, piece);
    UnitRules.TryGet(piece.Type, out UnitRule destroyedRule);
    RemovePiece(state, piece.Id);

    foreach (AbilityDamageInstruction instruction in deathExplosion)
'''
    text = text.replace(old, new, 1)
    text = text.replace('    if (!wasRoyal || state.Winner is not null)', '    if (!royalDeath || state.Winner is not null)', 1)
    # Remove the now-duplicated Goblin Royalty special case; shared RoyalAbilityRules decided it.
    goblin = '''    // Goblin Royalty is one Royal represented by four separate units. Losing one goblin is not
    // Royal death while another Goblin Royalty unit belonging to that team remains.
    if (piece.Type == nameof(PieceType.GoblinRoyalty) && state.Pieces.Any(candidate =>
      candidate.Team == piece.Team && candidate.Type == nameof(PieceType.GoblinRoyalty)))
    {
      return;
    }

'''
    text = text.replace(goblin, '', 1)
    return text


def server(text: str) -> str:
    text = text.replace(
'''    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLandAt(match, piece, rule, destination, HasPalaceSupport(match, piece)),
      (from, to) => CanTravelThrough(match, piece, rule, from, to),
      destination => GetMovementCost(match, piece, rule, destination),
      (from, to) => CrossesRiver(match, piece, rule, from, to),
      (from, destination) => GetMovementCost(match, piece, rule, from, destination),
      destination => rule.MoveRange + (IsPalaceAssistedMovement(
        match, piece, rule, (piece.X, piece.Y), destination) ? 1 : 0),
      rule.MoveRange + (HasPalaceSupport(match, piece) ? 1 : 0)
    );
    return paths.TryGetValue((destinationX, destinationY), out path!);
''',
'''    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      rule,
      (piece.X, piece.Y),
      piece.Team,
      destination => CanLandAt(match, piece, rule, destination, HasPalaceSupport(match, piece)),
      (from, to) => CanTravelThrough(match, piece, rule, from, to),
      destination => GetMovementCost(match, piece, rule, destination),
      (from, to) => CrossesRiver(match, piece, rule, from, to),
      (from, destination) => GetMovementCost(match, piece, rule, from, destination),
      destination => rule.MoveRange + (IsPalaceAssistedMovement(
        match, piece, rule, (piece.X, piece.Y), destination) ? 1 : 0),
      rule.MoveRange + (HasPalaceSupport(match, piece) ? 1 : 0),
      position => CanContinueServerChessPath(match, piece, rule, position)
    );
    AddServerPawnCapturePaths(match, piece, rule, paths);
    return paths.TryGetValue((destinationX, destinationY), out path!);
''', 1)
    text = text.replace(
'''  {
    if (!NetworkPieceRules.FootprintFitsBoard(match.Configuration, destination.x, destination.y, rule.Width, rule.Height)) return false;
''',
'''  {
    if (CanServerChessCaptureLand(match, piece, rule, destination)) return true;
    if (!NetworkPieceRules.FootprintFitsBoard(match.Configuration, destination.x, destination.y, rule.Width, rule.Height)) return false;
''', 1)
    text = text.replace(
'''      if ((piece.Type != "Elephant" && !mayUsePalaceSupport && match.Terrain.IsLake(square)) ||
          match.Barricades.ContainsKey(square)) return false;
''',
'''      bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) ||
        (mayUsePalaceSupport && IsPalaceAssistedMovement(match, piece, rule, (piece.X, piece.Y), destination));
      if ((!ignoresTerrain && match.Terrain.IsLake(square)) || match.Barricades.ContainsKey(square)) return false;
''', 1)
    text = text.replace(
'''        if (blocker is not null && !AbilityRules.CanTravelThroughUnit(rule, piece.Team, blocker.Team)) return false;
''',
'''        if (blocker is not null && !AbilityRules.CanTravelThroughUnit(rule, piece.Team, blocker.Team) &&
            GetServerChessCaptureTarget(match, piece, rule, position) != blocker) return false;
''', 1)
    text = text.replace(
'''      if (!plunderPickup && actor.Type != "Mercenary" && !CanUseActionSquare(actor, request.TargetX, request.TargetY))
''',
'''      if (!plunderPickup && actor.Type is not ("Mercenary" or "Phantom") &&
          !CanUseActionSquare(actor, request.TargetX, request.TargetY))
''', 1)

    # Authoritative landing capture happens before committing movement; surviving targets bounce
    # the mover back to the previous path square.
    text = text.replace(
'''      int pieceIndex = foundMatch.Pieces.FindIndex(candidate => candidate.Id == piece.Id);
      if (pieceIndex < 0) return new(false, "That unit is no longer on the board.", foundMatch.State());
      int oldX = piece.X;
      int oldY = piece.Y;
      piece = piece with
      {
        X = request.ToX,
        Y = request.ToY,
        HasMovedThisTurn = true,
        HasAttackedThisTurn = elephantDamagedAnEnemy || piece.HasAttackedThisTurn,
''',
'''      NetworkPiece? chessCaptureTarget = GetServerChessCaptureTarget(
        foundMatch, piece, UnitRules.GetRequired(piece.Type), (request.ToX, request.ToY));
      bool chessCaptureSurvived = false;
      if (chessCaptureTarget is not null)
      {
        ResolvePieceDamage(foundMatch, piece, player, chessCaptureTarget.Id, null);
        chessCaptureSurvived = foundMatch.Pieces.Any(candidate => candidate.Id == chessCaptureTarget.Id);
      }

      int pieceIndex = foundMatch.Pieces.FindIndex(candidate => candidate.Id == piece.Id);
      if (pieceIndex < 0) return new(false, "That unit is no longer on the board.", foundMatch.State());
      int oldX = piece.X;
      int oldY = piece.Y;
      (int finalX, int finalY) = chessCaptureSurvived
        ? ChessAbilityRules.GetFailedCaptureFallback((oldX, oldY), movementPath)
        : (request.ToX, request.ToY);
      List<(int x, int y)> actualMovementPath = chessCaptureSurvived && movementPath.Count > 0
        ? movementPath[..^1]
        : movementPath;
      piece = foundMatch.Pieces[pieceIndex] with
      {
        X = finalX,
        Y = finalY,
        HasMovedThisTurn = true,
        HasAttackedThisTurn = chessCaptureTarget is not null || elephantDamagedAnEnemy || foundMatch.Pieces[pieceIndex].HasAttackedThisTurn,
''', 1)
    text = text.replace(
'''      TriggerMinesAlongMovement(foundMatch, piece, movementPath);
''',
'''      TriggerMinesAlongMovement(foundMatch, piece, actualMovementPath);
''', 1)
    text = text.replace(
'''          IsEscortVictory(foundMatch, piece, request.ToX, request.ToY))
''',
'''          IsEscortVictory(foundMatch, piece, finalX, finalY))
''', 1)
    return text


edit('MedivalChess.Shared/Piece.cs', piece_defs)
edit('Game1.cs', game)
edit('MedivalChess.CPU/CpuGameRules.Spatial.cs', cpu_spatial)
edit('MedivalChess.CPU/CpuGameRules.Actions.cs', cpu_actions)
edit('MedivalChess.CPU/CpuGameRules.cs', cpu_core)
edit('MedivalChess.CPU/CpuGameRules.Abilities.cs', cpu_abilities)
edit('MedivalChess.Server/MatchHub.cs', server)
