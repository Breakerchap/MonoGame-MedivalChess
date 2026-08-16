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
  Forward,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  ForwardDiagonal,
  ForwardLine,
  PierceStraight,
  ChessKnight,
  MoveOnEnemy,
  None
}

public enum PieceType
{
  Peasant, Swordsman, Defender, Archer, Crossbowman, Knight, Cavalier, Bombard, Cannon, Catapult, Guard, Mason, Farm,
  King, Baron,
  Ashigaru, Samurai, Ninja, Sumo, Elephant, Ox, Carpenter, Emperor, TerracottaWarrior, Palace,
  Commoner, Adventurer, Elf, Orc, Mimic, Wizard, Witch, Druid, Dragon, Sorceress, GoblinRoyalty, Giant,
  Cyclops,
  Skeleton, Banshee, Reaper, Zombie, Flesh, Abomination, Ghoul, Vampire, Lich, Phylactery, Phantom,
  Heracles, Ares, Pegasus, Spartan, Hermes, Artemis, Chariot, Ballista, Chimera, Zeus, Daedalus, Atlas, Chronos,
  Viking, Hunter, Sleipnir, Raider, Beserker, Valkyrie, Runesmith, Jarl,
  Civilian, Officer, Gunman, Sniper, Terrorist, Spy, Tank, Engineer, Mercenary, President,
  Brawler, Cowboy, Demolitionist, Pickpocket, Stagecoach, Sherrif,
  Fiend, Cherub, Fallen, Gatekeeper, Archangel, Archdemon, Succubus, Herald,
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
  Medival,
  Dynasty,
  Fantasy,
  Undead,
  Greek,
  Norse,
  Modern,
  WildWest,
  AngelsDemons,
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
    PieceType.Wizard or PieceType.Witch or PieceType.Artemis or PieceType.Gunman or PieceType.Sniper or
    PieceType.Cowboy or PieceType.Hunter or PieceType.Elf or PieceType.Banshee or PieceType.Lich or
    PieceType.Fallen or PieceType.Cherub or PieceType.Archdemon => PieceCategory.Ranged,
    PieceType.Cannon or PieceType.Catapult or PieceType.Ballista or PieceType.Tank => PieceCategory.Mechanical,
    PieceType.Spy or PieceType.Engineer or PieceType.Carpenter or PieceType.Druid or PieceType.Mason or
    PieceType.Gatekeeper or PieceType.Daedalus or PieceType.Runesmith or PieceType.Demolitionist or PieceType.Pickpocket => PieceCategory.Intelligence,
    PieceType.Farm => PieceCategory.Structure,
    PieceType.Ox or PieceType.Stagecoach => PieceCategory.Transport,
    PieceType.King or PieceType.Baron or PieceType.Sorceress or PieceType.Palace or PieceType.Emperor or
    PieceType.TerracottaWarrior or PieceType.GoblinRoyalty or PieceType.Atlas or PieceType.Chronos or
    PieceType.President or PieceType.Jarl or PieceType.Phylactery or PieceType.Phantom or PieceType.Sherrif or
    PieceType.Herald or PieceType.ChessKing => PieceCategory.Royal,
    _ => PieceCategory.Melee
  };
}

public static class PieceDefinitions
{
  public const int NeutralMercenaryHireCost = 50;

