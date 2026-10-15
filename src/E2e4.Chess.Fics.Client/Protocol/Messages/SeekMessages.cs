namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>A "&lt;s&gt;" (or "&lt;sn&gt;" for our own) seek advertisement ("iset seekinfo 1").</summary>
public sealed class SeekAdMessage : FicsMessage
{
    public required int Index { get; init; }
    public required string Handle { get; init; }

    /// <summary>The ti= title bit mask, as sent (hex string).</summary>
    public string TitleCode { get; init; } = "";

    /// <summary>The rt= rating as sent, possibly suffixed (e.g. "1586P"); "0" when unknown.</summary>
    public string Rating { get; init; } = "";

    public int Time { get; init; }
    public int Increment { get; init; }
    public bool Rated { get; init; }
    public string GameType { get; init; } = "";

    /// <summary>'?' (either), 'W' or 'B'.</summary>
    public char Color { get; init; } = '?';

    public string RatingRange { get; init; } = "";
    public bool Automatic { get; init; }
    public bool FormulaChecked { get; init; }

    /// <summary>True for "&lt;sn&gt;" — our own seek echoed back ("iset showownseek 1").</summary>
    public bool IsOwnSeek { get; init; }
}

/// <summary>"&lt;sc&gt;" — all seek ads cleared.</summary>
public sealed class SeeksClearedMessage : FicsMessage
{
}

/// <summary>"&lt;sr&gt; id..." — the listed seek ads were removed.</summary>
public sealed class SeekRemovedMessage : FicsMessage
{
    public required int[] Ids { get; init; }
}
