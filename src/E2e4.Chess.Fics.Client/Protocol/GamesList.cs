using System.Text.RegularExpressions;

namespace E2e4.Chess.Fics.Protocol;

/// <summary>One row of the "games" command's listing of games in progress.</summary>
public sealed class GameListEntry
{
    public GameListEntry(
        int gameNumber, string white, string whiteRating, string black, string blackRating, string flags, string rawLine)
    {
        GameNumber = gameNumber;
        White = white;
        WhiteRating = whiteRating;
        Black = black;
        BlackRating = blackRating;
        Flags = flags;
        RawLine = rawLine;
    }

    public int GameNumber { get; }

    /// <summary>The white player's handle; empty when only the game number could be recovered.</summary>
    public string White { get; }

    /// <summary>Rating text such as "1719", or "++++"/"----" for unrated or guest players, or "Exam".</summary>
    public string WhiteRating { get; }

    public string Black { get; }

    public string BlackRating { get; }

    /// <summary>The bracketed flags field, e.g. "br 2 12" (rated blitz 2 12).</summary>
    public string Flags { get; }

    /// <summary>White's remaining clock as the server prints it, e.g. "1:34"; empty when the listing has none.</summary>
    public string WhiteClock { get; init; } = "";

    /// <summary>Black's remaining clock, e.g. "0:39"; empty when the listing has none.</summary>
    public string BlackClock { get; init; } = "";

    /// <summary>Material strength of each side as printed, e.g. "34-39"; empty when the listing has none.</summary>
    public string Material { get; init; } = "";

    /// <summary>"W" or "B", the side to move; empty when the listing has none.</summary>
    public string ToMove { get; init; } = "";

    /// <summary>The move number as printed, e.g. "25"; empty when the listing has none.</summary>
    public string MoveNumber { get; init; } = "";

    /// <summary>The full server line the entry was parsed from.</summary>
    public string RawLine { get; }
}

/// <summary>Parses the output of the "games" command.</summary>
public static partial class GamesListParser
{
    internal const int MaxLineLength = 1024;

    // Matches the fixed-width "games" listing, e.g.:
    // "100  1719 Sisyphus     1691 guest2      [ br  2  12]   1:34 -   0:39 (34-39) W: 25"
    [GeneratedRegex(@"^\s*(?<game>\d+)\s+(?<wr>\d+|\+{4}|-{4})\s+(?<white>\S+)\s+(?<br>\d+|\+{4}|-{4})\s+(?<black>\S+)\s+\[(?<flags>[^\]]*)\](?:\s+(?<wc>-?\d+:\d+)\s+-\s+(?<bc>-?\d+:\d+)\s+\((?<mat>\d+-\d+)\)\s+(?<tm>[WB]):\s*(?<mn>\d+))?")]
    private static partial Regex PlayedGameRegex();

    // Games being examined (e.g. lectures) have no clock fields: "13 (Exam.  0 white  0 black ) [ uu  0  0] W: 1".
    [GeneratedRegex(@"^\s*(?<game>\d+)\s+\(Exam\.\s+\d+\s+(?<white>\S+)\s+\d+\s+(?<black>\S+)\s*\)\s+\[(?<flags>[^\]]*)\](?:\s+(?<tm>[WB]):\s*(?<mn>\d+))?")]
    private static partial Regex ExaminedGameRegex();

    [GeneratedRegex(@"^\s*(?<game>\d+)\b")]
    private static partial Regex GameNumberOnlyRegex();

    /// <summary>
    /// Extracts the games from "games" output. Lines in other layouts (private games and so
    /// on) still yield an entry carrying just the game number so the game stays selectable;
    /// blank lines, the "games displayed" footer and lines with no leading number are skipped.
    /// </summary>
    public static IReadOnlyList<GameListEntry> Parse(string text)
    {
        List<GameListEntry> entries = new();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim('\r', ' ');

            // A listing line is about a hundred characters; the cap keeps the backtracking in the
            // fixed-width patterns bounded when the text is not really a listing.
            if (line.Length == 0 || line.Length > MaxLineLength || line.Contains("games displayed", StringComparison.Ordinal))
            {
                continue;
            }

            Match played = PlayedGameRegex().Match(line);
            if (played.Success && FicsNumber.TryParse(played.Groups["game"].Value, out int playedNumber))
            {
                entries.Add(new GameListEntry(
                    playedNumber,
                    played.Groups["white"].Value,
                    played.Groups["wr"].Value,
                    played.Groups["black"].Value,
                    played.Groups["br"].Value,
                    played.Groups["flags"].Value.Trim(),
                    line)
                {
                    WhiteClock = played.Groups["wc"].Value,
                    BlackClock = played.Groups["bc"].Value,
                    Material = played.Groups["mat"].Value,
                    ToMove = played.Groups["tm"].Value,
                    MoveNumber = played.Groups["mn"].Value,
                });
                continue;
            }

            Match examined = ExaminedGameRegex().Match(line);
            if (examined.Success && FicsNumber.TryParse(examined.Groups["game"].Value, out int examinedNumber))
            {
                entries.Add(new GameListEntry(
                    examinedNumber,
                    examined.Groups["white"].Value,
                    "Exam",
                    examined.Groups["black"].Value,
                    "Exam",
                    examined.Groups["flags"].Value.Trim(),
                    line)
                {
                    ToMove = examined.Groups["tm"].Value,
                    MoveNumber = examined.Groups["mn"].Value,
                });
                continue;
            }

            Match numberOnly = GameNumberOnlyRegex().Match(line);
            if (numberOnly.Success && FicsNumber.TryParse(numberOnly.Groups["game"].Value, out int number))
            {
                entries.Add(new GameListEntry(number, "", "", "", "", "", line));
            }
        }

        return entries;
    }
}