  // Medival
  public static readonly PieceDefinition Peasant = new(PieceType.Peasant, "Pes", Pack.Medival, (1, Shape.Any), 5, 5, (1, 1), (1, 1), Shape.Straight, 10);
  public static readonly PieceDefinition Swordsman = new(PieceType.Swordsman, "Swo", Pack.Medival, (3, Shape.Straight), 20, 30, (1, 1), (1, 1), Shape.Straight, 35);
  public static readonly PieceDefinition Defender = new(PieceType.Defender, "Def", Pack.Medival, (2, Shape.Any), 10, 50, (1, 1), (1, 1), Shape.Straight, 35);
  public static readonly PieceDefinition Archer = new(PieceType.Archer, "Arc", Pack.Medival, (3, Shape.Circle), 20, 20, (1, 1), (2, 3), Shape.Circle, 45);
  public static readonly PieceDefinition Crossbowman = new(PieceType.Crossbowman, "Cbo", Pack.Medival, (3, Shape.Straight), 35, 30, (1, 1), (1, 3), Shape.Any, 100);
  public static readonly PieceDefinition Knight = new(PieceType.Knight, "Knt", Pack.Medival, (3, Shape.Straight), 45, 60, (1, 1), (1, 1), Shape.Any, 110);
  public static readonly PieceDefinition Cavalier = new(PieceType.Cavalier, "Cav", Pack.Medival, (4, Shape.Any), 30, 40, (1, 1), (1, 1), Shape.Any, 110, "If movement is already used, after attacking allow a 2-Straight movement.");
  public static readonly PieceDefinition Bombard = new(PieceType.Bombard, "Bom", Pack.Medival, (2, Shape.Straight), 25, 30, (1, 1), (2, 3), Shape.Circle, 90, "Every adjacent unit to the target take 25 damage, including friendly units.");
  public static readonly PieceDefinition Cannon = new(PieceType.Cannon, "Can", Pack.Medival, (2, Shape.Straight), 60, 30, (1, 2), (2, 3), Shape.Line, 100);
  public static readonly PieceDefinition Catapult = new(PieceType.Catapult, "Cat", Pack.Medival, (1, Shape.Any), 40, 20, (1, 2), (4, 5), Shape.Circle, 105, "Attacks over terrain and pieces.");
  public static readonly PieceDefinition Guard = new(PieceType.Guard, "Grd", Pack.Medival, (3, Shape.Straight), 10, 50, (1, 1), (1, 1), Shape.Straight, 70, "Attaches to a friendly, non-royal unit and takes damage for it.");
  public static readonly PieceDefinition Mason = new(PieceType.Mason, "Mas", Pack.Medival, (3, Shape.Circle), 0, 40, (1, 1), (1, 1), Shape.Any, 45, "As an attack, may build stone walls or gatehouses. May demolish any in-range structure for free.");
  public static readonly PieceDefinition Farm = new(PieceType.Farm, "Frm", Pack.Medival, (0, Shape.None), 0, 60, (3, 3), (0, 0), Shape.None, 80, "Earns 10 gold at the start of each owner turn. Units may move and attack over it.");
  public static readonly PieceDefinition King = new(PieceType.King, "KIN", Pack.Medival, (2, Shape.Straight), 30, 190, (1, 1), (1, 1), Shape.Any, 0);
  public static readonly PieceDefinition Baron = new(PieceType.Baron, "BAR", Pack.Medival, (3, Shape.Straight), 20, 160, (1, 1), (1, 1), Shape.Any, 0, "Adjacent allies deal 10 additional damage and take 10 less damage.");

