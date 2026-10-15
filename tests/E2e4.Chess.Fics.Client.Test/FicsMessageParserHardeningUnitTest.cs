using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Protocol.Messages;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>
/// The parser sees whatever a server (or a man in the middle) sends. It must turn
/// malformed input into <see cref="UnknownLineMessage"/> by construction, not by
/// catching its own bugs.
/// </summary>
public class FicsMessageParserHardeningUnitTest
{
    private const string HugeNumber = "99999999999999999999";

    [Fact]
    public void ALineBeyondTheLengthCapIsUnknownWithoutBeingMatched()
    {
        string line = "someone tells you: " + new string('x', FicsMessageParser.MaxLineLength);

        Assert.IsType<UnknownLineMessage>(FicsMessageParser.Parse(line));
        Assert.IsType<TellMessage>(FicsMessageParser.Parse("someone tells you: hello"));
    }

    [Fact]
    public void AnOversizedGamesListLineIsSkippedQuickly()
    {
        string hostile = "1 " + string.Concat(Enumerable.Repeat("1 a ", GamesListParser.MaxLineLength));

        Assert.Empty(GamesListParser.Parse(hostile));
    }

    public static TheoryData<string> OverflowingNumberLines() => new()
    {
        $"{{Game {HugeNumber} (a vs. b) Creating rated blitz match.}}",
        $"{{Game {HugeNumber} (a vs. b) b resigns}} 1-0",
        $"relay(TD)({HugeNumber}): hello",
        $"watcher(1834)[{HugeNumber}] kibitzes: nice move",
        $"watcher(1834)[{HugeNumber}] whispers: hmm",
        $"<s> {HugeNumber} w=somebody t=5 i=3",
        $"<g1> {HugeNumber} p=0",
        $"<b1> game {HugeNumber} white [P] black [N]",
        $"<pf> {HugeNumber} w=somebody t=match p=blitz",
        $"<pr> {HugeNumber}",
        $"<sr> {HugeNumber}",
    };

    [Theory]
    [MemberData(nameof(OverflowingNumberLines))]
    public void NumbersBeyondInt32AreRejectedAsUnknownLines(string line)
    {
        FicsMessage message = FicsMessageParser.Parse(line);

        Assert.IsType<UnknownLineMessage>(message);
        Assert.Equal(line, message.RawText);
    }

    [Fact]
    public void OverflowingTimeAndIncrementInASeekAdFallBackToZero()
    {
        SeekAdMessage seek = Assert.IsType<SeekAdMessage>(
            FicsMessageParser.Parse($"<s> 7 w=somebody t={HugeNumber} i={HugeNumber} r=r tp=blitz"));

        Assert.Equal(7, seek.Index);
        Assert.Equal(0, seek.Time);
        Assert.Equal(0, seek.Increment);
    }

    [Fact]
    public void AnOverflowingIdAmongSeekRemovalsIsSkipped()
    {
        SeekRemovedMessage removed = Assert.IsType<SeekRemovedMessage>(
            FicsMessageParser.Parse($"<sr> {HugeNumber} 3 25"));

        Assert.Equal(new[] { 3, 25 }, removed.Ids);
    }

