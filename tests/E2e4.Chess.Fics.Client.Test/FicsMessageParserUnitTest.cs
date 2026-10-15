using E2e4.Chess.Fics.Protocol.Messages;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

public class FicsMessageParserUnitTest
{
    [Fact]
    public void Style12LineParsesToStyle12Message()
    {
        FicsMessage message = FicsMessageParser.Parse(FicsTestData.Style12StartPosition);

        Style12Message style12 = Assert.IsType<Style12Message>(message);
        Assert.Equal(314, style12.Board.GameNumber);
        Assert.Equal(FicsTestData.Style12StartPosition, style12.RawText);
    }

    [Fact]
    public void GameEndedLineFromTheCapturedGameParsesWithResult()
    {
        FicsMessage message = FicsMessageParser.Parse(FicsTestData.GameEndLine);

        GameEndedMessage ended = Assert.IsType<GameEndedMessage>(message);
        Assert.Equal(314, ended.GameNumber);
        Assert.Equal("GuestWHTE", ended.WhiteName);
        Assert.Equal("GuestBLCK", ended.BlackName);
        Assert.Equal("GuestBLCK resigns", ended.Reason);
        Assert.Equal("1-0", ended.Result);
    }

    [Fact]
    public void GameCreationNoteWithoutResultParsesAsGameStarted()
    {
        FicsMessage message = FicsMessageParser.Parse(
            "{Game 314 (GuestWHTE vs. GuestBLCK) Creating unrated blitz match.}");

        GameStartedMessage started = Assert.IsType<GameStartedMessage>(message);
        Assert.Equal(314, started.GameNumber);
        Assert.Equal("Creating unrated blitz match.", started.Reason);
    }

    [Fact]
    public void SeekAdLineParsesAllKeyValueFields()
    {
        FicsMessage message = FicsMessageParser.Parse(
            "<s> 8 w=visar ti=02 rt=2194 t=15 i=0 r=r tp=standard c=? rr=1800-9999 a=t f=f");

        SeekAdMessage seek = Assert.IsType<SeekAdMessage>(message);
        Assert.Equal(8, seek.Index);
        Assert.Equal("visar", seek.Handle);
        Assert.Equal("02", seek.TitleCode);
        Assert.Equal("2194", seek.Rating);
        Assert.Equal(15, seek.Time);
        Assert.Equal(0, seek.Increment);
        Assert.True(seek.Rated);
        Assert.Equal("standard", seek.GameType);
        Assert.Equal('?', seek.Color);
        Assert.Equal("1800-9999", seek.RatingRange);
        Assert.True(seek.Automatic);
        Assert.False(seek.FormulaChecked);
        Assert.False(seek.IsOwnSeek);
    }

    [Fact]
    public void OwnSeekLineIsMarkedAsOwnSeek()
    {
        FicsMessage message = FicsMessageParser.Parse(
            "<sn> 12 w=GuestWHTE ti=01 rt=0P t=2 i=12 r=u tp=blitz c=W rr=0-9999 a=f f=f");

        SeekAdMessage seek = Assert.IsType<SeekAdMessage>(message);
        Assert.True(seek.IsOwnSeek);
        Assert.Equal('W', seek.Color);
        Assert.False(seek.Rated);
        Assert.False(seek.Automatic);
    }

    [Fact]
    public void SeekRemovedLineCollectsAllIds()
    {
        FicsMessage message = FicsMessageParser.Parse("<sr> 3 25 51");

        SeekRemovedMessage removed = Assert.IsType<SeekRemovedMessage>(message);
        Assert.Equal(new[] { 3, 25, 51 }, removed.Ids);
    }

    [Fact]
    public void SeeksClearedLineParses()
    {
        Assert.IsType<SeeksClearedMessage>(FicsMessageParser.Parse("<sc>"));
    }

    [Fact]
    public void IncomingPendingOfferParsesHandleTypeAndParameters()
    {
        FicsMessage message = FicsMessageParser.Parse(
            "<pf> 3 w=GuestBLCK t=match p=GuestBLCK (----) GuestWHTE (----) unrated blitz 2 12");

        PendingOfferMessage offer = Assert.IsType<PendingOfferMessage>(message);
        Assert.True(offer.IsIncoming);
        Assert.Equal(3, offer.Index);
        Assert.Equal("GuestBLCK", offer.Handle);
        Assert.Equal("match", offer.OfferType);
        Assert.Equal("GuestBLCK (----) GuestWHTE (----) unrated blitz 2 12", offer.Parameters);
    }

    [Fact]
    public void OutgoingPendingOfferIsNotIncoming()
    {
        FicsMessage message = FicsMessageParser.Parse("<pt> 4 w=somebody t=draw p=");

        PendingOfferMessage offer = Assert.IsType<PendingOfferMessage>(message);
        Assert.False(offer.IsIncoming);
        Assert.Equal("draw", offer.OfferType);
    }

    [Fact]
    public void PendingRemovedLineParsesIndex()
    {
        PendingRemovedMessage removed =
            Assert.IsType<PendingRemovedMessage>(FicsMessageParser.Parse("<pr> 3"));

        Assert.Equal(3, removed.Index);
    }

