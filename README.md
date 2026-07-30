### Piece Shapes
- `Straight`: Any directly adjacent square
- `Diagonal`: Any diagonally aajacent square
- `Any`: Staight && Diagonal
- `Forward`: In the direction of the opposing player
- `Absolute`: Pick one; can't mix and match

## Host the match server on Render

The repository includes a Render Blueprint (`render.yaml`) and Dockerfile, so the server can be deployed without changing code.

1. Push this project to a GitHub repository.
2. In [Render](https://dashboard.render.com/), choose **New** > **Blueprint** and connect that repository.
3. Render detects `render.yaml`. Create the `crown-and-siege-server` web service and wait for the deploy to finish.
4. Open the service and copy its public URL, for example `https://crown-and-siege-server.onrender.com`. Visiting `https://your-url/health` should return a small healthy response.
5. In the game, open **Online Multiplayer** and enter that exact URL in the **Match Server URL** field. Do not add `/gamehub`; the game adds it itself. Click either field and press **Ctrl+V** to paste. The host chooses the game mode, battlefield, terrain, and economy in the in-game setup screens before the room is created. The joining player enters the same URL and the five-character room code.

Render provides HTTPS and supports WebSocket connections, which SignalR uses. No environment variables are required. The client automatically reconnects to the same room and side after a short connection interruption; disconnected rooms are retained for five minutes, while abandoned rooms are cleaned up in the background.

The free Render plan can spin down when idle, so the first connection after a quiet period may take a short while. Matches are stored only in server memory: a server restart or redeploy clears open rooms and matches.

## Build the game

Install the .NET 9 SDK for the architecture you are building on, then restore the local tools and project dependencies from the repository root:

```bash
dotnet tool restore
dotnet restore
```

Publish a self-contained release for the target platform. Each command places the packaged game in `publish/<runtime>`:

| Platform | Runtime identifier | Command |
| --- | --- | --- |
| Linux Intel/AMD 64-bit | `linux-x64` | `dotnet publish MedivalChess.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64` |
| Linux ARM 64-bit | `linux-arm64` | `dotnet publish MedivalChess.csproj -c Release -r linux-arm64 --self-contained true -o publish/linux-arm64` |
| macOS Intel | `osx-x64` | `dotnet publish MedivalChess.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64` |
| macOS Apple silicon | `osx-arm64` | `dotnet publish MedivalChess.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64` |
| Windows Intel/AMD 64-bit | `win-x64` | `dotnet publish MedivalChess.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64` |

On Linux and macOS, make the published executable runnable before starting it:

```bash
chmod +x publish/<runtime>/MedivalChess
./publish/<runtime>/MedivalChess
```

On Windows, run `publish/win-x64/MedivalChess.exe`. If MonoGame content has not already been built as part of publishing, build it with:

```bash
dotnet mgcb Content/Content.mgcb
```
