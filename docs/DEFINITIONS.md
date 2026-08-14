# Crown & Seige — Internal Definitions & Terminology

This document defines the **canonical terminology** used when discussing, documenting and implementing Crown & Seige.

Its purpose is to avoid ambiguity between game-design terminology, code names and ordinary meanings of words such as *adjacent*, *straight*, *range* and *forward*.

When adding rules, abilities, documentation or comments, follow these definitions unless a mechanic explicitly states otherwise.

---

# Naming

## Crown & Seige

**Crown & Seige** is the name of the game.

Use this in player-facing text, documentation and discussion of the game itself.

## MedivalChess

`MedivalChess` is the historical/internal project name.

Note the spelling:

```text
MedivalChess
```

not:

```text
MedievalChess
```

Existing namespaces, project names, directories and code identifiers may continue to use `MedivalChess`.

Conceptually:

```text
MedivalChess → Crown & Seige
```

Do not casually rename existing `MedivalChess` namespaces/projects purely for spelling or branding reasons, as other projects and build scripts may depend on them.

---

# Distance Systems

Crown & Seige uses three main kinds of distance.

## Square / Chessboard Distance

Also known mathematically as **Chebyshev distance**.

```text
distance = max(|Δx|, |Δy|)
```

A square of radius `r` contains every square whose chessboard distance is at most `r`.

For example, radius 2:

```text
XXXXX
XXXXX
XXOXX
XXXXX
XXXXX
```

`O` is the origin.

Diagonal movement costs the same distance as orthogonal movement.

Examples from `(0, 0)`:

```text
(2, 0) → distance 2
(2, 1) → distance 2
(2, 2) → distance 2
```

In the code, this geometry is generally represented by:

```text
Any
```

Therefore:

```text
Any → Square
```

Use **Square** when describing the geometry itself.

Use **Any** when referring specifically to the code `Shape.Any` / `RuleShape.Any`.

---

## Diamond / Taxicab Distance

Also known as **Manhattan distance**.

```text
distance = |Δx| + |Δy|
```

A radius forms a diamond on the board.

For example, radius 2:

```text
..X..
.XXX.
XXOXX
.XXX.
..X..
```

Examples from `(0, 0)`:

```text
(2, 0) → distance 2
(1, 1) → distance 2
(2, 1) → distance 3
(2, 2) → distance 4
```

In the code, this geometry is represented by:

```text
Straight
```

Therefore:

```text
Straight → Diamond
```

Use **Diamond** when describing the geometry itself.

Use **Straight** when referring specifically to the code `Shape.Straight` / `RuleShape.Straight`.

Importantly, **Straight does not mean "must travel in a straight line"**.

A `Straight` range is a taxicab-distance diamond.

For an actual straight orthogonal line, use `Line`.

---

## Circle / Euclidean Distance

Uses ordinary Euclidean distance:

```text
distance = √(Δx² + Δy²)
```

A square is included when its coordinate falls within the specified Euclidean radius.

Examples from `(0, 0)`:

```text
(3, 0) → distance 3
(2, 2) → distance √8 ≈ 2.83
(3, 3) → distance √18 ≈ 4.24
```

In code:

```text
Circle
```

means:

```text
Euclidean circle
```

This is distinct from both `Any`/Square and `Straight`/Diamond.

---

# Shape Definitions

## `Any`

Geometry:

```text
Square
```

Distance:

```text
Chessboard / Chebyshev distance
```

Within range, any combination of horizontal, vertical and diagonal displacement is allowed.

---

## `Straight`

Geometry:

```text
Diamond
```

Distance:

```text
Taxicab / Manhattan distance
```

Despite the code name, this does **not** mean an orthogonal straight line.

---

## `Circle`

Geometry:

```text
Circle
```

Distance:

```text
Euclidean distance
```

---

## `Line`

A single **orthogonal line**.

Valid directions:

```text
↑
↓
←
→
```

The movement/attack may not turn.

Examples:

```text
(0, 3)  ✓
(-4, 0) ✓
(2, 2)  ✗
(2, 1)  ✗
```

---

## `Diagonal`

A diagonal line.

Valid directions:

```text
↖ ↗
↙ ↘
```

The absolute X and Y displacement must be equal:

```text
|Δx| = |Δy|
```

---

## `LineOrDiagonal`

A straight chess-style line in any of the eight directions:

```text
↖ ↑ ↗
←   →
↙ ↓ ↘
```