  // Dynasty
  public static readonly PieceDefinition Ashigaru = new(PieceType.Ashigaru, "Asi", Pack.Dynasty, (4, Shape.Any), 15, 25, (1, 1), (1, 1), Shape.Straight, 35);
  public static readonly PieceDefinition Samurai = new(PieceType.Samurai, "Sam", Pack.Dynasty, (3, Shape.Straight), 25, 30, (1, 1), (1, 1), Shape.Any, 75, "Takes 15 less damage from units more than 1 Square away. To a minimum of 0 damage.");
  public static readonly PieceDefinition Ninja = new(PieceType.Ninja, "Nja", Pack.Dynasty, (4, Shape.Circle), 10, 20, (1, 1), (2, 4), Shape.Circle, 75, "May attack up to three times per turn.");
  public static readonly PieceDefinition Sumo = new(PieceType.Sumo, "Sum", Pack.Dynasty, (2, Shape.Straight), 10, 55, (1, 1), (1, 1), Shape.Straight, 55, "Pushes attacked units 2 tiles back.");
  public static readonly PieceDefinition Elephant = new(PieceType.Elephant, "Ele", Pack.Dynasty, (3, Shape.Straight), 30, 120, (2, 2), (0, 0), Shape.None, 110, "May move through enemies, damaging each crossed unit. Ignores terrain.");
  public static readonly PieceDefinition Ox = new(PieceType.Ox, "Ox", Pack.Dynasty, (4, Shape.Any), 10, 50, (1, 1), (1, 1), Shape.Straight, 70, "Attaches to a 1x1 friendly unit and increases that unit's Movement by 2. While attached, if attacked, both units take damage.");
  public static readonly PieceDefinition Carpenter = new(PieceType.Carpenter, "Crp", Pack.Dynasty, (3, Shape.Straight), 0, 45, (1, 1), (1, 2), Shape.Straight, 45, "As an attack, may build bridges or a watchtower. May demolish any in-range structure for free.");
  public static readonly PieceDefinition Emperor = new(PieceType.Emperor, "EMP", Pack.Dynasty, (2, Shape.Straight), 10, 80, (1, 1), (1, 1), Shape.Straight, 0, "After dying, revive in the same position as a Terracotta Warrior.");
  public static readonly PieceDefinition TerracottaWarrior = new(PieceType.TerracottaWarrior, "TWA", Pack.Dynasty, (0, Shape.None), 0, 100, (1, 1), (0, 0), Shape.None, 0, "This unit cannot be bought.", displayName: "Terracotta Warrior");
  public static readonly PieceDefinition Palace = new(PieceType.Palace, "PAL", Pack.Dynasty, (0, Shape.None), 0, 220, (3, 2), (0, 0), Shape.None, 0, "Friendly pieces moving in the direction of the Palace gain +1 movement and ignore terrain.");

  // Fantasy
  public static readonly PieceDefinition Commoner = new(PieceType.Commoner, "Com", Pack.Fantasy, (1, Shape.Any), 15, 10, (1, 1), (1, 1), Shape.Any, 25);
  public static readonly PieceDefinition Adventurer = new(PieceType.Adventurer, "Adv", Pack.Fantasy, (3, Shape.Circle), 20, 40, (1, 1), (1, 1), Shape.Any, 50);
  public static readonly PieceDefinition Elf = new(PieceType.Elf, "Elf", Pack.Fantasy, (5, Shape.Circle), 20, 15, (1, 1), (1, 1), Shape.Circle, 45, "Ignores forest for movement");
  public static readonly PieceDefinition Orc = new(PieceType.Orc, "Orc", Pack.Fantasy, (2, Shape.Straight), 30, 65, (1, 1), (1, 1), Shape.Any, 105, "When attacking, attack all units in range");
  public static readonly PieceDefinition Mimic = new(PieceType.Mimic, "Mmc", Pack.Fantasy, (6, Shape.Circle), 35, 15, (1, 1), (1, 1), Shape.Any, 65, "Cannot move normally; instead must swap places with another unit.");
  public static readonly PieceDefinition Wizard = new(PieceType.Wizard, "Wiz", Pack.Fantasy, (2, Shape.Straight), 25, 25, (1, 1), (2, 4), Shape.Circle, 100, "Deals damage in a 3x3 area centred on the unit it attacks.");
  public static readonly PieceDefinition Witch = new(PieceType.Witch, "Wit", Pack.Fantasy, (3, Shape.Circle), 0, 20, (1, 1), (2, 2), Shape.Any, 65, "Summons cloud of poison in a 3x3 area that deals 15 damage / turn to all units inside until dispelled or Witch is killed. Cannot have more than 1 cloud active at a time.");
  public static readonly PieceDefinition Druid = new(PieceType.Druid, "Dru", Pack.Fantasy, (2, Shape.Straight), 10, 25, (1, 1), (2, 2), Shape.Straight, 50, "As an attack, may grow a Bramble on an adjacent empty square. Brambles have 30 Health, damage units that enter them, and cannot be ended on.");
  public static readonly PieceDefinition Dragon = new(PieceType.Dragon, "Dra", Pack.Fantasy, (4, Shape.Straight), 45, 120, (2, 3), (3, 3), Shape.ForwardLine, 185, "Hits all units within attack range.");
  public static readonly PieceDefinition Sorceress = new(PieceType.Sorceress, "PRI", Pack.Fantasy, (2, Shape.Straight), 20, 140, (1, 1), (1, 4), Shape.Any, 0, "May attack over units and terrain and barricades.");
  public static readonly PieceDefinition GoblinRoyalty = new(PieceType.GoblinRoyalty, "GK, GQ, GP, GP", Pack.Fantasy, (2, Shape.Any), 10, 35, (1, 1), (1, 1), Shape.Straight, 0, "This royal is made up of 4, seperate units. You lose if all of them die.", displayName: "Goblin Royalty");
  public static readonly PieceDefinition Giant = new(PieceType.Giant, "Gia", Pack.Fantasy, (2, Shape.Any), 20, 70, (2, 2), (1, 1), Shape.Straight, 115, "Can pick up a directly adjacent 1x1 unit as your attack - they move with you and are unable to move themselves; they can attack you if you are within range. You can throw a carried unit anywhere within a 2-3 Circle.");

