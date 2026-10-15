namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>A "&lt;12&gt;" board update.</summary>
public sealed class Style12Message : FicsMessage
{
    public required Style12 Board { get; init; }
}

/// <summary>A "&lt;g1&gt;" gameinfo line ("iset gameinfo 1"). Properties are the raw key=value pairs.</summary>
public sealed class GameInfoMessage : FicsMessage
{
    public required int GameNumber { get; init; }

    public required IReadOnlyDictionary<string, string> Properties { get; init; }
}

/// <summary>A "&lt;b1&gt;" bughouse piece-holdings line, parsed minimally.</summary>
public sealed class BughouseHoldingsMessage : FicsMessage
{
    public required int GameNumber { get; init; }
}

/// <summary>"{Game N (white vs. black) reason}" without a result — a game is starting or resuming.</summary>
public sealed class GameStartedMessage : FicsMessage
{
    public required int GameNumber { get; init; }
    public required string WhiteName { get; init; }
    public required string BlackName { get; init; }
    public required string Reason { get; init; }
}

/// <summary>"{Game N (white vs. black) reason} result" — a game ended (result may be "*").</summary>
public sealed class GameEndedMessage : FicsMessage
{
    public required int GameNumber { get; init; }
    public required string WhiteName { get; init; }
    public required string BlackName { get; init; }
    public required string Reason { get; init; }
    public required string Result { get; init; }
}

/// <summary>"Challenge: ..." — an incoming match offer.</summary>
public sealed class ChallengeMessage : FicsMessage
{
    public required string Details { get; init; }
}

/// <summary>"Creating: ..." — a game of ours is being created.</summary>
public sealed class CreatingMessage : FicsMessage
{
    public required string Details { get; init; }
}
