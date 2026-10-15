using System.Text;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>
/// Canned protocol samples. Wire lines from FICS are terminated "\n\r"; the
/// real lines below are lifted from the captured session transcripts in
/// the Fixtures folder (copied to the output directory at build time).
/// </summary>
internal static class FicsTestData
{
    public static readonly Encoding Wire = Encoding.Latin1;

    // From "02 With OpenSeal on.txt": the starting position of game 314.
    public const string Style12StartPosition =
        "<12> rnbqkbnr pppppppp -------- -------- -------- -------- PPPPPPPP RNBQKBNR W -1 1 1 1 1 0 314 GuestWHTE GuestBLCK 1 2 12 39 39 120 120 1 none (0:00) none 0 0 0";

    // The same session after 1.e4 — Black to move, en passant file e (4).
    public const string Style12AfterE4 =
        "<12> rnbqkbnr pppppppp -------- -------- ----P--- -------- PPPP-PPP RNBQKBNR B 4 1 1 1 1 0 314 GuestWHTE GuestBLCK -1 2 12 39 39 120 120 1 P/e2-e4 (0:00) e4 0 0 0";

    // Later in that session, after White castles — White lost both castling rights.
    public const string Style12AfterWhiteCastles =
        "<12> rnbqk--r ppppppbp -----np- -------- ----P--- -----N-- PPPPBPPP RNBQ-RK- B -1 0 0 1 1 3 314 GuestWHTE GuestBLCK -1 2 12 39 39 156 136 4 o-o (0:00) O-O 0 1 8647";

    public const string GameEndLine =
        "{Game 314 (GuestWHTE vs. GuestBLCK) GuestBLCK resigns} 1-0";

    public static byte[] WireBytes(string text)
    {
        return Wire.GetBytes(text);
    }
}
