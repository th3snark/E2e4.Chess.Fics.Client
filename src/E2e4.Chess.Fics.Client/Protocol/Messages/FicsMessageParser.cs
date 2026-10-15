using System.Text.RegularExpressions;

namespace E2e4.Chess.Fics.Protocol.Messages;

/// <summary>
/// Turns a single server line into a typed <see cref="FicsMessage"/>. Anything
/// unrecognised or malformed (including numbers that do not fit an int) comes back as
/// <see cref="UnknownLineMessage"/>; the parser deliberately has no catch-all, so a bug
/// shows up as an exception rather than as silently misparsed lines.
/// </summary>
public static partial class FicsMessageParser
{
    // Handles are alphanumeric; titles like "(U)", "(SR)(TM)", "(*)" may follow.
    private const string HandlePattern = @"(?<handle>[a-zA-Z][a-zA-Z0-9]*)";
    private const string TitlesPattern = @"(?<titles>(?:\([A-Z0-9*]+\))*)";

    [GeneratedRegex(@"^\{Game (?<num>\d+) \((?<white>\S+) vs\. (?<black>\S+)\) (?<reason>[^}]*)\}(?: (?<result>\S+))?$")]
    private static partial Regex GameNoteRegex();

    [GeneratedRegex($@"^{HandlePattern}{TitlesPattern} tells you: (?<text>.*)$")]
    private static partial Regex TellRegex();

    [GeneratedRegex($@"^{HandlePattern}{TitlesPattern}\((?<channel>\d+)\): (?<text>.*)$")]
    private static partial Regex ChannelTellRegex();

    [GeneratedRegex($@"^{HandlePattern}{TitlesPattern} (?<cshout>c-)?shouts: (?<text>.*)$")]
    private static partial Regex ShoutRegex();

    [GeneratedRegex($@"^--> {HandlePattern}{TitlesPattern}(?<text>.*)$")]
    private static partial Regex EmoteShoutRegex();

    [GeneratedRegex($@"^{HandlePattern}{TitlesPattern}\((?<rating>[^)]*)\)\[(?<game>\d+)\] (?<kind>kibitzes|whispers): (?<text>.*)$")]
    private static partial Regex KibitzWhisperRegex();

    [GeneratedRegex($@"^\s*\*\*ANNOUNCEMENT\*\* from {HandlePattern}: (?<text>.*)$")]
    private static partial Regex AnnouncementRegex();

    /// <summary>
    /// The longest line the parser will try to match. The server caps its own lines at about a
    /// kilobyte, but the framing layer allows a frame of several megabytes, so anything longer
    /// than this is treated as unrecognised rather than handed to the regular expressions.
    /// </summary>
    internal const int MaxLineLength = 8192;

