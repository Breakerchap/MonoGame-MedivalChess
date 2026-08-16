# Crown & Siege — Contributor Guide

This document is intended for people working on **Crown & Siege** itself.

For general information, installation and normal build instructions, see the root [`README.md`](../README.md).

## Development Setup

You will normally need:

* Git
* .NET 9 SDK
* an IDE/editor with C# support
* the .NET Android workload if working on Android

From the repository root:

```bash
dotnet tool restore
dotnet restore
dotnet build CrownAndSiege.sln
dotnet test MedivalChess.Tests/MedivalChess.Tests.csproj
```

Run the desktop game with:

```bash
dotnet run --project MedivalChess.csproj
```

## Project Structure

```text
MonoGame-MedivalChess/
│
├── Game1.cs
├── Game1.UnitAbilities.cs
│
├── UiLayout.cs
├── UiRenderer.cs
├── UiText.cs
├── UiTheme.cs
├── PlatformInput.cs
├── OnlineMatchClient.cs
├── Debug.cs
├── Program.cs
│
├── Campaign/
├── GameBoard/
├── Player/
├── Content/
│
├── MedivalChess.Shared/
├── MedivalChess.CPU/
├── MedivalChess.Server/
├── MedivalChess.Tests/
├── MedivalChess.Android/
│
├── TerrainPainter/
├── tools/
│
├── MedivalChess.csproj
└── CrownAndSiege.sln
```

## Where Do I Change Something?

### Units, stats and packs

Start with:

```text
MedivalChess.Shared/Piece.cs
```

This contains core definitions including:

* `PieceType`
* `PieceCategory`
* `Pack`
* `Shape`
* `MovementDefinition`
* `AttackRange`
* `PieceDefinition`
* `PieceDefinitions`

If you are changing a unit's:

* movement
* attack
* health
* size
* range
* attack pattern
* cost
* pack
* displayed ability description

then `PieceDefinitions` is normally the first place to look.

Do **not** create another independent list of piece stats somewhere else unless there is a specific reason.

### Movement and attack shapes

Geometry for movement/attack shapes is primarily handled in:

```text
MedivalChess.Shared/ShapeGeometryRules.cs
```

This includes shapes such as:

```text
Any
Straight
Circle
Line
Diagonal
LineOrDiagonal
Forward
ForwardLine
...
```

If adding or changing a shape, also add/update tests in:

```text
MedivalChess.Tests/ShapeGeometryTests.cs
```

### Shared gameplay rules

Reusable rules that should not depend on rendering generally belong in:

```text
MedivalChess.Shared/
```

Important files include:

```text
AbilityAttackRules.cs
AbilityRules.cs
AbilityStateRules.cs
BoardRules.cs
CombatRules.cs
EconomyRules.cs
MatchRules.cs
PackRules.cs
ShapeGeometryRules.cs
Piece.cs
```

There are also shared campaign and networking models in this project.

When possible, put deterministic gameplay logic here rather than implementing the same rule separately for the player, CPU and server.

## Main Desktop Game

The desktop executable is:

```text
MedivalChess.csproj
```

The main MonoGame class is:

```text
Game1.cs
```

`Game1.cs` currently contains a large portion of:

* game state
* menu state
* input handling
* rendering flow
* match setup
* turn handling
* local gameplay
* transitions between screens

It is already a very large file. Prefer extracting reusable systems or adding appropriate partial files instead of making `Game1.cs` even larger where practical.

Some functionality has already been split into partial files:

```text
Game1.UnitAbilities.cs
```

## UI

Shared desktop/Android UI helpers are at the repository root:

```text
UiLayout.cs
UiRenderer.cs
UiText.cs
UiTheme.cs
```

Rough responsibilities:

* `UiLayout.cs` — positioning/layout calculations
* `UiRenderer.cs` — common UI drawing
* `UiText.cs` — text handling/helpers
* `UiTheme.cs` — common visual styling/theme values

General input abstraction is in:

```text
PlatformInput.cs
```

When adding UI, try to use the common helpers rather than hard-coding equivalent rendering in multiple screens.

Remember that much of this UI is also compiled into the Android client.

## Board and Terrain

Board-related files live in:

```text
GameBoard/
```

Important files include:

```text
Board.cs
BattlefieldTerrain.cs
MovementPathfinder.cs
Peice.cs
PieceControl.cs
```

The `Peice.cs` spelling is historical. Do not rename it casually because project files currently reference it directly.

Board layouts:

```text
GameBoard/board_small.json
GameBoard/board_medium.json
GameBoard/board_large.json
```

Terrain layouts:

```text
GameBoard/BoardTerrains/
```

Terrain files normally use:

```text
.mctrn
```

`Board.cs` and `BattlefieldTerrain.cs` are compiled through `MedivalChess.Shared`, so they can be used by systems other than the desktop frontend.

## Player and Team Logic

Player-specific setup/state code is under:

```text
Player/
```

Currently this includes:

```text
InitialBuyPhase.cs
Teams.cs
```

Use this area for logic specifically related to player/team setup rather than generic board or combat rules.

## Campaign and Level Editor

Runtime campaign/editor code lives in:

```text
Campaign/
```

Important files include:

```text
CampaignLevelConverter.cs
CampaignRuntimeFactory.cs
CampaignRuntimeObjectives.cs
CustomLevelBrowser.cs
LevelEditorScreen.cs
LevelEditorState.cs
LevelFilePicker.cs
```

Data structures and validation shared by other projects live in:

