using System.Buffers;
using E2e4.Chess.Fics.Protocol;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

public class FicsFrameTokenizerUnitTest
{
    private static List<FicsFrame> Feed(FicsFrameTokenizer tokenizer, byte[] bytes)
    {
        List<FicsFrame> frames = new();
        ReadOnlySequence<byte> buffer = new(bytes);
        while (tokenizer.TryReadFrame(ref buffer, out FicsFrame? frame))
        {
            frames.Add(frame!);
        }

        return frames;
    }

    private static List<FicsFrame> FeedText(FicsFrameTokenizer tokenizer, string wireText)
    {
        return Feed(tokenizer, FicsTestData.WireBytes(wireText));
    }

    [Fact]
    public void LinesTerminatedByNewlineCarriageReturnAreSplitCorrectly()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "first line\n\rsecond line\n\r");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsFrameKind.Line, frames[0].Kind);
        Assert.Equal("first line", frames[0].Text);
        Assert.Equal("second line", frames[1].Text);
    }

    [Theory]
    [InlineData("line one\nline two\n")]
    [InlineData("line one\r\nline two\r\n")]
    [InlineData("line one\n\rline two\n\r")]
    public void AllThreeLineTerminatorStylesProduceTheSameLines(string wireText)
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, wireText);

        Assert.Equal(2, frames.Count);
        Assert.Equal("line one", frames[0].Text);
        Assert.Equal("line two", frames[1].Text);
    }

    [Fact]
    public void PromptWithoutNewlineIsEmittedAsPromptFrame()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "some output\n\rfics% ");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsFrameKind.Line, frames[0].Kind);
        Assert.Equal(FicsFrameKind.Prompt, frames[1].Kind);
        Assert.Equal("fics% ", frames[1].Text);
    }

    [Fact]
    public void LoginPromptAtEndOfBannerIsEmittedAsPromptFrame()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "some banner text\n\rlogin: ");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsFrameKind.Prompt, frames[1].Kind);
        Assert.Equal("login: ", frames[1].Text);
    }

    [Fact]
    public void PromptSplitAcrossTwoReadsIsRecognisedOnceComplete()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> first = FeedText(tokenizer, "fic");
        List<FicsFrame> second = FeedText(tokenizer, "s% ");

        Assert.Empty(first);
        FicsFrame frame = Assert.Single(second);
        Assert.Equal(FicsFrameKind.Prompt, frame.Kind);
    }

    [Fact]
    public void PromptFollowedByBufferedOutputIsStillRecognisedAsPrompt()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "fics% \n\rnext line\n\r");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsFrameKind.Prompt, frames[0].Kind);
        Assert.Equal("next line", frames[1].Text);
    }

    [Fact]
    public void TerminatorPairSplitAcrossReadsDoesNotProduceAnEmptyLine()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> first = Feed(tokenizer, FicsTestData.WireBytes("hello\n"));
        List<FicsFrame> second = Feed(tokenizer, FicsTestData.WireBytes("\rworld\n\r"));

        FicsFrame firstFrame = Assert.Single(first);
        Assert.Equal("hello", firstFrame.Text);
        FicsFrame secondFrame = Assert.Single(second);
        Assert.Equal("world", secondFrame.Text);
    }

    [Fact]
    public void TelnetWillEchoSequenceIsStrippedFromTheStream()
    {
        FicsFrameTokenizer tokenizer = new();
        byte[] bytes = { (byte)'a', 0xFF, 0xFB, 0x01, (byte)'b', (byte)'\n', (byte)'\r' };

        List<FicsFrame> frames = Feed(tokenizer, bytes);

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal("ab", frame.Text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TelnetSequenceSplitAtEveryByteBoundaryIsStillStripped(int splitIndex)
    {
        FicsFrameTokenizer tokenizer = new();
        byte[] bytes = { (byte)'a', 0xFF, 0xFB, 0x01, (byte)'b', (byte)'\n', (byte)'\r' };

        List<FicsFrame> frames = Feed(tokenizer, bytes[..splitIndex]);
        frames.AddRange(Feed(tokenizer, bytes[splitIndex..]));

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal("ab", frame.Text);
    }

    [Fact]
    public void EscapedIacIacBecomesOneLiteral0xFFByte()
    {
        FicsFrameTokenizer tokenizer = new();
        byte[] bytes = { (byte)'x', 0xFF, 0xFF, (byte)'y', (byte)'\n', (byte)'\r' };

        List<FicsFrame> frames = Feed(tokenizer, bytes);

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal("xÿy", frame.Text);
    }

    [Fact]
    public void TelnetSubnegotiationIsDroppedEntirely()
    {
        FicsFrameTokenizer tokenizer = new();
        byte[] bytes = { (byte)'a', 0xFF, 0xFA, 0x18, 0x01, 0x02, 0xFF, 0xF0, (byte)'b', (byte)'\n', (byte)'\r' };

        List<FicsFrame> frames = Feed(tokenizer, bytes);

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal("ab", frame.Text);
    }

    [Fact]
    public void BlockFrameInOneChunkIsParsedIntoIdCodeAndPayload()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "\u00153\u001680\u0016some output\u0017");

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal(FicsFrameKind.Block, frame.Kind);
        Assert.Equal(3, frame.BlockCommandId);
        Assert.Equal(80, frame.BlockCode);
        Assert.Equal("some output", frame.Text);
    }

    [Fact]
    public void BlockFrameSpanningMultipleReadsIsAccumulated()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> first = FeedText(tokenizer, "\u00151\u001637\u0016partial ");
        List<FicsFrame> second = FeedText(tokenizer, "response\u0017");

        Assert.Empty(first);
        FicsFrame frame = Assert.Single(second);
        Assert.Equal(1, frame.BlockCommandId);
        Assert.Equal(37, frame.BlockCode);
        Assert.Equal("partial response", frame.Text);
    }

    [Fact]
    public void BlockPayloadMayContainLineTerminators()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "\u00152\u001651\u0016line one\n\rline two\n\r\u0017after\n\r");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsFrameKind.Block, frames[0].Kind);
        Assert.Equal("line one\n\rline two\n\r", frames[0].Text);
        Assert.Equal("after", frames[1].Text);
    }

    [Fact]
    public void PromptDirectlyBeforeBlockStartIsEmittedAsPrompt()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "fics% \u00151\u0016155\u0016done\u0017");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsFrameKind.Prompt, frames[0].Kind);
        Assert.Equal(FicsFrameKind.Block, frames[1].Kind);
    }

    [Fact]
    public void TimesealPingLineBecomesTimesealPingFrame()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "[G]\n\r");

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal(FicsFrameKind.TimesealPing, frame.Kind);
    }

    [Fact]
    public void PartialTimesealPingWithoutTerminatorProducesNoFrame()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "[G");

        Assert.Empty(frames);
    }

    [Fact]
    public void LeadingBellCharacterIsStrippedFromLineContent()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, "\a\n\r<12> some board\n\r");

        Assert.Equal(2, frames.Count);
        Assert.Equal("", frames[0].Text);
        Assert.Equal("<12> some board", frames[1].Text);
    }

    [Fact]
    public void RealStyle12LineRoundTripsThroughTheTokenizer()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = FeedText(tokenizer, FicsTestData.Style12StartPosition + "\n\rfics% ");

        Assert.Equal(2, frames.Count);
        Assert.Equal(FicsTestData.Style12StartPosition, frames[0].Text);
        Assert.Equal(FicsFrameKind.Prompt, frames[1].Kind);
    }

    [Fact]
    public void CustomPromptAddedToPromptsListIsRecognised()
    {
        FicsFrameTokenizer tokenizer = new();
        tokenizer.Prompts.Add("chess% ");

        List<FicsFrame> frames = FeedText(tokenizer, "chess% ");

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal(FicsFrameKind.Prompt, frame.Kind);
    }
}
