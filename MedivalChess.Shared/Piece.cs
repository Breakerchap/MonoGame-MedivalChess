namespace MedivalChess.Shared;

/// <summary>An inclusive distance interval used by an attack. For example, <c>(2, 4)</c> permits targets two through four squares away.</summary>
public readonly record struct AttackRange
{
  public int Minimum { get; }
  public int Maximum { get; }

  public AttackRange(int minimum, int maximum)
  {
    if (minimum < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(minimum), "Attack range cannot be negative.");
    }

    if (maximum < minimum)
    {
      throw new ArgumentOutOfRangeException(nameof(maximum), "Attack maximum must be at least its minimum.");
    }

    Minimum = minimum;
    Maximum = maximum;
  }

  public static implicit operator AttackRange((int minimum, int maximum) range) =>
    new(range.minimum, range.maximum);
}

public enum Shape
{
  Any,
  Straight,
  Line,
  Diagonal,
  LineOrDiagonal,
  ChessKnight,
  Forward,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  ForwardLine,
  PierceStraight,
  MoveOnEnemy,
  None
}

public enum PieceType
{
  // Base
  Soldier,
  Defender,
  Archer,
  Peasant,
  Knight,
  Crossbowman,
  Cavalier,
  Cannon,
  Catapult,
  Bombard,
  Guard,
  Farm,

  King,
  Princess,
  Palace,
  Baron,
  Emissary,

  // Dynasty
  Elephant,
  Ox,
  Ninja,
  Samurai,
  Emperor,
  TerracottaWarrior,

  // Fantasy
  Dragon,
  GoblinRoyalty,
  Adventurer,
  Wizard,
  Dragonborn,

  // Undead
  Skeleton,
  Zombie,
  Flesh,
  Ghoul,
  Phantom,

  // Greek
  Chariot,
  Ballista,
  Zeus,
  Heracles,
  Hermes,
  Ares,
  Chimera,
  Pegasus,

  // Modern
  Spy,
  Engineer,
  Mercenary,

  // Chess
  Pawn,
  ChessKnight,
  Bishop,
  Rook,
  Queen,
  ChessKing
}

public enum Pack
{
  Base,
  Dynasty,
  Fantasy,
  Undead,
  Greek,
  Modern,
  Chess
}

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

  public static implicit operator (int range, Shape shape)(MovementDefinition movement) =>
    (movement.Maximum, movement.Shape);

  public void Deconstruct(out int range, out Shape shape)
  {
    range = Maximum;
    shape = Shape;
  }
}

/// <summary>Authoritative game-facing unit definition. All unit stats are declared in <see cref="PieceDefinitions"/>.</summary>
public sealed class PieceDefinition
{
  public PieceType Type { get; }

  /// <summary>Stable purchase/editor identifier. Native units use their <see cref="Type"/> name.</summary>
  public string Identifier { get; }

  /// <summary>Player-facing name; custom campaign units can differ from their copied ability source.</summary>
  public string DisplayName { get; }

  /// <summary>Short in-board label. Empty uses the game's standard abbreviation for native units.</summary>
  public string? Abbreviation { get; }

  public Pack Pack { get; }

  public PieceCategory Category { get; }

  public MovementDefinition Movement { get; }

  public int Attack { get; }

  public int Health { get; }

  public (int x, int y) Size { get; }

  public AttackRange AttackRange { get; }

  public Shape AttackPattern { get; }

  public (int range, Shape shape) AttackShape =>
    (AttackRange.Maximum, AttackPattern);

  public int MinimumAttackRange => AttackRange.Minimum;

  public int Cost { get; }

  public string AbilityDescription { get; }

  public PieceDefinition(
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
}

/// <summary>
/// The single source of truth for every unit's stats. Attack ranges are inclusive: use
/// <c>(1, 3)</c> for one to three squares, or <c>(2, 4)</c> for two to four.
/// </summary>
public static class PieceDefinitions
{
  /// <summary>Fixed cost to hire a neutral Mercenary; rival buyouts still use the bid ladder.</summary>
  public const int NeutralMercenaryHireCost = 15;

  // ============================================================
  // Base
  // ============================================================

  public static readonly PieceDefinition Soldier = new(
    PieceType.Soldier,
    "Sol",
    Pack.Base,
    ((1, 3), Shape.Straight),
    10,
    15,
    (1, 1),
    (1, 1),
    Shape.Straight,
    20
  );

