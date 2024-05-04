using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BlindChessServer.GamesManager;

public class GamesManager
{
	private readonly ConcurrentDictionary<Guid, GameManager> games = [];

	public Task<bool> TryJoin(Guid gameId, WebSocketManager webSocketManager)
	{
		var gameManager = games.AddOrUpdate(
			gameId,
			_ => new GameManagerSingle(() => games.Remove(gameId, out var _)),
			(_, g) => g.TryJoin()
		);
		return gameManager.Wait(webSocketManager);
	}
}
