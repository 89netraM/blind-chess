using System;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BlindChessServer;

public class WebSocketConnection(WebSocket webSocket) : IAsyncDisposable
{
	private static readonly JsonSerializerOptions jsonSerializerOptions =
		new()
		{
			AllowTrailingCommas = true,
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), },
		};

	public bool IsOpen => webSocket.State == WebSocketState.Open;

	public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
	{
		using var stream = new MemoryStream();
		await JsonSerializer.SerializeAsync(stream, message, jsonSerializerOptions, cancellationToken);
		stream.Position = 0;

		await CopyAsync(
			async (m, ct) =>
			{
				var count = await stream.ReadAsync(m, cancellationToken);
				return (count, stream.Position == stream.Length);
			},
			async (m, e, ct) => await webSocket.SendAsync(m, WebSocketMessageType.Text, e, ct),
			cancellationToken
		);
	}

	public async Task<T?> ReceiveAsync<T>(CancellationToken cancellationToken = default)
	{
		using var stream = new MemoryStream();
		await CopyAsync(
			async (m, ct) =>
			{
				var receiveResult = await webSocket.ReceiveAsync(m, ct);
				return (receiveResult.Count, receiveResult.EndOfMessage);
			},
			async (m, _, ct) => await stream.WriteAsync(m, ct),
			cancellationToken
		);
		stream.Position = 0;

		return await JsonSerializer.DeserializeAsync<T>(stream, jsonSerializerOptions, cancellationToken);
	}

	private static async Task CopyAsync(
		Func<Memory<byte>, CancellationToken, Task<(int, bool)>> read,
		Func<Memory<byte>, bool, CancellationToken, Task> write,
		CancellationToken cancellationToken
	)
	{
		Memory<byte> memory = new byte[1024 * 4];
		var endOfMessage = false;
		while (!endOfMessage)
		{
			(var count, endOfMessage) = await read(memory, cancellationToken);
			await write(memory[..count], endOfMessage, cancellationToken);
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			if (webSocket.State is WebSocketState.Open)
			{
				await webSocket.CloseAsync(
					WebSocketCloseStatus.NormalClosure,
					"Connection disposed",
					CancellationToken.None
				);
			}
		}
		catch { }
		webSocket.Dispose();
	}
}

public static class WebSocketManagerExtensions
{
	public static async Task<WebSocketConnection> AcceptWebSocketConnectionAsync(
		this WebSocketManager webSocketManager
	) => new(await webSocketManager.AcceptWebSocketAsync());
}