Equivalent geometrically to the movement lines of a chess Queen.

It does **not** include arbitrary squares within the surrounding Square.

For example:

```text
(3, 0) ✓
(3, 3) ✓
(3, 2) ✗
```

---

## `ChessKnight`

The standard chess Knight offsets:

```text
(±1, ±2)
(±2, ±1)
```

---

## `Forward`

Moves or attacks directly in the unit's team's forward direction.

It does not automatically include forward diagonals.

---

## `ForwardLine`

A line extending directly forward.

Unlike a general `Line`, only the team's forward direction is valid.

---

## `ForwardOrForwardDiagonal`

Includes:

* directly forward
* forward-left
* forward-right

"Left" and "right" are relative to the team's facing direction.

---

## `AbsoluteStraightOrDiagonal`

An orthogonal or diagonal line.

Use this where the mechanic is based on absolute board directions rather than a team's facing.

---

## `PierceStraight`

A straight orthogonal line used for piercing behaviour.

Geometrically it follows `Line`:

```text
↑
↓
←
→
```

The distinction is in how pieces/targets along the attack are handled rather than the underlying direction.

---

## `MoveOnEnemy`

Special movement behaviour involving enemy-occupied squares.

Its basic reachable geometry is based on `Any`/Square unless the relevant ability adds further restrictions.

---

## `None`

No movement/attack geometry.

Used by units which cannot perform the corresponding action.

---

# Adjacency

These words have specific meanings.

## Adjacent

**Adjacent means all eight surrounding squares.**

```text
XXX
XOX
XXX
```

Therefore adjacent includes:

* directly above
* directly below
* directly left
* directly right
* all four diagonals

In coordinate terms:

```text
|Δx| ≤ 1
|Δy| ≤ 1
```

excluding:

```text
Δx = 0 and Δy = 0
```

---

## Directly Adjacent

**Directly adjacent means only the four orthogonal neighbouring squares.**

```text
.X.
XOX
.X.
```

These are:

```text
↑
↓
←
→
```

Equivalently:

```text
|Δx| + |Δy| = 1
```

---

## Diagonally Adjacent

Only the four diagonal neighbouring squares:

```text
X.X
.O.
X.X
```

These satisfy:

```text
|Δx| = 1
|Δy| = 1
```

---

# Range

Ranges are **inclusive**.

For example:

```text
Attack Range: 2–4
```

means distances:

```text
2
3
4
```

are permitted.

Distance 1 is not.

Distance 5 is not.

Internally this is represented as a minimum and maximum range.

---

## Single-number ranges

A range such as:

```text
3
```

normally means:

```text
1–3
```

unless the mechanic explicitly defines a different minimum.

---

## Range and shape are separate

A shape tells you **how distance/direction is measured**.

A range tells you **how far that shape extends**.

For example:

```text
3 Any
```

means:

```text
Square radius 3
```

while:

```text
3 Straight
```

means:

```text
Diamond radius 3
```

and:

```text
3 Line
```

means:

```text
up to 3 squares in one orthogonal line
```

These are three different areas.

---

# Movement vs Attack Geometry

The same shape names may be used for both movement and attacks.

For example:

```text
Movement: 3 Straight
```

means the movement's basic range is a taxicab-distance Diamond.

```text
Attack: 1–3 Any
```

means the attack covers the Square region between chessboard distances 1 and 3.

Other rules — terrain, pieces blocking paths, special abilities, team direction, etc. — may further restrict what is actually legal.

The shape defines the **base geometry**, not every rule governing the action.

---

# Forward

`Forward` is relative to the team, not the screen in general.

Canonical directions are:

```text
Red    → Up
Blue   → Down
Green  → Right
Yellow → Left
```

Using board coordinates:

```text
Red    → ( 0, -1)
Blue   → ( 0,  1)
Green  → ( 1,  0)
Yellow → (-1,  0)
```

Therefore words such as:

```text
forward
forward-left
forward-right
behind
```

must always be interpreted relative to the piece's team.

---

# Directions

## Orthogonal

One of:

```text
↑ ↓ ← →
```

No diagonal component.

---

## Diagonal

One of:

```text
↖ ↗ ↙ ↘
```

Both coordinates change by the same absolute amount when moving along a diagonal line.

---

## Eight Directions

Collectively:

```text
↖ ↑ ↗
←   →
↙ ↓ ↘
```

These are the eight directions surrounding a square.

---

# Pieces and Units

## Unit

