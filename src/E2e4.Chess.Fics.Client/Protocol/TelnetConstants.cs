namespace E2e4.Chess.Fics.Protocol;

/// <summary>
/// Telnet command bytes (RFC 854). FICS rarely negotiates, but it does send
/// IAC WILL/WONT ECHO around the password prompt, so inbound IAC sequences
/// must be recognised and stripped.
/// </summary>
internal static class TelnetConstants
{
    public const byte InterpretAsCommand = 0xFF; // IAC
    public const byte Dont = 0xFE;
    public const byte Do = 0xFD;
    public const byte Wont = 0xFC;
    public const byte Will = 0xFB;
    public const byte SubnegotiationBegin = 0xFA; // SB
    public const byte SubnegotiationEnd = 0xF0;   // SE
}