    public static FicsMessage Parse(string line)
    {
        if (line.Length > MaxLineLength)
        {
            return new UnknownLineMessage { RawText = line };
        }

        if (line.StartsWith("<12> ", StringComparison.Ordinal))
        {
            return Style12.TryParse(line, out Style12? board)
                ? new Style12Message { RawText = line, Board = board! }
                : new UnknownLineMessage { RawText = line };
        }

        if (line.StartsWith("<g1> ", StringComparison.Ordinal))
        {
            return ParseGameInfo(line);
        }

        if (line.StartsWith("<b1> ", StringComparison.Ordinal))
        {
            return ParseBughouseHoldings(line);
        }

        if (line.StartsWith("<s> ", StringComparison.Ordinal) ||
            line.StartsWith("<sn> ", StringComparison.Ordinal))
        {
            return ParseSeekAd(line);
        }

        if (line == "<sc>" || line.StartsWith("<sc> ", StringComparison.Ordinal))
        {
            return new SeeksClearedMessage { RawText = line };
        }

        if (line.StartsWith("<sr> ", StringComparison.Ordinal))
        {
            return ParseSeekRemoved(line);
        }

        if (line.StartsWith("<pf> ", StringComparison.Ordinal) ||
            line.StartsWith("<pt> ", StringComparison.Ordinal))
        {
            return ParsePendingOffer(line);
        }

        if (line.StartsWith("<pr> ", StringComparison.Ordinal))
        {
            string[] prTokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (prTokens.Length >= 2 && FicsNumber.TryParse(prTokens[1], out int prIndex))
            {
                return new PendingRemovedMessage { RawText = line, Index = prIndex };
            }

            return new UnknownLineMessage { RawText = line };
        }

        if (line.StartsWith("<wa> ", StringComparison.Ordinal) ||
            line.StartsWith("<wd> ", StringComparison.Ordinal))
        {
            return ParsePin(line);
        }

        if (line.StartsWith("{Game ", StringComparison.Ordinal))
        {
            Match game = GameNoteRegex().Match(line);
            if (game.Success)
            {
                if (!FicsNumber.TryParse(game.Groups["num"].Value, out int gameNumber))
                {
                    return new UnknownLineMessage { RawText = line };
                }

                string white = game.Groups["white"].Value;
                string black = game.Groups["black"].Value;
                string reason = game.Groups["reason"].Value;
                string result = game.Groups["result"].Value;

                return result.Length > 0
                    ? new GameEndedMessage
                    {
                        RawText = line,
                        GameNumber = gameNumber,
                        WhiteName = white,
                        BlackName = black,
                        Reason = reason,
                        Result = result,
                    }
                    : new GameStartedMessage
                    {
                        RawText = line,
                        GameNumber = gameNumber,
                        WhiteName = white,
                        BlackName = black,
                        Reason = reason,
                    };
            }

            return new UnknownLineMessage { RawText = line };
        }

        if (line.StartsWith("Challenge: ", StringComparison.Ordinal))
        {
            return new ChallengeMessage { RawText = line, Details = line["Challenge: ".Length..] };
        }

        if (line.StartsWith("Creating: ", StringComparison.Ordinal))
        {
            return new CreatingMessage { RawText = line, Details = line["Creating: ".Length..] };
        }

        // Every chat form below contains ": " (or is the "--> " emote), so most other lines
        // (games lists, who output, ...) can skip the regexes altogether.
        if (!line.Contains(": ", StringComparison.Ordinal) && !line.StartsWith("--> ", StringComparison.Ordinal))
        {
            return new UnknownLineMessage { RawText = line };
        }

        Match announcement = AnnouncementRegex().Match(line);
        if (announcement.Success)
        {
            return new AnnouncementMessage
            {
                RawText = line,
                Handle = announcement.Groups["handle"].Value,
                Text = announcement.Groups["text"].Value,
            };
        }

        Match tell = TellRegex().Match(line);
        if (tell.Success)
        {
            return new TellMessage
            {
                RawText = line,
                Handle = tell.Groups["handle"].Value,
                Titles = tell.Groups["titles"].Value,
                Text = tell.Groups["text"].Value,
            };
        }

        Match kibitzWhisper = KibitzWhisperRegex().Match(line);
        if (kibitzWhisper.Success)
        {
            if (!FicsNumber.TryParse(kibitzWhisper.Groups["game"].Value, out int gameNumber))
            {
                return new UnknownLineMessage { RawText = line };
            }

            string handle = kibitzWhisper.Groups["handle"].Value;
            string rating = kibitzWhisper.Groups["rating"].Value;
            string text = kibitzWhisper.Groups["text"].Value;

            return kibitzWhisper.Groups["kind"].Value == "kibitzes"
                ? new KibitzMessage { RawText = line, Handle = handle, Rating = rating, GameNumber = gameNumber, Text = text }
                : new WhisperMessage { RawText = line, Handle = handle, Rating = rating, GameNumber = gameNumber, Text = text };
        }

        Match channelTell = ChannelTellRegex().Match(line);
        if (channelTell.Success)
        {
            if (!FicsNumber.TryParse(channelTell.Groups["channel"].Value, out int channel))
            {
                return new UnknownLineMessage { RawText = line };
            }

            return new ChannelTellMessage
            {
                RawText = line,
                Handle = channelTell.Groups["handle"].Value,
                Titles = channelTell.Groups["titles"].Value,
                Channel = channel,
                Text = channelTell.Groups["text"].Value,
            };
        }

        Match shout = ShoutRegex().Match(line);
        if (shout.Success)
        {
            return new ShoutMessage
            {
                RawText = line,
                Handle = shout.Groups["handle"].Value,
                Titles = shout.Groups["titles"].Value,
                Text = shout.Groups["text"].Value,
                IsCShout = shout.Groups["cshout"].Success && shout.Groups["cshout"].Value.Length > 0,
            };
        }

        Match emote = EmoteShoutRegex().Match(line);
        if (emote.Success)
        {
            return new ShoutMessage
            {
                RawText = line,
                Handle = emote.Groups["handle"].Value,
                Titles = emote.Groups["titles"].Value,
                Text = emote.Groups["text"].Value.TrimStart(),
                IsEmote = true,
            };
        }

        return new UnknownLineMessage { RawText = line };
    }

