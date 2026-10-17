using System.Buffers;
using System.Text;

namespace E2e4.Chess.Fics.Client.Transport;

internal static class FicsLineEncoder
{
    private static readonly SearchValues<char> Forbidden = SearchValues.Create("\r\n\0\u00FF");

    /// <summary>
    /// Encodes one outbound line as wire bytes. A timeseal codec's output contains chars in
    /// 0x80-0xFF and already ends with its own terminator, so it must be transmitted as
    /// ISO-8859-1 with nothing appended; plaintext lines get a single "\n".
    /// </summary>
    public static byte[] Encode(string line, ITimesealCodec? timeseal)
    {
        ThrowIfNotSingleLine(line, nameof(line));
        string wire = timeseal is null ? line + "\n" : timeseal.Encode(line);
        return Encoding.Latin1.GetBytes(wire);
    }

    /// <summary>
    /// Rejects a line that would reach the server as more than one line: a CR, LF or NUL inside
    /// a command, user name or password would otherwise inject further commands. U+00FF is rejected
    /// too: as a Latin-1 byte it is the Telnet IAC marker and would be read as a protocol command.
    /// </summary>
    public static void ThrowIfNotSingleLine(string line, string paramName)
    {
        ArgumentNullException.ThrowIfNull(line, paramName);

        if (line.AsSpan().IndexOfAny(Forbidden) >= 0)
        {
            throw new ArgumentException("A line must not contain a carriage return, line feed, NUL or U+00FF character.", paramName);
        }
    }
}
