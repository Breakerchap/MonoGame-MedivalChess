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

  public static implicit operator AttackRange((int minimum, int maximum) range) => new(range.minimum, range.maximum);
}

public enum Shape
{
  Any,
  Straight,
  Line,
  Forward,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  PierceStraight,
  MoveOnEnemy,
  None
}

public enum PieceType
{
  Soldier, Defender, Archer, Spearman,
  Peasant, Knight, Crossbowman, Cavalier, Chariot,
  Cannon, Spy, Catapult, Bombard, Ox, Engineer, Ballista,
  Elephant, Guard, Mercenary, Farm,

  King, Princess, Palace, Baron, Emissary
}

public enum PieceCategory
{
  Melee, Ranged, Intelligence,
  Mechanical, Structure, Transport,
  Royal
}

/// <summary>Authoritative game-facing unit definition. All unit stats are declared in <see cref="PieceDefinitions"/>.</summary>
public sealed class PieceDefinition
{
  public PieceType Type { get; }
  public PieceCategory Category { get; }
  public (int range, Shape shape) Movement { get; }
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
    PieceCategory category,
    (int range, Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    AttackRange attackRange,
    Shape attackPattern,
    int cost,
    string abilityDescription = ""
  )
  {
    Type = type;
    Category = category;
    Movement = movement;
    Attack = attack;
    Health = health;
    Size = size;
    AttackRange = attackRange;
    AttackPattern = attackPattern;
    Cost = cost;
    AbilityDescription = abilityDescription;
  }
}

