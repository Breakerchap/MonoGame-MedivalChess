from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    if new in text:
        print(f"{path}: already hooked")
        return
    if old not in text:
        raise RuntimeError(f"{path}: expected fragment not found")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: hooked")


replace_once(
    "Game1.cs",
'''    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentPieceOfType(damagedPiece, PieceType.Baron, damagedPiece.Team),
      IsPieceInForest(damagedPiece),
      _terrain.ForestDamageReduction
    );
    damagedPiece.CurrentHealth -= damage;
''',
'''    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentPieceOfType(damagedPiece, PieceType.Baron, damagedPiece.Team),
      IsPieceInForest(damagedPiece),
      _terrain.ForestDamageReduction
    );
    damage = ApplyLocalChessKingDeathRule(damagedPiece, damage);
    damagedPiece.CurrentHealth -= damage;
''')

replace_once(
    "Game1.cs",
'''  private void ResolveMineDamage(Piece target, TeamName mineOwner)
  {
    target.CurrentHealth -= AbilityRules.EngineerMineDamage;
    Console.WriteLine($"Mine dealt {AbilityRules.EngineerMineDamage} damage to {target.Definition.Type}.");
    HandlePieceDestroyed(target, mineOwner);
  }
''',
'''  private void ResolveMineDamage(Piece target, TeamName mineOwner)
  {
    int damage = ApplyLocalChessKingDeathRule(target, AbilityRules.EngineerMineDamage);
    target.CurrentHealth -= damage;
    Console.WriteLine($"Mine dealt {damage} damage to {target.Definition.Type}.");
    HandlePieceDestroyed(target, mineOwner);
  }
''')

replace_once(
    "MedivalChess.CPU/CpuGameRules.Abilities.cs",
'''    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(state, damaged, damaged.Team, nameof(PieceType.Baron)),
      IsInForest(state, damaged),
      state.Source.Terrain.ForestDamageReduction
    );
    int damagedIndex = FindPieceIndex(state.Pieces, damaged.Id);
''',
'''    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(state, damaged, damaged.Team, nameof(PieceType.Baron)),
      IsInForest(state, damaged),
      state.Source.Terrain.ForestDamageReduction
    );
    damage = ApplyCpuChessKingDeathRule(state, damaged, damage);
    int damagedIndex = FindPieceIndex(state.Pieces, damaged.Id);
''')

replace_once(
    "MedivalChess.CPU/CpuGameRules.Abilities.cs",
'''    int index = FindPieceIndex(state.Pieces, target.Id);
    if (index < 0) return;
    NetworkPiece live = state.Pieces[index];
    if (live.Health > appliedDamage)
''',
'''    int index = FindPieceIndex(state.Pieces, target.Id);
    if (index < 0) return;
    NetworkPiece live = state.Pieces[index];
    appliedDamage = ApplyCpuChessKingDeathRule(state, live, appliedDamage);
    if (live.Health > appliedDamage)
''')

replace_once(
    "MedivalChess.Server/MatchHub.cs",
'''    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(match, damagedPiece, damagedPiece.Team, "Baron"),
      IsInForest(match, damagedPiece),
      match.Terrain.ForestDamageReduction
    );
    int damagedIndex = match.Pieces.FindIndex(piece => piece.Id == damagedPiece.Id);
''',
'''    int damage = CombatRules.CalculateDamage(
      unmitigatedDamage,
      false,
      false,
      HasAdjacentUnit(match, damagedPiece, damagedPiece.Team, "Baron"),
      IsInForest(match, damagedPiece),
      match.Terrain.ForestDamageReduction
    );
    damage = ApplyServerChessKingDeathRule(match, damagedPiece, damage);
    int damagedIndex = match.Pieces.FindIndex(piece => piece.Id == damagedPiece.Id);
''')

replace_once(
    "MedivalChess.Server/MatchHub.cs",
'''    NetworkPiece target = match.Pieces[index];
    if (target.Health > AbilityRules.EngineerMineDamage)
    {
      match.Pieces[index] = target with { Health = target.Health - AbilityRules.EngineerMineDamage };
''',
'''    NetworkPiece target = match.Pieces[index];
    int damage = ApplyServerChessKingDeathRule(match, target, AbilityRules.EngineerMineDamage);
    if (target.Health > damage)
    {
      match.Pieces[index] = target with { Health = target.Health - damage };
''')

# Remove two small remaining runtime literals that duplicate shared unit rules.
replace_once(
    "Game1.cs",
'''      ? rule with { MoveRange = 2, MovePattern = RuleShape.Straight }
''',
'''      ? rule with { MoveRange = AbilityRules.CavalierFollowUpMovement, MovePattern = RuleShape.Straight }
''')

replace_once(
    "MedivalChess.CPU/CpuGameRules.Spatial.cs",
'''    bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) || mayUsePalaceSupport;
''',
'''    bool ignoresTerrain = AbilityRules.IgnoresImpassableTerrain(rule) ||
      (mayUsePalaceSupport && IsPalaceAssistedMovement(state, pieces, piece, rule, (piece.X, piece.Y), destination));
''')
