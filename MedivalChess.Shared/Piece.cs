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
    string? displayName = null)
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
    PieceType.Archer or PieceType.Crossbowman or PieceType.Bombard or PieceType.Dragon or PieceType.Ninja or
    PieceType.Wizard or PieceType.Artemis or PieceType.Gunman or PieceType.Sniper or PieceType.Cowboy => PieceCategory.Ranged,
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

public static class PieceDefinitions
{
  public const int NeutralMercenaryHireCost = 50;

  // Base
  public static readonly PieceDefinition Soldier = new(PieceType.Soldier, "Sol", Pack.Base, (3, Shape.Straight), 20, 30, (1, 1), (1, 1), Shape.Straight, 40);
  public static readonly PieceDefinition Defender = new(PieceType.Defender, "Def", Pack.Base, (2, Shape.Any), 10, 50, (1, 1), (1, 1), Shape.Straight, 40);
  public static readonly PieceDefinition Archer = new(PieceType.Archer, "Arc", Pack.Base, (3, Shape.Circle), 20, 20, (1, 1), (2, 3), Shape.Circle, 50);
  public static readonly PieceDefinition Peasant = new(PieceType.Peasant, "Pes", Pack.Base, (1, Shape.Any), 10, 10, (1, 1), (1, 1), Shape.Straight, 20);
  public static readonly PieceDefinition Knight = new(PieceType.Knight, "Knt", Pack.Base, (3, Shape.Straight), 40, 60, (1, 1), (1, 1), Shape.Any, 110);
  public static readonly PieceDefinition Crossbowman = new(PieceType.Crossbowman, "Cbo", Pack.Base, (2, Shape.Any), 40, 30, (1, 1), (1, 3), Shape.Any, 100);
  public static readonly PieceDefinition Cavalier = new(PieceType.Cavalier, "Cav", Pack.Base, (4, Shape.Any), 30, 40, (1, 1), (1, 1), Shape.Any, 110, "If movement is already used, after attacking allow a 2-Straight movement.");
  public static readonly PieceDefinition Cannon = new(PieceType.Cannon, "Cn", Pack.Base, (2, Shape.Straight), 60, 30, (1, 2), (2, 3), Shape.Line, 100);
  public static readonly PieceDefinition Catapult = new(PieceType.Catapult, "Cat", Pack.Base, (1, Shape.Any), 40, 30, (1, 2), (4, 5), Shape.Any, 110, "Attacks over terrain and pieces.");
  public static readonly PieceDefinition Bombard = new(PieceType.Bombard, "Bom", Pack.Base, (2, Shape.Straight), 30, 30, (1, 1), (2, 3), Shape.Circle, 110, "Every adjacent and diagonally adjacent unit to the target takes 20 damage, including friendly units.");
  public static readonly PieceDefinition Guard = new(PieceType.Guard, "Grd", Pack.Base, (3, Shape.Straight), 20, 50, (1, 1), (1, 1), Shape.Straight, 70, "Attaches to a friendly, non-royal unit and takes damage for it.");
  public static readonly PieceDefinition Farm = new(PieceType.Farm, "Frm", Pack.Base, (0, Shape.None), 0, 60, (3, 3), (0, 0), Shape.None, 80, "Earns 10 gold at the start of each owner turn. Units may move and attack over it.");

  public static readonly PieceDefinition King = new(PieceType.King, "KIN", Pack.Base, (1, Shape.Any), 30, 190, (1, 1), (1, 1), Shape.Any, 0);
  public static readonly PieceDefinition Princess = new(PieceType.Princess, "PRI", Pack.Base, (2, Shape.Straight), 20, 140, (1, 1), (1, 4), Shape.Any, 0, "May attack over units and terrain and barricades.");
  public static readonly PieceDefinition Palace = new(PieceType.Palace, "PAL", Pack.Base, (0, Shape.None), 0, 220, (3, 2), (0, 0), Shape.None, 0, "Friendly pieces moving in the direction of the Palace gain +1 movement and ignore terrain.");
  public static readonly PieceDefinition Baron = new(PieceType.Baron, "BAR", Pack.Base, (3, Shape.Straight), 20, 160, (1, 1), (1, 1), Shape.Any, 0, "Adjacent allies deal 10 additional damage and take 10 less damage.");
  public static readonly PieceDefinition Emissary = new(PieceType.Emissary, "EMI", Pack.Base, (4, Shape.Circle), 10, 140, (1, 1), (1, 1), Shape.Any, 0, "Moves (diagonally) adjacent friendly 1x1 pieces with it.");

