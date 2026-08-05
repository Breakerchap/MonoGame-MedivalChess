namespace MedivalChess.GameBoard;

using System.Collections.Generic;
using MedivalChess.Player;

internal enum AttachmentKind
{
  None,
  Guard,
  Carried
}

internal sealed class Piece
{
  internal PieceDefinition Definition { get; }
  internal int CurrentHealth { get; set; }
  internal (int x, int y) Position { get; set; }
  internal TeamName Team { get; set; }
  internal int LastBid { get; set; }
  internal Piece MarkedTarget { get; set; }
  internal Piece AttachedTo { get; set; }
  internal AttachmentKind AttachmentKind { get; set; }
  internal string NetworkId { get; set; } = System.Guid.NewGuid().ToString("N");
  internal bool HasMovedThisTurn { get; set; }
  internal bool HasAttackedThisTurn { get; set; }
  internal int EngineerBuildsThisTurn { get; set; }
  internal bool CannotContributeToConquestThisTurn { get; set; }
  internal long NextMercenaryBid => (long)LastBid + 10;

  internal Piece(PieceDefinition definition, (int x, int y) position, TeamName team)
  {
    Definition = definition;
    CurrentHealth = Definition.Health;
    Position = position;
    Team = team;
    LastBid = definition.Cost;
  }

  internal bool Occupies((int x, int y) position)
  {
    return
      position.x >= Position.x &&
      position.x < Position.x + Definition.Size.x &&
      position.y >= Position.y &&
      position.y < Position.y + Definition.Size.y;
  }

  internal IEnumerable<(int x, int y)> OccupiedSquares()
  {
    for (int y = 0; y < Definition.Size.y; y++)
    {
      for (int x = 0; x < Definition.Size.x; x++)
      {
        yield return (Position.x + x, Position.y + y);
      }
    }
  }
}

internal sealed class PieceDefinition
{
  internal PieceType Type { get; }
  internal PieceCategory Category { get; }
  internal (int range, Shape shape) Movement { get; }
  internal int Attack { get; }
  internal int Health { get; }
  internal (int x, int y) Size { get; }
  internal (int range, Shape shape) AttackShape { get; }
  internal int Cost { get; }
  internal int MinimumAttackRange { get; }
  // UI-facing metadata is carried with the same definition object as the stats.
  internal string AbilityDescription { get; }

  internal PieceDefinition(
    PieceType name,
    PieceCategory category,
    (int range, Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    (int range, Shape shape) attackShape,
    int cost,
    int minimumAttackRange = 1,
    string abilityDescription = ""
  )
  {
    Type = name;
    Category = category;
    Movement = movement;
    Attack = attack;
    Health = health;
    Size = size;
    AttackShape = attackShape;
    Cost = cost;
    MinimumAttackRange = minimumAttackRange;
    AbilityDescription = abilityDescription;
  }
}

internal enum Shape
{
  Any, Straight, Forward,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  PierceStraight,
  MoveOnEnemy, None
}

internal enum PieceType
{
  Soldier, Defender, Archer, Scout, Spearman,
  Peasant, Knight, Crossbowman, Cavalier, Chariot,
  Cannon, Spy, Catapult, Bombard, Ox, Engineer, Ballista,
  Elephant, Guard, Mercenary, Farm,

  King, Princess, Palace, Baron, Emissary
}

internal enum PieceCategory
{
  Melee, Ranged, Intelligence,
  Mechanical, Structure, Transport,
  Royal
}

internal static class PieceDefinitions
{
  internal static readonly PieceDefinition Soldier = new(
    PieceType.Soldier,
    PieceCategory.Melee,
    (3, Shape.Straight),
    10,
    15,
    (1, 1),
    (1, Shape.Straight),
    20
  );
  internal static readonly PieceDefinition Defender = new(
    PieceType.Defender,
    PieceCategory.Melee,
    (2, Shape.Any),
    5,
    25,
    (1, 1),
    (1, Shape.Straight),
    15
  );

