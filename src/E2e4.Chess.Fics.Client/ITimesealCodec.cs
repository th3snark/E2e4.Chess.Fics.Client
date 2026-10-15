namespace E2e4.Chess.Fics.Client;

/// <summary>
/// Encodes outbound lines for the FICS timeseal handshake (server-side lag compensation).
/// The library has no implementation of its own: supply one through
/// <see cref="FicsClientOptions.Timeseal"/>, or leave it null to send plain lines.
/// </summary>
public interface ITimesealCodec
{
    /// <summary>
    /// Returns the complete wire text for one outbound line, including its terminator.
    /// The characters are sent as ISO-8859-1 with nothing appended.
    /// </summary>
    string Encode(string line);
}
