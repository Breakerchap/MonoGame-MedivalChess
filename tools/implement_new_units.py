from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


def replace_expected(text: str, old: str, new: str, expected: int, label: str) -> str:
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"{label}: expected {expected} matches, found {count}")
    return text.replace(old, new)


# -----------------------------------------------------------------------------
# Piece definitions: restore the compatibility surface used throughout the game,
# keep pack metadata, and make movement min/max ranges real rather than nested
# tuples that break the existing code.
# -----------------------------------------------------------------------------
piece_path = "MedivalChess.Shared/Piece.cs"
piece = read(piece_path)

pack_block = '''public enum Pack
{
  Base,
  Dynasty,
  Fantasy,
  Undead,
  Greek,
  Modern,
  Chess
}
'''
pack_and_compat = pack_block + '''
public enum PieceCategory
{
  Melee,
  Ranged,
  Intelligence,
  Mechanical,
  Structure,
  Transport,
  Royal
}

/// <summary>Inclusive movement distance range with legacy range/shape accessors.</summary>
public readonly record struct MovementDefinition
{
  public int Minimum { get; }
  public int Maximum { get; }
  public Shape Shape { get; }

  // Existing gameplay, CPU, and editor code historically consumed Movement.range/shape.
  public int range => Maximum;
  public Shape shape => Shape;
  public int minRange => Minimum;
  public int maxRange => Maximum;

  public MovementDefinition(int minimum, int maximum, Shape shape)
  {
    if (minimum < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(minimum), "Movement range cannot be negative.");
    }
    if (maximum < minimum)
    {
      throw new ArgumentOutOfRangeException(nameof(maximum), "Movement maximum must be at least its minimum.");
    }

    Minimum = minimum;
    Maximum = maximum;
    Shape = shape;
  }

  public static implicit operator MovementDefinition((int range, Shape shape) movement) =>
    new(movement.range == 0 ? 0 : 1, movement.range, movement.shape);

  public static implicit operator MovementDefinition(((int minRange, int maxRange) range, Shape shape) movement) =>
    new(movement.range.minRange, movement.range.maxRange, movement.shape);

  public void Deconstruct(out int range, out Shape shape)
  {
    range = Maximum;
    shape = Shape;
  }
}
'''
piece = replace_once(piece, pack_block, pack_and_compat, "insert compatibility types")

piece = replace_once(
    piece,
    '''  public Pack Pack { get; }\n\n  public ((int minRange, int maxRange), Shape shape) Movement { get; }''',
    '''  public Pack Pack { get; }\n\n  public PieceCategory Category { get; }\n\n  public MovementDefinition Movement { get; }''',
    "piece properties",
)

old_constructor = '''  public PieceDefinition(
    PieceType type,
    string abbreviation,
    Pack pack,
    ((int minRange, int maxRange), Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    AttackRange attackRange,
    Shape attackPattern,
    int cost,
    string abilityDescription = "",
    string? identifier = null,
    string? displayName = null
  )
  {
    Type = type;
    Identifier = string.IsNullOrWhiteSpace(identifier)
      ? type.ToString()
      : identifier;

    DisplayName = string.IsNullOrWhiteSpace(displayName)
      ? type.ToString()
      : displayName;

    Abbreviation = string.IsNullOrWhiteSpace(abbreviation)
      ? null
      : abbreviation;

    Pack = pack;
    Movement = movement;
    Attack = attack;
    Health = health;
    Size = size;
    AttackRange = attackRange;
    AttackPattern = attackPattern;
    Cost = cost;
    AbilityDescription = abilityDescription;
  }
'''
new_constructor = '''  public PieceDefinition(
    PieceType type,
    string abbreviation,
    Pack pack,
    ((int minRange, int maxRange), Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    AttackRange attackRange,
    Shape attackPattern,
    int cost,
    string abilityDescription = "",
    string? identifier = null,
    string? displayName = null
  ) : this(
    type,
    GetDefaultCategory(type),
    pack,
    movement,
    attack,
    health,
    size,
    attackRange,
    attackPattern,
    cost,
    abilityDescription,
    identifier,
    displayName,
    abbreviation
  )
  {
  }

  /// <summary>Compatibility constructor for existing gameplay and campaign code.</summary>
  public PieceDefinition(
    PieceType type,
    PieceCategory category,
    (int range, Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    AttackRange attackRange,
    Shape attackPattern,
    int cost,
    string abilityDescription = "",
    string? identifier = null,
    string? displayName = null,
    string? abbreviation = null
  ) : this(
    type,
    category,
    GetDefaultPack(type),
    movement,
    attack,
    health,
    size,
    attackRange,
    attackPattern,
    cost,
    abilityDescription,
    identifier,
    displayName,
    abbreviation
  )
  {
  }

  /// <summary>Compatibility overload that preserves a movement minimum when cloning a definition.</summary>
  public PieceDefinition(
    PieceType type,
    PieceCategory category,
    MovementDefinition movement,
    int attack,
    int health,
    (int x, int y) size,
    AttackRange attackRange,
    Shape attackPattern,
    int cost,
    string abilityDescription = "",
    string? identifier = null,
    string? displayName = null,
    string? abbreviation = null
  ) : this(
    type,
    category,
    GetDefaultPack(type),
    movement,
    attack,
    health,
    size,
    attackRange,
    attackPattern,
    cost,
    abilityDescription,
    identifier,
    displayName,
    abbreviation
  )
  {
  }

  public PieceDefinition(
    PieceType type,
    PieceCategory category,
    Pack pack,
    MovementDefinition movement,
    int attack,
    int health,
    (int x, int y) size,
    AttackRange attackRange,
    Shape attackPattern,
    int cost,
    string abilityDescription = "",
    string? identifier = null,
    string? displayName = null,
    string? abbreviation = null
  )
  {
    Type = type;
    Identifier = string.IsNullOrWhiteSpace(identifier) ? type.ToString() : identifier;
    DisplayName = string.IsNullOrWhiteSpace(displayName) ? type.ToString() : displayName;
    Abbreviation = string.IsNullOrWhiteSpace(abbreviation) ? null : abbreviation;
    Category = category;
    Pack = pack;
    Movement = movement;
    Attack = attack;
    Health = health;
    Size = size;
    AttackRange = attackRange;
    AttackPattern = attackPattern;
    Cost = cost;
    AbilityDescription = abilityDescription;
  }

  private static PieceCategory GetDefaultCategory(PieceType type) => type switch
  {
    PieceType.Archer or PieceType.Crossbowman or PieceType.Bombard or
      PieceType.Ninja or PieceType.Dragon or PieceType.Wizard => PieceCategory.Ranged,
    PieceType.Cannon or PieceType.Catapult or PieceType.Ballista => PieceCategory.Mechanical,
    PieceType.Spy or PieceType.Engineer => PieceCategory.Intelligence,
    PieceType.Ox => PieceCategory.Transport,
    PieceType.Farm => PieceCategory.Structure,
    PieceType.King or PieceType.Princess or PieceType.Palace or PieceType.Baron or PieceType.Emissary or
      PieceType.Emperor or PieceType.TerracottaWarrior or PieceType.GoblinRoyalty or PieceType.Phantom or
      PieceType.Zeus or PieceType.ChessKing => PieceCategory.Royal,
    _ => PieceCategory.Melee
  };

  private static Pack GetDefaultPack(PieceType type) => type switch
  {
    PieceType.Elephant or PieceType.Ox or PieceType.Ninja or PieceType.Samurai or
      PieceType.Emperor or PieceType.TerracottaWarrior => Pack.Dynasty,
    PieceType.Dragon or PieceType.GoblinRoyalty or PieceType.Adventurer or
      PieceType.Wizard or PieceType.Dragonborn => Pack.Fantasy,
    PieceType.Skeleton or PieceType.Zombie or PieceType.Flesh or PieceType.Ghoul or PieceType.Phantom => Pack.Undead,
    PieceType.Chariot or PieceType.Ballista or PieceType.Zeus or PieceType.Heracles or
      PieceType.Hermes or PieceType.Ares or PieceType.Chimera or PieceType.Pegasus => Pack.Greek,
    PieceType.Spy or PieceType.Engineer or PieceType.Mercenary => Pack.Modern,
    PieceType.Pawn or PieceType.ChessKnight or PieceType.Bishop or PieceType.Rook or
      PieceType.Queen or PieceType.ChessKing => Pack.Chess,
    _ => Pack.Base
  };
'''
piece = replace_once(piece, old_constructor, new_constructor, "piece constructors")

