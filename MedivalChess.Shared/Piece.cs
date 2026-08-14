namespace MedivalChess.Shared;

/// <summary>An inclusive distance interval used by an attack.</summary>
public readonly record struct AttackRange
{
  public int Minimum { get; }
  public int Maximum { get; }

  public AttackRange(int minimum, int maximum)
  {
    if (minimum < 0) throw new ArgumentOutOfRangeException(nameof(minimum), "Attack range cannot be negative.");
    if (maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum), "Attack maximum must be at least its minimum.");
    Minimum = minimum;
    Maximum = maximum;
  }

  public static implicit operator AttackRange((int minimum, int maximum) range) => new(range.minimum, range.maximum);
}

public enum Shape
{
  Any,
  Straight,
  Circle,
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
  Soldier, Defender, Archer, Peasant, Knight, Crossbowman, Cavalier, Cannon, Catapult, Bombard, Guard, Farm,
  King, Princess, Palace, Baron, Emissary,

  // Dynasty
  Elephant, Ox, Ninja, Samurai, Emperor, TerracottaWarrior,

  // Fantasy
  Dragon, GoblinRoyalty, Adventurer, Wizard, Dragonborn, Commoner, Shieldbearer, Orc,

  // Undead
  Skeleton, Zombie, Flesh, Ghoul, Phantom, Vampire,

  // Greek
  Chariot, Ballista, Zeus, Heracles, Hermes, Ares, Chimera, Pegasus, Artemis,

  // Norse
  Viking, Sleipnir, Raider, Berserker,

  // Modern
  Spy, Engineer, Mercenary, President, Gunman, Sniper, Terrorist, Tank, Civilian,

  // Wild West
  Tumbleweed, Cowboy,

  // Chess -- intentionally unchanged by the balance workbook import
  Pawn, ChessKnight, Bishop, Rook, Queen, ChessKing
}

public enum PieceCategory
{
  Melee, Ranged, Intelligence,
  Mechanical, Structure, Transport,
  Royal
}

public enum Pack
{
  Base,
  Dynasty,
  Fantasy,
  Undead,
  Greek,
  Norse,
  Modern,
  WildWest,
  Chess
}

