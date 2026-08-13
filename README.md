### Piece Shapes

- `Straight`: Any square within the range measured by taxicab (Manhattan) distance.
- `Line`: One or more squares in a single orthogonal straight line; it cannot turn.
- `Diagonal`: One directly adjacent diagonal square.
- `Any`: Any square within the range measured by chessboard distance.
- `Forward`: In the direction of the opposing player.
- `Absolute`: Pick one; can't mix and match.

Attack ranges are inclusive. Define a unit's range as `(minimum, maximum)`: `(1, 3)` allows one to three squares, and `(2, 4)` allows two to four.

## Host the match server on Render

The repository includes a Render Blueprint (`render.yaml`) and Dockerfile, so the server can be deployed without changing code.

1. Push this project to a GitHub repository.
2. In [Render](https://dashboard.render.com/), choose **New** > **Blueprint** and connect that repository.
3. Render detects `render.yaml`. Create the `crown-and-siege-server` web service and wait for the deploy to finish.
4. Open the service and copy its public URL, for example `https://crown-and-siege-server.onrender.com`. Visiting `https://your-url/health` should return a small healthy response.
5. In the game, open **Online Multiplayer** and enter that exact URL in the **Match Server URL** field. Do not add `/gamehub`; the game adds it itself. Click either field and press **Ctrl+V** to paste. The host chooses the game mode, player count (2–4), battlefield, terrain, and economy in the in-game setup screens before the room is created. Every joining player enters the same URL and five-character room code; the match begins setup once all configured seats are filled.

## Two to four players

Local and online matches support 2, 3, or 4 players. Orange and Purple start from the bottom and top edges; Green and Gold enter from the left and right edges. Each side has its own turn, economy, opening purchases, royal, and forward-facing unit direction.

Render provides HTTPS and supports WebSocket connections, which SignalR uses. No environment variables are required. The client automatically reconnects to the same room and side after a short connection interruption; disconnected rooms are retained for five minutes, while abandoned rooms are cleaned up in the background.

The free Render plan can spin down when idle, so the first connection after a quiet period may take a short while. Matches are stored only in server memory: a server restart or redeploy clears open rooms and matches.

## Build the game

Install the .NET 9 SDK for the architecture you are building on, then restore the local tools and project dependencies from the repository root:

```bash
dotnet tool restore
dotnet restore
```

Publish a self-contained release for the target platform. Each command places the packaged game in `publish/<runtime>`:

| Platform                 | Runtime identifier | Command                                                                                                     |
| ------------------------ | ------------------ | ----------------------------------------------------------------------------------------------------------- |
| Linux Intel/AMD 64-bit   | `linux-x64`        | `dotnet publish MedivalChess.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64`     |
| Linux ARM 64-bit         | `linux-arm64`      | `dotnet publish MedivalChess.csproj -c Release -r linux-arm64 --self-contained true -o publish/linux-arm64` |
| macOS Intel              | `osx-x64`          | `dotnet publish MedivalChess.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64`         |
| macOS Apple silicon      | `osx-arm64`        | `dotnet publish MedivalChess.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64`     |
| Windows Intel/AMD 64-bit | `win-x64`          | `dotnet publish MedivalChess.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64`         |

On Linux and macOS, make the published executable runnable before starting it:

```bash
chmod +x publish/<runtime>/MedivalChess
./publish/<runtime>/MedivalChess
```

The game prints startup diagnostics to the terminal. It also appends diagnostics
and any crash stack trace to `~/Library/Application Support/CrownAndSiege/startup.log`
on macOS, which is useful when launching the executable from Finder or when no
terminal remains open.

On Windows, run `publish/win-x64/MedivalChess.exe`. If MonoGame content has not already been built as part of publishing, build it with:

```bash
dotnet mgcb Content/Content.mgcb
```

## Android

The Android client is in `MedivalChess.Android` and targets Android 6.0 (API 23) or newer. Install the .NET 9 Android workload once, then restore and build a signed APK from the repository root:

```bash
dotnet tool restore
dotnet workload install android
dotnet restore MedivalChess.Android/MedivalChess.Android.csproj
dotnet build MedivalChess.Android/MedivalChess.Android.csproj -c Release -f net9.0-android -t:SignAndroidPackage -p:AndroidPackageFormats=apk
```

`SignAndroidPackage` produces a `*-Signed.apk` under `MedivalChess.Android/bin/Release/net9.0-android/`. Without a release keystore configured, .NET for Android uses a generated debug-signing key, which is suitable for local sideload/testing but not a store release. To embed the current Git commit so the app can tell whether `master` has moved on, add `-p:AndroidBuildCommit=<commit-sha>` to the build command.

Android uses the same gameplay/UI code as desktop. Touch controls are:

- Tap: normal/left-click action (select, move, buy, buttons).
- Long press: secondary/right-click action (attack and special actions).
- One-finger drag: pan the battlefield.
- Two-finger pinch: zoom.
- Two-finger drag: pan while zooming.

The Android launcher uses a 720-pixel logical landscape height and preserves the device aspect ratio. This keeps the desktop UI layout responsive while making controls physically larger on high-DPI phones and tablets.

When the app starts with internet access it checks the latest `master` commit on GitHub without blocking startup. If the installed build embeds an older commit, Android offers to open the repository for the newer build. If the device is offline or GitHub cannot be reached, the check is skipped and the game starts normally. Android cannot safely replace a running installed APK with a literal `git pull`, so executable updates still require installing the newer APK.

Current Android caveat: gameplay and menu buttons are touch-enabled, but the existing text-entry screens still consume key states directly. Until the Android software-keyboard bridge is wired in, entering/editing server URLs, room codes and other free-text/numeric fields requires a hardware/Bluetooth keyboard.

### Run Terrain Painter:
` `