# A single Move value means 1..N. Pegasus is the only currently authored non-zero
# minimum (2..4), and its existing unequal pair is therefore intentionally untouched.
def fix_single_move(match: re.Match[str]) -> str:
    maximum = int(match.group(1))
    minimum = 0 if maximum == 0 else 1
    return f"(({minimum}, {maximum}), Shape."

piece, movement_fix_count = re.subn(r"\(\((\d+), \1\), Shape\.", fix_single_move, piece)
if movement_fix_count < 30:
    raise RuntimeError(f"movement range normalisation: expected many definitions, found {movement_fix_count}")

piece = replace_once(piece, 'PieceType.Pegasus,\n    "Peg",', 'PieceType.Pegasus,\n    "",', "Pegasus abbreviation")
piece = replace_expected(piece, "    Shape.MoveOnEnemy,", "    Shape.None,", 6, "defer Chess landing-capture ability")
piece = replace_once(
    piece,
    '''    // Dynasty\n    Emperor,\n    TerracottaWarrior,\n\n    // Fantasy''',
    '''    // Dynasty\n    Emperor,\n\n    // Fantasy''',
    "Terracotta is not a selectable royal",
)
write(piece_path, piece)


# -----------------------------------------------------------------------------
# Pack filtering shared by local, CPU, server, and campaign code.
# -----------------------------------------------------------------------------
write(
    "MedivalChess.Shared/PackRules.cs",
    '''namespace MedivalChess.Shared;

/// <summary>Shared parsing and filtering for the packs enabled in a match or campaign level.</summary>
public static class PackRules
{
  private static readonly Pack[] Packs = Enum.GetValues<Pack>();

  public static IReadOnlyList<Pack> All => Packs;
  public static IReadOnlyList<string> AllNames { get; } = Packs.Select(pack => pack.ToString()).ToArray();

  /// <summary>Null means every pack for backwards-compatible network messages.</summary>
  public static IReadOnlySet<Pack> GetAllowedPacks(IEnumerable<string>? names)
  {
    if (names is null)
    {
      return Packs.ToHashSet();
    }

    HashSet<Pack> allowed = [];
    foreach (string? name in names)
    {
      if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse(name.Trim(), true, out Pack pack) && Enum.IsDefined(pack))
      {
        allowed.Add(pack);
      }
    }
    return allowed;
  }

  public static bool TryNormaliseAllowedPacks(IEnumerable<string>? names, out string[] normalised)
  {
    if (names is null)
    {
      normalised = [.. AllNames];
      return true;
    }

    HashSet<Pack> parsed = [];
    foreach (string? name in names)
    {
      if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse(name.Trim(), true, out Pack pack) || !Enum.IsDefined(pack))
      {
        normalised = [];
        return false;
      }
      parsed.Add(pack);
    }

    normalised = Packs.Where(parsed.Contains).Select(pack => pack.ToString()).ToArray();
    return normalised.Length > 0;
  }

  public static bool IsAllowed(Pack pack, IEnumerable<string>? names) =>
    names is null || GetAllowedPacks(names).Contains(pack);

  public static bool IsAllowed(PieceDefinition definition, IEnumerable<string>? names) =>
    IsAllowed(definition.Pack, names);

  public static bool IsAllowed(string identifier, IEnumerable<string>? names)
  {
    PieceDefinition? definition = PieceDefinitions.All.FirstOrDefault(candidate =>
      string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
    return definition is not null && IsAllowed(definition, names);
  }
}
''',
)


# -----------------------------------------------------------------------------
# Shared unit rules and geometry.
# -----------------------------------------------------------------------------
unit_path = "MedivalChess.Shared/UnitRules.cs"
unit = read(unit_path)
unit = replace_once(
    unit,
    '''  Line,\n  Forward,''',
    '''  Line,\n  Diagonal,\n  LineOrDiagonal,\n  ChessKnight,\n  ForwardLine,\n  Forward,''',
    "rule shapes",
)
unit = replace_once(
    unit,
    '''  int MinimumAttackRange = 1,\n  string AbilityDescription = ""\n)''',
    '''  int MinimumAttackRange = 1,\n  string AbilityDescription = "",\n  int MinimumMoveRange = 1\n)''',
    "unit rule movement minimum",
)
unit = replace_once(
    unit,
    '''      definition.AttackRange.Minimum,\n      definition.AbilityDescription\n    ))''',
    '''      definition.AttackRange.Minimum,\n      definition.AbilityDescription,\n      definition.Movement.Minimum\n    ))''',
    "native rule conversion",
)
unit = replace_once(
    unit,
    '''  public static IReadOnlyList<UnitRule> All => Rules;\n  public static IReadOnlyList<UnitRule> Purchasable { get; } = Rules.Where(rule =>\n    rule.Category != RuleCategory.Royal\n  ).ToArray();\n  public static IReadOnlyList<UnitRule> Royals { get; } = Rules.Where(rule => rule.Category == RuleCategory.Royal).ToArray();''',
    '''  public static IReadOnlyList<UnitRule> All => Rules;\n  public static IReadOnlyList<UnitRule> Purchasable { get; } = PieceDefinitions.Purchasable\n    .Select(definition => ByType[definition.Identifier])\n    .ToArray();\n  public static IReadOnlyList<UnitRule> Royals { get; } = PieceDefinitions.Royals\n    .Select(definition => ByType[definition.Identifier])\n    .ToArray();''',
    "authoritative purchasable and royal lists",
)
unit = replace_once(
    unit,
    '''      definition.AttackRange.Minimum,\n      definition.AbilityDescription\n    );''',
    '''      definition.AttackRange.Minimum,\n      definition.AbilityDescription,\n      definition.Movement.Minimum\n    );''',
    "runtime rule conversion",
)
old_map = '''  private static RuleShape ToRuleShape(Shape shape) => shape switch
  {
    Shape.Any => RuleShape.Any,
    Shape.Straight => RuleShape.Straight,
    Shape.Line => RuleShape.Line,
    Shape.Forward => RuleShape.Forward,
    Shape.AbsoluteStraightOrDiagonal => RuleShape.AbsoluteStraightOrDiagonal,
    Shape.ForwardOrForwardDiagonal => RuleShape.ForwardOrForwardDiagonal,
    Shape.PierceStraight => RuleShape.PierceStraight,
    _ => RuleShape.None
  };'''
new_map = '''  private static RuleShape ToRuleShape(Shape shape) => shape switch
  {
    Shape.Any => RuleShape.Any,
    Shape.Straight => RuleShape.Straight,
    Shape.Line => RuleShape.Line,
    Shape.Diagonal => RuleShape.Diagonal,
    Shape.LineOrDiagonal => RuleShape.LineOrDiagonal,
    Shape.ChessKnight => RuleShape.ChessKnight,
    Shape.ForwardLine => RuleShape.ForwardLine,
    Shape.Forward => RuleShape.Forward,
    Shape.AbsoluteStraightOrDiagonal => RuleShape.AbsoluteStraightOrDiagonal,
    Shape.ForwardOrForwardDiagonal => RuleShape.ForwardOrForwardDiagonal,
    Shape.PierceStraight => RuleShape.PierceStraight,
    _ => RuleShape.None
  };'''