/// <summary>Movement distance and geometry. Normal movement has a minimum of one; units such as Pegasus can use a larger minimum.</summary>
public readonly record struct MovementDefinition
{
  public int Minimum { get; }
  public int Maximum { get; }
  public Shape Shape { get; }
  public int range => Maximum;
  public Shape shape => Shape;

  public MovementDefinition(int minimum, int maximum, Shape shape)
  {
    if (minimum < 0) throw new ArgumentOutOfRangeException(nameof(minimum));
    if (maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
    Minimum = minimum;
    Maximum = maximum;
    Shape = shape;
  }

  public static implicit operator MovementDefinition(((int minRange, int maxRange) range, Shape shape) value) =>
    new(value.range.minRange, value.range.maxRange, value.shape);
  public static implicit operator MovementDefinition((int range, Shape shape) value) =>
    new(value.range == 0 ? 0 : 1, value.range, value.shape);
  public static implicit operator (int range, Shape shape)(MovementDefinition value) => (value.Maximum, value.Shape);
}

public sealed class PieceDefinition
{
  public PieceType Type { get; }
  public string Identifier { get; }
  public string DisplayName { get; }
  public string? Abbreviation { get; }
  public PieceCategory Category { get; }
  public Pack Pack { get; }
  public MovementDefinition Movement { get; }
  public int Attack { get; }
  public int Health { get; }
  public (int x, int y) Size { get; }
  public AttackRange AttackRange { get; }
  public Shape AttackPattern { get; }
  public (int range, Shape shape) AttackShape => (AttackRange.Maximum, AttackPattern);
  public int MinimumAttackRange => AttackRange.Minimum;
  public int Cost { get; }
  public string AbilityDescription { get; }

  public PieceDefinition(
    PieceType type,
    string abbreviation,
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
    string? displayName = null
  )
  {
    Type = type;
    Identifier = string.IsNullOrWhiteSpace(identifier) ? type.ToString() : identifier;
    DisplayName = string.IsNullOrWhiteSpace(displayName) ? type.ToString() : displayName;
    Abbreviation = string.IsNullOrWhiteSpace(abbreviation) ? null : abbreviation;
    Category = GetDefaultCategory(type);
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
    PieceType.Archer or PieceType.Crossbowman or PieceType.Bombard or PieceType.Wizard or PieceType.Dragon or
    PieceType.Ninja or PieceType.Artemis or PieceType.Gunman or PieceType.Sniper or PieceType.Cowboy => PieceCategory.Ranged,
    PieceType.Cannon or PieceType.Catapult or PieceType.Ballista or PieceType.Tank => PieceCategory.Mechanical,
    PieceType.Spy or PieceType.Engineer => PieceCategory.Intelligence,
    PieceType.Farm => PieceCategory.Structure,
    PieceType.Ox => PieceCategory.Transport,
    PieceType.King or PieceType.Princess or PieceType.Palace or PieceType.Baron or PieceType.Emissary or
    PieceType.Emperor or PieceType.TerracottaWarrior or PieceType.GoblinRoyalty or PieceType.Phantom or
    PieceType.Zeus or PieceType.President or PieceType.ChessKing => PieceCategory.Royal,
    _ => PieceCategory.Melee
  };
}

/// <summary>Authoritative unit stats imported from MedievalChessUpdated.xlsx. Chess remains unchanged.</summary>
public static class PieceDefinitions
{
  public const int NeutralMercenaryHireCost = 50;

  // Base
  public static readonly PieceDefinition Soldier = new(PieceType.Soldier, "Sol", Pack.Base, (3, Shape.Straight), 20, 30, (1, 1), (1, 1), Shape.Straight, 40);
  public static readonly PieceDefinition Defender = new(PieceType.Defender, "Def", Pack.Base, (2, Shape.Any), 10, 50, (1, 1), (1, 1), Shape.Straight, 40);
  public static readonly PieceDefinition Archer = new(PieceType.Archer, "Arc", Pack.Base, (3, Shape.Circle), 20, 20, (1, 1), (2, 3), Shape.Circle, 50);
  public static readonly PieceDefinition Peasant = new(PieceType.Peasant, "Pes", Pack.Base, (1, Shape.Any), 10, 10, (1, 1), (1, 1), Shape.Straight, 20);
  public static readonly PieceDefinition Knight = new(PieceType.Knight, "Knt", Pack.Base, (3, Shape.Straight), 40, 60, (1, 1), (1, 1), Shape.Any, 110);
  public static readonly PieceDefinition Crossbowman = new(PieceType.Crossbowman, "Cbo", Pack.Base, (2, Shape.Any), 40, 30, (1, 1), (1, 3), Shape.Any, 100, displayName: "Crossbow");
  public static readonly PieceDefinition Cavalier = new(PieceType.Cavalier, "Cav", Pack.Base, (4, Shape.Any), 30, 40, (1, 1), (1, 1), Shape.Any, 110, "After attacking, may move up to 2 squares in a Diamond if it already moved this turn.");
  public static readonly PieceDefinition Cannon = new(PieceType.Cannon, "Cn", Pack.Base, (2, Shape.Straight), 60, 30, (1, 2), (2, 3), Shape.Straight, 100);
  public static readonly PieceDefinition Catapult = new(PieceType.Catapult, "Cat", Pack.Base, (1, Shape.Any), 40, 30, (1, 2), (4, 5), Shape.Any, 110, "Attacks over terrain and pieces.");
  public static readonly PieceDefinition Bombard = new(PieceType.Bombard, "Bom", Pack.Base, (2, Shape.Straight), 30, 30, (1, 1), (2, 3), Shape.Circle, 110, "After the normal hit, every unit in the 3x3 area centred on the target loses an additional 20 health.");
  public static readonly PieceDefinition Guard = new(PieceType.Guard, "Grd", Pack.Base, (3, Shape.Straight), 20, 50, (1, 1), (1, 1), Shape.Straight, 70, "May attach to one adjacent non-Royal ally and absorbs all damage for it until the Guard dies.");
  public static readonly PieceDefinition Farm = new(PieceType.Farm, "Frm", Pack.Base, (0, Shape.None), 0, 60, (3, 3), (0, 0), Shape.None, 80, "Earns 10 gold at the start of each owner's turn. Units may move and attack over it.");

  public static readonly PieceDefinition King = new(PieceType.King, "KIN", Pack.Base, (1, Shape.Any), 30, 190, (1, 1), (1, 1), Shape.Any, 0, "Adjacent friendly units take 10 less damage.");
  public static readonly PieceDefinition Princess = new(PieceType.Princess, "PRI", Pack.Base, (2, Shape.Straight), 20, 140, (1, 1), (1, 4), Shape.Any, 0, "May attack through units, but not through forests.");
  public static readonly PieceDefinition Palace = new(PieceType.Palace, "PAL", Pack.Base, (0, Shape.None), 0, 220, (3, 3), (0, 0), Shape.None, 0, "Friendly pieces moving toward the Palace gain +1 movement.");
  public static readonly PieceDefinition Baron = new(PieceType.Baron, "BAR", Pack.Base, (1, Shape.Any), 20, 160, (1, 1), (1, 1), Shape.Any, 0, "Adjacent allies deal 10 additional damage and take 10 less damage. Does not stack.");
  public static readonly PieceDefinition Emissary = new(PieceType.Emissary, "EMI", Pack.Base, (4, Shape.Circle), 10, 140, (1, 1), (1, 1), Shape.Any, 0, "When moving, bordering friendly units are moved by the same amount and direction.");

  // Dynasty
  public static readonly PieceDefinition Elephant = new(PieceType.Elephant, "Ele", Pack.Dynasty, (3, Shape.Straight), 20, 120, (3, 2), (0, 0), Shape.None, 100, "Movement ignores terrain and other units and damages every crossed unit.");
  public static readonly PieceDefinition Ox = new(PieceType.Ox, "Ox", Pack.Dynasty, (2, Shape.Any), 10, 50, (2, 1), (1, 1), Shape.Any, 70, "May carry one non-Royal 1x1 or Mechanical unit. Uses the cargo's movement +2; both may attack and incoming damage is shared.");
  public static readonly PieceDefinition Ninja = new(PieceType.Ninja, "Nin", Pack.Dynasty, (4, Shape.Circle), 10, 20, (1, 1), (2, 4), Shape.Circle, 80, "May attack up to three times each turn.");
  public static readonly PieceDefinition Samurai = new(PieceType.Samurai, "Sam", Pack.Dynasty, (3, Shape.Straight), 30, 30, (1, 1), (1, 1), Shape.Straight, 70, "Cannot be damaged by projectiles.");
  public static readonly PieceDefinition Emperor = new(PieceType.Emperor, "EMP", Pack.Dynasty, (1, Shape.Any), 10, 120, (1, 1), (1, 1), Shape.Any, 0, "The first time it dies, it is replaced in the same position by a Terracotta Warrior.");
  public static readonly PieceDefinition TerracottaWarrior = new(PieceType.TerracottaWarrior, "TW", Pack.Dynasty, (1, Shape.Any), 10, 120, (1, 1), (1, 1), Shape.Any, 0, "Cannot be purchased; created when the Emperor dies.", displayName: "Terracotta Warrior");

  // Fantasy
  public static readonly PieceDefinition Dragon = new(PieceType.Dragon, "Dra", Pack.Fantasy, (5, Shape.Straight), 40, 120, (2, 3), (3, 3), Shape.ForwardLine, 180, "An attack hits every unit in its attack line.");
  public static readonly PieceDefinition GoblinRoyalty = new(PieceType.GoblinRoyalty, "Gob", Pack.Fantasy, (2, Shape.Any), 10, 40, (1, 1), (1, 1), Shape.Any, 0, "Royal consists of four separate goblins; the team loses only after all four are dead.", displayName: "Goblin Royalty");
  public static readonly PieceDefinition Adventurer = new(PieceType.Adventurer, "Adv", Pack.Fantasy, (3, Shape.Circle), 20, 40, (1, 1), (1, 1), Shape.Any, 50);
  public static readonly PieceDefinition Wizard = new(PieceType.Wizard, "Wiz", Pack.Fantasy, (1, Shape.Any), 30, 20, (1, 1), (2, 4), Shape.Circle, 70, "Deals its damage to every unit in the 3x3 area centred on the targeted square.");
  public static readonly PieceDefinition Dragonborn = new(PieceType.Dragonborn, "Dgb", Pack.Fantasy, (2, Shape.Any), 40, 40, (1, 1), (1, 1), Shape.Any, 80, "After attacking, the same target loses 10 more health at the start of the next turn.");
  public static readonly PieceDefinition Commoner = new(PieceType.Commoner, "Com", Pack.Fantasy, (1, Shape.Any), 15, 10, (1, 1), (1, 1), Shape.Any, 25);
  public static readonly PieceDefinition Shieldbearer = new(PieceType.Shieldbearer, "Shb", Pack.Fantasy, (1, Shape.Any), 25, 35, (1, 1), (1, 1), Shape.Any, 65, "The first time it dies, revives with 20 health.");
  public static readonly PieceDefinition Orc = new(PieceType.Orc, "Orc", Pack.Fantasy, (2, Shape.Straight), 15, 35, (1, 1), (1, 1), Shape.Any, 0, "When attacking, attacks every unit in its attack range. Purchase cost is unspecified in the source workbook.");

  // Undead
  public static readonly PieceDefinition Skeleton = new(PieceType.Skeleton, "Ske", Pack.Undead, (2, Shape.Straight), 20, 20, (1, 1), (1, 1), Shape.Any, 30);
  public static readonly PieceDefinition Zombie = new(PieceType.Zombie, "Zom", Pack.Undead, (1, Shape.Straight), 20, 10, (1, 1), (1, 1), Shape.Any, 50, "After one owner turn, transforms into Flesh.");
  public static readonly PieceDefinition Flesh = new(PieceType.Flesh, "Fle", Pack.Undead, (0, Shape.None), 0, 10, (1, 1), (0, 0), Shape.None, 0, "Cannot be purchased. After one owner turn, transforms into Zombie.");
  public static readonly PieceDefinition Ghoul = new(PieceType.Ghoul, "Ghl", Pack.Undead, (2, Shape.Any), 30, 9999, (1, 1), (1, 1), Shape.Any, 70, "Dies after four owner turns.");
  public static readonly PieceDefinition Phantom = new(PieceType.Phantom, "PHA", Pack.Undead, (1, Shape.Straight), 0, 20, (1, 1), (1, 1), Shape.Straight, 0, "May possess an enemy in range: that unit switches team and becomes the Royal; the Phantom is sacrificed.");
  public static readonly PieceDefinition Vampire = new(PieceType.Vampire, "Vam", Pack.Undead, (3, Shape.Circle), 30, 40, (1, 1), (1, 1), Shape.Straight, 80, "Heals 20 health after attacking, up to its maximum health.");

  // Greek
  public static readonly PieceDefinition Chariot = new(PieceType.Chariot, "Cha", Pack.Greek, (5, Shape.Line), 30, 50, (1, 1), (1, 3), Shape.Line, 80);
  public static readonly PieceDefinition Ballista = new(PieceType.Ballista, "Bal", Pack.Greek, (1, Shape.Straight), 40, 40, (1, 1), (2, 5), Shape.Line, 110, "Its attack pierces every unit on its line but is stopped by forest.");
  public static readonly PieceDefinition Zeus = new(PieceType.Zeus, "ZEU", Pack.Greek, (2, Shape.Any), 30, 150, (1, 1), (4, 4), Shape.Circle, 0, "After the first full-damage target, lightning chains through connected units within one square for 10 damage each.");
  public static readonly PieceDefinition Heracles = new(PieceType.Heracles, "Hrc", Pack.Greek, (2, Shape.Straight), 20, 30, (1, 1), (1, 1), Shape.Straight, 30);
  public static readonly PieceDefinition Hermes = new(PieceType.Hermes, "Her", Pack.Greek, (4, Shape.Any), 20, 30, (1, 1), (1, 1), Shape.Straight, 70);
  public static readonly PieceDefinition Ares = new(PieceType.Ares, "Are", Pack.Greek, (2, Shape.Straight), 60, 20, (1, 1), (1, 1), Shape.Straight, 60);
  public static readonly PieceDefinition Chimera = new(PieceType.Chimera, "Chi", Pack.Greek, (3, Shape.Any), 30, 70, (1, 1), (1, 1), Shape.Any, 0, "Attacks made behind it deal 20 additional damage.");
  public static readonly PieceDefinition Pegasus = new(PieceType.Pegasus, "Peg", Pack.Greek, ((4, 6), Shape.Circle), 20, 50, (1, 1), (1, 1), Shape.Straight, 60);
  public static readonly PieceDefinition Artemis = new(PieceType.Artemis, "Art", Pack.Greek, (2, Shape.Any), 20, 30, (1, 1), (2, 3), Shape.Any, 60, "Attacks through forests and deals 10 additional damage to targets in forest.");

  // Norse
  public static readonly PieceDefinition Viking = new(PieceType.Viking, "Vik", Pack.Norse, (4, Shape.Straight), 20, 20, (1, 1), (1, 1), Shape.Any, 50);
  public static readonly PieceDefinition Sleipnir = new(PieceType.Sleipnir, "Sle", Pack.Norse, ((4, 6), Shape.Circle), 40, 60, (1, 2), (2, 2), Shape.Straight, 90, "Ignores terrain and other units while moving; roads still apply their movement benefit.");
  public static readonly PieceDefinition Raider = new(PieceType.Raider, "Rai", Pack.Norse, (2, Shape.Circle), 40, 35, (1, 1), (1, 1), Shape.Straight, 85, "May move two additional squares when moving forward.");
  public static readonly PieceDefinition Berserker = new(PieceType.Berserker, "Ber", Pack.Norse, (4, Shape.Any), 20, 40, (1, 1), (1, 1), Shape.Straight, 100, "At 20 health or less, its attack becomes 40.");

  // Modern
  public static readonly PieceDefinition Spy = new(PieceType.Spy, "Spy", Pack.Modern, (5, Shape.Any), 0, 30, (1, 1), (1, 3), Shape.Straight, 60, "Marks an enemy. The next attack against that target deals double damage, then removes the mark.");
  public static readonly PieceDefinition Engineer = new(PieceType.Engineer, "Eng", Pack.Modern, (3, Shape.Any), 0, 40, (1, 1), (1, 1), Shape.Any, 50, "Builds up to two roads, 40-health barriers, or mines each turn; may demolish Engineer structures in range and does not trigger mines.");
  public static readonly PieceDefinition Mercenary = new(PieceType.Mercenary, "Mrc", Pack.Modern, (3, Shape.Any), 30, 40, (1, 1), (1, 2), Shape.Straight, 50, "Costs 20 gold at the start of each owner's turn; if unpaid, transfers to an opponent.");
  public static readonly PieceDefinition President = new(PieceType.President, "PRE", Pack.Modern, (3, Shape.Circle), 40, 220, (1, 1), (1, 2), Shape.Straight, 0, "The team must pay 10 gold at the start of every turn or loses.");
  public static readonly PieceDefinition Gunman = new(PieceType.Gunman, "Gun", Pack.Modern, (3, Shape.Straight), 30, 30, (1, 1), (2, 4), Shape.Any, 60);
  public static readonly PieceDefinition Sniper = new(PieceType.Sniper, "Snp", Pack.Modern, (0, Shape.None), 50, 30, (1, 1), (8, 8), Shape.Circle, 100);
  public static readonly PieceDefinition Terrorist = new(PieceType.Terrorist, "Ter", Pack.Modern, (4, Shape.Straight), 90, 10, (1, 1), (1, 1), Shape.Any, 70, "Dies immediately after attacking. Whenever it dies, every other unit in its attack range takes its full attack damage.");
  public static readonly PieceDefinition Tank = new(PieceType.Tank, "Tnk", Pack.Modern, (2, Shape.Straight), 60, 100, (2, 2), (2, 4), Shape.Line, 140, "Faces one direction. If an attack target is not in front, the Tank turns toward it and the attack action ends without firing.");
  public static readonly PieceDefinition Civilian = new(PieceType.Civilian, "Civ", Pack.Modern, (1, Shape.Any), 10, 20, (1, 1), (1, 1), Shape.Straight, 30);

  // Wild West
  public static readonly PieceDefinition Tumbleweed = new(PieceType.Tumbleweed, "Tum", Pack.WildWest, (3, Shape.Straight), 5, 5, (1, 1), (1, 1), Shape.Straight, 10, "Dies after three rounds if it has not already been destroyed.", displayName: "Tumble Weed");
  public static readonly PieceDefinition Cowboy = new(PieceType.Cowboy, "Cow", Pack.WildWest, (4, Shape.Circle), 25, 25, (1, 1), (3, 3), Shape.Straight, 60);

  // Chess -- kept exactly as it was
  public static readonly PieceDefinition Pawn = new(PieceType.Pawn, "Pwn", Pack.Chess, (2, Shape.Forward), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.");
  public static readonly PieceDefinition ChessKnight = new(PieceType.ChessKnight, "KnC", Pack.Chess, (3, Shape.ChessKnight), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.", displayName: "Chess Knight");
  public static readonly PieceDefinition Bishop = new(PieceType.Bishop, "Bsh", Pack.Chess, (8, Shape.Diagonal), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.");
  public static readonly PieceDefinition Rook = new(PieceType.Rook, "Rok", Pack.Chess, (8, Shape.Line), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.");
  public static readonly PieceDefinition Queen = new(PieceType.Queen, "Qun", Pack.Chess, (8, Shape.LineOrDiagonal), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.");
  public static readonly PieceDefinition ChessKing = new(PieceType.ChessKing, "KIC", Pack.Chess, (1, Shape.Any), 60, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.", displayName: "Chess King");

  public static readonly PieceDefinition[] All =
  [
    Soldier, Defender, Archer, Peasant, Knight, Crossbowman, Cavalier, Cannon, Catapult, Bombard, Guard, Farm,
    King, Princess, Palace, Baron, Emissary,
    Elephant, Ox, Ninja, Samurai, Emperor, TerracottaWarrior,
    Dragon, GoblinRoyalty, Adventurer, Wizard, Dragonborn, Commoner, Shieldbearer, Orc,
    Skeleton, Zombie, Flesh, Ghoul, Phantom, Vampire,
    Chariot, Ballista, Zeus, Heracles, Hermes, Ares, Chimera, Pegasus, Artemis,
    Viking, Sleipnir, Raider, Berserker,
    Spy, Engineer, Mercenary, President, Gunman, Sniper, Terrorist, Tank, Civilian,
    Tumbleweed, Cowboy,
    Pawn, ChessKnight, Bishop, Rook, Queen, ChessKing
  ];

  public static readonly PieceDefinition[] Encyclopedia = [.. All];

  // Orc is deliberately excluded until the blank purchase cost in the workbook is filled in.
  public static readonly PieceDefinition[] Purchasable =
  [
    Soldier, Defender, Archer, Peasant, Knight, Crossbowman, Cavalier, Cannon, Catapult, Bombard, Guard, Farm,
    Elephant, Ox, Ninja, Samurai,
    Dragon, Adventurer, Wizard, Dragonborn, Commoner, Shieldbearer,
    Skeleton, Zombie, Ghoul, Vampire,
    Chariot, Ballista, Heracles, Hermes, Ares, Chimera, Pegasus, Artemis,
    Viking, Sleipnir, Raider, Berserker,
    Spy, Engineer, Mercenary, Gunman, Sniper, Terrorist, Tank, Civilian,
    Tumbleweed, Cowboy,
    Pawn, ChessKnight, Bishop, Rook, Queen
  ];

  public static readonly PieceDefinition[] Royals =
  [
    King, Princess, Palace, Baron, Emissary,
    Emperor, TerracottaWarrior,
    GoblinRoyalty,
    Phantom,
    Zeus,
    President,
    ChessKing
  ];
}
