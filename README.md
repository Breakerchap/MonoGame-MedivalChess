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
5. On each game computer, set the `CROWN_SIEGE_SERVER_URL` environment variable to that exact URL before launching the game. Do not add `/gamehub`; the game adds it itself.

PowerShell example:

```powershell
$env:CROWN_SIEGE_SERVER_URL = "https://crown-and-siege-server.onrender.com"
dotnet run --project .\MedivalChess.csproj
```

For a permanent Windows user environment variable:

```powershell
[Environment]::SetEnvironmentVariable("CROWN_SIEGE_SERVER_URL", "https://crown-and-siege-server.onrender.com", "User")
```

Restart the game after setting the permanent value. Then use **Online Multiplayer** in the title screen: one player hosts and shares the five-character code; the other player joins with it. Render provides HTTPS and supports WebSocket connections, which SignalR uses.

The free Render plan can spin down when idle, so the first connection after a quiet period may take a short while. Matches are stored only in server memory: a server restart or redeploy clears open rooms and matches.