unit = replace_once(unit, old_map, new_map, "shape mapping")
old_can_move = '''  public static bool CanMove(UnitRule rule, int fromX, int fromY, int toX, int toY)
  {
    int dx = Math.Abs(toX - fromX);
    int dy = Math.Abs(toY - fromY);
    if (dx == 0 && dy == 0) return false;

    int chessboardDistance = Math.Max(dx, dy);
    int taxicabDistance = dx + dy;
    return rule.MovePattern switch
    {
      RuleShape.Straight => taxicabDistance <= rule.MoveRange,
      RuleShape.Line => (dx == 0 || dy == 0) && chessboardDistance <= rule.MoveRange,
      RuleShape.Any => chessboardDistance <= rule.MoveRange,
      RuleShape.None => false,
      _ => chessboardDistance <= rule.MoveRange
    };
  }'''
new_can_move = '''  public static bool CanMove(UnitRule rule, int fromX, int fromY, int toX, int toY)
  {
    int dx = Math.Abs(toX - fromX);
    int dy = Math.Abs(toY - fromY);
    if (dx == 0 && dy == 0) return false;

    int chessboardDistance = Math.Max(dx, dy);
    int taxicabDistance = dx + dy;
    int distance = rule.MovePattern is RuleShape.Straight or RuleShape.ChessKnight
      ? taxicabDistance
      : chessboardDistance;
    if (distance < rule.MinimumMoveRange || distance > rule.MoveRange) return false;

    return rule.MovePattern switch
    {
      RuleShape.Straight => true,
      RuleShape.Line => dx == 0 || dy == 0,
      RuleShape.Diagonal => dx == dy,
      RuleShape.LineOrDiagonal => dx == 0 || dy == 0 || dx == dy,
      RuleShape.ChessKnight => chessboardDistance == 2 && taxicabDistance == 3,
      RuleShape.Any => true,
      RuleShape.None => false,
      _ => true
    };
  }'''
unit = replace_once(unit, old_can_move, new_can_move, "unit movement geometry")
unit = replace_once(
    unit,
    '''      RuleShape.Line or RuleShape.PierceStraight => dx == 0 || dy == 0,\n      RuleShape.Forward => IsForwardOffset(team, dx, dy, distance),''',
    '''      RuleShape.Line or RuleShape.PierceStraight => dx == 0 || dy == 0,\n      RuleShape.Diagonal => Math.Abs(dx) == Math.Abs(dy),\n      RuleShape.LineOrDiagonal => dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy),\n      RuleShape.ChessKnight => chessboardDistance == 2 && taxicabDistance == 3,\n      RuleShape.ForwardLine => IsForwardOffset(team, dx, dy, distance),\n      RuleShape.Forward => IsForwardOffset(team, dx, dy, distance),''',
    "attack shape geometry",
)
write(unit_path, unit)

movement_path = "MedivalChess.Shared/MovementRules.cs"
movement = read(movement_path)
movement = replace_once(
    movement,
    '''    if (unit.MovePattern == RuleShape.Line)\n    {''',
    '''    if (unit.MovePattern == RuleShape.ChessKnight)\n    {\n      return FindChessKnightPaths(unit, origin, canLand, landingCost, movementRangeAt, maximumRange);\n    }\n\n    if (unit.MovePattern is RuleShape.Line or RuleShape.Diagonal or RuleShape.LineOrDiagonal)\n    {''',
    "special movement patterns",
)
movement = replace_once(
    movement,
    '''          if (canLand(next)) paths[next] = nextPath;''',
    '''          if (nextPath.Count >= unit.MinimumMoveRange && canLand(next)) paths[next] = nextPath;''',
    "minimum movement in path search",
)
movement = replace_once(
    movement,
    '''        if (canLand(next)) paths[next] = [.. path];''',
    '''        if (distance >= unit.MinimumMoveRange && canLand(next)) paths[next] = [.. path];''',
    "minimum movement in ray search",
)
find_line_anchor = '''  private static Dictionary<(int x, int y), List<(int x, int y)>> FindLinePaths('''
knight_helper = '''  private static Dictionary<(int x, int y), List<(int x, int y)>> FindChessKnightPaths(
    UnitRule unit,
    (int x, int y) origin,
    Func<(int x, int y), bool> canLand,
    Func<(int x, int y), int> landingCost,
    Func<(int x, int y), int>? movementRangeAt,
    int maximumMovementRange
  )
  {
    Dictionary<(int x, int y), List<(int x, int y)>> paths = [];
    (int x, int y)[] offsets =
    [
      (2, 1), (2, -1), (-2, 1), (-2, -1),
      (1, 2), (1, -2), (-1, 2), (-1, -2)
    ];
    foreach ((int x, int y) offset in offsets)
    {
      (int x, int y) next = (origin.x + offset.x, origin.y + offset.y);
      int availableRange = movementRangeAt?.Invoke(next) ?? unit.MoveRange;
      if (maximumMovementRange < 3 || availableRange < 3 || landingCost(next) > availableRange || !canLand(next))
      {
        continue;
      }
      paths[next] = [next];
    }
    return paths;
  }

'''
movement = replace_once(movement, find_line_anchor, knight_helper + find_line_anchor, "Chess knight path helper")
old_dirs = '''  public static IReadOnlyList<(int x, int y)> GetStepDirections(RuleShape shape, NetworkTeam team) => shape switch
  {
    RuleShape.Straight => [(1, 0), (-1, 0), (0, 1), (0, -1)],
    RuleShape.Line => [(1, 0), (-1, 0), (0, 1), (0, -1)],
    RuleShape.Any => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.Forward => [TeamRules.GetForwardDirection(team)],
    RuleShape.ForwardOrForwardDiagonal => GetForwardAndDiagonalDirections(team),
    RuleShape.AbsoluteStraightOrDiagonal => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    _ => []
  };'''
new_dirs = '''  public static IReadOnlyList<(int x, int y)> GetStepDirections(RuleShape shape, NetworkTeam team) => shape switch
  {
    RuleShape.Straight => [(1, 0), (-1, 0), (0, 1), (0, -1)],
    RuleShape.Line => [(1, 0), (-1, 0), (0, 1), (0, -1)],
    RuleShape.Diagonal => [(1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.LineOrDiagonal => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.Any => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    RuleShape.Forward or RuleShape.ForwardLine => [TeamRules.GetForwardDirection(team)],
    RuleShape.ForwardOrForwardDiagonal => GetForwardAndDiagonalDirections(team),
    RuleShape.AbsoluteStraightOrDiagonal => [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)],
    _ => []
  };'''
movement = replace_once(movement, old_dirs, new_dirs, "movement directions")
write(movement_path, movement)

