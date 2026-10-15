using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Protocol;

namespace E2e4.Chess.Fics.Protocol;

/// <summary>The server's response to one command sent with <c>IFicsClient.SendCommandAsync</c>.</summary>
public sealed class FicsResponse
{
    public FicsResponse(int commandId, int code, string text, IReadOnlyList<FicsMessage> messages)
    {
        CommandId = commandId;
        Code = code;
        Text = text;
        Messages = messages;
    }

    /// <summary>The block-mode command identifier; 0 when block mode is off.</summary>
    public int CommandId { get; }

    /// <summary>The BLK_* command or error code; 0 when block mode is off.</summary>
    public int Code { get; }

    /// <summary>The raw response text.</summary>
    public string Text { get; }

    /// <summary>The response's lines run through the message parser.</summary>
    public IReadOnlyList<FicsMessage> Messages { get; }

    /// <summary>True when the block code is in the BLK_ERROR_* range. Always false with block mode off.</summary>
    public bool IsError => Code >= BlockCodes.FirstErrorCode;
}