/// <summary>
/// The single source of truth for every unit's stats. Attack ranges are inclusive: use
/// <c>(1, 3)</c> for one to three squares, or <c>(2, 4)</c> for two to four.
/// </summary>
public static class PieceDefinitions
{
  public static readonly PieceDefinition Soldier = new(
    PieceType.Soldier, PieceCategory.Melee, (3, Shape.Straight), 10, 15, (1, 1), (1, 1), Shape.Straight, 20);
  public static readonly PieceDefinition Defender = new(
    PieceType.Defender, PieceCategory.Melee, (2, Shape.Any), 5, 25, (1, 1), (1, 1), Shape.Straight, 15);
  public static readonly PieceDefinition Archer = new(
    PieceType.Archer, PieceCategory.Ranged, (3, Shape.Straight), 10, 10, (1, 1), (2, 3), Shape.Any, 30);
  public static readonly PieceDefinition Spearman = new(
    PieceType.Spearman, PieceCategory.Melee, (2, Shape.Any), 15, 15, (1, 1), (1, 1), Shape.ForwardOrForwardDiagonal, 25);
  public static readonly PieceDefinition Peasant = new(
    PieceType.Peasant, PieceCategory.Melee, (1, Shape.Any), 5, 5, (1, 1), (1, 1), Shape.Straight, 10);
  public static readonly PieceDefinition Knight = new(
    PieceType.Knight, PieceCategory.Melee, (4, Shape.Any), 20, 30, (1, 1), (1, 1), Shape.Any, 50);
  public static readonly PieceDefinition Crossbowman = new(
    PieceType.Crossbowman, PieceCategory.Ranged, (2, Shape.Any), 20, 15, (1, 1), (1, 3), Shape.Any, 50);
  public static readonly PieceDefinition Cavalier = new(
    PieceType.Cavalier, PieceCategory.Melee, (4, Shape.Any), 15, 20, (1, 1), (1, 1), Shape.Any, 50,
    "Movement refreshes after attacking.");
  public static readonly PieceDefinition Chariot = new(
    PieceType.Chariot, PieceCategory.Melee, (5, Shape.Line), 15, 25, (1, 1), (1, 3), Shape.Line, 40);
  public static readonly PieceDefinition Cannon = new(
    PieceType.Cannon, PieceCategory.Mechanical, (2, Shape.Straight), 30, 15, (1, 2), (2, 3), Shape.Line, 50);
  public static readonly PieceDefinition Spy = new(
    PieceType.Spy, PieceCategory.Intelligence, (5, Shape.Any), 0, 15, (1, 1), (1, 3), Shape.Straight, 35,
    "Marks an enemy; it takes double damage until attacked.");
  public static readonly PieceDefinition Catapult = new(
    PieceType.Catapult, PieceCategory.Mechanical, (1, Shape.Any), 20, 20, (1, 2), (3, 5), Shape.Any, 55,
    "Attacks over terrain and pieces.");
  public static readonly PieceDefinition Bombard = new(
    PieceType.Bombard, PieceCategory.Ranged, (2, Shape.Straight), 15, 15, (1, 1), (2, 3), Shape.Straight, 55,
    "Every adjacent and diagonally adjacent unit to the target take 10 damage, including friendly units.");
  public static readonly PieceDefinition Ox = new(
    PieceType.Ox, PieceCategory.Transport, (4, Shape.Any), 5, 25, (1, 1), (1, 1), Shape.Straight, 35,
    "Carries one friendly unit. While carrying, if attacked, both units take damage. Its movement becomes 3 Any while carrying a Mechanical unit.");
  public static readonly PieceDefinition Engineer = new(
    PieceType.Engineer, PieceCategory.Intelligence, (3, Shape.Any), 0, 20, (1, 1), (1, 1), Shape.Any, 25,
    "Builds up to two: roads, 20-health barricades, or mines each turn. It may also demolish Engineer structures within range. Doesn't trigger mines.");
  public static readonly PieceDefinition Ballista = new(
    PieceType.Ballista, PieceCategory.Mechanical, (1, Shape.Straight), 20, 20, (1, 2), (2, 5), Shape.Line, 55,
    "Its attack pierces enemies in a straight line.");
  public static readonly PieceDefinition Elephant = new(
    PieceType.Elephant, PieceCategory.Melee, (3, Shape.Straight), 10, 60, (2, 2), (0, 0), Shape.None, 60,
    "May move through enemies, damaging each crossed unit. Ignores terrain.");
  public static readonly PieceDefinition Guard = new(
    PieceType.Guard, PieceCategory.Melee, (3, Shape.Straight), 10, 25, (1, 1), (1, 1), Shape.Straight, 35,
    "Attaches to a friendly non-royal unit and takes damage for it.");
  public static readonly PieceDefinition Mercenary = new(
    PieceType.Mercenary, PieceCategory.Melee, (3, Shape.Any), 15, 20, (1, 1), (1, 2), Shape.Straight, 25,
    "Place anywhere in No-Man's-Land. Costs 10 gold per owner turn; it is fired if you cannot pay. Fire it to leave it neutral for either player to hire or kill.");
  public static readonly PieceDefinition Farm = new(
    PieceType.Farm, PieceCategory.Structure, (0, Shape.None), 0, 30, (3, 3), (0, 0), Shape.None, 60,
    "Earns 5 gold at the start of each owner turn. Units may move and attack over it.");
  public static readonly PieceDefinition King = new(
    PieceType.King, PieceCategory.Royal, (1, Shape.Any), 15, 110, (1, 1), (1, 1), Shape.Any, 0,
    "Adjacent allies take 5 less damage, to a minimum of 5.");
  public static readonly PieceDefinition Princess = new(
    PieceType.Princess, PieceCategory.Royal, (1, Shape.Any), 10, 80, (1, 1), (1, 4), Shape.Any, 0,
    "May attack over friendly units.");
  public static readonly PieceDefinition Palace = new(
    PieceType.Palace, PieceCategory.Royal, (0, Shape.None), 0, 150, (3, 2), (0, 0), Shape.None, 0,
    "Earns 5 gold at the start of each owner turn.");
  public static readonly PieceDefinition Baron = new(
    PieceType.Baron, PieceCategory.Royal, (2, Shape.Straight), 10, 100, (1, 1), (1, 1), Shape.Any, 0,
    "Adjacent allies deal 5 additional damage. Multiple bonuses do not stack.");
  public static readonly PieceDefinition Emissary = new(
    PieceType.Emissary, PieceCategory.Royal, (4, Shape.Any), 5, 80, (1, 1), (1, 1), Shape.Any, 0,
    "Moves directly adjacent friendly 1x1 allies with it.");

  public static readonly PieceDefinition[] All =
  [
    Soldier, Defender, Archer, Peasant, Knight,
    Crossbowman, Cavalier, Chariot, Cannon, Spy,
    Catapult, Bombard, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary, Farm, King, Princess,
    Palace, Baron, Emissary
  ];

  public static readonly PieceDefinition[] Encyclopedia =
  [
    Soldier, Defender, Archer, Peasant, Knight,
    Crossbowman, Cavalier, Chariot, Cannon, Spy,
    Catapult, Bombard, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary, Farm, King, Princess,
    Palace, Baron, Emissary
  ];

  public static readonly PieceDefinition[] Purchasable =
  [
    Soldier, Defender, Archer, Peasant, Knight,
    Crossbowman, Cavalier, Chariot, Cannon, Spy,
    Catapult, Bombard, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary, Farm
  ];

  public static readonly PieceDefinition[] Royals =
  [
    King, Princess, Palace, Baron, Emissary
  ];
}
