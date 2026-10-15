using System.Buffers;
using System.Text;
using E2e4.Chess.Fics.Protocol;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

public class FicsFrameTokenizerLimitsUnitTest
{
    private const char BlockStart = (char)0x15;
    private const char BlockSeparator = (char)0x16;
    private const char BlockEnd = (char)0x17;

    private static List<FicsFrame> Feed(FicsFrameTokenizer tokenizer, string wireText)
    {
        List<FicsFrame> frames = new();
        ReadOnlySequence<byte> buffer = new(Encoding.Latin1.GetBytes(wireText));
        while (tokenizer.TryReadFrame(ref buffer, out FicsFrame? frame))
        {
            frames.Add(frame!);
        }

        return frames;
    }

    [Fact]
    public void UnterminatedLineLongerThanTheLimitIsRejected()
    {
        FicsFrameTokenizer tokenizer = new() { MaxFrameLength = 1000 };

        Assert.Throws<InvalidDataException>(() => Feed(tokenizer, new string('a', 2000)));
    }

    [Fact]
    public void UnterminatedBlockLongerThanTheLimitIsRejected()
    {
        FicsFrameTokenizer tokenizer = new() { MaxFrameLength = 1000 };

        Assert.Throws<InvalidDataException>(() => Feed(tokenizer, BlockStart + "1" + BlockSeparator + "0" + BlockSeparator + new string('x', 5000)));
    }

    [Fact]
    public void TheLimitAppliesToOneFrameNotToTheWholeRead()
    {
        FicsFrameTokenizer tokenizer = new() { MaxFrameLength = 1000 };
        string wire = string.Concat(Enumerable.Range(0, 200).Select(i => $"line number {i:D4}\n\r"));

        List<FicsFrame> frames = Feed(tokenizer, wire);

        Assert.Equal(200, frames.Count);
        Assert.Equal("line number 0199", frames[^1].Text);
    }

    [Fact]
    public void AFrameJustUnderTheLimitIsAccepted()
    {
        FicsFrameTokenizer tokenizer = new() { MaxFrameLength = 1000 };

        List<FicsFrame> frames = Feed(tokenizer, new string('a', 900) + "\n\r");

        Assert.Single(frames);
        Assert.Equal(900, frames[0].Text.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LimitMustBePositive(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FicsFrameTokenizer { MaxFrameLength = limit });
    }

    [Fact]
    public void ManyFramesInOneReadAreTokenisedInOrder()
    {
        FicsFrameTokenizer tokenizer = new();
        string wire = string.Concat(Enumerable.Range(0, 20000).Select(i => $"line {i}\n\r"));

        List<FicsFrame> frames = Feed(tokenizer, wire);

        Assert.Equal(20000, frames.Count);
        Assert.Equal("line 0", frames[0].Text);
        Assert.Equal("line 12345", frames[12345].Text);
        Assert.Equal("line 19999", frames[^1].Text);
    }

    [Fact]
    public void MalformedBlockHeaderFallsBackToIdAndCodeZero()
    {
        FicsFrameTokenizer tokenizer = new();

        List<FicsFrame> frames = Feed(tokenizer, BlockStart + "x" + BlockSeparator + "y" + BlockSeparator + "payload" + BlockEnd);

        FicsFrame frame = Assert.Single(frames);
        Assert.Equal(FicsFrameKind.Block, frame.Kind);
        Assert.Equal(0, frame.BlockCommandId);
        Assert.Equal(0, frame.BlockCode);
        Assert.Equal("payload", frame.Text);
    }

    [Fact]
    public void SignedBlockHeaderNumbersAreNotAccepted()
    {
        FicsFrameTokenizer tokenizer = new();

        FicsFrame frame = Assert.Single(Feed(tokenizer, BlockStart + "-5" + BlockSeparator + "+7" + BlockSeparator + "p" + BlockEnd));

        Assert.Equal(0, frame.BlockCommandId);
        Assert.Equal(0, frame.BlockCode);
    }

    [Fact]
    public void TheTokenizerKeepsWorkingAfterABlockLargerThanItsInitialBuffer()
    {
        FicsFrameTokenizer tokenizer = new();
        string payload = new('z', 100_000);

        List<FicsFrame> frames = Feed(tokenizer, BlockStart + "3" + BlockSeparator + "0" + BlockSeparator + payload + BlockEnd + "after\n\r");

        Assert.Equal(2, frames.Count);
        Assert.Equal(payload, frames[0].Text);
        Assert.Equal("after", frames[1].Text);
    }
}