  // Undead
  public static readonly PieceDefinition Skeleton = new(PieceType.Skeleton, "Ske", Pack.Undead, (3, Shape.Circle), 15, 20, (1, 1), (1, 1), Shape.Straight, 30);
  public static readonly PieceDefinition Banshee = new(PieceType.Banshee, "Ban", Pack.Undead, (4, Shape.Circle), 15, 15, (1, 1), (3, 4), Shape.Circle, 40);
  public static readonly PieceDefinition Reaper = new(PieceType.Reaper, "Lic", Pack.Undead, (2, Shape.Any), 60, 50, (1, 1), (1, 1), Shape.Any, 115);
  public static readonly PieceDefinition Zombie = new(PieceType.Zombie, "Zom", Pack.Undead, (1, Shape.Straight), 15, 10, (1, 1), (1, 1), Shape.Any, 45, "After dying, spawn a Flesh in its position.");
  public static readonly PieceDefinition Flesh = new(PieceType.Flesh, "Fle", Pack.Undead, (0, Shape.None), 0, 5, (1, 1), (0, 0), Shape.None, 0, "This unit cannot be bought. After one turn, transform into Zombie.");
  public static readonly PieceDefinition Abomination = new(PieceType.Abomination, "Abm", Pack.Undead, (3, Shape.Line), 30, 65, (2, 2), (0, 0), Shape.None, 70, "Deals damage by ending movement on unit.");
  public static readonly PieceDefinition Ghoul = new(PieceType.Ghoul, "Gou", Pack.Undead, (2, Shape.Any), 25, 120, (1, 1), (1, 1), Shape.Straight, 65, "Dies after 4 turns.");
  public static readonly PieceDefinition Vampire = new(PieceType.Vampire, "Vmp", Pack.Undead, (3, Shape.Circle), 30, 40, (1, 1), (1, 1), Shape.Straight, 90, "Heals Vampire by 15 after attacking.");
  public static readonly PieceDefinition Lich = new(PieceType.Lich, "LIC", Pack.Undead, (1, Shape.Any), 25, 30, (1, 1), (3, 3), Shape.Line, 0, "Unchoosable. After dying, spawn a Flesh in its position.");
  public static readonly PieceDefinition Phylactery = new(PieceType.Phylactery, "PHY", Pack.Undead, (1, Shape.Straight), 0, 20, (1, 1), (0, 0), Shape.None, 0, "Unchoosable, cannot take damage.");
  public static readonly PieceDefinition Phantom = new(PieceType.Phantom, "PHA", Pack.Undead, (1, Shape.Straight), 0, 25, (1, 1), (1, 1), Shape.Straight, 0, "Can Possess any friendly unit on its turn, making it the royal. Can Unpossess on its turn. Cannot move or attack after unpossessing. If the possessed unit dies, so does the Phantom.");