  public static readonly PieceDefinition Defender = new(
    PieceType.Defender,
    "Def",
    Pack.Base,
    ((1, 2), Shape.Any),
    5,
    25,
    (1, 1),
    (1, 1),
    Shape.Straight,
    20
  );

  public static readonly PieceDefinition Archer = new(
    PieceType.Archer,
    "Arc",
    Pack.Base,
    ((1, 3), Shape.Straight),
    10,
    10,
    (1, 1),
    (2, 3),
    Shape.Any,
    25
  );

  public static readonly PieceDefinition Peasant = new(
    PieceType.Peasant,
    "Pes",
    Pack.Base,
    ((1, 1), Shape.Any),
    5,
    5,
    (1, 1),
    (1, 1),
    Shape.Straight,
    10
  );

  public static readonly PieceDefinition Knight = new(
    PieceType.Knight,
    "Knt",
    Pack.Base,
    ((1, 3), Shape.Straight),
    20,
    30,
    (1, 1),
    (1, 1),
    Shape.Any,
    55
  );

  public static readonly PieceDefinition Crossbowman = new(
    PieceType.Crossbowman,
    "Cbo",
    Pack.Base,
    ((1, 2), Shape.Any),
    20,
    15,
    (1, 1),
    (1, 3),
    Shape.Any,
    50
  );

  public static readonly PieceDefinition Cavalier = new(
    PieceType.Cavalier,
    "Cav",
    Pack.Base,
    ((1, 4), Shape.Any),
    15,
    20,
    (1, 1),
    (1, 1),
    Shape.Any,
    55,
    "If movement is already used, after attacking allow a 2-Straight movement."
  );

  public static readonly PieceDefinition Cannon = new(
    PieceType.Cannon,
    "Cn",
    Pack.Base,
    ((1, 2), Shape.Straight),
    30,
    15,
    (1, 2),
    (2, 3),
    Shape.Line,
    50
  );

  public static readonly PieceDefinition Catapult = new(
    PieceType.Catapult,
    "Cat",
    Pack.Base,
    ((1, 1), Shape.Any),
    20,
    15,
    (1, 2),
    (4, 5),
    Shape.Any,
    55,
    "Attacks over terrain and pieces."
  );

  public static readonly PieceDefinition Bombard = new(
    PieceType.Bombard,
    "Bom",
    Pack.Base,
    ((1, 2), Shape.Straight),
    15,
    15,
    (1, 1),
    (2, 3),
    Shape.Straight,
    55,
    "Every adjacent and diagonally adjacent unit to the target take 10 damage, including friendly units."
  );

  public static readonly PieceDefinition Guard = new(
    PieceType.Guard,
    "Grd",
    Pack.Base,
    ((1, 3), Shape.Straight),
    10,
    25,
    (1, 1),
    (1, 1),
    Shape.Straight,
    35,
    "Attaches to a friendly, non-royal unit and takes damage for it."
  );

  public static readonly PieceDefinition Farm = new(
    PieceType.Farm,
    "Frm",
    Pack.Base,
    ((0, 0), Shape.None),
    0,
    30,
    (3, 3),
    (0, 0),
    Shape.None,
    40,
    "Earns 5 gold at the start of each owner turn. Units may move and attack over it."
  );

  public static readonly PieceDefinition King = new(
    PieceType.King,
    "KIN",
    Pack.Base,
    ((1, 1), Shape.Any),
    15,
    95,
    (1, 1),
    (1, 1),
    Shape.Any,
    0
  );

  public static readonly PieceDefinition Princess = new(
    PieceType.Princess,
    "PRI",
    Pack.Base,
    ((1, 2), Shape.Straight),
    10,
    70,
    (1, 1),
    (1, 4),
    Shape.Any,
    0,
    "May attack over units and terrain and barricades."
  );

  public static readonly PieceDefinition Palace = new(
    PieceType.Palace,
    "PAL",
    Pack.Base,
    ((0, 0), Shape.None),
    0,
    110,
    (3, 2),
    (0, 0),
    Shape.None,
    0,
    "Friendly pieces moving in the direction of the Palace gain +1 movement and ignore terrain."
  );

  public static readonly PieceDefinition Baron = new(
    PieceType.Baron,
    "BAR",
    Pack.Base,
    ((1, 3), Shape.Straight),
    10,
    80,
    (1, 1),
    (1, 1),
    Shape.Any,
    0,
    "Adjacent allies deal 5 additional damage and take 5 less damage."
  );

