using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BlindChessServer.GamesManager;

public class GameManagerFull(GameManagerPair pair) : GameManager
{
	public override GameManager TryJoin() => this;

	public override Task<bool> Wait(WebSocketManager webSocketManager) => Task.FromResult(false);

	public override async ValueTask DisposeAsync()
	{
		await pair.DisposeAsync();
	}
}
