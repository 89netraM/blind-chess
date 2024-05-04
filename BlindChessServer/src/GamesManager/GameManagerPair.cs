using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Chess;
using Humanizer;
using Microsoft.AspNetCore.Http;

namespace BlindChessServer.GamesManager;

public class GameManagerPair(Action remove, TaskCompletionSource<bool> whiteTask, WebSocketConnection whiteConnection)
	: GameManager
{
	private readonly ChessBoard chessBoard = new();

	private WebSocketConnection? blackConnection;

	public override GameManager TryJoin() => new GameManagerFull(this);

	public override async Task<bool> Wait(WebSocketManager webSocketManager)
	{
		try
		{
			await Run(webSocketManager);
		}
		finally
		{
			await DisposeAsync();
		}
		return true;
	}

	private async Task Send(Color color, GameMessage message) => await GetConnection(color).SendAsync(message);

	private async Task<GameMessage?> Receive(Color color)
	{
		var connection = GetConnection(color);
		while (true)
		{
			try
			{
				var message = await connection.ReceiveAsync<GameMessage>();
				return message;
			}
			catch (JsonException ex)
			{
				await connection.SendAsync<GameMessage>(new InvalidMessage(ex.Message));
			}
		}
	}

	private WebSocketConnection GetConnection(Color color) =>
		color switch
		{
			Color.White => whiteConnection,
			Color.Black => blackConnection!,
			var c => throw new UnreachableException($"{nameof(GetConnection)} of color {c}"),
		};

	private async Task Run(WebSocketManager webSocketManager)
	{
		blackConnection = await webSocketManager.AcceptWebSocketConnectionAsync();
		await Send(Color.Black, new Hello(Color.Black));

		await RunTurns();

		var gameOverMessage = new GameOver(
			chessBoard.EndGame?.WonSide?.AsChar switch
			{
				'w' => Color.White,
				'b' => Color.Black,
				var c => throw new UnreachableException($"EndGame WonSide PieceColor is {c}"),
			},
			chessBoard.ToFen()
		);
		await Send(Color.White, gameOverMessage);
		await Send(Color.Black, gameOverMessage);
	}

	private async Task RunTurns()
	{
		var (active, idle) = (Color.White, Color.Black);
		while (!chessBoard.IsEndGame)
		{
			await Send(active, new YourTurn());
			await MakeMove(active, idle);
			(active, idle) = (idle, active);
		}
	}

	private async Task MakeMove(Color active, Color idle)
	{
		var moveAnswer = await ReceiveValidMove(active);
		if (moveAnswer is null)
		{
			return;
		}

		chessBoard.Move(moveAnswer.ToMove());
		await Send(active, moveAnswer);
		if (moveAnswer.CapturedPiece is Piece)
		{
			await Send(idle, new PieceCaptured(moveAnswer.To));
		}
	}

	private async Task<MoveAnswer?> ReceiveValidMove(Color active)
	{
		var request = await ReceiveMoveRequest(active);
		var answer = GetMoveAnswer(active, request);
		if (answer is null)
		{
			await Send(active, new InvalidMove());
		}
		return answer;
	}

	private async Task<MoveRequest> ReceiveMoveRequest(Color active)
	{
		while (true)
		{
			var message = await Receive(active);
			if (message is MoveRequest moveRequest)
			{
				return moveRequest;
			}
			await Send(active, new UnknownMessage(nameof(MoveRequest).Camelize()));
		}
	}

	private MoveAnswer? GetMoveAnswer(Color active, MoveRequest request)
	{
		if (
			chessBoard[request.From.ToFileAndRank()]?.Color is not PieceColor fromColor
			|| !IsSameColor(fromColor, active)
		)
		{
			return null;
		}

		if (!chessBoard.IsValidMove(request.ToMove()))
		{
			return null;
		}

		return new(request.From, request.To, TranslatePiece(chessBoard[request.To.ToFileAndRank()]?.Type));
	}

	private static bool IsSameColor(PieceColor pieceColor, Color color) =>
		(pieceColor.AsChar, color) switch
		{
			('w', Color.White) => true,
			('b', Color.Black) => true,
			_ => false,
		};

	private static Piece? TranslatePiece(PieceType? piece) =>
		piece?.AsChar switch
		{
			'p' => Piece.Pawn,
			'r' => Piece.Rook,
			'n' => Piece.Knight,
			'b' => Piece.Bishop,
			'q' => Piece.Queen,
			'k' => Piece.King,
			_ => null,
		};

	public override async ValueTask DisposeAsync()
	{
		await whiteConnection.DisposeAsync();
		whiteTask.TrySetCanceled();
		if (blackConnection is not null)
		{
			await blackConnection.DisposeAsync();
		}
		remove();
	}
}