  public static readonly PieceDefinition Emissary = new(
    PieceType.Emissary,
    "EMI",
    Pack.Base,
    ((1, 4), Shape.Any),
    5,
    70,
    (1, 1),
    (1, 1),
    Shape.Any,
    0,
    "Moves (diagonally) adjacent friendly 1x1 pieces with it."
  );

  // ============================================================
  // Dynasty
  // ============================================================

  public static readonly PieceDefinition Elephant = new(
    PieceType.Elephant,
    "Ele",
    Pack.Dynasty,
    ((1, 3), Shape.Straight),
    10,
    60,
    (2, 2),
    (0, 0),
    Shape.None,
    50,
    "May move through enemies, damaging each crossed unit. Ignores terrain."
  );

  public static readonly PieceDefinition Ox = new(
    PieceType.Ox,
    "Ox",
    Pack.Dynasty,
    ((1, 4), Shape.Any),
    5,
    25,
    (1, 1),
    (1, 1),
    Shape.Straight,
    35,
    "Attaches to a 1x1 friendly unit and increases that unit's Movement by 2. While attached, if attacked, both units take damage."
  );

  public static readonly PieceDefinition Ninja = new(
    PieceType.Ninja,
    "Nj",
    Pack.Dynasty,
    ((1, 4), Shape.Straight),
    5,
    10,
    (1, 1),
    (2, 4),
    Shape.Straight,
    40,
    "May attack up to three times per turn."
  );

  public static readonly PieceDefinition Samurai = new(
    PieceType.Samurai,
    "Sam",
    Pack.Dynasty,
    ((1, 3), Shape.Straight),
    15,
    15,
    (1, 1),
    (1, 1),
    Shape.Straight,
    35,
    "Deflects incoming projectiles."
  );

  public static readonly PieceDefinition Emperor = new(
    PieceType.Emperor,
    "EP",
    Pack.Dynasty,
    ((1, 2), Shape.Straight),
    5,
    60,
    (1, 1),
    (1, 1),
    Shape.Straight,
    0,
    "After dying, revive in the same position as a Terracotta Warrior."
  );

  public static readonly PieceDefinition TerracottaWarrior = new(
    PieceType.TerracottaWarrior,
    "TW",
    Pack.Dynasty,
    ((0, 0), Shape.None),
    0,
    60,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "This unit cannot be bought.",
    displayName: "Terracotta Warrior"
  );

  // ============================================================
  // Fantasy
  // ============================================================

  public static readonly PieceDefinition Dragon = new(
    PieceType.Dragon,
    "Dra",
    Pack.Fantasy,
    ((1, 5), Shape.Straight),
    20,
    60,
    (2, 3),
    (3, 3),
    Shape.ForwardLine,
    90,
    "Hits all units within attack range."
  );

  public static readonly PieceDefinition GoblinRoyalty = new(
    PieceType.GoblinRoyalty,
    "GK/GQ/GP/GP",
    Pack.Fantasy,
    ((1, 2), Shape.Any),
    5,
    20,
    (1, 1),
    (1, 1),
    Shape.Straight,
    0,
    "This royal is made up of 4 separate units. You lose if all of them die.",
    displayName: "Goblin Royalty"
  );

  public static readonly PieceDefinition Adventurer = new(
    PieceType.Adventurer,
    "Adv",
    Pack.Fantasy,
    ((1, 3), Shape.Straight),
    10,
    20,
    (1, 1),
    (1, 1),
    Shape.Any,
    25
  );

  public static readonly PieceDefinition Wizard = new(
    PieceType.Wizard,
    "Wiz",
    Pack.Fantasy,
    ((1, 1), Shape.Any),
    15,
    10,
    (1, 1),
    (2, 4),
    Shape.Any,
    35,
    "Shoots a fireball exploding in a 3x3 area dealing 15 damage to all enemies not in the middle of the explosion."
  );

  public static readonly PieceDefinition Dragonborn = new(
    PieceType.Dragonborn,
    "Dgb",
    Pack.Fantasy,
    ((1, 2), Shape.Any),
    20,
    20,
    (1, 1),
    (1, 1),
    Shape.Any,
    40,
    "After attacking, leave a burn effect that deals 5 damage to the attacked enemy at the start of your next turn."
  );

  // ============================================================
  // Undead
  // ============================================================

  public static readonly PieceDefinition Skeleton = new(
    PieceType.Skeleton,
    "Ske",
    Pack.Undead,
    ((1, 2), Shape.Straight),
    10,
    10,
    (1, 1),
    (1, 1),
    Shape.Straight,
    15
  );

