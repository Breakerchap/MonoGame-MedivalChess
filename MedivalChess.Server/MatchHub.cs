using System.Collections.Concurrent;
using MedivalChess.Shared;
using Microsoft.AspNetCore.SignalR;

namespace MedivalChess.Server;

public sealed class MatchHub(MatchStore matches) : Hub
{
  public async Task<RoomJoinResult> CreateGame(CreateGameRequest request)
  {
    RoomJoinResult result = matches.Create(Context.ConnectionId, request);
    if (result.Accepted && result.JoinCode is not null)
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, result.JoinCode);
    }

    return result;
  }

  public async Task<RoomJoinResult> JoinGame(JoinGameRequest request)
  {
    RoomJoinResult result = matches.Join(Context.ConnectionId, request.JoinCode);
    if (result.Accepted && result.JoinCode is not null && result.State is not null)
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, result.JoinCode);
      await Clients.Group(result.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> AttemptMove(MoveRequest request)
  {
    ActionResult result = matches.TryMove(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }

  public async Task<ActionResult> ChooseRoyal(RoyalSelectionRequest request)
  {
    ActionResult result = matches.ChooseRoyal(Context.ConnectionId, request);
    if (result.Accepted && result.State is not null)
    {
      await Clients.Group(result.State.JoinCode).SendAsync("StateUpdated", result.State);
    }

    return result;
  }
}

public sealed class MatchStore
{
  private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  private readonly ConcurrentDictionary<string, Match> _matches = new(StringComparer.OrdinalIgnoreCase);

  public RoomJoinResult Create(string connectionId, CreateGameRequest request)
  {
    string code;
    NetworkTeam hostTeam = Random.Shared.Next(2) == 0 ? NetworkTeam.Red : NetworkTeam.Blue;
    do
    {
      code = string.Concat(Enumerable.Range(0, 5).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]));
    } while (!_matches.TryAdd(code, new Match(code, connectionId, hostTeam, request.Pieces)));

    Match match = _matches[code];
    return new(true, null, code, hostTeam, match.State());
  }

  public RoomJoinResult Join(string connectionId, string joinCode)
  {
    if (!_matches.TryGetValue(joinCode.Trim().ToUpperInvariant(), out Match? match))
    {
      return new(false, "Room not found.", null, null, null);
    }

    lock (match.Sync)
    {
      if (match.BlueConnectionId is not null)
      {
        return new(false, "Room already has two players.", null, null, null);
      }

      match.BlueConnectionId = connectionId;
      return new(true, null, match.Code, match.HostTeam == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red, match.State());
    }
  }

  public ActionResult TryMove(string connectionId, MoveRequest request)
  {
    Match? match = _matches.Values.FirstOrDefault(candidate => candidate.Contains(connectionId));
    if (match is null)
    {
      return new(false, "Join a room first.", null);
    }

    lock (match.Sync)
    {
      NetworkTeam? player = match.TeamFor(connectionId);
      if (!match.MatchReady)
      {
        return new(false, "Both players must choose a royal first.", match.State());
      }
      if (player is null || player != match.CurrentTurn)
      {
        return new(false, "It is not your turn.", match.State());
      }

      int index = match.Pieces.FindIndex(piece => piece.Id == request.PieceId);
      if (index < 0 || match.Pieces[index].Team != player)
      {
        return new(false, "You may only move your own pieces.", match.State());
      }

      NetworkPiece piece = match.Pieces[index];
      if (match.Pieces.Any(other => other.Id != piece.Id && other.X == request.ToX && other.Y == request.ToY))
      {
        return new(false, "That square is occupied.", match.State());
      }

      if (!NetworkMovementRules.IsLegal(piece, request.ToX, request.ToY))
      {
        return new(false, "That move is outside the unit's movement rule.", match.State());
      }

      match.Pieces[index] = piece with { X = request.ToX, Y = request.ToY };
      match.CurrentTurn = match.CurrentTurn == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red;
      match.Version++;
      return new(true, null, match.State());
    }
  }

  public ActionResult ChooseRoyal(string connectionId, RoyalSelectionRequest request)
  {
    Match? match = _matches.Values.FirstOrDefault(candidate => candidate.Contains(connectionId));
    if (match is null)
    {
      return new(false, "Join a room first.", null);
    }

    lock (match.Sync)
    {
      NetworkTeam? team = match.TeamFor(connectionId);
      if (team is null || match.BlueConnectionId is null)
      {
        return new(false, "Wait for the other player before choosing a royal.", match.State());
      }

      if (match.Pieces.Any(piece => piece.Team == team && RoyalTypes.Contains(piece.Type)))
      {
        return new(false, "You have already chosen your royal.", match.State());
      }

      if (!RoyalTypes.Contains(request.RoyalType))
      {
        return new(false, "That is not a valid royal.", match.State());
      }

      (int width, int height) = request.RoyalType == "Palace" ? (3, 2) : (1, 1);
      int x = (21 - width) / 2;
      int y = team == NetworkTeam.Red ? 25 - height : 0;
      int health = request.RoyalType switch
      {
        "King" => 120,
        "Princess" => 80,
        "Palace" => 160,
        "Baron" => 100,
        "Emissary" => 80,
        _ => 1
      };
      match.Pieces.Add(new NetworkPiece(Guid.NewGuid().ToString("N"), request.RoyalType, team.Value, x, y, health));
      match.Version++;
      return new(true, null, match.State());
    }
  }

  private static readonly HashSet<string> RoyalTypes = new(StringComparer.Ordinal)
  {
    "King", "Princess", "Palace", "Baron", "Emissary"
  };

  private sealed class Match(string code, string hostConnectionId, NetworkTeam hostTeam, IReadOnlyList<NetworkPiece> pieces)
  {
    internal object Sync { get; } = new();
    internal string Code { get; } = code;
    internal string HostConnectionId { get; } = hostConnectionId;
    internal NetworkTeam HostTeam { get; } = hostTeam;
    internal string? BlueConnectionId { get; set; }
    internal List<NetworkPiece> Pieces { get; } = [.. pieces];
    internal NetworkTeam CurrentTurn { get; set; } = NetworkTeam.Red;
    internal long Version { get; set; }
    internal bool Contains(string connectionId) => HostConnectionId == connectionId || BlueConnectionId == connectionId;
    internal NetworkTeam? TeamFor(string connectionId) => HostConnectionId == connectionId ? HostTeam : BlueConnectionId == connectionId ? (HostTeam == NetworkTeam.Red ? NetworkTeam.Blue : NetworkTeam.Red) : null;
    internal bool MatchReady => BlueConnectionId is not null &&
      Pieces.Any(piece => piece.Team == NetworkTeam.Red && RoyalTypes.Contains(piece.Type)) &&
      Pieces.Any(piece => piece.Team == NetworkTeam.Blue && RoyalTypes.Contains(piece.Type));
    internal NetworkGameState State() => new(Code, CurrentTurn, Pieces.ToArray(), Version, BlueConnectionId is null ? 1 : 2, MatchReady);
  }
}

internal static class NetworkMovementRules
{
  internal static bool IsLegal(NetworkPiece piece, int toX, int toY)
  {
    int dx = Math.Abs(toX - piece.X);
    int dy = Math.Abs(toY - piece.Y);
    if (dx == 0 && dy == 0)
    {
      return false;
    }

    (int range, bool straight) = piece.Type switch
    {
      "Soldier" or "Defender" => (2, true),
      "Chariot" or "Cannon" or "Ballista" => (piece.Type == "Ballista" ? 1 : 2, true),
      "Peasant" => (1, true),
      "Knight" => (3, false),
      "Cavalier" => (4, false),
      "Scout" or "Spy" or "Ambulance" => (piece.Type == "Scout" ? 4 : 5, false),
      "Teacher" => (4, false),
      "Engineer" => (3, false),
      "Elephant" => (2, true),
      "Emissary" => (3, false),
      "Palace" => (0, false),
      _ => (2, false)
    };
    return straight ? (dx == 0 || dy == 0) && dx + dy <= range : Math.Max(dx, dy) <= range;
  }
}
