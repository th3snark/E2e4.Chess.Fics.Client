using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Protocol;

namespace E2e4.Chess.Fics.Client;

public sealed class FicsMessageEventArgs : EventArgs
{
    public FicsMessageEventArgs(FicsMessage message)
    {
        Message = message;
    }

    public FicsMessage Message { get; }
}

public sealed class Style12EventArgs : EventArgs
{
    public Style12EventArgs(Style12 board)
    {
        Board = board;
    }

    public Style12 Board { get; }
}

public sealed class FicsDisconnectedEventArgs : EventArgs
{
    public FicsDisconnectedEventArgs(Exception? error)
    {
        Error = error;
    }

    /// <summary>The failure that ended the connection, or null for a clean close or a local dispose.</summary>
    public Exception? Error { get; }
}

public sealed class FicsRawTextEventArgs : EventArgs
{
    public FicsRawTextEventArgs(FicsFrameKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public FicsFrameKind Kind { get; }

    public string Text { get; }
}