  public static readonly PieceDefinition Zombie = new(
    PieceType.Zombie,
    "Zom",
    Pack.Undead,
    ((1, 1), Shape.Straight),
    10,
    5,
    (1, 1),
    (1, 1),
    Shape.Any,
    25,
    "After dying, spawn a Flesh in its position."
  );

  public static readonly PieceDefinition Flesh = new(
    PieceType.Flesh,
    "Fle",
    Pack.Undead,
    ((0, 0), Shape.None),
    0,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "This unit cannot be bought. After one turn, transform into Zombie."
  );

  /*
   * The PDF lists Ghoul's Health as "—", so there is no grounded numeric
   * value to put into PieceDefinition.Health yet.
   *
   * public static readonly PieceDefinition Ghoul = new(
   *   PieceType.Ghoul,
   *   "Gou",
   *   Pack.Undead,
   *   ((1, 2), Shape.Any),
   *   15,
   *   ???,
   *   (1, 1),
   *   (1, 1),
   *   Shape.Straight,
   *   35,
   *   "Dies after 4 turns."
   * );
   */

  public static readonly PieceDefinition Phantom = new(
    PieceType.Phantom,
    "PHA",
    Pack.Undead,
    ((1, 1), Shape.Straight),
    0,
    10,
    (1, 1),
    (1, 1),
    Shape.Straight,
    0,
    "Can 'Possess' any friendly unit, making it the royal. Can 'Unpossess' at any time."
  );

  // ============================================================
  // Greek
  // ============================================================

  public static readonly PieceDefinition Chariot = new(
    PieceType.Chariot,
    "Cha",
    Pack.Greek,
    ((1, 5), Shape.Line),
    15,
    25,
    (1, 1),
    (1, 3),
    Shape.Line,
    40
  );

  public static readonly PieceDefinition Ballista = new(
    PieceType.Ballista,
    "Bal",
    Pack.Greek,
    ((1, 1), Shape.Straight),
    20,
    20,
    (1, 2),
    (2, 5),
    Shape.Line,
    55,
    "Its attack pierces enemies in a straight line."
  );

  public static readonly PieceDefinition Zeus = new(
    PieceType.Zeus,
    "ZEU",
    Pack.Greek,
    ((1, 2), Shape.Any),
    15,
    75,
    (1, 1),
    (4, 4),
    Shape.Any,
    0,
    "Its attack can chain to enemies directly next to it and enemies next to that one etc. Deals 5 damage."
  );

  public static readonly PieceDefinition Heracles = new(
    PieceType.Heracles,
    "Hcl",
    Pack.Greek,
    ((1, 2), Shape.Straight),
    10,
    15,
    (1, 1),
    (1, 1),
    Shape.Straight,
    15
  );

  public static readonly PieceDefinition Hermes = new(
    PieceType.Hermes,
    "Hem",
    Pack.Greek,
    ((1, 4), Shape.Any),
    10,
    15,
    (1, 1),
    (1, 1),
    Shape.Straight,
    35
  );

  public static readonly PieceDefinition Ares = new(
    PieceType.Ares,
    "Are",
    Pack.Greek,
    ((1, 2), Shape.Straight),
    30,
    10,
    (1, 1),
    (1, 1),
    Shape.Straight,
    30
  );

  public static readonly PieceDefinition Chimera = new(
    PieceType.Chimera,
    "Chi",
    Pack.Greek,
    ((1, 3), Shape.Any),
    15,
    35,
    (1, 2),
    (1, 1),
    Shape.Any,
    55,
    "Attacks it makes behind it do 10 more damage."
  );

  public static readonly PieceDefinition Pegasus = new(
    PieceType.Pegasus,
    "",
    Pack.Greek,
    ((2, 4), Shape.Straight),
    10,
    25,
    (1, 1),
    (1, 1),
    Shape.Straight,
    30
  );

  // ============================================================
  // Modern
  // ============================================================

  public static readonly PieceDefinition Spy = new(
    PieceType.Spy,
    "Spy",
    Pack.Modern,
    ((1, 5), Shape.Any),
    0,
    15,
    (1, 1),
    (1, 3),
    Shape.Straight,
    35,
    "Marks an enemy; it takes double damage until attacked."
  );

  public static readonly PieceDefinition Engineer = new(
    PieceType.Engineer,
    "Eng",
    Pack.Modern,
    ((1, 3), Shape.Any),
    0,
    20,
    (1, 1),
    (1, 1),
    Shape.Any,
    25,
    "Builds up to two: roads, 20-health barricades, or mines each turn. It may also demolish Engineer structures within range. Doesn't trigger mines."
  );

