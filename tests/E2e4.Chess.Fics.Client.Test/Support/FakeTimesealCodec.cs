namespace E2e4.Chess.Fics.Client.Test.Support;

/// <summary>
/// A stand-in for a real timeseal cipher: the wire form is "SEAL:" + line + "\x80\n", which has the
/// same framing as the real one (non-ASCII terminator, single trailing line feed) without any
/// cipher. The cipher itself is tested by whichever package supplies the real codec.
/// </summary>
internal sealed class FakeTimesealCodec : ITimesealCodec
{
    public const string Prefix = "SEAL:";

    public static FakeTimesealCodec Instance { get; } = new();

    public string Encode(string line) => Prefix + line + "\u0080\n";
}