control_path = "GameBoard/PieceControl.cs"
control = read(control_path)
control = replace_once(
    control,
    '''      Shape.Straight or Shape.Line => ShapeFuncs.OrthogonalStepDirections(),\n      Shape.Any =>''',
    '''      Shape.Straight or Shape.Line => ShapeFuncs.OrthogonalStepDirections(),\n      Shape.Diagonal => ShapeFuncs.DiagonalStepDirections(),\n      Shape.LineOrDiagonal => ShapeFuncs.AllLineStepDirections(),\n      Shape.ChessKnight => ShapeFuncs.ChessKnightShape(),\n      Shape.ForwardLine => ShapeFuncs.ForwardShape(piece.Team, 1, false),\n      Shape.Any =>''',
    "local movement directions",
)
control = replace_once(
    control,
    '''    Shape.Line => RuleShape.Line,\n    Shape.Forward => RuleShape.Forward,''',
    '''    Shape.Line => RuleShape.Line,\n    Shape.Diagonal => RuleShape.Diagonal,\n    Shape.LineOrDiagonal => RuleShape.LineOrDiagonal,\n    Shape.ChessKnight => RuleShape.ChessKnight,\n    Shape.ForwardLine => RuleShape.ForwardLine,\n    Shape.Forward => RuleShape.Forward,''',
    "local rule shape mapping",
)
control = replace_once(
    control,
    '''      case Shape.Any:\n        squares = ShapeFuncs.AnyShape(action.range);\n        break;\n\n      case Shape.Forward:''',
    '''      case Shape.Any:\n        squares = ShapeFuncs.AnyShape(action.range);\n        break;\n\n      case Shape.Diagonal:\n        squares = ShapeFuncs.DiagonalShape(action.range);\n        break;\n\n      case Shape.LineOrDiagonal:\n        squares = ShapeFuncs.LineOrDiagonalShape(action.range);\n        break;\n\n      case Shape.ChessKnight:\n        squares = ShapeFuncs.ChessKnightShape();\n        break;\n\n      case Shape.ForwardLine:\n        squares = ShapeFuncs.ForwardShape(piece.Team, action.range, false);\n        break;\n\n      case Shape.Forward:''',
    "local action shapes",
)
control = replace_once(
    control,
    '''    if (!isMoving && piece.Definition.MinimumAttackRange > 0)\n    {\n      squares.RemoveAll(square => ShapeFuncs.Distance(action.shape, square) < piece.Definition.MinimumAttackRange);\n    }''',
    '''    int minimumRange = isMoving ? piece.Definition.Movement.Minimum : piece.Definition.MinimumAttackRange;\n    if (minimumRange > 0)\n    {\n      squares.RemoveAll(square => ShapeFuncs.Distance(action.shape, square) < minimumRange);\n    }''',
    "local minimum movement",
)
control = replace_once(
    control,
    '''  internal static List<(int x, int y)> OrthogonalStepDirections()\n  {\n    return [(1, 0), (-1, 0), (0, 1), (0, -1)];\n  }''',
    '''  internal static List<(int x, int y)> OrthogonalStepDirections()\n  {\n    return [(1, 0), (-1, 0), (0, 1), (0, -1)];\n  }\n\n  internal static List<(int x, int y)> DiagonalStepDirections()\n  {\n    return [(1, 1), (1, -1), (-1, 1), (-1, -1)];\n  }\n\n  internal static List<(int x, int y)> AllLineStepDirections()\n  {\n    return [.. OrthogonalStepDirections(), .. DiagonalStepDirections()];\n  }''',
    "local step direction helpers",
)
control = replace_once(
    control,
    '''  internal static List<(int x, int y)> ForwardShape(TeamName team, int range, bool includeDiagonals)''',
    '''  internal static List<(int x, int y)> DiagonalShape(int range)\n  {\n    List<(int x, int y)> validSquares = [];\n    for (int distance = 1; distance <= range; distance++)\n    {\n      validSquares.Add((distance, distance));\n      validSquares.Add((distance, -distance));\n      validSquares.Add((-distance, distance));\n      validSquares.Add((-distance, -distance));\n    }\n    return validSquares;\n  }\n\n  internal static List<(int x, int y)> LineOrDiagonalShape(int range) =>\n    [.. LineShape(range), .. DiagonalShape(range)];\n\n  internal static List<(int x, int y)> ChessKnightShape() =>\n  [\n    (2, 1), (2, -1), (-2, 1), (-2, -1),\n    (1, 2), (1, -2), (-1, 2), (-1, -2)\n  ];\n\n  internal static List<(int x, int y)> ForwardShape(TeamName team, int range, bool includeDiagonals)''',
    "local shape helpers",
)
control = replace_once(
    control,
    '''    return shape == Shape.Straight\n      ? Math.Abs(offset.x) + Math.Abs(offset.y)\n      : Math.Max(Math.Abs(offset.x), Math.Abs(offset.y));''',
    '''    return shape is Shape.Straight or Shape.ChessKnight\n      ? Math.Abs(offset.x) + Math.Abs(offset.y)\n      : Math.Max(Math.Abs(offset.x), Math.Abs(offset.y));''',
    "local shape distance",
)
write(control_path, control)

combat_path = "MedivalChess.Shared/CombatRules.cs"
combat = read(combat_path)
combat = replace_once(
    combat,
    '''            (attacker.AttackPattern == RuleShape.Line && blocksDirectPath(square)))''',
    '''            (attacker.AttackPattern is RuleShape.Line or RuleShape.ForwardLine && blocksDirectPath(square)))''',
    "forward-line line of sight",
)
write(combat_path, combat)


# -----------------------------------------------------------------------------
# Network and campaign data carry allowed-pack choices.
# -----------------------------------------------------------------------------
network_path = "MedivalChess.Shared/NetworkMessages.cs"
network = read(network_path)
network = replace_once(
    network,
    '''  // When supplied, this authored preset is used directly instead of choosing one from the density filter.\n  string? PresetId = null\n);''',
    '''  // When supplied, this authored preset is used directly instead of choosing one from the density filter.\n  string? PresetId = null,\n  // Null is accepted from older clients and means every pack.\n  IReadOnlyList<string>? AllowedPacks = null\n);''',
    "network allowed packs",
)
write(network_path, network)

campaign_def_path = "MedivalChess.Shared/CampaignLevelDefinition.cs"
campaign_def = read(campaign_def_path)
campaign_def = replace_once(
    campaign_def,
    '''  public int? MoveRange { get; set; }\n  public Shape? MovePattern { get; set; }''',
    '''  public int? MinimumMoveRange { get; set; }\n  public int? MoveRange { get; set; }\n  public Shape? MovePattern { get; set; }''',
    "campaign minimum movement override",
)
campaign_def = replace_once(
    campaign_def,
    '''  public bool AbilitiesEnabled { get; set; } = true;\n  /// <summary>Empty means use the team's available unit list.</summary>''',
    '''  public bool AbilitiesEnabled { get; set; } = true;\n  public List<string> AllowedPacks { get; set; } = [.. PackRules.AllNames];\n  /// <summary>Empty means use the team's available unit list.</summary>''',
    "campaign allowed packs",
)
write(campaign_def_path, campaign_def)