  // Greek
  public static readonly PieceDefinition Heracles = new(PieceType.Heracles, "Hcl", Pack.Greek, (2, Shape.Straight), 25, 35, (1, 1), (1, 1), Shape.Straight, 35);
  public static readonly PieceDefinition Ares = new(PieceType.Ares, "Are", Pack.Greek, (2, Shape.Straight), 60, 15, (1, 1), (1, 1), Shape.Straight, 60);
  public static readonly PieceDefinition Pegasus = new(PieceType.Pegasus, "Peg", Pack.Greek, ((2, 4), Shape.Circle), 20, 45, (1, 2), (1, 1), Shape.Straight, 65);
  public static readonly PieceDefinition Spartan = new(PieceType.Spartan, "Shd", Pack.Greek, (1, Shape.Any), 25, 35, (1, 1), (1, 1), Shape.Any, 65, "The first time when dropping to 0 health, do not die and come back on 20 health.");
  public static readonly PieceDefinition Hermes = new(PieceType.Hermes, "Hem", Pack.Greek, (4, Shape.Circle), 15, 20, (1, 1), (1, 1), Shape.Straight, 75, "Can move twice per turn.");
  public static readonly PieceDefinition Artemis = new(PieceType.Artemis, "Art", Pack.Greek, (3, Shape.Circle), 20, 25, (1, 1), (2, 3), Shape.Any, 55, "Can attack through forests. Deals +10 damage to enemies in forests.");
  public static readonly PieceDefinition Chariot = new(PieceType.Chariot, "Cha", Pack.Greek, (5, Shape.Line), 30, 55, (1, 1), (1, 3), Shape.Line, 80);
  public static readonly PieceDefinition Ballista = new(PieceType.Ballista, "Bal", Pack.Greek, (1, Shape.Straight), 40, 25, (1, 1), (2, 5), Shape.Line, 100, "Its attack pierces enemies in a straight line within range.");
  public static readonly PieceDefinition Chimera = new(PieceType.Chimera, "Chi", Pack.Greek, (3, Shape.Any), 30, 75, (1, 1), (1, 1), Shape.Any, 110, "Attacking behind it deals +15 damage");
  public static readonly PieceDefinition Zeus = new(PieceType.Zeus, "ZEU", Pack.Greek, (2, Shape.Any), 30, 55, (1, 1), (4, 4), Shape.Circle, 140, "Its attack can chain to enemies adjacent to the target and enemies next to that one etc. Deals 20 damage");
  public static readonly PieceDefinition Daedalus = new(PieceType.Daedalus, "Dae", Pack.Greek, (3, Shape.Circle), 0, 30, (1, 1), (3, 3), Shape.Circle, 40, "As an attack, may build gates or a snare. May demolish any in-range structure for free.");
  public static readonly PieceDefinition Atlas = new(PieceType.Atlas, "ATL", Pack.Greek, (2, Shape.Straight), 10, 170, (2, 2), (1, 1), Shape.Straight, 0, "As an active ability once per turn, move all units on your team that have a non-zero movement value 1 tile in any direction. Whenever this unit attacks an enemy, push that enemy 2 tiles away.");
  public static readonly PieceDefinition Chronos = new(PieceType.Chronos, "CHR", Pack.Greek, (3, Shape.Circle), 15, 140, (1, 1), (2, 2), Shape.Line, 0);
  public static readonly PieceDefinition Cyclops = new(PieceType.Cyclops, "", Pack.Greek, (1, Shape.Straight), 35, 85, (2, 2), (1, 1), Shape.Straight, 125, "Can pick up a directly adjacent 1x1 unit as your attack - they move with you and are unable to move themselves; they can attack you if you are within range. You can throw a carried unit anywhere within a 2-3 Diamond.");

