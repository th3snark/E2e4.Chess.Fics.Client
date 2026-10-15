namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>A "&lt;pf&gt;"/"&lt;pt&gt;" pendinfo line — an offer from (&lt;pf&gt;) or to (&lt;pt&gt;) us.</summary>
public sealed class PendingOfferMessage : FicsMessage
{
    public required int Index { get; init; }

    /// <summary>The other party's handle (w=).</summary>
    public required string Handle { get; init; }

    /// <summary>The offer type (t=): match, draw, abort, takeback, ...</summary>
    public required string OfferType { get; init; }

    /// <summary>The offer parameters (p=), verbatim.</summary>
    public string Parameters { get; init; } = "";

    /// <summary>True for "&lt;pf&gt;" (offer made to us), false for "&lt;pt&gt;" (our outgoing offer).</summary>
    public required bool IsIncoming { get; init; }
}

/// <summary>"&lt;pr&gt; index" — the pending offer was removed.</summary>
public sealed class PendingRemovedMessage : FicsMessage
{
    public required int Index { get; init; }
}