```text
MedivalChess.Shared/CampaignLevelDefinition.cs
MedivalChess.Shared/CampaignLevelSerializer.cs
MedivalChess.Shared/CampaignLevelValidator.cs
MedivalChess.Shared/CampaignTerritoryRules.cs
MedivalChess.Shared/CampaignUnitResolver.cs
```

A useful rule of thumb is:

* **UI/editor behaviour** → `Campaign/`
* **level format, validation and reusable rules** → `MedivalChess.Shared/`

## CPU Player

All major CPU-player/search logic lives in:

```text
MedivalChess.CPU/
```

This is intentionally separate from the MonoGame frontend.

Important areas include:

```text
CpuActionCandidates.cs
CpuActionGenerator.cs
CpuActions.cs
CpuArmyPlanner.cs
CpuGameRules.*.cs
Evaluation.cs
CpuScenarios.cs
```

There are also CPU design/reference documents such as:

```text
CPU_PLAYER_IMPLEMENTATION.md
Combos.md
```

When modifying CPU behaviour, do not only test whether it compiles. Run the CPU test suite and ideally play actual matches against it.

CPU-related tests make up a significant part of:

```text
MedivalChess.Tests/
```

including tests for:

* game-state simulation
* tactical safety
* action generation
* search behaviour
* search optimisation
* strategy heuristics
* planning efficiency
* special-unit strategies

## Online Multiplayer

The desktop/Android client-side network wrapper is:

```text
OnlineMatchClient.cs
```

Shared network messages and game-state types live in:

```text
MedivalChess.Shared/
```

The server is a separate ASP.NET Core project:

```text
MedivalChess.Server/
```

Important server files include:

```text
Program.cs
MatchHub.cs
MatchStore.*.cs
RoomCleanupService.cs
Dockerfile
```

`MatchHub.cs` handles SignalR communication and room/match operations.

The server also contains gameplay/state logic used to validate and execute online actions.

### Changing gameplay rules

Be careful when changing rules used during online play.

A change may affect:

1. the local game
2. shared rule code
3. CPU simulation
4. server-side match execution
5. network state/messages
6. tests

Where possible, move the actual rule into `MedivalChess.Shared` and have each system call that common implementation.

This reduces the chance that local play, CPU play and online play behave differently.

## Android

Android-specific code lives in:

```text
MedivalChess.Android/
```

The project references:

```text
MedivalChess.Shared
MedivalChess.CPU
```

and directly links much of the desktop source, including:

```text
Game1.cs
Debug.cs
OnlineMatchClient.cs
PlatformInput.cs
Ui*.cs
Campaign/**/*.cs
Player/**/*.cs
```

That means changes to the main UI/game code can affect **both desktop and Android**.

Android-specific platform behaviour should generally stay in `MedivalChess.Android`, while common gameplay/UI behaviour should remain shared.

Build Android with:

```bash
dotnet build MedivalChess.Android/MedivalChess.Android.csproj \
  -c Release \
  -f net9.0-android \
  -t:SignAndroidPackage \
  -p:AndroidPackageFormats=apk
```

The Android project is **not currently part of `CrownAndSiege.sln`**, so building the solution alone does not verify Android.

## Terrain Painter

The standalone terrain editor is in:

```text
TerrainPainter/
```

It is a Windows Forms `.NET 9` project and references `MedivalChess.Shared`.

Run it on Windows with:

```bash
dotnet run --project TerrainPainter/TerrainPainter.csproj
```

The Terrain Painter is also **not part of `CrownAndSiege.sln`**.

If you change terrain formats or board geometry, check both the main game and Terrain Painter.

## Content

MonoGame content is under:

```text
Content/
```

The content pipeline definition is:

```text
Content/Content.mgcb
```

Fonts/assets used by MonoGame should normally be registered there.

If necessary, build content manually with:

```bash
dotnet mgcb Content/Content.mgcb
```

## Tests

Tests live in:

```text
MedivalChess.Tests/
```

Run everything with:

```bash
dotnet test MedivalChess.Tests/MedivalChess.Tests.csproj
```

Or:

```bash
dotnet test CrownAndSiege.sln
```

When fixing a gameplay bug, add a regression test where reasonably possible.

Especially add tests when modifying:

* shapes
* movement
* combat
* economy
* special abilities
* campaign serialisation/validation
* CPU behaviour
* network-visible game rules

## Before Committing

At minimum:

```bash
dotnet build CrownAndSiege.sln
dotnet test MedivalChess.Tests/MedivalChess.Tests.csproj
```

If your change affects Android:

```bash
dotnet build MedivalChess.Android/MedivalChess.Android.csproj
```

If your change affects terrain or board formats, also build:

```bash
dotnet build TerrainPainter/TerrainPainter.csproj
```

where supported.

Then test the relevant behaviour in the actual game.

## Code Style

Follow the surrounding code.

In particular:

* use clear C# names rather than abbreviations for internal APIs
* keep gameplay rules deterministic where possible
* avoid duplicating rule logic between the client, CPU and server
* prefer reusable shared rules over UI-specific implementations
* add tests for behaviour changes
* keep platform-specific code separated from shared gameplay code
* avoid unrelated refactors in the same change as a gameplay/balance change

## Balance Changes vs Logic Changes

A **balance change** changes values such as:

```text
damage
health
movement range
cost
attack range
```

These should generally remain small and easy to review.

A **logic change** alters what a unit or system actually does.

Try not to bury major logic changes inside huge balance commits. Separating them makes regressions and balance problems much easier to track.

## Historical Naming

The game is called:

**Crown & Siege**

Several project names still use the older spelling:

```text
MedivalChess
```

Do not mass-rename namespaces, folders, projects or assemblies without treating it as a dedicated migration. Many project references and build scripts currently depend on these names.