  // Norse
  public static readonly PieceDefinition Viking = new(PieceType.Viking, "Vik", Pack.Norse, (4, Shape.Straight), 20, 20, (1, 1), (1, 1), Shape.Any, 35);
  public static readonly PieceDefinition Hunter = new(PieceType.Hunter, "Hnt", Pack.Norse, (2, Shape.Straight), 30, 20, (1, 1), (4, 4), Shape.Circle, 50);
  public static readonly PieceDefinition Sleipnir = new(PieceType.Sleipnir, "Slp", Pack.Norse, ((4, 6), Shape.Circle), 40, 60, (1, 2), (2, 2), Shape.Straight, 100, "Movement ignores all terrain (apart from roads) and other units.");
  public static readonly PieceDefinition Raider = new(PieceType.Raider, "Rai", Pack.Norse, (2, Shape.Circle), 40, 35, (1, 1), (1, 1), Shape.Straight, 85, "Moves 2 faster forward");
  public static readonly PieceDefinition Beserker = new(PieceType.Beserker, "Bsk", Pack.Norse, (4, Shape.Any), 20, 40, (1, 1), (1, 1), Shape.Straight, 75, "Deals 40 damage when at or under 20 health");
  public static readonly PieceDefinition Valkyrie = new(PieceType.Valkyrie, "Val", Pack.Norse, (4, Shape.Circle), 35, 35, (1, 1), (1, 1), Shape.Straight, 80, "Can spawn in no-mans-land");
  public static readonly PieceDefinition Runesmith = new(PieceType.Runesmith, "Run", Pack.Norse, (4, Shape.Straight), 0, 25, (1, 1), (4, 4), Shape.Straight, 65, "As an attack, may build attack, movement, health or range runes. May demolish any in-range structure for free.");
  public static readonly PieceDefinition Jarl = new(PieceType.Jarl, "JAR", Pack.Norse, ((2, 4), Shape.Straight), 40, 160, (1, 1), (2, 2), Shape.Straight, 0);

  // Modern
  public static readonly PieceDefinition Civilian = new(PieceType.Civilian, "Civ", Pack.Modern, (1, Shape.Any), 10, 20, (1, 1), (1, 1), Shape.Straight, 25);
  public static readonly PieceDefinition Officer = new(PieceType.Officer, "Ofi", Pack.Modern, (3, Shape.Straight), 25, 30, (1, 1), (1, 1), Shape.Any, 40);
  public static readonly PieceDefinition Gunman = new(PieceType.Gunman, "Gun", Pack.Modern, (3, Shape.Straight), 30, 25, (1, 1), (3, 4), Shape.Any, 65);
  public static readonly PieceDefinition Sniper = new(PieceType.Sniper, "Sni", Pack.Modern, (0, Shape.None), 50, 30, (1, 1), (8, 8), Shape.Circle, 100);
  public static readonly PieceDefinition Terrorist = new(PieceType.Terrorist, "Ter", Pack.Modern, (4, Shape.Straight), 70, 10, (1, 1), (1, 1), Shape.Any, 70, "Dies to attack; hits all units in range.");
  public static readonly PieceDefinition Spy = new(PieceType.Spy, "Spy", Pack.Modern, (5, Shape.Any), 0, 30, (1, 1), (1, 3), Shape.Straight, 65, "Marks an enemy; it takes double damage until attacked.");
  public static readonly PieceDefinition Tank = new(PieceType.Tank, "Tnk", Pack.Modern, (3, Shape.Straight), 70, 115, (2, 2), (2, 4), Shape.Line, 155, "May only attack in the direction you are facing. If it attempts to attack in another direction, then it turns to face that direction.");
  public static readonly PieceDefinition Engineer = new(PieceType.Engineer, "Eng", Pack.Modern, (3, Shape.Any), 0, 40, (1, 1), (1, 1), Shape.Any, 55, "Builds up to two structures: roads that remove the forest they are placed on or set the movement cost for that previously empty tile to 0 for the team who built them, or 40-health barricades that can't be moved or attacked over. May demolish any in-range structure for free.");
  public static readonly PieceDefinition Mercenary = new(PieceType.Mercenary, "Mrc", Pack.Modern, (3, Shape.Any), 30, 40, (1, 1), (1, 2), Shape.Straight, 45, "Place anywhere in No-Man's-Land. Costs 25 gold per owner turn; it is fired if you cannot pay. Fire it to leave it neutral for either player to hire or kill.");
  public static readonly PieceDefinition President = new(PieceType.President, "PRE", Pack.Modern, (3, Shape.Circle), 35, 215, (1, 1), (1, 2), Shape.Straight, 0, "Costs 5 gold/turn to maintain. Lose if cannot afford.");

