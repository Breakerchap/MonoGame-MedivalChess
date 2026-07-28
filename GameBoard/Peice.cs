namespace MedivalChess.GameBoard;

using System.Collections.Generic;
using MedivalChess.Player;
using MedivalChess.Shared;

internal enum AttachmentKind
{
  None,
  Guard,
  Carried,
  Towed
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

  internal PieceDefinition(
    PieceType name,
    PieceCategory category,
    (int range, Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    (int range, Shape shape) attackShape,
    int cost,
    int minimumAttackRange = 1
  )
  {
    UnitRule sharedRule = UnitRules.GetRequired(name.ToString());
    Type = name;
    Category = (PieceCategory)sharedRule.Category;
    Movement = (sharedRule.MoveRange, ToLocalShape(sharedRule.MovePattern));
    Attack = sharedRule.Attack;
    Health = sharedRule.Health;
    Size = (sharedRule.Width, sharedRule.Height);
    AttackShape = (sharedRule.AttackRange, ToLocalShape(sharedRule.AttackPattern));
    Cost = sharedRule.Cost;
    MinimumAttackRange = sharedRule.MinimumAttackRange;
  }

  private static Shape ToLocalShape(RuleShape shape) => shape switch
  {
    RuleShape.Any => Shape.Any,
    RuleShape.Straight => Shape.Straight,
    RuleShape.Forward => Shape.Forward,
    RuleShape.AbsoluteStraightOrDiagonal => Shape.AbsoluteStraightOrDiagonal,
    RuleShape.ForwardOrForwardDiagonal => Shape.ForwardOrForwardDiagonal,
    RuleShape.PierceStraight => Shape.PierceStraight,
    _ => Shape.None
  };
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
  Cannon, Spy, Catapult, FieldHospital,
  Ambulance, Ox, Engineer, Ballista,
  Elephant, Guard, Mercenary, Assassin,

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
    (2, Shape.Straight),
    10,
    15,
    (1, 1),
    (1, Shape.Straight),
    20
  );
  internal static readonly PieceDefinition Defender = new(
    PieceType.Defender,
    PieceCategory.Melee,
    (2, Shape.Straight),
    5,
    25,
    (1, 1),
    (1, Shape.Straight),
    20
  );

  internal static readonly PieceDefinition Archer = new(
    PieceType.Archer,
    PieceCategory.Ranged,
    (2, Shape.Any),
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
    (1, Shape.Straight),
    5,
    5,
    (1, 1),
    (1, Shape.ForwardOrForwardDiagonal),
    10
  );

  internal static readonly PieceDefinition Knight = new(
    PieceType.Knight,
    PieceCategory.Melee,
    (3, Shape.Any),
    20,
    25,
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
    45
  );

  internal static readonly PieceDefinition Cavalier = new(
    PieceType.Cavalier,
    PieceCategory.Melee,
    (4, Shape.Any),
    15,
    20,
    (1, 1),
    (1, Shape.Any),
    40
  );

  internal static readonly PieceDefinition Chariot = new(
    PieceType.Chariot,
    PieceCategory.Melee,
    (4, Shape.Straight),
    15,
    25,
    (1, 1),
    (1, Shape.Straight),
    40
  );

  internal static readonly PieceDefinition Cannon = new(
    PieceType.Cannon,
    PieceCategory.Mechanical,
    (2, Shape.Straight),
    30,
    25,
    (1, 2),
    (5, Shape.Straight),
    50,
    2
  );

  internal static readonly PieceDefinition Spy = new(
    PieceType.Spy,
    PieceCategory.Intelligence,
    (5, Shape.Any),
    0,
    10,
    (1, 1),
    (3, Shape.Any),
    35,
    1
  );

  internal static readonly PieceDefinition Catapult = new(
    PieceType.Catapult,
    PieceCategory.Mechanical,
    (1, Shape.Any),
    20,
    20,
    (1, 2),
    (6, Shape.Any),
    55,
    3
  );

  internal static readonly PieceDefinition FieldHospital = new(
    PieceType.FieldHospital,
    PieceCategory.Structure,
    (1, Shape.Any),
    0,
    0,
    (2, 2),
    (0, Shape.None),
    0
  );