  // Dynasty
  public static readonly PieceDefinition Elephant = new(PieceType.Elephant, "Ele", Pack.Dynasty, (3, Shape.Straight), 20, 120, (2, 2), (0, 0), Shape.None, 100, "May move through enemies, damaging each crossed unit. Ignores terrain.");
  public static readonly PieceDefinition Ox = new(PieceType.Ox, "Ox", Pack.Dynasty, (4, Shape.Any), 10, 50, (1, 1), (1, 1), Shape.Straight, 70, "Attaches to a 1x1 friendly unit and increases that unit's Movement by 2. While attached, if attacked, both units take damage.");
  public static readonly PieceDefinition Ninja = new(PieceType.Ninja, "Nj", Pack.Dynasty, (4, Shape.Circle), 10, 20, (1, 1), (2, 4), Shape.Circle, 80, "May attack up to three times per turn.");
  public static readonly PieceDefinition Samurai = new(PieceType.Samurai, "Sam", Pack.Dynasty, (3, Shape.Straight), 30, 30, (1, 1), (1, 1), Shape.Any, 70, "Cannot be damaged by projectiles.");
  public static readonly PieceDefinition Emperor = new(PieceType.Emperor, "EP", Pack.Dynasty, (2, Shape.Straight), 10, 120, (1, 1), (1, 1), Shape.Straight, 0, "After dying, revive in the same position as a Terracotta Warrior.");
  public static readonly PieceDefinition TerracottaWarrior = new(PieceType.TerracottaWarrior, "TW", Pack.Dynasty, (0, Shape.None), 0, 120, (1, 1), (0, 0), Shape.None, 0, "This unit cannot be bought.", displayName: "Terracotta Warrior");

  // Fantasy
  public static readonly PieceDefinition Dragon = new(PieceType.Dragon, "Dra", Pack.Fantasy, (5, Shape.Straight), 40, 120, (2, 3), (3, 3), Shape.ForwardLine, 180, "Hits all units within attack range.");
  public static readonly PieceDefinition GoblinRoyalty = new(PieceType.GoblinRoyalty, "GK", Pack.Fantasy, (2, Shape.Any), 10, 40, (1, 1), (1, 1), Shape.Straight, 0, "This royal is made up of four separate units. You lose if all of them die.", displayName: "Goblin Royalty");
  public static readonly PieceDefinition Adventurer = new(PieceType.Adventurer, "Adv", Pack.Fantasy, (3, Shape.Circle), 20, 40, (1, 1), (1, 1), Shape.Any, 50);
  public static readonly PieceDefinition Wizard = new(PieceType.Wizard, "Wiz", Pack.Fantasy, (1, Shape.Any), 30, 20, (1, 1), (2, 4), Shape.Circle, 70, "Deals damage in a 3x3 area centred on the unit it attacks.");
  public static readonly PieceDefinition Dragonborn = new(PieceType.Dragonborn, "Dgb", Pack.Fantasy, (2, Shape.Any), 40, 40, (1, 1), (1, 1), Shape.Any, 80, "After attacking, leaves a burn effect that deals 10 damage to the enemy attacked at the start of your next turn.");
  public static readonly PieceDefinition Commoner = new(PieceType.Commoner, "Com", Pack.Fantasy, (1, Shape.Any), 15, 10, (1, 1), (1, 1), Shape.Any, 25);
  public static readonly PieceDefinition Shieldbearer = new(PieceType.Shieldbearer, "Shd", Pack.Fantasy, (1, Shape.Any), 25, 35, (1, 1), (1, 1), Shape.Any, 65, "When dropping to 0 health, does not die and comes back on 20 health.");
  public static readonly PieceDefinition Orc = new(PieceType.Orc, "Orc", Pack.Fantasy, (2, Shape.Straight), 15, 35, (1, 1), (1, 1), Shape.Any, 105, "When attacking, attacks all units in range.");