  // Wild West
  public static readonly PieceDefinition Brawler = new(PieceType.Brawler, "Bra", Pack.WildWest, (3, Shape.Any), 30, 40, (1, 1), (1, 1), Shape.Any, 45);
  public static readonly PieceDefinition Cowboy = new(PieceType.Cowboy, "Cow", Pack.WildWest, (4, Shape.Circle), 25, 25, (1, 1), (3, 3), Shape.Straight, 55);
  public static readonly PieceDefinition Demolitionist = new(PieceType.Demolitionist, "Dem", Pack.WildWest, (3, Shape.Straight), 0, 20, (1, 1), (1, 1), Shape.Straight, 65, "As their attack, can place a TNT that, at on your turn, you may detonate, damaging all units adjacent and above by 30. Also destroys terrain in that area. Cannot place a tnt if there's already one.");
  public static readonly PieceDefinition Pickpocket = new(PieceType.Pickpocket, "Pik", Pack.WildWest, (3, Shape.Circle), 0, 20, (1, 1), (1, 1), Shape.Straight, 35, "Steals 20 gold from the other play when attacking a unit.");
  public static readonly PieceDefinition Stagecoach = new(PieceType.Stagecoach, "Stg", Pack.WildWest, (5, Shape.Line), 30, 85, (1, 2), (3, 3), Shape.Straight, 125, "May move through enemies, damaging each crossed unit for 25 Health.");
  public static readonly PieceDefinition Sherrif = new(PieceType.Sherrif, "SHR", Pack.WildWest, (2, Shape.Any), 15, 170, (1, 1), (3, 3), Shape.Circle, 0, "Can attack twice per turn.");

  // Angels & Demons
  public static readonly PieceDefinition Fiend = new(PieceType.Fiend, "Fie", Pack.AngelsDemons, ((2, 4), Shape.Circle), 15, 20, (1, 1), (1, 1), Shape.Straight, 25);
  public static readonly PieceDefinition Cherub = new(PieceType.Cherub, "Che", Pack.AngelsDemons, (3, Shape.Circle), 15, 15, (1, 1), (1, 2), Shape.Straight, 35);
  public static readonly PieceDefinition Fallen = new(PieceType.Fallen, "Fal", Pack.AngelsDemons, (5, Shape.Diagonal), 25, 40, (1, 1), (1, 4), Shape.Line, 45);
  public static readonly PieceDefinition Gatekeeper = new(PieceType.Gatekeeper, "Gat", Pack.AngelsDemons, (3, Shape.Straight), 0, 20, (1, 1), (1, 6), Shape.Circle, 70, "As an attack, may build one of the following structures on an unoccupied square: 2 connected portals that teleport friendly units between when they land on it; 2 impassable seals that disappear at the beginning of your next turn. May demolish any in-range structure for free.");
  public static readonly PieceDefinition Archangel = new(PieceType.Archangel, "Ang", Pack.AngelsDemons, (3, Shape.Straight), 15, 110, (2, 2), (1, 1), Shape.Any, 175);
  public static readonly PieceDefinition Archdemon = new(PieceType.Archdemon, "Ade", Pack.AngelsDemons, (5, Shape.Circle), 110, 40, (2, 2), (1, 2), Shape.Straight, 175);
  public static readonly PieceDefinition Succubus = new(PieceType.Succubus, "Suc", Pack.AngelsDemons, (4, Shape.Straight), 5, 30, (1, 1), (1, 1), Shape.Any, 0, "When attacking an enemy, attach to it. A unit may not attack a Succubus that is attached to it; the Succubus has to be attacked by another unit. At any time on the owner's turn the Succubus may detach.");
  public static readonly PieceDefinition Herald = new(PieceType.Herald, "HRA", Pack.AngelsDemons, (4, Shape.Circle), 10, 150, (1, 1), (1, 1), Shape.Straight, 0, "Moves adjacent friendly 1x1 pieces with it.");

