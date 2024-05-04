using System;
using BlindChessServer.GamesManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GamesManager>();

var app = builder.Build();

app.UseWebSockets();
app.Map(
	"/game/{gameId}",
	async (Guid gameId, GamesManager gamesManager, HttpContext context) =>
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			return Results.BadRequest("Connect using WebSocket");
		}

		if (!await gamesManager.TryJoin(gameId, context.WebSockets))
		{
			return Results.BadRequest($"Cannot join game {gameId}");
		}

		return Results.Empty;
	}
);

app.Run();

record Message(int Id, string Text);
