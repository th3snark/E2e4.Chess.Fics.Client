namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>"handle tells you: text".</summary>
public sealed class TellMessage : FicsMessage
{
    public required string Handle { get; init; }

    /// <summary>Title tags as sent, e.g. "(U)" or "(SR)(TM)"; empty if none.</summary>
    public string Titles { get; init; } = "";

    public required string Text { get; init; }
}

/// <summary>"handle(channel): text".</summary>
public sealed class ChannelTellMessage : FicsMessage
{
    public required string Handle { get; init; }
    public string Titles { get; init; } = "";
    public required int Channel { get; init; }
    public required string Text { get; init; }
}

/// <summary>"handle shouts: text", "handle c-shouts: text" or the "--> handle text" emote form.</summary>
public sealed class ShoutMessage : FicsMessage
{
    public required string Handle { get; init; }
    public string Titles { get; init; } = "";
    public required string Text { get; init; }
    public bool IsEmote { get; init; }
    public bool IsCShout { get; init; }
}

/// <summary>"handle(rating)[game] kibitzes: text".</summary>
public sealed class KibitzMessage : FicsMessage
{
    public required string Handle { get; init; }
    public string Rating { get; init; } = "";
    public required int GameNumber { get; init; }
    public required string Text { get; init; }
}

/// <summary>"handle(rating)[game] whispers: text".</summary>
public sealed class WhisperMessage : FicsMessage
{
    public required string Handle { get; init; }
    public string Rating { get; init; } = "";
    public required int GameNumber { get; init; }
    public required string Text { get; init; }
}

/// <summary>"**ANNOUNCEMENT** from handle: text".</summary>
public sealed class AnnouncementMessage : FicsMessage
{
    public required string Handle { get; init; }
    public required string Text { get; init; }
}