  // Chess
  private const string ChessLandingCaptureAbility = "Attack a unit by landing on them; if they die, move onto the square they were on, else go to the previous square of your movement.";
  public static readonly PieceDefinition Pawn = new(PieceType.Pawn, "Pwn", Pack.Chess, (2, Shape.Forward), 120, 5, (1, 1), (1, 1), Shape.ForwardDiagonal, 10, ChessLandingCaptureAbility);
  public static readonly PieceDefinition ChessKnight = new(PieceType.ChessKnight, "KnC", Pack.Chess, (3, Shape.ChessKnight), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 30, ChessLandingCaptureAbility, displayName: "Chess Knight");
  public static readonly PieceDefinition Bishop = new(PieceType.Bishop, "Bsh", Pack.Chess, (8, Shape.Diagonal), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 30, ChessLandingCaptureAbility);
  public static readonly PieceDefinition Rook = new(PieceType.Rook, "Rok", Pack.Chess, (8, Shape.Line), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 50, ChessLandingCaptureAbility);
  public static readonly PieceDefinition Queen = new(PieceType.Queen, "Qun", Pack.Chess, (8, Shape.LineOrDiagonal), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 90, ChessLandingCaptureAbility);
  public static readonly PieceDefinition ChessKing = new(PieceType.ChessKing, "CKI", Pack.Chess, (1, Shape.Any), 120, 5, (1, 1), (0, 0), Shape.MoveOnEnemy, 0, ChessLandingCaptureAbility + " Must be checkmated to die.", displayName: "Chess King");

  public static readonly PieceDefinition[] All =
  [
    Peasant, Swordsman, Defender, Archer, Crossbowman, Knight, Cavalier, Bombard, Cannon, Catapult, Guard, Mason, Farm, King, Baron,
    Ashigaru, Samurai, Ninja, Sumo, Elephant, Ox, Carpenter, Emperor, TerracottaWarrior, Palace,
    Commoner, Adventurer, Elf, Orc, Mimic, Wizard, Witch, Druid, Dragon, Sorceress, GoblinRoyalty, Giant,
    Skeleton, Banshee, Reaper, Zombie, Flesh, Abomination, Ghoul, Vampire, Lich, Phylactery, Phantom,
    Heracles, Ares, Pegasus, Spartan, Hermes, Artemis, Chariot, Ballista, Chimera, Zeus, Daedalus, Atlas, Chronos, Cyclops,
    Viking, Hunter, Sleipnir, Raider, Beserker, Valkyrie, Runesmith, Jarl,
    Civilian, Officer, Gunman, Sniper, Terrorist, Spy, Tank, Engineer, Mercenary, President,
    Brawler, Cowboy, Demolitionist, Pickpocket, Stagecoach, Sherrif,
    Fiend, Cherub, Fallen, Gatekeeper, Archangel, Archdemon, Succubus, Herald,
    Pawn, ChessKnight, Bishop, Rook, Queen, ChessKing
  ];

  public static readonly PieceDefinition[] Encyclopedia = [.. All];

  public static readonly PieceDefinition[] Purchasable =
  [
    Peasant, Swordsman, Defender, Archer, Crossbowman, Knight, Cavalier, Bombard, Cannon, Catapult, Guard, Mason, Farm,
    Ashigaru, Samurai, Ninja, Sumo, Elephant, Ox, Carpenter,
    Commoner, Adventurer, Elf, Orc, Mimic, Wizard, Witch, Druid, Dragon, Giant,
    Skeleton, Banshee, Reaper, Zombie, Abomination, Ghoul, Vampire,
    Heracles, Ares, Pegasus, Spartan, Hermes, Artemis, Chariot, Ballista, Chimera, Zeus, Daedalus, Cyclops,
    Viking, Hunter, Sleipnir, Raider, Beserker, Valkyrie, Runesmith,
    Civilian, Officer, Gunman, Sniper, Terrorist, Spy, Tank, Engineer, Mercenary,
    Brawler, Cowboy, Demolitionist, Pickpocket, Stagecoach,
    Fiend, Cherub, Fallen, Gatekeeper, Archangel, Archdemon,
    Pawn, ChessKnight, Bishop, Rook, Queen
  ];

  public static readonly PieceDefinition[] Royals =
  [
    King, Baron,
    Emperor, TerracottaWarrior, Palace,
    Sorceress, GoblinRoyalty,
    Phylactery, Phantom,
    Atlas, Chronos,
    President, Jarl, Sherrif,
    Herald, ChessKing
  ];
}
