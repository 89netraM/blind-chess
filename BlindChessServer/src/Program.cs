using System;
using System.Threading;
using BlindChessServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

app.MapGet("/", () => "Hello World!");
app.Map(
	"/game",
	async (HttpContext context, ILogger<Program> logger) =>
	{
		using var _ = logger.BeginScope("WebSocket request");
		if (!context.WebSockets.IsWebSocketRequest)
		{
			logger.LogInformation("Non WebSocket request to WebSocket endpoint");
			return Results.BadRequest("");
		}

		logger.LogInformation("Accepting WebSocket request");
		await using var connection = await context.WebSockets.AcceptWebSocketConnectionAsync();

		while (connection.IsOpen)
		{
			var message = await connection.ReceiveAsync<Message>(CancellationToken.None);
			Console.WriteLine(message);
			if (message is null)
			{
				logger.LogWarning("Received null message");
				continue;
			}

			logger.LogInformation("{Id}: {Text}", message.Id, message.Text);
			await connection.SendAsync(new Message(message.Id + 1, message.Text));
		}

		return Results.Empty;
	}
);

app.Run();

record Message(int Id, string Text);