  internal static readonly PieceDefinition Archer = new(
    PieceType.Archer,
    PieceCategory.Ranged,
    (3, Shape.Straight),
    10,
    10,
    (1, 1),
    (3, Shape.Any),
    30,
    2
  );

  internal static readonly PieceDefinition Scout = new(
    PieceType.Scout,
    PieceCategory.Intelligence,
    (4, Shape.Any),
    5,
    10,
    (1, 1),
    (1, Shape.Straight),
    20
  );

  internal static readonly PieceDefinition Spearman = new(
    PieceType.Spearman,
    PieceCategory.Melee,
    (2, Shape.Any),
    15,
    15,
    (1, 1),
    (1, Shape.ForwardOrForwardDiagonal),
    25
  );

  internal static readonly PieceDefinition Peasant = new(
    PieceType.Peasant,
    PieceCategory.Melee,
    (1, Shape.Any),
    5,
    5,
    (1, 1),
    (1, Shape.Straight),
    10
  );

  internal static readonly PieceDefinition Knight = new(
    PieceType.Knight,
    PieceCategory.Melee,
    (4, Shape.Any),
    20,
    30,
    (1, 1),
    (1, Shape.Any),
    50
  );

  internal static readonly PieceDefinition Crossbowman = new(
    PieceType.Crossbowman,
    PieceCategory.Ranged,
    (2, Shape.Any),
    20,
    15,
    (1, 1),
    (3, Shape.Any),
    50
  );

  internal static readonly PieceDefinition Cavalier = new(
    PieceType.Cavalier,
    PieceCategory.Melee,
    (4, Shape.Any),
    15,
    20,
    (1, 1),
    (1, Shape.Any),
    50
  );

  internal static readonly PieceDefinition Chariot = new(
    PieceType.Chariot,
    PieceCategory.Melee,
    (5, Shape.Straight),
    15,
    25,
    (1, 1),
    (2, Shape.Straight),
    40,
    2
  );

  internal static readonly PieceDefinition Cannon = new(
    PieceType.Cannon,
    PieceCategory.Mechanical,
    (2, Shape.Straight),
    30,
    15,
    (1, 2),
    (4, Shape.Straight),
    50,
    2
  );

  internal static readonly PieceDefinition Spy = new(
    PieceType.Spy,
    PieceCategory.Intelligence,
    (5, Shape.Any),
    0,
    15,
    (1, 1),
    (3, Shape.Straight),
    35,
    1,
    "Marks an enemy; it takes double damage until attacked."
  );

  internal static readonly PieceDefinition Catapult = new(
    PieceType.Catapult,
    PieceCategory.Mechanical,
    (1, Shape.Any),
    20,
    20,
    (1, 2),
    (5, Shape.Any),
    55,
    3,
    "Attacks over terrain and enemies."
  );

  internal static readonly PieceDefinition Bombard = new(
    PieceType.Bombard,
    PieceCategory.Ranged,
    (2, Shape.Straight),
    15,
    20,
    (1, 1),
    (4, Shape.Straight),
    55,
    2,
    "The target and every adjacent unit take 10 damage, including friendly units. Large units take damage only once."
  );

  internal static readonly PieceDefinition Ox = new(
    PieceType.Ox,
    PieceCategory.Transport,
    (4, Shape.Any),
    5,
    25,
    (1, 1),
    (1, Shape.Straight),
    35,
    1,
    "Carries one friendly unit. Its movement becomes 3 Any while carrying a Mechanical unit."
  );

  internal static readonly PieceDefinition Engineer = new(
    PieceType.Engineer,
    PieceCategory.Intelligence,
    (3, Shape.Any),
    0,
    20,
    (1, 1),
    (1, Shape.Any),
    25,
    1,
    "Builds up to two roads, 20-health barricades, or mines each turn. It may also demolish an adjacent Engineer structure without triggering mines."
  );

  internal static readonly PieceDefinition Ballista = new(
    PieceType.Ballista,
    PieceCategory.Mechanical,
    (1, Shape.Straight),
    25,
    20,
    (1, 2),
    (5, Shape.Straight),
    55,
    2,
    "Its attack pierces enemies in a straight line."
  );

