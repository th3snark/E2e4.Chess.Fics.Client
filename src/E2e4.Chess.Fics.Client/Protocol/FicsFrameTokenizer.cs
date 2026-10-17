using System.Buffers;
using System.Globalization;
using System.Text;

namespace E2e4.Chess.Fics.Protocol;

/// <summary>
/// Stateful, incremental tokenizer that turns the raw FICS byte stream into
/// <see cref="FicsFrame"/>s: complete lines, unterminated prompts, "iset block"
/// framed responses and timeseal "[G]" pings. Telnet IAC sequences are stripped.
///
/// Feed it buffers from a <see cref="System.IO.Pipelines.PipeReader"/>; each call
/// consumes the entire input sequence into internal state and yields at most one
/// frame, so drain it with a loop until it returns false before reading more bytes.
/// All state survives arbitrary split points, including IAC sequences, "\n\r"
/// terminator pairs and block frames spanning multiple network reads.
/// </summary>
public sealed class FicsFrameTokenizer
{
    private enum TelnetState
    {
        Normal,
        SawIac,
        SawIacOption,
        InSubnegotiation,
        InSubnegotiationSawIac,
    }

    /// <summary>The default for <see cref="MaxFrameLength"/>: 4 MiB.</summary>
    public const int DefaultMaxFrameLength = 4 * 1024 * 1024;

    private const byte Bell = 0x07;
    private const byte Lf = 0x0A;
    private const byte Cr = 0x0D;

    // Bytes received but not yet turned into frames live in _buffer[_start.._end).
    // Consuming a frame just advances _start, so draining many frames out of one large
    // read is linear rather than shifting the remainder for every frame.
    private byte[] _buffer = new byte[1024];
    private int _start;
    private int _end;
    private int _maxFrameLength = DefaultMaxFrameLength;
    private TelnetState _telnetState = TelnetState.Normal;

    // When a line terminator's first char was the last byte available, its pair
    // mate ("\n\r" or "\r\n") may arrive at the start of the next read; this
    // records the byte to silently drop if it shows up first.
    private byte _skipMateByte;

    // FICS separates a prompt from subsequent asynchronous output with one line
    // terminator; swallowing it keeps the frame stream identical no matter how
    // the bytes were chunked by the network.
    private bool _swallowTerminatorAfterPrompt;

    private readonly List<string> _prompts = new() { FicsPrompts.Command, FicsPrompts.Login, FicsPrompts.Password };

    /// <summary>Unterminated strings recognised as prompts. Mutable so hosts can add custom prompts.</summary>
    public IList<string> Prompts => _prompts;

