# Crown & Siege

**Crown & Siege** is a turn-based strategy game built with C# and MonoGame. It takes the basic idea of chess-like board combat and expands it with different unit types, asymmetric abilities, terrain, an economy, multiple armies and royals, CPU opponents, and online multiplayer.

> **Status:** Crown & Siege is still in active development. Rules, balance, UI and compatibility may change frequently.

## Features

* Turn-based tactical combat on grid-based battlefields
* Large selection of units with different:

  * movement patterns
  * attack ranges
  * health and damage
  * sizes
  * costs
  * special abilities
* Multiple unit packs, including:

  * Base
  * Dynasty
  * Fantasy
  * Undead
  * Greek
  * Norse
  * Modern
  * Wild West
  * Chess
* Different royals with unique playstyles
* Terrain and multiple battlefield sizes
* Economy and unit purchasing
* 2–4 player local multiplayer
* Online multiplayer using a SignalR match server
* CPU opponents
* Campaign/custom-level support
* Built-in level editor
* Desktop support for Windows, Linux and macOS
* Android client with touch controls

## Requirements

For desktop development/building:

* [Git](https://git-scm.com/)
* [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

MonoGame dependencies are restored automatically through NuGet and the repository's local .NET tools.

## Quick Start

Clone the repository:

```bash
git clone https://github.com/Breakerchap/MonoGame-MedivalChess.git
cd MonoGame-MedivalChess
```

Restore dependencies and MonoGame tools:

```bash
dotnet tool restore
dotnet restore
```

Run the desktop game:

```bash
dotnet run --project MedivalChess.csproj
```

## Building

Build the desktop game:

```bash
dotnet build MedivalChess.csproj -c Release
```

Or build the main solution, including the game, shared rules, CPU, server and tests:

```bash
dotnet build CrownAndSiege.sln -c Release
```

### Self-contained desktop builds

Self-contained builds include the .NET runtime and can therefore be run on a compatible machine without separately installing .NET.

| Platform            | Runtime       | Command                                                                                                     |
| ------------------- | ------------- | ----------------------------------------------------------------------------------------------------------- |
| Windows x64         | `win-x64`     | `dotnet publish MedivalChess.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64`         |
| Linux x64           | `linux-x64`   | `dotnet publish MedivalChess.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64`     |
| Linux ARM64         | `linux-arm64` | `dotnet publish MedivalChess.csproj -c Release -r linux-arm64 --self-contained true -o publish/linux-arm64` |
| macOS Intel         | `osx-x64`     | `dotnet publish MedivalChess.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64`         |
| macOS Apple silicon | `osx-arm64`   | `dotnet publish MedivalChess.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64`     |

On Linux/macOS:

```bash
chmod +x publish/<runtime>/MedivalChess
./publish/<runtime>/MedivalChess
```

On Windows:

```text
publish\win-x64\MedivalChess.exe
```

If MonoGame content ever needs to be built manually:

```bash
dotnet mgcb Content/Content.mgcb
```

## Helper Scripts

The repository also contains update/build scripts for several desktop platforms:

```text
updateAndBuildMedivalChessWin11.bat
updateAndBuildMedivalChessLinuxIntel.sh
updateAndBuildMedivalChessMacIntel.sh
```

These are useful for quickly updating a local copy and rebuilding the game.

## Android

The Android project is located in:

```text
MedivalChess.Android/
```

It targets **Android 6.0 / API 23 or newer**.

Install the Android workload:

```bash
dotnet workload install android
```

Then restore and build:

```bash
dotnet tool restore
dotnet restore MedivalChess.Android/MedivalChess.Android.csproj

dotnet build MedivalChess.Android/MedivalChess.Android.csproj \
  -c Release \
  -f net9.0-android \
  -t:SignAndroidPackage \
  -p:AndroidPackageFormats=apk
```

The resulting APK is placed under:

```text
MedivalChess.Android/bin/Release/net9.0-android/
```

### Android Controls

* **Tap** — normal/left-click action
* **Long press** — secondary/right-click action, including attacks and special actions
* **One-finger drag** — pan the battlefield
* **Pinch** — zoom
* **Two-finger drag** — pan while zooming

The Android version shares most gameplay and UI code with the desktop game.

Some text-entry screens currently still work best with a physical or Bluetooth keyboard.

## Online Multiplayer

Online multiplayer uses a separate ASP.NET Core/SignalR server contained in:

```text
MedivalChess.Server/
```

Run a local server with:

```bash
dotnet run --project MedivalChess.Server/MedivalChess.Server.csproj
```

The repository also includes:

```text
render.yaml
MedivalChess.Server/Dockerfile
```

which can be used to deploy the server to services such as Render.

When connecting from the game, enter the server's base URL. The client handles the SignalR `/gamehub` path itself.

Online matches support **2–4 players**.

The server currently stores matches in memory, so restarting or redeploying the server clears active rooms.

## Terrain Painter

A separate Windows Forms tool is included for creating/editing battlefield terrain:

```text
TerrainPainter/
```

Run it on Windows with:

```bash
dotnet run --project TerrainPainter/TerrainPainter.csproj
```

Terrain files use the `.mctrn` format and are stored under:

```text
GameBoard/BoardTerrains/
```

## Tests

Run the automated test suite with:

```bash
dotnet test MedivalChess.Tests/MedivalChess.Tests.csproj
```

The tests cover areas including CPU behaviour, game rules, campaign levels, economy, movement shapes and other gameplay systems.

## Repository

Source code:

https://github.com/Breakerchap/MonoGame-MedivalChess

The repository and project names still use the historical `MedivalChess` spelling internally, while the game is called **Crown & Siege**.
