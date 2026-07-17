using System;

namespace MedivalChess.GameBoard;

class Piece
{
  public PieceType Type;
  public PieceCategory Category;
  public int Health;

  public Piece(PieceType type, PieceCategory category, int health)
  {
    Type = type;
    Category = category;
    Health = health;
  }
}

enum PieceType
{
  Soldier, Defender, Archer, Scout, Spearman, 
  Peasant, Knight, Crossbowman,Cavalier,	Chariot,
  Cannon, Spy, Catapult, Field, Hospital,
  Ambulance, Teacher, Ox, Engineer, Ballista, 
  Elephant, Guard, Mercenary, Assassin, 
  
  King, Princess, Palace, Baron,Emissary
}

enum PieceCategory
{
  Melee, Ranged, Intelligence,
  Mechanical, Structure, Transport,
  Royal
}