  internal static readonly PieceDefinition Ambulance = new(
    PieceType.Ambulance,
    PieceCategory.Transport,
    (5, Shape.Any),
    0,
    10,
    (1, 2),
    (0, Shape.None),
    35
  );

  internal static readonly PieceDefinition Ox = new(
    PieceType.Ox,
    PieceCategory.Transport,
    (4, Shape.Any),
    5,
    25,
    (1, 1),
    (1, Shape.Forward),
    35
  );

  internal static readonly PieceDefinition Engineer = new(
    PieceType.Engineer,
    PieceCategory.Intelligence,
    (3, Shape.Any),
    0,
    15,
    (1, 1),
    (1, Shape.Straight),
    35
  );

  internal static readonly PieceDefinition Ballista = new(
    PieceType.Ballista,
    PieceCategory.Mechanical,
    (1, Shape.Straight),
    25,
    20,
    (2, 2),
    (5, Shape.PierceStraight),
    55,
    2
  );

  internal static readonly PieceDefinition Elephant = new(
    PieceType.Elephant,
    PieceCategory.Melee,
    (2, Shape.Straight),
    15,
    50,
    (2, 2),
    (0, Shape.None),
    60
  );

  internal static readonly PieceDefinition Guard = new(
    PieceType.Guard,
    PieceCategory.Melee,
    (3, Shape.Any),
    10,
    25,
    (1, 1),
    (1, Shape.Any),
    35
  );

  internal static readonly PieceDefinition Mercenary = new(
    PieceType.Mercenary,
    PieceCategory.Melee,
    (3, Shape.Any),
    25,
    20,
    (1, 1),
    (1, Shape.Any),
    45
  );

  internal static readonly PieceDefinition Assassin = new(
    PieceType.Assassin,
    PieceCategory.Melee,
    (3, Shape.Any),
    30,
    10,
    (1, 1),
    (1, Shape.Any),
    60
  );

  internal static readonly PieceDefinition King = new(
    PieceType.King,
    PieceCategory.Royal,
    (1, Shape.Any),
    15,
    120,
    (1, 1),
    (1, Shape.Any),
    0
  );

  internal static readonly PieceDefinition Princess = new(
    PieceType.Princess,
    PieceCategory.Royal,
    (1, Shape.Any),
    15,
    80,
    (1, 1),
    (3, Shape.Any),
    0,
    1
  );

  internal static readonly PieceDefinition Palace = new(
    PieceType.Palace,
    PieceCategory.Royal,
    (0, Shape.None),
    0,
    160,
    (3, 2),
    (0, Shape.None),
    0
  );

  internal static readonly PieceDefinition Baron = new(
    PieceType.Baron,
    PieceCategory.Royal,
    (1, Shape.Any),
    5,
    100,
    (1, 1),
    (1, Shape.Any),
    0
  );

  internal static readonly PieceDefinition Emissary = new(
    PieceType.Emissary,
    PieceCategory.Royal,
    (3, Shape.Any),
    5,
    80,
    (1, 1),
    (1, Shape.Any),
    0
  );

  internal static readonly PieceDefinition[] All =
  [
    Soldier, Defender, Archer, Spearman, Knight,
    Crossbowman, Cavalier, Chariot, Cannon, Spy,
    Catapult, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary, King, Princess,
    Palace, Baron, Emissary
  ];

  internal static readonly PieceDefinition[] Encyclopedia =
  [
    Soldier, Defender, Archer, Scout, Spearman, Peasant,
    Knight, Crossbowman, Cavalier, Chariot, Cannon, Spy,
    Catapult, FieldHospital, Ambulance, Ox, Engineer,
    Ballista, Elephant, Guard, Mercenary, Assassin,
    King, Princess, Palace, Baron, Emissary
  ];

  internal static readonly PieceDefinition[] Purchasable =
  [
    Soldier, Defender, Archer, Spearman, Knight,
    Crossbowman, Cavalier, Chariot, Cannon, Spy,
    Catapult, Ox, Engineer, Ballista,
    Elephant, Guard, Mercenary
  ];

  internal static readonly PieceDefinition[] Royals =
  [
    King, Princess, Palace, Baron, Emissary
  ];
}