  public static readonly PieceDefinition Mercenary = new(
    PieceType.Mercenary,
    "Mrc",
    Pack.Modern,
    ((1, 3), Shape.Any),
    15,
    20,
    (1, 1),
    (1, 2),
    Shape.Straight,
    25,
    "Place anywhere in No-Man's-Land. Costs 10 gold per owner turn; it is fired if you cannot pay. Fire it to leave it neutral for either player to hire or kill."
  );

  // President is not defined here because the PDF only supplies:
  // Move: 3
  // Move Pattern: Straight
  // The remaining columns are blank.

  // ============================================================
  // Chess
  // ============================================================

  public static readonly PieceDefinition Pawn = new(
    PieceType.Pawn,
    "Pwn",
    Pack.Chess,
    ((1, 2), Shape.Forward),
    60,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement."
  );

  public static readonly PieceDefinition ChessKnight = new(
    PieceType.ChessKnight,
    "KnC",
    Pack.Chess,
    ((1, 3), Shape.ChessKnight),
    60,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.",
    displayName: "Chess Knight"
  );

  public static readonly PieceDefinition Bishop = new(
    PieceType.Bishop,
    "Bsh",
    Pack.Chess,
    ((1, 8), Shape.Diagonal),
    60,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement."
  );

  public static readonly PieceDefinition Rook = new(
    PieceType.Rook,
    "Rok",
    Pack.Chess,
    ((1, 8), Shape.Line),
    60,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement."
  );

  public static readonly PieceDefinition Queen = new(
    PieceType.Queen,
    "Qun",
    Pack.Chess,
    ((1, 8), Shape.LineOrDiagonal),
    60,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement."
  );

  public static readonly PieceDefinition ChessKing = new(
    PieceType.ChessKing,
    "KIC",
    Pack.Chess,
    ((1, 1), Shape.Any),
    60,
    5,
    (1, 1),
    (0, 0),
    Shape.None,
    0,
    "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.",
    displayName: "Chess King"
  );

  // ============================================================
  // Collections
  // ============================================================

  public static readonly PieceDefinition[] All =
  [
    // Base
    Soldier,
    Defender,
    Archer,
    Peasant,
    Knight,
    Crossbowman,
    Cavalier,
    Cannon,
    Catapult,
    Bombard,
    Guard,
    Farm,
    King,
    Princess,
    Palace,
    Baron,
    Emissary,

    // Dynasty
    Elephant,
    Ox,
    Ninja,
    Samurai,
    Emperor,
    TerracottaWarrior,

    // Fantasy
    Dragon,
    GoblinRoyalty,
    Adventurer,
    Wizard,
    Dragonborn,

    // Undead
    Skeleton,
    Zombie,
    Flesh,
    Phantom,

    // Greek
    Chariot,
    Ballista,
    Zeus,
    Heracles,
    Hermes,
    Ares,
    Chimera,
    Pegasus,

    // Modern
    Spy,
    Engineer,
    Mercenary,

    // Chess
    Pawn,
    ChessKnight,
    Bishop,
    Rook,
    Queen,
    ChessKing
  ];

  public static readonly PieceDefinition[] Encyclopedia =
  [
    .. All
  ];

  public static readonly PieceDefinition[] Purchasable =
  [
    // Base
    Soldier,
    Defender,
    Archer,
    Peasant,
    Knight,
    Crossbowman,
    Cavalier,
    Cannon,
    Catapult,
    Bombard,
    Guard,
    Farm,

    // Dynasty
    Elephant,
    Ox,
    Ninja,
    Samurai,

    // Fantasy
    Dragon,
    Adventurer,
    Wizard,
    Dragonborn,

    // Undead
    Skeleton,
    Zombie,

    // Greek
    Chariot,
    Ballista,
    Heracles,
    Hermes,
    Ares,
    Chimera,
    Pegasus,

    // Modern
    Spy,
    Engineer,
    Mercenary,

    // Chess
    Pawn,
    ChessKnight,
    Bishop,
    Rook,
    Queen
  ];

  public static readonly PieceDefinition[] Royals =
  [
    // Base
    King,
    Princess,
    Palace,
    Baron,
    Emissary,

    // Dynasty
    Emperor,

    // Fantasy
    GoblinRoyalty,

    // Undead
    Phantom,

    // Greek
    Zeus,

    // Chess
    ChessKing
  ];
}