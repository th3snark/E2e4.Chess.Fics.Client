namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>A "&lt;wa&gt;" line ("iset pin 1") — a player arrived. Data is the unparsed rating blob.</summary>
public sealed class PlayerArrivedMessage : FicsMessage
{
    public required string Handle { get; init; }

    public string Data { get; init; } = "";
}

/// <summary>A "&lt;wd&gt;" line ("iset pin 1") — a player departed.</summary>
public sealed class PlayerDepartedMessage : FicsMessage
{
    public required string Handle { get; init; }
}
