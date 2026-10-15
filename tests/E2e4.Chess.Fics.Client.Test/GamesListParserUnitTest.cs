using E2e4.Chess.Fics.Protocol;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

public class GamesListParserUnitTest
{
    private const string PlayedLine = "100  1719 Sisyphus     1691 guest2      [ br  2  12]   1:34 -   0:39 (34-39) W: 25";
    private const string UnratedLine = " 37 ++++ GuestAAAA    ---- GuestBBBB   [ uu  5   0]   4:59 -   5:00 (39-39) B:  2";
    private const string ExaminedLine = " 13 (Exam.  0 white  0 black ) [ uu  0  0] W: 1";
    private const string Footer = "  3 games displayed.";

    [Fact]
    public void ParsesAPlayedGameRow()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(PlayedLine));

        Assert.Equal(100, entry.GameNumber);
        Assert.Equal("Sisyphus", entry.White);
        Assert.Equal("1719", entry.WhiteRating);
        Assert.Equal("guest2", entry.Black);
        Assert.Equal("1691", entry.BlackRating);
        Assert.Equal("br  2  12", entry.Flags);
        Assert.Equal(PlayedLine, entry.RawLine);
    }

    [Fact]
    public void ParsesTheClocksMaterialSideToMoveAndMoveNumber()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(PlayedLine));

        Assert.Equal("1:34", entry.WhiteClock);
        Assert.Equal("0:39", entry.BlackClock);
        Assert.Equal("34-39", entry.Material);
        Assert.Equal("W", entry.ToMove);
        Assert.Equal("25", entry.MoveNumber);
    }

    [Fact]
    public void ParsesAnExaminedGamesSideToMoveAndMoveNumber()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(ExaminedLine));

        Assert.Equal("", entry.WhiteClock);
        Assert.Equal("W", entry.ToMove);
        Assert.Equal("1", entry.MoveNumber);
    }

    [Fact]
    public void ParsesANegativeClock()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(" 37 1500 Alice 1600 Bob [ br  5  0]  -0:05 -   4:00 (39-39) B: 40"));

        Assert.Equal("-0:05", entry.WhiteClock);
        Assert.Equal("B", entry.ToMove);
    }

    [Fact]
    public void ParsesUnratedAndGuestPlaceholdersInTheRatingColumns()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(UnratedLine));

        Assert.Equal(37, entry.GameNumber);
        Assert.Equal("++++", entry.WhiteRating);
        Assert.Equal("----", entry.BlackRating);
        Assert.Equal("GuestAAAA", entry.White);
        Assert.Equal("GuestBBBB", entry.Black);
    }

    [Fact]
    public void ParsesAnExaminedGame()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(ExaminedLine));

        Assert.Equal(13, entry.GameNumber);
        Assert.Equal("white", entry.White);
        Assert.Equal("black", entry.Black);
        Assert.Equal("Exam", entry.WhiteRating);
        Assert.Equal("Exam", entry.BlackRating);
        Assert.Equal("uu  0  0", entry.Flags);
    }

    [Fact]
    public void ALineInAnotherLayoutStillYieldsItsGameNumber()
    {
        GameListEntry entry = Assert.Single(GamesListParser.Parse(" 55 some private game layout"));

        Assert.Equal(55, entry.GameNumber);
        Assert.Equal("", entry.White);
        Assert.Equal("", entry.Flags);
    }

    [Fact]
    public void SkipsBlankLinesTheFooterAndLinesWithoutAGameNumber()
    {
        string text = string.Join("\n", "", PlayedLine, "   ", "Some chatter that is not a game", UnratedLine, Footer, "");

        Assert.Equal(new[] { 100, 37 }, GamesListParser.Parse(text).Select(e => e.GameNumber));
    }

    [Fact]
    public void HandlesCarriageReturnLineEndings()
    {
        string text = PlayedLine + "\r\n" + UnratedLine + "\r\n" + Footer + "\r\n";

        Assert.Equal(2, GamesListParser.Parse(text).Count);
    }

    [Fact]
    public void EmptyTextGivesNoEntries()
    {
        Assert.Empty(GamesListParser.Parse(""));
    }

    [Fact]
    public void AGameNumberBeyondInt32IsSkippedNotThrown()
    {
        Assert.Empty(GamesListParser.Parse("99999999999999999999 whatever"));
    }
}
