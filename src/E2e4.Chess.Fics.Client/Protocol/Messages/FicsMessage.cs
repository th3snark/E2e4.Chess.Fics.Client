namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>Base class for every parsed server line.</summary>
public abstract class FicsMessage
{
    /// <summary>The original server line, verbatim.</summary>
    public required string RawText { get; init; }

    public override string ToString() => $"{GetType().Name}: {RawText}";
}

/// <summary>Fallback for any line the parser does not recognise.</summary>
public sealed class UnknownLineMessage : FicsMessage
{
}
