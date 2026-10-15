using E2e4.Chess.Fics.Protocol;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

public class Style12UnitTest
{
    private const string AfterMoveLine =
        "<12> rnbqkbnr pppppppp -------- -------- ----P--- -------- PPPP-PPP RNBQKBNR B 4 1 1 1 1 0 314 A B -1 2 12 39 39 120 120 1 MOVE (0:01) e4 0 0 0";

    [Theory]
    [InlineData("P/e2-e4", "B", "e2", "e4")]
    [InlineData("N/g8-f6", "W", "g8", "f6")]
    [InlineData("P/e7-e8=Q", "W", "e7", "e8")]
    [InlineData("o-o", "B", "e1", "g1")]
    [InlineData("o-o-o", "B", "e1", "c1")]
    [InlineData("o-o", "W", "e8", "g8")]
    [InlineData("o-o-o", "W", "e8", "c8")]
    [InlineData("none", "W", null, null)]
    [InlineData("P/@@-e4", "W", null, null)]
    public void TheLastMoveSquaresComeFromTheVerboseMove(string verbose, string toMove, string? from, string? to)
    {
        string line = AfterMoveLine.Replace("MOVE", verbose).Replace(" B 4 ", $" {toMove} 4 ");
        Assert.True(Style12.TryParse(line, out Style12? style12));

        Assert.Equal(from, style12!.LastMoveFromSquare);
        Assert.Equal(to, style12.LastMoveToSquare);
    }

    [Fact]
    public void ParsingTheStartingPositionLineSetsEveryField()
    {
        bool parsed = Style12.TryParse(FicsTestData.Style12StartPosition, out Style12? style12);

        Assert.True(parsed);
        Assert.NotNull(style12);
        Assert.Equal("rnbqkbnr", style12!.Rank[0]);
        Assert.Equal("pppppppp", style12.Rank[1]);
        Assert.Equal("--------", style12.Rank[2]);
        Assert.Equal("PPPPPPPP", style12.Rank[6]);
        Assert.Equal("RNBQKBNR", style12.Rank[7]);
        Assert.Equal('W', style12.ToMove);
        Assert.Equal(-1, style12.DoublePawnPush);
        Assert.True(style12.WhiteCanCastleShort);
        Assert.True(style12.WhiteCanCastleLong);
        Assert.True(style12.BlackCanCastleShort);
        Assert.True(style12.BlackCanCastleLong);
        Assert.Equal(0, style12.MovesSinceLastIrreversibleMove);
        Assert.Equal(314, style12.GameNumber);
        Assert.Equal("GuestWHTE", style12.WhiteName);
        Assert.Equal("GuestBLCK", style12.BlackName);
        Assert.Equal(Style12.GameRelation.PlayingMyMove, style12.MyGameRelation);
        Assert.Equal(2, style12.TimeControlInitialMinutes);
        Assert.Equal(12, style12.TimeControlIncrementSeconds);
        Assert.Equal(39, style12.WhiteMaterialStrength);
        Assert.Equal(39, style12.BlackMaterialStrength);
        Assert.Equal(120, style12.WhiteTimeRemaining);
        Assert.Equal(120, style12.BlackTimeRemaining);
        Assert.Equal(1, style12.MoveNumber);
        Assert.Equal("none", style12.MoveVerbose);
        Assert.Equal(0, style12.MoveTimeTakenMinutes);
        Assert.Equal(0, style12.MoveTimeTakenSeconds);
        Assert.Equal("none", style12.MovePretty);
        Assert.Equal(0, style12.Orientation);
        Assert.False(style12.ClockTicking);
        Assert.Equal(0, style12.LagMilliseconds);
        Assert.Equal("", style12.UnparsedText);
    }

    [Fact]
    public void ToFenOfTheStartingPositionMatchesTheStandardStartFen()
    {
        Assert.True(Style12.TryParse(FicsTestData.Style12StartPosition, out Style12? style12));

        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", style12!.ToFen());
    }

    [Fact]
    public void ToFenAfterE4IncludesTheEnPassantSquareE3()
    {
        Assert.True(Style12.TryParse(FicsTestData.Style12AfterE4, out Style12? style12));

        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", style12!.ToFen());
    }

    [Fact]
    public void CastlingRightsDecayAfterWhiteCastles()
    {
        Assert.True(Style12.TryParse(FicsTestData.Style12AfterWhiteCastles, out Style12? style12));

        Assert.False(style12!.WhiteCanCastleShort);
        Assert.False(style12.WhiteCanCastleLong);
        Assert.True(style12.BlackCanCastleShort);
        Assert.True(style12.BlackCanCastleLong);
        Assert.Contains(" b - ", style12.ToFen().Replace(" kq ", " - "));
    }

    [Fact]
    public void LagFieldFromARealInGameLineIsParsed()
    {
        Assert.True(Style12.TryParse(FicsTestData.Style12AfterWhiteCastles, out Style12? style12));

        Assert.Equal(8647, style12!.LagMilliseconds);
        Assert.True(style12.ClockTicking);
    }

    [Fact]
    public void MoveDurationWithMillisecondsFromIsetMsIsParsed()
    {
        string line = FicsTestData.Style12AfterE4.Replace("(0:00)", "(0:02.345)");

        bool parsed = Style12.TryParse(line, out Style12? style12);

        Assert.True(parsed);
        Assert.Equal(0, style12!.MoveTimeTakenMinutes);
        Assert.Equal(2, style12.MoveTimeTakenSeconds);
        Assert.Equal(345, style12.MoveTimeTakenMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<12>")]
    [InlineData("<12> rnbqkbnr pppppppp")]
    [InlineData("not a style 12 line at all")]
    [InlineData("<12> rnbqkbnr pppppppp -------- -------- -------- -------- PPPPPPPP RNBQKBNR X -1 1 1 1 1 0 314 A B 1 2 12 39 39 120 120 1 none (0:00) none 0 0 0")]
    [InlineData("<12> rnbqkbnr pppppppp -------- -------- -------- -------- PPPPPPPP RNBQKBNR W -1 1 1 1 1 zz 314 A B 1 2 12 39 39 120 120 1 none (0:00) none 0 0 0")]
    public void MalformedLinesFailToParseWithoutThrowing(string line)
    {
        bool parsed = Style12.TryParse(line, out Style12? style12);

        Assert.False(parsed);
        Assert.Null(style12);
    }

    [Fact]
    public void ExtraTrailingFieldsLandInUnparsedText()
    {
        string line = FicsTestData.Style12StartPosition + " future1 future2";

        bool parsed = Style12.TryParse(line, out Style12? style12);

        Assert.True(parsed);
        Assert.Equal("future1 future2", style12!.UnparsedText);
    }

    [Fact]
    public void NegativeClockTimesFromAFlaggedGameAreParsed()
    {
        string line = FicsTestData.Style12StartPosition.Replace(" 120 120 ", " -3 120 ");

        bool parsed = Style12.TryParse(line, out Style12? style12);

        Assert.True(parsed);
        Assert.Equal(-3, style12!.WhiteTimeRemaining);
    }
}
