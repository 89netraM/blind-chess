using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BlindChessServer.GamesManager;

public class GameManagerSingle(Action remove) : GameManager
{
	private TaskCompletionSource<bool>? whiteTask;
	private WebSocketConnection? whiteConnection;

	public override GameManager TryJoin() => new GameManagerPair(remove, whiteTask!, whiteConnection!);

	public override Task<bool> Wait(WebSocketManager webSocketManager)
	{
		whiteTask = new TaskCompletionSource<bool>();
		return Connect(webSocketManager).ContinueWith(_ => whiteTask.Task).Unwrap();
	}

	private async Task Connect(WebSocketManager webSocketManager)
	{
		whiteConnection = await webSocketManager.AcceptWebSocketConnectionAsync();
		await whiteConnection.SendAsync<GameMessage>(new Hello(Color.White));
	}

	public override async ValueTask DisposeAsync()
	{
		if (whiteConnection is not null)
		{
			await whiteConnection.DisposeAsync();
		}
		whiteTask?.SetCanceled();
		remove();
	}
}
