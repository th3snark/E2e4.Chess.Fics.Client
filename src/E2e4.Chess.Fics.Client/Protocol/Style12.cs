using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Text;

// https://www.freechess.org/Help/HelpFiles/style12.html

namespace E2e4.Chess.Fics.Protocol;

/// <summary>
/// A parsed "style 12" board update line. Fields are parsed left to right and
/// extra trailing fields are tolerated (the server may append new ones), per the
/// style 12 documentation.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Style12
{
    public enum GameRelation
    {
        IsolatedPosition = -3,
        ObservingBeingExamined = -2,
        PlayingOpponentsMove = -1,
        Observer = 0,
        PlayingMyMove = 1,
        Examiner = 2,
    }

    private static readonly string[] EmptyBoard = Enumerable.Repeat("--------", 8).ToArray();

    /// <summary>Eight rank strings, rank 8 first, each 8 chars from [-PRNBQKprnbqk].</summary>
    public IReadOnlyList<string> Rank { get; init; } = EmptyBoard;

    public char ToMove { get; init; }

    /// <summary>File (0-7) of a double pawn push on the previous move, or -1.</summary>
    public int DoublePawnPush { get; init; } = -1;

    public bool WhiteCanCastleShort { get; init; }
    public bool WhiteCanCastleLong { get; init; }
    public bool BlackCanCastleShort { get; init; }
    public bool BlackCanCastleLong { get; init; }
    public int MovesSinceLastIrreversibleMove { get; init; }
    public int GameNumber { get; init; }
    public string WhiteName { get; init; } = "";
    public string BlackName { get; init; } = "";
    public GameRelation MyGameRelation { get; init; }
    public int TimeControlInitialMinutes { get; init; }
    public int TimeControlIncrementSeconds { get; init; }
    public int WhiteMaterialStrength { get; init; }
    public int BlackMaterialStrength { get; init; }

    /// <summary>Remaining clock time; seconds, or milliseconds when "iset ms 1" was negotiated.</summary>
    public int WhiteTimeRemaining { get; init; }

    /// <summary>Remaining clock time; seconds, or milliseconds when "iset ms 1" was negotiated.</summary>
    public int BlackTimeRemaining { get; init; }

    public int MoveNumber { get; init; }
    public string MoveVerbose { get; init; } = "";
    public int MoveTimeTakenMinutes { get; init; }
    public int MoveTimeTakenSeconds { get; init; }

    /// <summary>Millisecond part of the previous move's duration; nonzero only with "iset ms 1".</summary>
    public int MoveTimeTakenMilliseconds { get; init; }

    public string MovePretty { get; init; } = "";
    public int Orientation { get; init; }
    public bool ClockTicking { get; init; }
    public int LagMilliseconds { get; init; }
    public string UnparsedText { get; init; } = "";

    public static bool TryParse(string line, [NotNullWhen(true)] out Style12? result)
    {
        result = null;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // "<12>" plus 30 mandatory fields (through the orientation flag); the
        // clock-ticking and lag fields are later server additions.
        if (tokens.Length < 31 || tokens[0] != "<12>")
        {
            return false;
        }

        string[] ranks = new string[8];
        for (int i = 0; i < 8; i++)
        {
            string rank = tokens[1 + i];
            if (rank.Length != 8)
            {
                return false;
            }

            foreach (char c in rank)
            {
                if (c != '-' && "PRNBQKprnbqk".IndexOf(c) < 0)
                {
                    return false;
                }
            }

            ranks[i] = rank;
        }

        if (tokens[9] is not ("W" or "B" or "w" or "b"))
        {
            return false;
        }

        if (!FicsNumber.TryParse(tokens[10], out int doublePawnPush) ||
            !TryParseFlag(tokens[11], out bool whiteShort) ||
            !TryParseFlag(tokens[12], out bool whiteLong) ||
            !TryParseFlag(tokens[13], out bool blackShort) ||
            !TryParseFlag(tokens[14], out bool blackLong) ||
            !FicsNumber.TryParse(tokens[15], out int irreversible) ||
            !FicsNumber.TryParse(tokens[16], out int gameNumber) ||
            !FicsNumber.TryParse(tokens[19], out int relation) ||
            !FicsNumber.TryParse(tokens[20], out int initialMinutes) ||
            !FicsNumber.TryParse(tokens[21], out int incrementSeconds) ||
            !FicsNumber.TryParse(tokens[22], out int whiteMaterial) ||
            !FicsNumber.TryParse(tokens[23], out int blackMaterial) ||
            !FicsNumber.TryParse(tokens[24], out int whiteTime) ||
            !FicsNumber.TryParse(tokens[25], out int blackTime) ||
            !FicsNumber.TryParse(tokens[26], out int moveNumber) ||
            !TryParseMoveDuration(tokens[28], out int tookMinutes, out int tookSeconds, out int tookMilliseconds) ||
            !FicsNumber.TryParse(tokens[30], out int orientation))
        {
            return false;
        }

        bool clockTicking = false;
        if (tokens.Length > 31 && FicsNumber.TryParse(tokens[31], out int ticking))
        {
            clockTicking = ticking == 1;
        }

        int lag = 0;
        if (tokens.Length > 32)
        {
            // The lag field is optional trailing data; if it is malformed the update is still good and lag stays 0.
            _ = FicsNumber.TryParse(tokens[32], out lag);
        }

        result = new Style12
        {
            Rank = ranks,
            ToMove = char.ToUpperInvariant(tokens[9][0]),
            DoublePawnPush = doublePawnPush,
            WhiteCanCastleShort = whiteShort,
            WhiteCanCastleLong = whiteLong,
            BlackCanCastleShort = blackShort,
            BlackCanCastleLong = blackLong,
            MovesSinceLastIrreversibleMove = irreversible,
            GameNumber = gameNumber,
            WhiteName = tokens[17],
            BlackName = tokens[18],
            MyGameRelation = (GameRelation)relation,
            TimeControlInitialMinutes = initialMinutes,
            TimeControlIncrementSeconds = incrementSeconds,
            WhiteMaterialStrength = whiteMaterial,
            BlackMaterialStrength = blackMaterial,
            WhiteTimeRemaining = whiteTime,
            BlackTimeRemaining = blackTime,
            MoveNumber = moveNumber,
            MoveVerbose = tokens[27],
            MoveTimeTakenMinutes = tookMinutes,
            MoveTimeTakenSeconds = tookSeconds,
            MoveTimeTakenMilliseconds = tookMilliseconds,
            MovePretty = tokens[29],
            Orientation = orientation,
            ClockTicking = clockTicking,
            LagMilliseconds = lag,
            UnparsedText = tokens.Length > 33 ? string.Join(' ', tokens[33..]) : "",
        };
        return true;
    }

    private static bool TryParseFlag(string token, out bool value)
    {
        value = token == "1";
        return token is "0" or "1";
    }

    /// <summary>Parses "(m:ss)" or, with "iset ms 1", "(m:ss.mmm)".</summary>
    private static bool TryParseMoveDuration(string token, out int minutes, out int seconds, out int milliseconds)
    {
        minutes = seconds = milliseconds = 0;
        if (token.Length < 2 || token[0] != '(' || token[^1] != ')')
        {
            return false;
        }

        string[] parts = token[1..^1].Split(':');
        if (parts.Length != 2 || !FicsNumber.TryParse(parts[0], out minutes))
        {
            return false;
        }

        string secondsPart = parts[1];
        int dot = secondsPart.IndexOf('.');
        if (dot >= 0)
        {
            if (!FicsNumber.TryParse(secondsPart[(dot + 1)..], out milliseconds))
            {
                return false;
            }

            secondsPart = secondsPart[..dot];
        }

        return FicsNumber.TryParse(secondsPart, out seconds);
    }

    /// <summary>
    /// Converts the position to a full FEN string (placement, side to move, castling rights,
    /// en passant square and the move counters).
    /// </summary>
    public string ToFen()
    {
        StringBuilder sb = new();

        for (int i = 0; i < 8; i++)
        {
            if (i > 0)
            {
                sb.Append('/');
            }

            int emptyRun = 0;
            foreach (char c in Rank[i])
            {
                if (c == '-')
                {
                    emptyRun++;
                }
                else
                {
                    if (emptyRun > 0)
                    {
                        sb.Append((char)('0' + emptyRun));
                        emptyRun = 0;
                    }

                    sb.Append(c);
                }
            }

            if (emptyRun > 0)
            {
                sb.Append((char)('0' + emptyRun));
            }
        }

        sb.Append(' ').Append(ToMove == 'W' ? 'w' : 'b').Append(' ');

        int castlingStart = sb.Length;
        if (WhiteCanCastleShort) sb.Append('K');
        if (WhiteCanCastleLong) sb.Append('Q');
        if (BlackCanCastleShort) sb.Append('k');
        if (BlackCanCastleLong) sb.Append('q');
        if (sb.Length == castlingStart)
        {
            sb.Append('-');
        }

        sb.Append(' ');
        if (DoublePawnPush >= 0 && DoublePawnPush <= 7)
        {
            // The push was the previous move, so the capture square is behind the
            // pawn of the side that just moved.
            sb.Append((char)('a' + DoublePawnPush)).Append(ToMove == 'W' ? '6' : '3');
        }
        else
        {
            sb.Append('-');
        }

        sb.Append(' ').Append(MovesSinceLastIrreversibleMove);
        sb.Append(' ').Append(MoveNumber);

        return sb.ToString();
    }

    /// <summary>
    /// The square the last move started from ("e2"), read from <see cref="MoveVerbose"/> ("P/e2-e4",
    /// "o-o", "o-o-o"); null when there is no last move or it is not a board move (a drop, say).
    /// </summary>
    public string? LastMoveFromSquare => LastMoveSquares().From;

    /// <summary>The square the last move ended on ("e4"); null under the same conditions as <see cref="LastMoveFromSquare"/>.</summary>
    public string? LastMoveToSquare => LastMoveSquares().To;

    private (string? From, string? To) LastMoveSquares()
    {
        string move = MoveVerbose;

        // Castling is reported without squares. The side that just moved is the one not to move now.
        bool whiteMoved = ToMove == 'B';
        if (move == "o-o")
        {
            return whiteMoved ? ("e1", "g1") : ("e8", "g8");
        }

        if (move == "o-o-o")
        {
            return whiteMoved ? ("e1", "c1") : ("e8", "c8");
        }

        // "P/e2-e4", also "P/e7-e8=Q" and "N/g1-f3".
        if (move.Length >= 7 && move[1] == '/' && move[4] == '-'
            && IsSquare(move, 2) && IsSquare(move, 5))
        {
            return (move.Substring(2, 2), move.Substring(5, 2));
        }

        return (null, null);
    }

    private static bool IsSquare(string text, int index)
    {
        return text[index] >= 'a' && text[index] <= 'h' && text[index + 1] >= '1' && text[index + 1] <= '8';
    }

    private string DebuggerDisplay => $"{MoveNumber}. {MovePretty}";
}