    [Fact]
    public void NonAsciiDigitsMatchedByTheRegexAreRejectedNotThrown()
    {
        string arabicIndicOne = ((char)0x0661).ToString();

        FicsMessage message = FicsMessageParser.Parse($"{{Game {arabicIndicOne} (a vs. b) Creating rated blitz match.}}");

        Assert.IsType<UnknownLineMessage>(message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("<")]
    [InlineData("<12>")]
    [InlineData("<12> ")]
    [InlineData("<g1>")]
    [InlineData("<g1> ")]
    [InlineData("<b1> game")]
    [InlineData("<s>")]
    [InlineData("<s> ")]
    [InlineData("<sr>")]
    [InlineData("<sr> ")]
    [InlineData("<pf>")]
    [InlineData("<pr>")]
    [InlineData("<wa>")]
    [InlineData("<wd> ")]
    [InlineData("{Game")]
    [InlineData("{Game }")]
    [InlineData("Challenge:")]
    [InlineData("--> ")]
    [InlineData(" tells you: ")]
    [InlineData("(U) tells you: hi")]
    [InlineData("a tells you:")]
    public void DegenerateLinesDoNotThrow(string line)
    {
        FicsMessage message = FicsMessageParser.Parse(line);

        Assert.Equal(line, message.RawText);
    }

    [Fact]
    public void Style12WithAnOverflowingFieldFailsToParseWithoutThrowing()
    {
        string line = FicsTestData.Style12StartPosition.Replace(" 314 ", $" {HugeNumber} ");

        Assert.False(Style12.TryParse(line, out Style12? board));
        Assert.Null(board);
    }

    [Fact]
    public void Style12AcceptsNegativeClockValues()
    {
        Assert.True(Style12.TryParse(FicsTestData.Style12StartPosition.Replace(" 120 120 ", " -1500 120 "), out Style12? board));
        Assert.Equal(-1500, board!.WhiteTimeRemaining);
    }

    public static TheoryData<string> CorpusLines() => new()
    {
        FicsTestData.Style12StartPosition,
        FicsTestData.Style12AfterE4,
        FicsTestData.Style12AfterWhiteCastles,
        FicsTestData.GameEndLine,
        "{Game 314 (GuestWHTE vs. GuestBLCK) Creating unrated blitz match.}",
        "<g1> 7 p=0 t=blitz r=u u=1,1 it=300,300 i=300,300 pt=0 rt=0,0 ts=1,1 m=2 n=1",
        "<b1> game 6 white [PNBRQ] black [PNB]",
        "<s> 12 w=GuestABCD ti=00 rt=1500P t=5 i=0 r=r tp=blitz c=? rr=0-9999 a=t f=f",
        "<sn> 3 w=somebody ti=02 rt=2100E t=15 i=10 r=u tp=standard c=W rr=1500-2500 a=f f=t",
        "<sr> 3 25 51",
        "<sc>",
        "<pf> 4 w=somebody t=match p=somebody (1500) GuestABCD (1600) unrated blitz 5 0",
        "<pt> 4 w=somebody t=draw p=",
        "<pr> 3",
        "<wa> mekk 1400P1500E",
        "<wd> mekk",
        "GuestBLCK(U) tells you: good game",
        "relay(TD)(49): Game starting shortly",
        "somebody shouts: anyone for blitz?",
        "somebody c-shouts: chess talk only",
        "--> somebody waves hello",
        "watcher(1834)[314] kibitzes: nice move",
        "watcher(----)[314] whispers: he missed Nf3",
        "    **ANNOUNCEMENT** from relay: FICS: There is a game to relay",
        "Challenge: GuestBLCK (----) GuestWHTE (----) unrated blitz 2 12.",
        "Creating: GuestWHTE (++++) GuestBLCK (++++) unrated blitz 2 12",
    };

    [Theory]
    [MemberData(nameof(CorpusLines))]
    public void RandomlyDamagedServerLinesNeverThrow(string original)
    {
        // Deterministic: the seed is derived from the line so a failure reproduces.
        Random random = new(original.Aggregate(17, (hash, ch) => unchecked(hash * 31 + ch)));
        string noise = "0123456789 -+()[]{}<>:=!*/.,pPkKwWbB" + (char)0x0661 + (char)0xFF;

        for (int round = 0; round < 400; round++)
        {
            string line = original;
            int edits = 1 + random.Next(4);
            for (int edit = 0; edit < edits && line.Length > 0; edit++)
            {
                int at = random.Next(line.Length);
                line = random.Next(5) switch
                {
                    0 => line.Remove(at, 1),
                    1 => line.Insert(at, noise[random.Next(noise.Length)].ToString()),
                    2 => line[..at],
                    3 => line[..at] + line[at..] + line[at..],
                    _ => line.Remove(at, 1).Insert(at, noise[random.Next(noise.Length)].ToString()),
                };
            }

            FicsMessage message = FicsMessageParser.Parse(line);
            Assert.Equal(line, message.RawText);
            _ = Style12.TryParse(line, out _);
        }
    }
}