General term for a controllable game entity.

This may include ordinary soldiers, royals, mechanical units, structures and other specialised pieces depending on context.

## Piece

Often interchangeable with **unit**, particularly within the code.

`PieceDefinition` is the canonical definition of a unit's statistics and basic properties.

---

# Piece Size

A unit's size is written:

```text
width × height
```

For example:

```text
1×1
1×2
2×2
3×2
```

A multi-square unit occupies its entire rectangular footprint.

Collision, targeting and range calculations involving larger units may consider the relevant occupied squares rather than treating the unit as a single point.

---

# Royal

A **Royal** is the unit or entity whose survival is tied to the player's royal/win-condition mechanics.

Do not assume "Royal" means specifically a King.

Different packs may use completely different Royal units and Royal abilities.

---

# Pack

A **Pack** is a group of units associated with a particular theme or ruleset.

Current code-level pack names include:

```text
Base
Dynasty
Fantasy
Undead
Greek
Norse
Modern
WildWest
Chess
```

Use the actual pack definition rather than inferring a unit's pack purely from its historical/theme inspiration.

---

# Damage, Attack and Health

## Attack

The base amount of damage an attack deals before applicable modifiers.

## Health

The amount of damage a unit can sustain according to its rules before death/destruction.

## Damage

The actual health reduction caused after relevant abilities and modifiers are applied.

Therefore:

```text
Attack ≠ necessarily final Damage
```

---

# Ability Wording

Ability text should use the terminology in this document consistently.

Prefer:

```text
adjacent
directly adjacent
diagonally adjacent
Square
Diamond
Circle
orthogonal
diagonal
forward
```

rather than ambiguous alternatives.

For example, avoid:

```text
nearby units
units next to it
straight-shaped range
all units around it
```

when a more precise defined term exists.

---

# Canonical Examples

## Adjacent allies

> Adjacent allies gain +10 damage.

Means allies in **any of the eight surrounding squares**.

---

## Directly adjacent enemies

> Deals 20 damage to directly adjacent enemies.

Means only enemies:

```text
above
below
left
right
```

not diagonals.

---

## Movement 3 Any

Game-design terminology:

```text
Movement: 3 Square
```

Code:

```text
(3, Shape.Any)
```

Distance:

```text
max(|Δx|, |Δy|) ≤ 3
```

---

## Movement 3 Straight

Game-design terminology:

```text
Movement: 3 Diamond
```

Code:

```text
(3, Shape.Straight)
```

Distance:

```text
|Δx| + |Δy| ≤ 3
```

---

## Attack 2–4 Circle

Game-design terminology:

```text
Attack Range: 2–4 Circle
```

Distance:

```text
2 ≤ √(Δx² + Δy²) ≤ 4
```

---

## Attack 1–4 Line

Means an attack may travel one to four squares:

```text
up
down
left
right
```

but not diagonally and not with a turn.

---

# Quick Reference

| Code / Term           | Canonical Meaning                           |
| --------------------- | ------------------------------------------- |
| `Any`                 | Square                                      |
| Square                | Chessboard / Chebyshev distance             |
| `Straight`            | Diamond                                     |
| Diamond               | Taxicab / Manhattan distance                |
| `Circle`              | Euclidean distance                          |
| `Line`                | Single orthogonal line                      |
| `Diagonal`            | Single diagonal line                        |
| `LineOrDiagonal`      | Single orthogonal or diagonal line          |
| `ChessKnight`         | Chess Knight movement                       |
| `Adjacent`            | Any of the 8 surrounding squares            |
| `Directly adjacent`   | Any of the 4 orthogonal surrounding squares |
| `Diagonally adjacent` | Any of the 4 diagonal surrounding squares   |
| `Forward`             | Relative to the piece's team                |
| Range `a–b`           | Inclusive from `a` through `b`              |
| `MedivalChess`        | Historical/internal code/project name       |
| Crown & Seige         | Public/game name                            |

---

# Rule of Thumb

When discussing geometry:

```text
Any      → Square
Straight → Diamond
Circle   → Circle
Line     → Orthogonal line
```

When discussing neighbours:

```text
Adjacent            → 8
Directly adjacent   → 4 orthogonal
Diagonally adjacent → 4 diagonal
```

When discussing names:

```text
Internal/code → MedivalChess
Game/public   → Crown & Seige
```

These definitions should be treated as the default meaning everywhere in the project unless a specific mechanic explicitly overrides them.
