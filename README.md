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
5. In the game, open **Online Multiplayer** and enter that exact URL in the **Match Server URL** field. Do not add `/gamehub`; the game adds it itself. The joining player enters the same URL and the five-character room code.

Render provides HTTPS and supports WebSocket connections, which SignalR uses. No environment variables are required.

The free Render plan can spin down when idle, so the first connection after a quiet period may take a short while. Matches are stored only in server memory: a server restart or redeploy clears open rooms and matches.