[JsonPolymorphic(
	IgnoreUnrecognizedTypeDiscriminators = true,
	TypeDiscriminatorPropertyName = "message",
	UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType
)]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(YourTurn), "yourTurn")]
[JsonDerivedType(typeof(MoveRequest), "moveRequest")]
[JsonDerivedType(typeof(MoveAnswer), "moveAnswer")]
[JsonDerivedType(typeof(PromotionRequest), "promotionRequest")]
[JsonDerivedType(typeof(PromotionAnswer), "promotionAnswer")]
[JsonDerivedType(typeof(KingsideCastlingRequest), "kingsideCastlingRequest")]
[JsonDerivedType(typeof(KingsideCastlingAnswer), "kingsideCastlingAnswer")]
[JsonDerivedType(typeof(QueensideCastlingRequest), "queensideCastlingRequest")]
[JsonDerivedType(typeof(QueensideCastlingAnswer), "queensideCastlingAnswer")]
[JsonDerivedType(typeof(PieceCaptured), "pieceCaptured")]
[JsonDerivedType(typeof(InvalidMove), "invalidMove")]
[JsonDerivedType(typeof(NoMove), "noMove")]
[JsonDerivedType(typeof(UnknownMessage), "unknownMessage")]
[JsonDerivedType(typeof(InvalidMessage), "invalidMessage")]
[JsonDerivedType(typeof(GameOver), "gameOver")]
public record GameMessage;

public record Hello(Color Color) : GameMessage;

public enum Color
{
	White,
	Black,
}

public record YourTurn : GameMessage;

public record MoveRequest(Position From, Position To) : GameMessage
{
	public Move ToMove() => new(From.ToFileAndRank(), To.ToFileAndRank());
}

[JsonConverter(typeof(FileAndRankConverter))]
public record Position(File File, Rank Rank)
{
	public string ToFileAndRank() => $"{File.Humanize().ToLower()}{(int)Rank + 1}";
}

public class FileAndRankConverter : JsonConverter<Position>
{
	public override Position? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.GetString() is not string fileAndRank)
		{
			return null;
		}

		if (fileAndRank is not [var file, var rank])
		{
			throw new JsonException($@"Unexpected file and rank ""{fileAndRank}""");
		}

		return new(
			file switch
			{
				'a' => File.A,
				'b' => File.B,
				'c' => File.C,
				'd' => File.D,
				'e' => File.E,
				'f' => File.F,
				'g' => File.G,
				_ => throw new JsonException($"Unrecognized file {file}"),
			},
			rank switch
			{
				'1' => Rank.One,
				'2' => Rank.Two,
				'3' => Rank.Three,
				'4' => Rank.Four,
				'5' => Rank.Five,
				'6' => Rank.Six,
				'7' => Rank.Seven,
				'8' => Rank.Eight,
				_ => throw new JsonException($"Unrecognized rank {rank}"),
			}
		);
	}

	public override void Write(Utf8JsonWriter writer, Position value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToFileAndRank());
	}
}

public enum File
{
	A,
	B,
	C,
	D,
	E,
	F,
	G,
	H,
}

public enum Rank
{
	One,
	Two,
	Three,
	Four,
	Five,
	Six,
	Seven,
	Eight,
}

public record PromotionRequest : GameMessage;

public record PromotionAnswer(PromotionPiece Piece) : GameMessage;

public enum PromotionPiece
{
	Queen,
	Rook,
	Bishop,
	Knight,
}

public record MoveAnswer(Position From, Position To, Piece? CapturedPiece) : GameMessage
{
	public Move ToMove() => new(From.ToFileAndRank(), To.ToFileAndRank());
}

public record KingsideCastlingRequest : GameMessage;

public record KingsideCastlingAnswer : GameMessage;

public record QueensideCastlingRequest : GameMessage;

public record QueensideCastlingAnswer : GameMessage;

public record InvalidMove : GameMessage;

public record NoMove : GameMessage;

public record UnknownMessage(string ExpectedMessageType) : GameMessage;

public record InvalidMessage(string Message) : GameMessage;

public record GameOver(Color Winner, string Board) : GameMessage;

public record PieceCaptured(Position Position) : GameMessage;

public enum Piece
{
	King,
	Queen,
	Rook,
	Bishop,
	Knight,
	Pawn,
}
