using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BlindChessServer.GamesManager;

public abstract class GameManager : IAsyncDisposable
{
	public abstract GameManager TryJoin();

	public abstract Task<bool> Wait(WebSocketManager webSocketManager);

	public abstract ValueTask DisposeAsync();
}