  internal static readonly PieceDefinition Elephant = new(
    PieceType.Elephant,
    PieceCategory.Melee,
    (4, Shape.Straight),
    15,
    60,
    (2, 2),
    (0, Shape.None),
    55,
    0,
    "May move through enemies, damaging each crossed unit. Ignores terrain."
  );

  internal static readonly PieceDefinition Guard = new(
    PieceType.Guard,
    PieceCategory.Melee,
    (3, Shape.Straight),
    10,
    25,
    (1, 1),
    (1, Shape.Straight),
    35,
    1,
    "Attaches to a friendly non-royal unit and takes damage for it."
  );

  internal static readonly PieceDefinition Mercenary = new(
    PieceType.Mercenary,
    PieceCategory.Melee,
    (3, Shape.Any),
    25,
    20,
    (1, 1),
    (2, Shape.Any),
    10,
    1,
    "Place anywhere in No-Man's-Land. Costs 10 gold per owner turn; it is fired if you cannot pay. Fire it to leave it neutral for either player to hire or kill."
  );

  internal static readonly PieceDefinition King = new(
    PieceType.King,
    PieceCategory.Royal,
    (1, Shape.Any),
    15,
    110,
    (1, 1),
    (1, Shape.Any),
    0,
    1,
    "Adjacent allies take 5 less damage, to a minimum of 5."
  );

  internal static readonly PieceDefinition Princess = new(
    PieceType.Princess,
    PieceCategory.Royal,
    (1, Shape.Any),
    10,
    80,
    (1, 1),
    (4, Shape.Any),
    0,
    1,
    "May attack over friendly units."
  );

  internal static readonly PieceDefinition Palace = new(
    PieceType.Palace,
    PieceCategory.Royal,
    (0, Shape.None),
    0,
    150,
    (3, 2),
    (0, Shape.None),
    0,
    0,
    "Earns 5 gold at the start of each owner turn."
  );

  internal static readonly PieceDefinition Baron = new(
    PieceType.Baron,
    PieceCategory.Royal,
    (2, Shape.Straight),
    10,
    100,
    (1, 1),
    (1, Shape.Any),
    0,
    1,
    "Adjacent allies deal 5 additional damage. Multiple bonuses do not stack."
  );

  internal static readonly PieceDefinition Emissary = new(
    PieceType.Emissary,
    PieceCategory.Royal,
    (4, Shape.Any),
    5,
    80,
    (1, 1),
    (1, Shape.Any),
    0,
    1,
    "Moves directly adjacent friendly 1x1 allies with it."
  );

  internal static readonly PieceDefinition Farm = new(
    PieceType.Farm,
    PieceCategory.Structure,
    (0, Shape.None),
    0,
    30,
    (3, 3),
    (0, Shape.None),
    60,
    0,
    "Earns the configured gold amount at the start of each owner turn (default 5). Units may move and attack over it."
  );
  internal static readonly PieceDefinition[] All =
  [
    Soldier, Defender, Archer, Peasant, Knight,
    Crossbowman, Chariot, Cannon, Spy,
    Catapult, Bombard, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary, Farm, King, Princess,
    Palace, Baron, Emissary
  ];

  internal static readonly PieceDefinition[] Encyclopedia =
  [
    Soldier, Defender, Archer, Peasant,
    Knight, Crossbowman, Chariot, Cannon, Spy,
    Catapult, Bombard, Ox, Engineer,
    Ballista, Elephant, Guard, Mercenary, Farm,
    King, Princess, Palace, Baron, Emissary
  ];

  internal static readonly PieceDefinition[] Purchasable =
  [
    Soldier, Defender, Archer, Peasant, Knight,
    Crossbowman, Chariot, Cannon, Spy,
    Catapult, Bombard, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary, Farm
  ];

  internal static readonly PieceDefinition[] Royals =
  [
    King, Princess, Palace, Baron, Emissary
  ];
}