    /// <summary>Splits "key=value" tokens; the p= value of pendinfo runs to end of line.</summary>
    private static Dictionary<string, string> ParseKeyValueTokens(string[] tokens, int start, string? tailKey = null)
    {
        Dictionary<string, string> pairs = new(StringComparer.Ordinal);
        for (int i = start; i < tokens.Length; i++)
        {
            int eq = tokens[i].IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = tokens[i][..eq];
            string value = tokens[i][(eq + 1)..];
            if (key == tailKey)
            {
                value = value.Length > 0
                    ? value + " " + string.Join(' ', tokens[(i + 1)..])
                    : string.Join(' ', tokens[(i + 1)..]);
                pairs[key] = value.TrimEnd();
                break;
            }

            pairs[key] = value;
        }

        return pairs;
    }

    private static FicsMessage ParseSeekAd(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3 || !FicsNumber.TryParse(tokens[1], out int index))
        {
            return new UnknownLineMessage { RawText = line };
        }

        Dictionary<string, string> pairs = ParseKeyValueTokens(tokens, 2);
        if (!pairs.TryGetValue("w", out string? handle))
        {
            return new UnknownLineMessage { RawText = line };
        }

        // A missing or malformed time or increment is 0.
        _ = FicsNumber.TryParse(pairs.GetValueOrDefault("t"), out int time);
        _ = FicsNumber.TryParse(pairs.GetValueOrDefault("i"), out int increment);
        string color = pairs.GetValueOrDefault("c", "?");

        return new SeekAdMessage
        {
            RawText = line,
            Index = index,
            Handle = handle,
            TitleCode = pairs.GetValueOrDefault("ti", ""),
            Rating = pairs.GetValueOrDefault("rt", ""),
            Time = time,
            Increment = increment,
            Rated = pairs.GetValueOrDefault("r") == "r",
            GameType = pairs.GetValueOrDefault("tp", ""),
            Color = color.Length > 0 ? char.ToUpperInvariant(color[0]) : '?',
            RatingRange = pairs.GetValueOrDefault("rr", ""),
            Automatic = pairs.GetValueOrDefault("a") == "t",
            FormulaChecked = pairs.GetValueOrDefault("f") == "t",
            IsOwnSeek = tokens[0] == "<sn>",
        };
    }

    private static FicsMessage ParseSeekRemoved(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<int> ids = new();
        for (int i = 1; i < tokens.Length; i++)
        {
            if (FicsNumber.TryParse(tokens[i], out int id))
            {
                ids.Add(id);
            }
        }

        return ids.Count > 0
            ? new SeekRemovedMessage { RawText = line, Ids = ids.ToArray() }
            : new UnknownLineMessage { RawText = line };
    }

    private static FicsMessage ParsePendingOffer(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3 || !FicsNumber.TryParse(tokens[1], out int index))
        {
            return new UnknownLineMessage { RawText = line };
        }

        Dictionary<string, string> pairs = ParseKeyValueTokens(tokens, 2, tailKey: "p");
        if (!pairs.TryGetValue("w", out string? handle))
        {
            return new UnknownLineMessage { RawText = line };
        }

        return new PendingOfferMessage
        {
            RawText = line,
            Index = index,
            Handle = handle,
            OfferType = pairs.GetValueOrDefault("t", ""),
            Parameters = pairs.GetValueOrDefault("p", ""),
            IsIncoming = tokens[0] == "<pf>",
        };
    }

    private static FicsMessage ParsePin(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return new UnknownLineMessage { RawText = line };
        }

        return tokens[0] == "<wa>"
            ? new PlayerArrivedMessage
            {
                RawText = line,
                Handle = tokens[1],
                Data = tokens.Length > 2 ? string.Join(' ', tokens[2..]) : "",
            }
            : new PlayerDepartedMessage { RawText = line, Handle = tokens[1] };
    }

    private static FicsMessage ParseGameInfo(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !FicsNumber.TryParse(tokens[1], out int gameNumber))
        {
            return new UnknownLineMessage { RawText = line };
        }

        return new GameInfoMessage
        {
            RawText = line,
            GameNumber = gameNumber,
            Properties = ParseKeyValueTokens(tokens, 2),
        };
    }

    private static FicsMessage ParseBughouseHoldings(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // "<b1> game 6 white [PNBRQ] black [PNB]"
        if (tokens.Length >= 3 && tokens[1] == "game")
        {
            string gameToken = tokens[2].TrimEnd(',');
            if (FicsNumber.TryParse(gameToken, out int gameNumber))
            {
                return new BughouseHoldingsMessage { RawText = line, GameNumber = gameNumber };
            }
        }

        return new UnknownLineMessage { RawText = line };
    }
}
