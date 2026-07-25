using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MedivalChess.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace MedivalChess;

internal sealed class OnlineMatchClient : IAsyncDisposable
{
  private readonly ConcurrentQueue<NetworkGameState> _pendingStates = new();
  private readonly HubConnection _connection;

  internal NetworkTeam? Team { get; private set; }
  internal string JoinCode { get; private set; }

  internal OnlineMatchClient(string serverUrl)
  {
    _connection = new HubConnectionBuilder()
      .WithUrl($"{serverUrl.TrimEnd('/')}/gamehub")
      .WithAutomaticReconnect()
      .Build();
    _connection.On<NetworkGameState>("StateUpdated", state => _pendingStates.Enqueue(state));
  }

  internal async Task<RoomJoinResult> HostAsync(CreateGameRequest request)
  {
    await _connection.StartAsync();
    RoomJoinResult result = await _connection.InvokeAsync<RoomJoinResult>("CreateGame", request);
    Accept(result);
    return result;
  }

  internal async Task<RoomJoinResult> JoinAsync(string joinCode)
  {
    await _connection.StartAsync();
    RoomJoinResult result = await _connection.InvokeAsync<RoomJoinResult>("JoinGame", new JoinGameRequest(joinCode));
    Accept(result);
    return result;
  }

  internal async Task<ActionResult> MoveAsync(string pieceId, int x, int y)
  {
    return await _connection.InvokeAsync<ActionResult>("AttemptMove", new MoveRequest(pieceId, x, y));
  }

  internal async Task<ActionResult> ChooseRoyalAsync(string royalType)
  {
    return await _connection.InvokeAsync<ActionResult>("ChooseRoyal", new RoyalSelectionRequest(royalType));
  }

  internal void DrainStates(Action<NetworkGameState> apply)
  {
    while (_pendingStates.TryDequeue(out NetworkGameState state))
    {
      apply(state);
    }
  }

  private void Accept(RoomJoinResult result)
  {
    if (!result.Accepted)
    {
      return;
    }

    Team = result.Team;
    JoinCode = result.JoinCode;
    if (result.State is not null)
    {
      _pendingStates.Enqueue(result.State);
    }
  }

  public async ValueTask DisposeAsync()
  {
    await _connection.DisposeAsync();
  }
}