resolver_path = "MedivalChess.Shared/CampaignUnitResolver.cs"
resolver = read(resolver_path)
resolver = replace_once(
    resolver,
    '''        new PieceDefinition(nativeAbilitySource.Type, native.Category, native.Movement, native.Attack, native.Health, native.Size,\n          native.AttackRange, native.AttackPattern, native.Cost, nativeAbilitySource.AbilityDescription, native.Identifier, template.Name, template.Abbreviation),''',
    '''        new PieceDefinition(nativeAbilitySource.Type, native.Category, native.Pack, native.Movement, native.Attack, native.Health, native.Size,\n          native.AttackRange, native.AttackPattern, native.Cost, nativeAbilitySource.AbilityDescription, native.Identifier, template.Name, template.Abbreviation),''',
    "native override pack",
)
resolver = replace_once(
    resolver,
    '''        abilitySource.Type,\n        baseDefinition.Category,\n        baseDefinition.Movement,''',
    '''        abilitySource.Type,\n        baseDefinition.Category,\n        baseDefinition.Pack,\n        baseDefinition.Movement,''',
    "custom unit pack",
)
resolver = replace_once(
    resolver,
    '''  public static IReadOnlyList<string> GetPurchasableIdentifiers(CampaignLevelDefinition level) =>\n    PieceDefinitions.Purchasable.Where(definition => !((level.UnitOverrides ?? [])''',
    '''  public static IReadOnlyList<string> GetPurchasableIdentifiers(CampaignLevelDefinition level) =>\n    PieceDefinitions.Purchasable\n      .Where(definition => PackRules.IsAllowed(definition, level.Restrictions?.AllowedPacks))\n      .Where(definition => !((level.UnitOverrides ?? [])''',
    "campaign purchase pack filter",
)
resolver = replace_once(
    resolver,
    '''      int minimumAttackRange = overrides?.MinimumAttackRange ?? source.AttackRange.Minimum;\n      int maximumAttackRange = overrides?.MaximumAttackRange ?? source.AttackRange.Maximum;\n      definition = new PieceDefinition(\n        source.Type,\n        source.Category,\n        (overrides?.MoveRange ?? source.Movement.range, overrides?.MovePattern ?? source.Movement.shape),''',
    '''      int minimumMoveRange = overrides?.MinimumMoveRange ?? source.Movement.Minimum;\n      int maximumMoveRange = overrides?.MoveRange ?? source.Movement.Maximum;\n      int minimumAttackRange = overrides?.MinimumAttackRange ?? source.AttackRange.Minimum;\n      int maximumAttackRange = overrides?.MaximumAttackRange ?? source.AttackRange.Maximum;\n      definition = new PieceDefinition(\n        source.Type,\n        source.Category,\n        source.Pack,\n        new MovementDefinition(minimumMoveRange, maximumMoveRange, overrides?.MovePattern ?? source.Movement.shape),''',
    "campaign movement range preservation",
)
write(resolver_path, resolver)

validator_path = "MedivalChess.Shared/CampaignLevelValidator.cs"
validator = read(validator_path)
validator = replace_once(
    validator,
    '''    if (overrides.MoveRange is < 0 or > 32 || overrides.Attack is < 0 or > 10_000 ||''',
    '''    if (overrides.MinimumMoveRange is < 0 or > 32 || overrides.MoveRange is < 0 or > 32 || overrides.Attack is < 0 or > 10_000 ||''',
    "validate minimum move range",
)
validator = replace_once(
    validator,
    '''    if (overrides.MinimumAttackRange.HasValue && overrides.MaximumAttackRange.HasValue &&\n        overrides.MaximumAttackRange.Value < overrides.MinimumAttackRange.Value)''',
    '''    if (overrides.MinimumMoveRange.HasValue && overrides.MoveRange.HasValue &&\n        overrides.MoveRange.Value < overrides.MinimumMoveRange.Value)\n    {\n      problems.Add(CampaignValidationProblem.Error(code, "Maximum movement range cannot be lower than minimum movement range."));\n    }\n    if (overrides.MinimumAttackRange.HasValue && overrides.MaximumAttackRange.HasValue &&\n        overrides.MaximumAttackRange.Value < overrides.MinimumAttackRange.Value)''',
    "validate movement range ordering",
)
validator = replace_once(
    validator,
    '''    ValidateUnitIdentifiers(restrictions.AllowedUnitTypes, "restriction.allowedUnits", customUnitIds, problems);''',
    '''    if (!PackRules.TryNormaliseAllowedPacks(restrictions.AllowedPacks, out _))\n    {\n      problems.Add(CampaignValidationProblem.Error("restriction.allowedPacks", "Choose at least one recognised unit pack."));\n    }\n    ValidateUnitIdentifiers(restrictions.AllowedUnitTypes, "restriction.allowedUnits", customUnitIds, problems);''',
    "validate allowed packs",
)
write(validator_path, validator)

state_path = "Campaign/LevelEditorState.cs"
state = read(state_path)
state = replace_once(
    state,
    '''  public void UpdateScenario(Action<CampaignScenarioDefinition> update)\n  {\n    ArgumentNullException.ThrowIfNull(update);\n    Change("Update scenario", level => update(level.Scenario));\n  }''',
    '''  public void UpdateScenario(Action<CampaignScenarioDefinition> update)\n  {\n    ArgumentNullException.ThrowIfNull(update);\n    Change("Update scenario", level => update(level.Scenario));\n  }\n\n  public void UpdateRestrictions(Action<CampaignRestrictionsDefinition> update)\n  {\n    ArgumentNullException.ThrowIfNull(update);\n    Change("Update restrictions", level => update(level.Restrictions));\n  }''',
    "editor restriction state update",
)
write(state_path, state)

editor_path = "Campaign/LevelEditorScreen.cs"
editor = read(editor_path)
editor = replace_once(
    editor,
    '''    _ui.TextWrapped("Team-level buying and available-unit lists override this global rule. Disable abilities or a unit here to create hard campaign restrictions.", layout.RulesHelp, UiTheme.TextMuted, 0.62f);''',
    '''    _ui.TextFitted("ALLOWED PACKS", new Vector2(layout.RulesHelp.X, layout.RulesHelp.Y), layout.RulesHelp.Width, UiTheme.GoldBright, 0.58f, 0.40f);\n    IReadOnlySet<Pack> allowedPacks = PackRules.GetAllowedPacks(rules.AllowedPacks);\n    foreach ((Pack pack, Rectangle bounds) in GetRestrictionPackButtons(layout))\n    {\n      bool allowed = allowedPacks.Contains(pack);\n      _ui.Button(bounds, pack.ToString().ToUpperInvariant(), allowed ? UiButtonTone.Primary : UiButtonTone.Neutral, allowed, 0.48f);\n    }''',
    "editor pack buttons draw",
)
editor = replace_once(
    editor,
    '''    if (layout.TeamUnitToggle.Contains(point))\n    {\n      IReadOnlyList<(string identifier, PieceDefinition definition)> buyPalette = GetPurchasableUnitPalette();\n      string type = buyPalette[_restrictionUnitIndex % buyPalette.Count].identifier;\n      State.UpdateScenario(_ => ToggleUnit(State.Level.Restrictions.DisabledUnitTypes, type));\n      return true;\n    }\n    return false;''',
    '''    if (layout.TeamUnitToggle.Contains(point))\n    {\n      IReadOnlyList<(string identifier, PieceDefinition definition)> buyPalette = GetPurchasableUnitPalette();\n      string type = buyPalette[_restrictionUnitIndex % buyPalette.Count].identifier;\n      State.UpdateRestrictions(rules => ToggleUnit(rules.DisabledUnitTypes, type));\n      return true;\n    }\n    foreach ((Pack pack, Rectangle bounds) in GetRestrictionPackButtons(layout))\n    {\n      if (!bounds.Contains(point)) continue;\n      IReadOnlySet<Pack> allowed = PackRules.GetAllowedPacks(State.Level.Restrictions.AllowedPacks);\n      if (allowed.Contains(pack) && allowed.Count <= 1)\n      {\n        _status = "A level must keep at least one unit pack enabled.";\n        return true;\n      }\n      State.UpdateRestrictions(rules =>\n      {\n        HashSet<Pack> updated = PackRules.GetAllowedPacks(rules.AllowedPacks).ToHashSet();\n        if (!updated.Add(pack)) updated.Remove(pack);\n        rules.AllowedPacks = PackRules.All.Where(updated.Contains).Select(value => value.ToString()).ToList();\n      });\n      _unitPaletteIndex = 0;\n      _restrictionUnitIndex = 0;\n      _unitCataloguePage = 0;\n      _expandedCatalogueUnitId = null;\n      _status = $"{pack} pack {(PackRules.GetAllowedPacks(State.Level.Restrictions.AllowedPacks).Contains(pack) ? "enabled" : "disabled")}.";\n      return true;\n    }\n    return false;''',
    "editor pack button clicks",
)
# Use the spare rules-help rectangle as a compact 4x2 pack grid.
editor = replace_once(
    editor,
    '''  private void CycleRestrictionUnit(int direction)\n  {''',
    '''  private static IReadOnlyList<(Pack pack, Rectangle bounds)> GetRestrictionPackButtons(EditorLayout layout)\n  {\n    IReadOnlyList<Pack> packs = PackRules.All;\n    Rectangle area = layout.RulesHelp;\n    const int columns = 4;\n    const int gap = 3;\n    int top = area.Y + 17;\n    int availableHeight = Math.Max(20, area.Bottom - top);\n    int rowHeight = Math.Max(18, (availableHeight - gap) / 2);\n    int width = Math.Max(24, (area.Width - gap * (columns - 1)) / columns);\n    List<(Pack pack, Rectangle bounds)> result = [];\n    for (int index = 0; index < packs.Count; index++)\n    {\n      int row = index / columns;\n      int column = index % columns;\n      result.Add((packs[index], new Rectangle(area.X + column * (width + gap), top + row * (rowHeight + gap), width, rowHeight)));\n    }\n    return result;\n  }\n\n  private void CycleRestrictionUnit(int direction)\n  {''',
    "editor pack button layout",
)
editor = replace_once(
    editor,
    '''    foreach (PieceDefinition native in PieceDefinitions.All)\n    {\n      if (!CampaignUnitResolver.TryResolve(State.Level, native.Identifier, null, out PieceDefinition definition)) continue;''',
    '''    IReadOnlySet<Pack> allowedPacks = PackRules.GetAllowedPacks(State.Level.Restrictions.AllowedPacks);\n    foreach (PieceDefinition native in PieceDefinitions.All)\n    {\n      if (!allowedPacks.Contains(native.Pack)) continue;\n      if (!CampaignUnitResolver.TryResolve(State.Level, native.Identifier, null, out PieceDefinition definition)) continue;''',
    "editor catalogue pack filter",
)
write(editor_path, editor)


