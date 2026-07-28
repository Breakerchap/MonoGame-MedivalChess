using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MedivalChess.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace MedivalChess;

internal sealed class OnlineMatchClient : IAsyncDisposable
{
  private readonly ConcurrentQueue<NetworkGameState> _pendingStates = new();
  private readonly ConcurrentQueue<string> _pendingErrors = new();
  private readonly HubConnection _connection;

  internal NetworkTeam? Team { get; private set; }
  internal string JoinCode { get; private set; }
  internal string ReconnectToken { get; private set; }

  internal OnlineMatchClient(string serverUrl)
  {
    _connection = new HubConnectionBuilder()
      .WithUrl($"{serverUrl.TrimEnd('/')}/gamehub")
      .WithAutomaticReconnect()
      .Build();
    _connection.On<NetworkGameState>("StateUpdated", state => _pendingStates.Enqueue(state));
    _connection.Reconnected += RejoinRoomAfterReconnectAsync;
  }

  internal async Task<RoomJoinResult> HostAsync(CreateGameRequest request)
  {
    await _connection.StartAsync();
    RoomJoinResult result = await _connection.InvokeAsync<RoomJoinResult>("CreateGame", request);
    Accept(result);
    return result;
  }

  internal async Task<RoomJoinResult> JoinAsync(string joinCode, string reconnectToken = null)
  {
    await _connection.StartAsync();
    RoomJoinResult result = await _connection.InvokeAsync<RoomJoinResult>(
      "JoinGame",
      new JoinGameRequest(joinCode, reconnectToken)
    );
    Accept(result);
    return result;
  }

  internal async Task<ActionResult> MoveAsync(string pieceId, int x, int y)
  {
    return await _connection.InvokeAsync<ActionResult>("AttemptMove", new MoveRequest(pieceId, x, y));
  }

  internal async Task<ActionResult> AttackAsync(string attackerId, string targetId, int? targetX = null, int? targetY = null)
  {
    return await _connection.InvokeAsync<ActionResult>("AttemptAttack", new AttackRequest(attackerId, targetId, targetX, targetY));
  }

  internal async Task<ActionResult> SpecialAsync(
    string actorId,
    string ability,
    string targetId,
    int targetX,
    int targetY
  )
  {
    return await _connection.InvokeAsync<ActionResult>(
      "AttemptSpecial",
      new SpecialActionRequest(actorId, ability, targetId, targetX, targetY)
    );
  }

  internal async Task<ActionResult> ChooseRoyalAsync(string royalType)
  {
    return await _connection.InvokeAsync<ActionResult>("ChooseRoyal", new RoyalSelectionRequest(royalType));
  }

  internal async Task<ActionResult> PurchaseInitialUnitAsync(string pieceType, int x, int y)
  {
    return await _connection.InvokeAsync<ActionResult>(
      "PurchaseInitialUnit",
      new PurchaseRequest(pieceType, x, y)
    );
  }

  internal async Task<ActionResult> PurchaseUnitAsync(string pieceType, int x, int y)
  {
    return await _connection.InvokeAsync<ActionResult>("PurchaseUnit", new PurchaseRequest(pieceType, x, y));
  }

  internal async Task<ActionResult> StopInitialBuyingAsync()
  {
    return await _connection.InvokeAsync<ActionResult>("StopInitialBuying");
  }

  internal async Task<ActionResult> SkipTurnAsync()
  {
    return await _connection.InvokeAsync<ActionResult>("SkipTurn", new SkipTurnRequest());
  }

  internal async Task<ActionResult> CompleteCavalierActivationAsync(string pieceId)
  {
    return await _connection.InvokeAsync<ActionResult>(
      "CompleteCavalierActivation",
      new CompleteCavalierActivationRequest(pieceId)
    );
  }

  internal void DrainStates(Action<NetworkGameState> apply, Action<string> reportError = null)
  {
    while (_pendingErrors.TryDequeue(out string error))
    {
      reportError?.Invoke(error);
    }

    while (_pendingStates.TryDequeue(out NetworkGameState state))
    {
      apply(state);
    }
  }

  private async Task RejoinRoomAfterReconnectAsync(string connectionId)
  {
    if (string.IsNullOrWhiteSpace(JoinCode) || string.IsNullOrWhiteSpace(ReconnectToken))
    {
      return;
    }

    try
    {
      RoomJoinResult result = await _connection.InvokeAsync<RoomJoinResult>(
        "JoinGame",
        new JoinGameRequest(JoinCode, ReconnectToken)
      );
      if (result.Accepted)
      {
        Accept(result);
      }
      else
      {
        _pendingErrors.Enqueue(result.Error ?? "Could not reconnect to the room.");
      }
    }
    catch (Exception exception)
    {
      _pendingErrors.Enqueue($"Could not reconnect to the room: {exception.Message}");
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
    ReconnectToken = result.ReconnectToken;
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