  // Undead
  public static readonly PieceDefinition Skeleton = new(PieceType.Skeleton, "Ske", Pack.Undead, (2, Shape.Straight), 20, 20, (1, 1), (1, 1), Shape.Straight, 30);
  public static readonly PieceDefinition Zombie = new(PieceType.Zombie, "Zom", Pack.Undead, (1, Shape.Straight), 20, 10, (1, 1), (1, 1), Shape.Any, 50, "After dying, spawns a Flesh in its position.");
  public static readonly PieceDefinition Flesh = new(PieceType.Flesh, "Fle", Pack.Undead, (0, Shape.None), 0, 10, (1, 1), (0, 0), Shape.None, 0, "This unit cannot be bought. After one turn, transforms into Zombie.");
  public static readonly PieceDefinition Ghoul = new(PieceType.Ghoul, "Gou", Pack.Undead, (2, Shape.Any), 30, int.MaxValue, (1, 1), (1, 1), Shape.Straight, 70, "Has no Health value in the workbook and dies after 4 turns.");
  public static readonly PieceDefinition Phantom = new(PieceType.Phantom, "PHA", Pack.Undead, (1, Shape.Straight), 0, 20, (1, 1), (1, 1), Shape.Straight, 0, "Can possess any friendly unit, making it the Royal, and can unpossess at any time.");
  public static readonly PieceDefinition Vampire = new(PieceType.Vampire, "Vmp", Pack.Undead, (3, Shape.Circle), 30, 40, (1, 1), (1, 1), Shape.Straight, 80, "Heals by 20 after attacking.");

  // Greek
  public static readonly PieceDefinition Chariot = new(PieceType.Chariot, "Cha", Pack.Greek, (5, Shape.Line), 30, 50, (1, 1), (1, 3), Shape.Line, 80);
  public static readonly PieceDefinition Ballista = new(PieceType.Ballista, "Bal", Pack.Greek, (1, Shape.Straight), 40, 40, (1, 1), (2, 5), Shape.Line, 110, "Its attack pierces enemies in a straight line.");
  public static readonly PieceDefinition Zeus = new(PieceType.Zeus, "ZEU", Pack.Greek, (2, Shape.Any), 30, 150, (1, 1), (4, 4), Shape.Circle, 0, "Its attack can chain to enemies directly next to the target, then enemies next to those, dealing 10 damage.");
  public static readonly PieceDefinition Heracles = new(PieceType.Heracles, "Hcl", Pack.Greek, (2, Shape.Straight), 20, 30, (1, 1), (1, 1), Shape.Straight, 30);
  public static readonly PieceDefinition Hermes = new(PieceType.Hermes, "Hem", Pack.Greek, (4, Shape.Any), 20, 30, (1, 1), (1, 1), Shape.Straight, 70);
  public static readonly PieceDefinition Ares = new(PieceType.Ares, "Are", Pack.Greek, (2, Shape.Straight), 60, 20, (1, 1), (1, 1), Shape.Straight, 60);
  public static readonly PieceDefinition Chimera = new(PieceType.Chimera, "Chi", Pack.Greek, (3, Shape.Any), 30, 70, (1, 1), (1, 1), Shape.Any, 110, "Attacking behind it deals +20 damage.");
  public static readonly PieceDefinition Pegasus = new(PieceType.Pegasus, "Peg", Pack.Greek, ((2, 4), Shape.Circle), 20, 50, (1, 1), (1, 1), Shape.Straight, 60);
  public static readonly PieceDefinition Artemis = new(PieceType.Artemis, "Art", Pack.Greek, (2, Shape.Any), 20, 30, (1, 1), (2, 3), Shape.Any, 60, "Can attack through forests. Deals +10 damage to enemies in forests.");