# -----------------------------------------------------------------------------
# Local/online setup UI and runtime filtering.
# -----------------------------------------------------------------------------
game_path = "Game1.cs"
game = read(game_path)
game = replace_once(
    game,
    '''  private enum SetupStage\n  {\n    Mode,\n    Battlefield,''',
    '''  private enum SetupStage\n  {\n    Mode,\n    Packs,\n    Battlefield,''',
    "setup pack stage enum",
)
game = replace_once(
    game,
    '''  private SetupStage _setupStage = SetupStage.Mode;\n  private BoardSize _selectedBoardSize = BoardSize.Medium;''',
    '''  private SetupStage _setupStage = SetupStage.Mode;\n  private readonly HashSet<Pack> _allowedPacks = [.. PackRules.All];\n  private BoardSize _selectedBoardSize = BoardSize.Medium;''',
    "allowed packs state",
)
game = replace_once(
    game,
    '''    _cpuMatchVariationSeed = Random.Shared.Next();\n    _screen = Screen.Setup;\n    SetPlayerCount(2);''',
    '''    _cpuMatchVariationSeed = Random.Shared.Next();\n    _screen = Screen.Setup;\n    _allowedPacks.Clear();\n    _allowedPacks.UnionWith(PackRules.All);\n    SetPlayerCount(2);''',
    "reset setup packs",
)
game = replace_once(
    game,
    '''            else if (GetSetupConfirmButtonBounds().Contains(mousePosition))\n            {\n              _setupStage = SetupStage.Battlefield;\n            }\n          }\n        }\n        else if (_setupStage == SetupStage.Battlefield)''',
    '''            else if (GetSetupConfirmButtonBounds().Contains(mousePosition))\n            {\n              _setupStage = SetupStage.Packs;\n            }\n          }\n        }\n        else if (_setupStage == SetupStage.Packs)\n        {\n          bool handledPack = false;\n          foreach (Pack pack in PackRules.All)\n          {\n            if (!GetSetupPackButtonBounds(pack).Contains(mousePosition)) continue;\n            ToggleSetupPack(pack);\n            handledPack = true;\n            break;\n          }\n          if (!handledPack && GetSetupConfirmButtonBounds().Contains(mousePosition) && GetAllowedRoyals().Length > 0)\n          {\n            _selectedRoyalIndex = 0;\n            _setupStage = SetupStage.Battlefield;\n          }\n        }\n        else if (_setupStage == SetupStage.Battlefield)''',
    "setup pack clicks",
)
game = replace_once(
    game,
    '''      case SetupStage.Battlefield:\n        _setupStage = SetupStage.Mode;\n        break;''',
    '''      case SetupStage.Packs:\n        _setupStage = SetupStage.Mode;\n        break;\n      case SetupStage.Battlefield:\n        _setupStage = SetupStage.Packs;\n        break;''',
    "setup back navigation",
)
game = replace_once(
    game,
    '''    SetupStage[] stages = [SetupStage.Mode, SetupStage.Battlefield, SetupStage.Economy, SetupStage.ModeSettings, SetupStage.RoyalSelection];\n    string[] labels = ["MODE", "MAP", "ECONOMY", "RULES", "ROYAL"];''',
    '''    SetupStage[] stages = [SetupStage.Mode, SetupStage.Packs, SetupStage.Battlefield, SetupStage.Economy, SetupStage.ModeSettings, SetupStage.RoyalSelection];\n    string[] labels = ["MODE", "PACKS", "MAP", "ECONOMY", "RULES", "ROYAL"];''',
    "setup progress packs",
)
game = replace_once(
    game,
    '''    if (_setupStage == SetupStage.Battlefield)\n    {\n      DrawBattlefieldSetup(panel);''',
    '''    if (_setupStage == SetupStage.Packs)\n    {\n      DrawPackSetup(panel);\n      return;\n    }\n\n    if (_setupStage == SetupStage.Battlefield)\n    {\n      DrawBattlefieldSetup(panel);''',
    "draw pack setup dispatch",
)
pack_ui_anchor = '''  private void DrawModeSetup(Rectangle panel)\n  {'''
pack_ui = '''  private Rectangle GetSetupPackButtonBounds(Pack pack)\n  {\n    Rectangle content = UiLayout.Inset(GetSetupPanelBounds(), UiTheme.SpaceLg);\n    int index = Array.IndexOf(Enum.GetValues<Pack>(), pack);\n    const int columns = 2;\n    const int gap = 10;\n    const int buttonHeight = 46;\n    int width = (content.Width - gap) / columns;\n    int row = index / columns;\n    int column = index % columns;\n    return new Rectangle(content.X + column * (width + gap), content.Y + 112 + row * (buttonHeight + gap), width, buttonHeight);\n  }\n\n  private PieceDefinition[] GetAllowedRoyals() => PieceDefinitions.Royals\n    .Where(royal => _allowedPacks.Contains(royal.Pack))\n    .ToArray();\n\n  private void ToggleSetupPack(Pack pack)\n  {\n    if (_allowedPacks.Contains(pack))\n    {\n      if (_allowedPacks.Count <= 1) return;\n      _allowedPacks.Remove(pack);\n      if (GetAllowedRoyals().Length == 0)\n      {\n        _allowedPacks.Add(pack);\n        return;\n      }\n      if (pack == Pack.Base) _farmsEnabled = false;\n    }\n    else\n    {\n      _allowedPacks.Add(pack);\n    }\n    _selectedRoyalIndex = 0;\n  }\n\n  private void DrawPackSetup(Rectangle panel)\n  {\n    Rectangle content = UiLayout.Inset(panel, UiTheme.SpaceLg);\n    DrawPanel(panel, UiTheme.Panel, UiTheme.Gold);\n    _ui.Text("ALLOWED PACKS", new Vector2(content.X, content.Y), UiTheme.Gold);\n    DrawMenuButton(GetSetupBackButtonBounds(), "BACK", UiButtonTone.Neutral);\n    _ui.Text("Choose which unit packs can be bought and which Royals can be selected.", new Vector2(content.X, content.Y + 28), UiTheme.TextMuted, 0.72f);\n    _ui.Divider(content, content.Y + 56);\n    DrawSetupProgress(content);\n\n    foreach (Pack pack in PackRules.All)\n    {\n      bool selected = _allowedPacks.Contains(pack);\n      DrawMenuButton(GetSetupPackButtonBounds(pack), pack.ToString().ToUpperInvariant(), selected ? UiButtonTone.Primary : UiButtonTone.Neutral, selected, 0.76f);\n    }\n\n    string hint = GetAllowedRoyals().Length == 0\n      ? "Select a pack containing a complete Royal before continuing."\n      : $"{_allowedPacks.Count} pack{(_allowedPacks.Count == 1 ? string.Empty : "s")} enabled.";\n    _ui.Text(hint, new Vector2(content.X, content.Bottom - 92), GetAllowedRoyals().Length == 0 ? UiTheme.Attack : UiTheme.TextMuted, 0.66f);\n    DrawMenuButton(GetSetupConfirmButtonBounds(), "CONTINUE", GetAllowedRoyals().Length == 0 ? UiButtonTone.Danger : UiButtonTone.Primary);\n  }\n\n'''
game = replace_once(game, pack_ui_anchor, pack_ui + pack_ui_anchor, "pack setup UI")
# Replace all selection-time Royal access; the helper itself is inserted after this replacement.
# It is intentionally done here before writing, then restore the helper's implementation if needed.
royal_ref_count = game.count("PieceDefinitions.Royals")
if royal_ref_count < 5:
    raise RuntimeError(f"royal filtering: expected several Royal references, found {royal_ref_count}")
