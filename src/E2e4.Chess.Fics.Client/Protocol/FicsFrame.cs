namespace E2e4.Chess.Fics.Protocol;

public enum FicsFrameKind
{
    /// <summary>A complete line of server output, without its terminator.</summary>
    Line,

    /// <summary>A prompt ("fics% ", "login: ", "password: ") — arrives with no line terminator.</summary>
    Prompt,

    /// <summary>An "iset block 1" framed command response.</summary>
    Block,

    /// <summary>A timeseal "[G]" ping that must be answered immediately with an encrypted \x02 9.</summary>
    TimesealPing,
}

public sealed class FicsFrame
{
    private FicsFrame(FicsFrameKind kind, string text, int blockCommandId, int blockCode)
    {
        Kind = kind;
        Text = text;
        BlockCommandId = blockCommandId;
        BlockCode = blockCode;
    }

    public FicsFrameKind Kind { get; }

    /// <summary>Line/Prompt: the text. Block: the payload between the second separator and BLOCK_END.</summary>
    public string Text { get; }

    /// <summary>Block frames only: the client-chosen command identifier (0 for server-posed output).</summary>
    public int BlockCommandId { get; }

    /// <summary>Block frames only: the BLK_* command or error code.</summary>
    public int BlockCode { get; }

    public static FicsFrame Line(string text) => new(FicsFrameKind.Line, text, 0, 0);

    public static FicsFrame Prompt(string text) => new(FicsFrameKind.Prompt, text, 0, 0);

    public static FicsFrame Block(int commandId, int code, string payload) =>
        new(FicsFrameKind.Block, payload, commandId, code);

    public static FicsFrame TimesealPing() => new(FicsFrameKind.TimesealPing, "[G]", 0, 0);

    public override string ToString() =>
        Kind == FicsFrameKind.Block ? $"Block[{BlockCommandId}:{BlockCode}] {Text}" : $"{Kind} {Text}";
}
