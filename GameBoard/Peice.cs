namespace MedivalChess.GameBoard;

using MedivalChess.Player;

internal sealed class Piece
{
  internal PieceDefinition Definition { get; }
  internal int CurrentHealth { get; set; }
  internal (int x, int y) Position { get; set; }
  internal TeamName Team { get; }

  internal Piece(PieceDefinition definition, (int x, int y) position, TeamName team)
  {
    Definition = definition;
    CurrentHealth = Definition.Health;
    Position = position;
    Team = team;
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

  internal PieceDefinition(
    PieceType name,
    PieceCategory category,
    (int range, Shape shape) movement,
    int attack,
    int health,
    (int x, int y) size,
    (int range, Shape shape) attackShape,
    int cost
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
  }

}

internal enum Shape
{
  Any, Straight, Forward,
  AbsoluteStraightOrDiagonal,
  ForwardOrForwardDiagonal,
  FourSquare, PierceStraight,
  MoveOnEnemy, None
}

internal enum PieceType
{
  Soldier, Defender, Archer, Scout, Spearman,
  Peasant, Knight, Crossbowman, Cavalier, Chariot,
  Cannon, Spy, Catapult, FieldHospital,
  Ambulance, Teacher, Ox, Engineer, Ballista,
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
    10,
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
    (2, Shape.Any),
    25
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
    10,
    (1, 1),
    (2, Shape.Forward),
    20
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
    40
  );

  internal static readonly PieceDefinition Crossbowman = new(
    PieceType.Crossbowman,
    PieceCategory.Ranged,
    (2, Shape.Any),
    20,
    20,
    (1, 1),
    (3, Shape.Any),
    40
  );

  internal static readonly PieceDefinition Cavalier = new(
    PieceType.Cavalier,
    PieceCategory.Melee,
    (4, Shape.Any),
    15,
    25,
    (1, 1),
    (2, Shape.Any),
    40
  );

  internal static readonly PieceDefinition Chariot = new(
    PieceType.Chariot,
    PieceCategory.Melee,
    (4, Shape.Straight),
    15,
    20,
    (1, 1),
    (1, Shape.Any),
    35
  );

  internal static readonly PieceDefinition Cannon = new(
    PieceType.Cannon,
    PieceCategory.Mechanical,
    (2, Shape.AbsoluteStraightOrDiagonal),
    25,
    25,
    (1, 2),
    (4, Shape.Any),
    40
  );

  internal static readonly PieceDefinition Spy = new(
    PieceType.Spy,
    PieceCategory.Intelligence,
    (5, Shape.Any),
    0,
    20,
    (1, 1),
    (0, Shape.None),
    35
  );

  internal static readonly PieceDefinition Catapult = new(
    PieceType.Catapult,
    PieceCategory.Mechanical,
    (1, Shape.Any),
    30,
    15,
    (2, 2),
    (6, Shape.FourSquare),
    50
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

  internal static readonly PieceDefinition Teacher = new(
    PieceType.Teacher,
    PieceCategory.Intelligence,
    (4, Shape.Any),
    0,
    5,
    (1, 1),
    (0, Shape.None),
    30
  );

  internal static readonly PieceDefinition Ox = new(
    PieceType.Ox,
    PieceCategory.Transport,
    (4, Shape.Any),
    10,
    10,
    (1, 1),
    (2, Shape.Forward),
    40
  );

  internal static readonly PieceDefinition Engineer = new(
    PieceType.Engineer,
    PieceCategory.Intelligence,
    (3, Shape.Any),
    0,
    15,
    (1, 1),
    (0, Shape.None),
    45
  );

  internal static readonly PieceDefinition Ballista = new(
    PieceType.Ballista,
    PieceCategory.Mechanical,
    (1, Shape.Straight),
    20,
    15,
    (2, 2),
    (5, Shape.PierceStraight),
    55
  );

  internal static readonly PieceDefinition Elephant = new(
    PieceType.Elephant,
    PieceCategory.Melee,
    (2, Shape.Straight),
    10,
    45,
    (2, 2),
    (1, Shape.MoveOnEnemy),
    55
  );

  internal static readonly PieceDefinition Guard = new(
    PieceType.Guard,
    PieceCategory.Melee,
    (3, Shape.Any),
    10,
    20,
    (1, 1),
    (1, Shape.Any),
    25
  );

  internal static readonly PieceDefinition Mercenary = new(
    PieceType.Mercenary,
    PieceCategory.Melee,
    (3, Shape.Any),
    25,
    20,
    (1, 1),
    (1, Shape.Any),
    20
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
    0
  );

  internal static readonly PieceDefinition Palace = new(
    PieceType.Palace,
    PieceCategory.Royal,
    (0, Shape.None),
    0,
    160,
    (2, 2),
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
}