game = game.replace("PieceDefinitions.Royals", "GetAllowedRoyals()")
# The newly inserted helper was also transformed by the global replacement; fix its body once.
game = replace_once(
    game,
    '''  private PieceDefinition[] GetAllowedRoyals() => GetAllowedRoyals()\n    .Where(royal => _allowedPacks.Contains(royal.Pack))\n    .ToArray();''',
    '''  private PieceDefinition[] GetAllowedRoyals() => PieceDefinitions.Royals\n    .Where(royal => _allowedPacks.Contains(royal.Pack))\n    .ToArray();''',
    "restore allowed royal helper",
)
game = replace_once(
    game,
    '''    else\n    {\n      definitions = PieceDefinitions.Purchasable;\n    }''',
    '''    else\n    {\n      definitions = PieceDefinitions.Purchasable.Where(definition => _allowedPacks.Contains(definition.Pack));\n    }''',
    "local purchase pack filter",
)
game = replace_once(
    game,
    '''      _chessTimerIncrementSeconds,\n      _terrainSource.ToString(),\n      _selectedTerrainPresetId\n    );''',
    '''      _chessTimerIncrementSeconds,\n      _terrainSource.ToString(),\n      _selectedTerrainPresetId,\n      _allowedPacks.Select(pack => pack.ToString()).ToArray()\n    );''',
    "network configuration packs",
)
game = replace_once(
    game,
    '''    _selectedTerrainPresetId = configuration.PresetId;\n    _selectedTerrainPresetName = null;\n    _gameMode = gameMode;''',
    '''    _selectedTerrainPresetId = configuration.PresetId;\n    _selectedTerrainPresetName = null;\n    _allowedPacks.Clear();\n    _allowedPacks.UnionWith(PackRules.GetAllowedPacks(configuration.AllowedPacks));\n    _gameMode = gameMode;''',
    "apply online allowed packs",
)
game = replace_once(
    game,
    '''    _campaignTestDefinition = CampaignLevelCloner.Clone(snapshot.Level);\n    _campaignCompletedRounds = 0;''',
    '''    _campaignTestDefinition = CampaignLevelCloner.Clone(snapshot.Level);\n    _allowedPacks.Clear();\n    _allowedPacks.UnionWith(PackRules.GetAllowedPacks(_campaignTestDefinition.Restrictions.AllowedPacks));\n    _campaignCompletedRounds = 0;''',
    "campaign test pack sync",
)
write(game_path, game)


# -----------------------------------------------------------------------------
# Authoritative server and CPU legality must enforce the same pack choice.
# -----------------------------------------------------------------------------
server_path = "MedivalChess.Server/MatchHub.cs"
server = read(server_path)
server = replace_once(
    server,
    '''      if (!RoyalTypes.Contains(request.RoyalType) ||\n          (foundMatch.Configuration.GameMode == "Escort" && request.RoyalType == "Palace"))''',
    '''      if (!RoyalTypes.Contains(request.RoyalType) ||\n          !PackRules.IsAllowed(request.RoyalType, foundMatch.Configuration.AllowedPacks) ||\n          (foundMatch.Configuration.GameMode == "Escort" && request.RoyalType == "Palace"))''',
    "server royal pack filter",
)
server = replace_once(
    server,
    '''    if (!UnitRules.TryGet(type, out UnitRule rule) ||\n        !UnitRules.Purchasable.Contains(rule) ||''',
    '''    if (!UnitRules.TryGet(type, out UnitRule rule) ||\n        !UnitRules.Purchasable.Contains(rule) ||\n        !PackRules.IsAllowed(type, match.Configuration.AllowedPacks) ||''',
    "server purchase pack filter",
)
server = replace_once(
    server,
    '''    sanitized = configuration with\n    {\n      PresetId = string.IsNullOrWhiteSpace(configuration.PresetId) ? null : configuration.PresetId.Trim()\n    };''',
    '''    if (!PackRules.TryNormaliseAllowedPacks(configuration.AllowedPacks, out string[] allowedPacks))\n    {\n      error = "Choose at least one recognised unit pack.";\n      return false;\n    }\n\n    sanitized = configuration with\n    {\n      PresetId = string.IsNullOrWhiteSpace(configuration.PresetId) ? null : configuration.PresetId.Trim(),\n      AllowedPacks = allowedPacks,\n      FarmsEnabled = configuration.FarmsEnabled && allowedPacks.Contains(Pack.Base.ToString(), StringComparer.Ordinal)\n    };''',
    "server pack configuration validation",
)
write(server_path, server)

cpu_rules_path = "MedivalChess.CPU/CpuGameRules.cs"
cpu_rules = read(cpu_rules_path)
cpu_rules = replace_once(
    cpu_rules,
    '''    if (!UnitRules.TryGet(action.UnitType, out UnitRule rule) || !UnitRules.Purchasable.Contains(rule) ||\n        (rule.Type == "Farm" && !state.Configuration.FarmsEnabled))''',
    '''    if (!UnitRules.TryGet(action.UnitType, out UnitRule rule) || !UnitRules.Purchasable.Contains(rule) ||\n        !PackRules.IsAllowed(action.UnitType, state.Configuration.AllowedPacks) ||\n        (rule.Type == "Farm" && !state.Configuration.FarmsEnabled))''',
    "CPU purchase pack legality",
)
write(cpu_rules_path, cpu_rules)

cpu_actions_path = "MedivalChess.CPU/CpuActionGenerator.cs"
cpu_actions = read(cpu_actions_path)
cpu_actions = replace_once(
    cpu_actions,
    '''      : UnitRules.Purchasable.Where(rule => rule.Type == "Mercenary" ||\n        availableMoney >= GetPurchaseCost(state, rule));''',
    '''      : UnitRules.Purchasable\n        .Where(rule => PackRules.IsAllowed(rule.Type, state.Configuration.AllowedPacks))\n        .Where(rule => rule.Type == "Mercenary" || availableMoney >= GetPurchaseCost(state, rule));''',
    "CPU purchase generation pack filter",
)
write(cpu_actions_path, cpu_actions)