    [Fact]
    public void PinArrivalAndDepartureLinesParseHandles()
    {
        PlayerArrivedMessage arrived =
            Assert.IsType<PlayerArrivedMessage>(FicsMessageParser.Parse("<wa> mekk 1400P1500E"));
        PlayerDepartedMessage departed =
            Assert.IsType<PlayerDepartedMessage>(FicsMessageParser.Parse("<wd> mekk"));

        Assert.Equal("mekk", arrived.Handle);
        Assert.Equal("1400P1500E", arrived.Data);
        Assert.Equal("mekk", departed.Handle);
    }

    [Fact]
    public void GameInfoLineParsesIntoPropertyBag()
    {
        FicsMessage message = FicsMessageParser.Parse(
            "<g1> 1 p=0 t=blitz r=1 u=1,1 it=5,5 i=8,8 pt=0 rt=1586E,2100 ts=1,0 m=2 n=0");

        GameInfoMessage info = Assert.IsType<GameInfoMessage>(message);
        Assert.Equal(1, info.GameNumber);
        Assert.Equal("blitz", info.Properties["t"]);
        Assert.Equal("1586E,2100", info.Properties["rt"]);
    }

    [Fact]
    public void DirectTellWithTitledHandleParses()
    {
        FicsMessage message = FicsMessageParser.Parse("GuestBLCK(U) tells you: good game");

        TellMessage tell = Assert.IsType<TellMessage>(message);
        Assert.Equal("GuestBLCK", tell.Handle);
        Assert.Equal("(U)", tell.Titles);
        Assert.Equal("good game", tell.Text);
    }

    [Fact]
    public void ChannelTellParsesChannelNumber()
    {
        FicsMessage message = FicsMessageParser.Parse("relay(TD)(49): Game starting shortly");

        ChannelTellMessage tell = Assert.IsType<ChannelTellMessage>(message);
        Assert.Equal("relay", tell.Handle);
        Assert.Equal("(TD)", tell.Titles);
        Assert.Equal(49, tell.Channel);
        Assert.Equal("Game starting shortly", tell.Text);
    }

    [Fact]
    public void ShoutAndCShoutLinesParse()
    {
        ShoutMessage shout = Assert.IsType<ShoutMessage>(
            FicsMessageParser.Parse("somebody shouts: anyone for blitz?"));
        ShoutMessage cshout = Assert.IsType<ShoutMessage>(
            FicsMessageParser.Parse("somebody c-shouts: chess talk only"));

        Assert.False(shout.IsCShout);
        Assert.Equal("anyone for blitz?", shout.Text);
        Assert.True(cshout.IsCShout);
    }

    [Fact]
    public void EmoteShoutParsesAsShoutWithEmoteFlag()
    {
        ShoutMessage shout = Assert.IsType<ShoutMessage>(
            FicsMessageParser.Parse("--> somebody waves hello"));

        Assert.True(shout.IsEmote);
        Assert.Equal("somebody", shout.Handle);
        Assert.Equal("waves hello", shout.Text);
    }

    [Fact]
    public void KibitzAndWhisperLinesParseRatingAndGameNumber()
    {
        KibitzMessage kibitz = Assert.IsType<KibitzMessage>(
            FicsMessageParser.Parse("watcher(1834)[314] kibitzes: nice move"));
        WhisperMessage whisper = Assert.IsType<WhisperMessage>(
            FicsMessageParser.Parse("watcher(----)[314] whispers: he missed Nf3"));

        Assert.Equal("watcher", kibitz.Handle);
        Assert.Equal("1834", kibitz.Rating);
        Assert.Equal(314, kibitz.GameNumber);
        Assert.Equal("nice move", kibitz.Text);
        Assert.Equal("----", whisper.Rating);
        Assert.Equal("he missed Nf3", whisper.Text);
    }

    [Fact]
    public void AnnouncementLineParses()
    {
        AnnouncementMessage announcement = Assert.IsType<AnnouncementMessage>(
            FicsMessageParser.Parse("    **ANNOUNCEMENT** from relay: FICS: There is a game to relay"));

        Assert.Equal("relay", announcement.Handle);
        Assert.Equal("FICS: There is a game to relay", announcement.Text);
    }

    [Fact]
    public void ChallengeAndCreatingLinesParse()
    {
        ChallengeMessage challenge = Assert.IsType<ChallengeMessage>(
            FicsMessageParser.Parse("Challenge: GuestBLCK (----) GuestWHTE (----) unrated blitz 2 12."));
        CreatingMessage creating = Assert.IsType<CreatingMessage>(
            FicsMessageParser.Parse("Creating: GuestWHTE (++++) GuestBLCK (++++) unrated blitz 2 12"));

        Assert.StartsWith("GuestBLCK", challenge.Details);
        Assert.StartsWith("GuestWHTE", creating.Details);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fics% ")]
    [InlineData("random server chatter with no structure")]
    [InlineData("<12> corrupted board line")]
    [InlineData("{Game not-a-number (A vs. B) x} 1-0")]
    public void UnrecognisedLinesFallBackToUnknownLineMessage(string line)
    {
        FicsMessage message = FicsMessageParser.Parse(line);

        Assert.IsType<UnknownLineMessage>(message);
        Assert.Equal(line, message.RawText);
    }
}