  // Norse
  public static readonly PieceDefinition Viking = new(PieceType.Viking, "Vik", Pack.Norse, (4, Shape.Straight), 20, 20, (1, 1), (1, 1), Shape.Any, 50);
  public static readonly PieceDefinition Sleipnir = new(PieceType.Sleipnir, "Slp", Pack.Norse, ((4, 6), Shape.Circle), 40, 60, (1, 2), (2, 2), Shape.Straight, 90, "Movement ignores all terrain apart from roads and ignores other units.");
  public static readonly PieceDefinition Raider = new(PieceType.Raider, "Rai", Pack.Norse, (2, Shape.Circle), 40, 35, (1, 1), (1, 1), Shape.Straight, 85, "Moves 2 faster forward.");
  public static readonly PieceDefinition Berserker = new(PieceType.Berserker, "Bsk", Pack.Norse, (4, Shape.Any), 20, 40, (1, 1), (1, 1), Shape.Straight, 100, "Deals 40 damage when at or under 20 health.");

  // Modern
  public static readonly PieceDefinition Spy = new(PieceType.Spy, "Spy", Pack.Modern, (5, Shape.Any), 0, 30, (1, 1), (1, 3), Shape.Straight, 60, "Marks an enemy; it takes double damage until attacked.");
  public static readonly PieceDefinition Engineer = new(PieceType.Engineer, "Eng", Pack.Modern, (3, Shape.Any), 0, 40, (1, 1), (1, 1), Shape.Any, 50, "Builds up to two roads, 40-health barricades, or mines each turn. It may also demolish Engineer structures within range. Doesn't trigger mines.");
  public static readonly PieceDefinition Mercenary = new(PieceType.Mercenary, "Mrc", Pack.Modern, (3, Shape.Any), 30, 40, (1, 1), (1, 2), Shape.Straight, 50, "Place anywhere in No-Man's-Land. Costs 20 gold per owner turn; it is fired if you cannot pay. Fire it to leave it neutral for either player to hire or kill.");
  public static readonly PieceDefinition President = new(PieceType.President, "PRE", Pack.Modern, (3, Shape.Circle), 40, 220, (1, 1), (1, 2), Shape.Straight, 0, "Costs 10 gold per turn to maintain. Lose if you cannot afford it.");
  public static readonly PieceDefinition Gunman = new(PieceType.Gunman, "Gun", Pack.Modern, (3, Shape.Straight), 30, 30, (1, 1), (2, 4), Shape.Any, 60);
  public static readonly PieceDefinition Sniper = new(PieceType.Sniper, "Sni", Pack.Modern, (0, Shape.None), 50, 30, (1, 1), (8, 8), Shape.Circle, 100);
  public static readonly PieceDefinition Terrorist = new(PieceType.Terrorist, "Ter", Pack.Modern, (4, Shape.Straight), 90, 10, (1, 1), (1, 1), Shape.Any, 70, "Hits all units in range when killed. If it attacks, it dies.");
  public static readonly PieceDefinition Tank = new(PieceType.Tank, "Tnk", Pack.Modern, (2, Shape.Straight), 60, 100, (2, 2), (2, 4), Shape.Line, 140, "May only attack in the direction it is facing. Attempting another direction turns it to face that direction instead.");
  public static readonly PieceDefinition Civilian = new(PieceType.Civilian, "Civ", Pack.Modern, (1, Shape.Any), 10, 20, (1, 1), (1, 1), Shape.Straight, 30);

  // Wild West
  public static readonly PieceDefinition Tumbleweed = new(PieceType.Tumbleweed, "Tmb", Pack.WildWest, (3, Shape.Straight), 5, 5, (1, 1), (1, 1), Shape.Straight, 10, "Dies after 3 rounds or when reduced to 0 health.", displayName: "Tumble weed");
  public static readonly PieceDefinition Cowboy = new(PieceType.Cowboy, "Cow", Pack.WildWest, (4, Shape.Circle), 25, 25, (1, 1), (3, 3), Shape.Straight, 60);

  // Chess -- unchanged
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

  public static readonly PieceDefinition[] Purchasable =
  [
    Soldier, Defender, Archer, Peasant, Knight, Crossbowman, Cavalier, Cannon, Catapult, Bombard, Guard, Farm,
    Elephant, Ox, Ninja, Samurai,
    Dragon, Adventurer, Wizard, Dragonborn, Commoner, Shieldbearer, Orc,
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