army_path = "MedivalChess.CPU/CpuArmyPlanner.cs"
army = read(army_path)
army = replace_once(
    army,
    '''    Dictionary<string, float> needs = UnitRules.Purchasable\n      .Where(rule => rule.Type != "Farm")''',
    '''    Dictionary<string, float> needs = UnitRules.Purchasable\n      .Where(rule => PackRules.IsAllowed(rule.Type, state.Configuration.AllowedPacks))\n      .Where(rule => rule.Type != "Farm")''',
    "CPU recruitment needs pack filter",
)
army = replace_once(
    army,
    '''    int cheapestCounter = UnitRules.Purchasable\n      .Where(rule => priorityCounters.Contains(rule.Type) && needs.GetValueOrDefault(rule.Type) >= 14f)''',
    '''    int cheapestCounter = UnitRules.Purchasable\n      .Where(rule => PackRules.IsAllowed(rule.Type, state.Configuration.AllowedPacks))\n      .Where(rule => priorityCounters.Contains(rule.Type) && needs.GetValueOrDefault(rule.Type) >= 14f)''',
    "CPU reserve pack filter",
)
write(army_path, army)


# -----------------------------------------------------------------------------
# Regression tests for PDF data, pack filtering, and new movement shapes.
# -----------------------------------------------------------------------------
write(
    "MedivalChess.Tests/NewUnitsAndPacksTests.cs",
    '''using MedivalChess.Shared;
using Xunit;

namespace MedivalChess.Tests;

public sealed class NewUnitsAndPacksTests
{
  [Fact]
  public void NewPackUnitsExposeAuthoredStatsWithoutNewAbilityBehaviour()
  {
    Assert.Equal(Pack.Dynasty, PieceDefinitions.Ninja.Pack);
    Assert.Equal((1, 4), (PieceDefinitions.Ninja.Movement.Minimum, PieceDefinitions.Ninja.Movement.Maximum));
    Assert.Equal(5, PieceDefinitions.Ninja.Attack);
    Assert.Equal(10, PieceDefinitions.Ninja.Health);
    Assert.Equal((2, 4), (PieceDefinitions.Ninja.AttackRange.Minimum, PieceDefinitions.Ninja.AttackRange.Maximum));
    Assert.Equal(40, PieceDefinitions.Ninja.Cost);

    Assert.Equal(Pack.Fantasy, PieceDefinitions.Dragon.Pack);
    Assert.Equal((2, 3), PieceDefinitions.Dragon.Size);
    Assert.Equal(Shape.ForwardLine, PieceDefinitions.Dragon.AttackPattern);
    Assert.Equal(90, PieceDefinitions.Dragon.Cost);

    Assert.Equal(Pack.Greek, PieceDefinitions.Pegasus.Pack);
    Assert.Equal((2, 4), (PieceDefinitions.Pegasus.Movement.Minimum, PieceDefinitions.Pegasus.Movement.Maximum));
    Assert.Null(PieceDefinitions.Pegasus.Abbreviation);

    Assert.Equal(10, PieceDefinitions.Princess.Attack);

    Assert.Equal(Pack.Chess, PieceDefinitions.Queen.Pack);
    Assert.Equal(Shape.LineOrDiagonal, PieceDefinitions.Queen.Movement.shape);
    Assert.Equal(Shape.None, PieceDefinitions.Queen.AttackPattern);
    Assert.Equal(60, PieceDefinitions.Queen.Attack);
  }

  [Fact]
  public void GeneratedOnlyUnitsAreNotPurchasableOrSelectableRoyals()
  {
    Assert.Contains(PieceDefinitions.Flesh, PieceDefinitions.All);
    Assert.DoesNotContain(PieceDefinitions.Flesh, PieceDefinitions.Purchasable);
    Assert.Contains(PieceDefinitions.TerracottaWarrior, PieceDefinitions.All);
    Assert.DoesNotContain(PieceDefinitions.TerracottaWarrior, PieceDefinitions.Purchasable);
    Assert.DoesNotContain(PieceDefinitions.TerracottaWarrior, PieceDefinitions.Royals);
  }

  [Fact]
  public void IncompletePdfUnitsAreNotInvented()
  {
    Assert.DoesNotContain(PieceDefinitions.All, definition => definition.Type == PieceType.Ghoul);
    Assert.DoesNotContain(PieceDefinitions.All, definition => definition.DisplayName == "President");
  }

  [Fact]
  public void PackRulesNormaliseAndFilterDefinitions()
  {
    Assert.True(PackRules.TryNormaliseAllowedPacks(["chess", "Base", "Chess"], out string[] packs));
    Assert.Equal(["Base", "Chess"], packs);
    Assert.True(PackRules.IsAllowed(PieceDefinitions.Soldier, packs));
    Assert.True(PackRules.IsAllowed(PieceDefinitions.Queen, packs));
    Assert.False(PackRules.IsAllowed(PieceDefinitions.Ninja, packs));
    Assert.False(PackRules.TryNormaliseAllowedPacks([], out _));
    Assert.False(PackRules.TryNormaliseAllowedPacks(["NotAPack"], out _));
  }

  [Fact]
  public void UnitRuleListsRespectExplicitPurchasableAndRoyalCollections()
  {
    Assert.DoesNotContain(UnitRules.Purchasable, rule => rule.Type == "Flesh");
    Assert.DoesNotContain(UnitRules.Purchasable, rule => rule.Type == "TerracottaWarrior");
    Assert.DoesNotContain(UnitRules.Royals, rule => rule.Type == "TerracottaWarrior");
    Assert.Contains(UnitRules.Royals, rule => rule.Type == "Emperor");
  }

  [Fact]
  public void NewMovementShapesUseTheirOwnGeometry()
  {
    UnitRule bishop = UnitRules.GetRequired("Bishop");
    Assert.True(UnitRules.CanMove(bishop, 0, 0, 4, 4));
    Assert.False(UnitRules.CanMove(bishop, 0, 0, 4, 3));

    UnitRule queen = UnitRules.GetRequired("Queen");
    Assert.True(UnitRules.CanMove(queen, 0, 0, 0, 7));
    Assert.True(UnitRules.CanMove(queen, 0, 0, 6, 6));
    Assert.False(UnitRules.CanMove(queen, 0, 0, 6, 4));

    UnitRule knight = UnitRules.GetRequired("ChessKnight");
    Assert.True(UnitRules.CanMove(knight, 0, 0, 2, 1));
    Assert.False(UnitRules.CanMove(knight, 0, 0, 1, 1));
  }

  [Fact]
  public void PegasusCannotStopInsideItsMinimumMovementDistance()
  {
    UnitRule pegasus = UnitRules.GetRequired("Pegasus");
    Dictionary<(int x, int y), List<(int x, int y)>> paths = MovementRules.FindPaths(
      pegasus,
      (0, 0),
      NetworkTeam.Red,
      _ => true,
      (_, _) => true,
      _ => 1,
      (_, _) => false
    );

    Assert.DoesNotContain((1, 0), paths.Keys);
    Assert.Contains((2, 0), paths.Keys);
    Assert.Contains((4, 0), paths.Keys);
  }

  [Fact]
  public void CampaignPurchasesRespectAllowedPacks()
  {
    CampaignLevelDefinition level = CampaignLevelDefinition.CreateNew();
    level.Restrictions.AllowedPacks = [Pack.Greek.ToString()];

    IReadOnlyList<string> purchasable = CampaignUnitResolver.GetPurchasableIdentifiers(level);
    Assert.Contains("Chariot", purchasable);
    Assert.Contains("Pegasus", purchasable);
    Assert.DoesNotContain("Soldier", purchasable);
    Assert.DoesNotContain("Ninja", purchasable);
  }
}
''',
)

print("New unit and pack integration patches applied.")