    /// <summary>
    /// The longest a single frame (an unterminated line, or a block frame without its end
    /// marker yet) may grow before <see cref="TryReadFrame"/> fails. Bounds memory when a
    /// peer never sends a terminator.
    /// </summary>
    public int MaxFrameLength
    {
        get => _maxFrameLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            _maxFrameLength = value;
        }
    }

    /// <exception cref="InvalidDataException">A frame has grown beyond <see cref="MaxFrameLength"/> without completing.</exception>
    public bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out FicsFrame? frame)
    {
        Ingest(ref buffer);
        if (TryExtractFrame(out frame))
        {
            return true;
        }

        if (_end - _start > _maxFrameLength)
        {
            throw new InvalidDataException(
                $"The server sent more than {_maxFrameLength} bytes without completing a line or block.");
        }

        return false;
    }

    private Span<byte> Pending => new(_buffer, _start, _end - _start);

    private void Consume(int count)
    {
        _start += count;
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }
    }

    private void AppendPending(byte b)
    {
        if (_end == _buffer.Length)
        {
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            else
            {
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }
        }

        _buffer[_end++] = b;
    }

    /// <summary>Runs every input byte through the telnet filter into the pending buffer.</summary>
    private void Ingest(ref ReadOnlySequence<byte> buffer)
    {
        foreach (ReadOnlyMemory<byte> segment in buffer)
        {
            foreach (byte b in segment.Span)
            {
                FilterTelnet(b);
            }
        }

        buffer = buffer.Slice(buffer.End);
    }

    private void FilterTelnet(byte b)
    {
        switch (_telnetState)
        {
            case TelnetState.Normal:
                if (b == TelnetConstants.InterpretAsCommand)
                {
                    _telnetState = TelnetState.SawIac;
                }
                else
                {
                    Append(b);
                }
                break;

            case TelnetState.SawIac:
                if (b == TelnetConstants.InterpretAsCommand)
                {
                    // IAC IAC is an escaped literal 0xFF data byte.
                    Append(b);
                    _telnetState = TelnetState.Normal;
                }
                else if (b == TelnetConstants.SubnegotiationBegin)
                {
                    _telnetState = TelnetState.InSubnegotiation;
                }
                else if (b >= TelnetConstants.Will && b <= TelnetConstants.Dont)
                {
                    _telnetState = TelnetState.SawIacOption;
                }
                else
                {
                    // Two-byte command (NOP, GA, ...): drop it.
                    _telnetState = TelnetState.Normal;
                }
                break;

            case TelnetState.SawIacOption:
                // The option byte of WILL/WONT/DO/DONT: drop it.
                _telnetState = TelnetState.Normal;
                break;

            case TelnetState.InSubnegotiation:
                if (b == TelnetConstants.InterpretAsCommand)
                {
                    _telnetState = TelnetState.InSubnegotiationSawIac;
                }
                break;

            case TelnetState.InSubnegotiationSawIac:
                _telnetState = b == TelnetConstants.SubnegotiationEnd
                    ? TelnetState.Normal
                    : TelnetState.InSubnegotiation;
                break;
        }
    }

    private void Append(byte b)
    {
        if (_skipMateByte != 0)
        {
            byte mate = _skipMateByte;
            _skipMateByte = 0;
            if (b == mate)
            {
                return;
            }
        }

        AppendPending(b);
    }

    private bool TryExtractFrame(out FicsFrame? frame)
    {
        frame = null;
        Span<byte> span = Pending;
        if (span.IsEmpty)
        {
            return false;
        }

        if (_swallowTerminatorAfterPrompt)
        {
            _swallowTerminatorAfterPrompt = false;
            if (span[0] == Lf || span[0] == Cr)
            {
                int swallowed = 1;
                byte mate = span[0] == Lf ? Cr : Lf;
                if (span.Length > 1)
                {
                    if (span[1] == mate)
                    {
                        swallowed++;
                    }
                }
                else
                {
                    _skipMateByte = mate;
                }

                Consume(swallowed);
                span = Pending;
                if (span.IsEmpty)
                {
                    return false;
                }
            }
        }

        if (span[0] == BlockConstants.BlockStart || span[0] == BlockConstants.BlockPoseStart)
        {
            return TryExtractBlockFrame(span, out frame);
        }

        // A prompt is recognised as soon as its full text is at the head of the
        // stream — it never gets a terminator, and output that was buffered in
        // the same read must not merge into it.
        foreach (string prompt in _prompts)
        {
            // An empty prompt would match everywhere and consume nothing, so the caller would spin.
            if (string.IsNullOrEmpty(prompt))
            {
                continue;
            }

            if (StartsWith(span, prompt))
            {
                frame = FicsFrame.Prompt(prompt);
                Consume(prompt.Length);
                _swallowTerminatorAfterPrompt = true;
                return true;
            }
        }

        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];

            if (b == BlockConstants.BlockStart || b == BlockConstants.BlockPoseStart)
            {
                // Text ran straight into a block frame with no terminator (e.g. a
                // prompt directly before a block response). Emit the text now and
                // leave the marker for the next call.
                frame = Classify(span[..i]);
                Consume(i);
                return true;
            }

            if (b == Lf || b == Cr)
            {
                int consumed = i + 1;
                byte mate = b == Lf ? Cr : Lf;
                if (i + 1 < span.Length)
                {
                    if (span[i + 1] == mate)
                    {
                        consumed++;
                    }
                }
                else
                {
                    _skipMateByte = mate;
                }

                frame = Classify(span[..i]);
                Consume(consumed);
                return true;
            }
        }

        // No terminator and no complete prompt: wait for more data.
        return false;
    }

    private static bool StartsWith(ReadOnlySpan<byte> span, string prefix)
    {
        if (span.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (span[i] != (byte)prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private bool TryExtractBlockFrame(Span<byte> span, out FicsFrame? frame)
    {
        frame = null;
        byte endMarker = span[0] == BlockConstants.BlockStart
            ? BlockConstants.BlockEnd
            : BlockConstants.BlockPoseEnd;

        int end = span.IndexOf(endMarker);
        if (end < 0)
        {
            return false;
        }

        Span<byte> body = span[1..end];

        int commandId = 0;
        int code = 0;
        int firstSeparator = body.IndexOf(BlockConstants.BlockSeparator);
        if (firstSeparator >= 0)
        {
            TryParseHeaderNumber(body[..firstSeparator], out commandId);
            Span<byte> afterId = body[(firstSeparator + 1)..];
            int secondSeparator = afterId.IndexOf(BlockConstants.BlockSeparator);
            if (secondSeparator >= 0)
            {
                TryParseHeaderNumber(afterId[..secondSeparator], out code);
                body = afterId[(secondSeparator + 1)..];
            }
            else
            {
                body = afterId;
            }
        }

        frame = FicsFrame.Block(commandId, code, Encoding.Latin1.GetString(body));
        Consume(end + 1);
        return true;
    }

    /// <summary>Parses a block header number (digits only); a malformed one reads as 0.</summary>
    private static void TryParseHeaderNumber(ReadOnlySpan<byte> digits, out int value)
    {
        if (!int.TryParse(Encoding.Latin1.GetString(digits), NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
        }
    }

    private FicsFrame Classify(ReadOnlySpan<byte> content)
    {
        // Server output such as style-12 updates is often preceded by a bell.
        while (!content.IsEmpty && content[0] == Bell)
        {
            content = content[1..];
        }

        string text = Encoding.Latin1.GetString(content);

        if (text == "[G]")
        {
            return FicsFrame.TimesealPing();
        }

        foreach (string prompt in _prompts)
        {
            if (text == prompt)
            {
                return FicsFrame.Prompt(text);
            }
        }

        return FicsFrame.Line(text);
    }
}
