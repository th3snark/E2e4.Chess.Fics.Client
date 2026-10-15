using System.Buffers;
using System.Text;
using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Protocol;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>
/// Replays the captured FICS session transcripts (the Fixtures folder) through the tokenizer
/// and parser. The logs are terminal transcripts written by an earlier test harness, so the
/// wire form has to be reconstructed: strip the UTF-8 BOM and the harness-injected ". "
/// annotation lines, and turn "\r\n" back into the "\n\r" FICS actually sends.
/// </summary>
public class LogTranscriptUnitTest
{
    private static byte[] LoadWireBytes(string fixtureName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        string text = File.ReadAllText(path, Encoding.UTF8);

        IEnumerable<string> lines = text
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.StartsWith(". ", StringComparison.Ordinal));

        // A real session ends when the connection drops; terminate the final line
        // so the tokenizer flushes it.
        string wire = string.Join("\n\r", lines) + "\n\r";
        return Encoding.Latin1.GetBytes(wire);
    }

    private static List<FicsFrame> Tokenize(byte[] wireBytes, int chunkSize)
    {
        FicsFrameTokenizer tokenizer = new();
        List<FicsFrame> frames = new();

        for (int offset = 0; offset < wireBytes.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, wireBytes.Length - offset);
            ReadOnlySequence<byte> buffer = new(wireBytes, offset, length);
            while (tokenizer.TryReadFrame(ref buffer, out FicsFrame? frame))
            {
                frames.Add(frame!);
            }
        }

        return frames;
    }

    [Theory]
    [InlineData("01 Without OpenSeal.txt", 1)]
    [InlineData("01 Without OpenSeal.txt", 7)]
    [InlineData("01 Without OpenSeal.txt", 4096)]
    [InlineData("02 With OpenSeal on.txt", 1)]
    [InlineData("02 With OpenSeal on.txt", 7)]
    [InlineData("02 With OpenSeal on.txt", 4096)]
    [InlineData("03 Full Game.txt", 1)]
    [InlineData("03 Full Game.txt", 7)]
    [InlineData("03 Full Game.txt", 4096)]
    public void EveryTranscriptTokenizesAndParsesWithoutThrowingAtAnyChunkSize(string fixtureName, int chunkSize)
    {
        byte[] wireBytes = LoadWireBytes(fixtureName);

        List<FicsFrame> frames = Tokenize(wireBytes, chunkSize);

        Assert.NotEmpty(frames);
        foreach (FicsFrame frame in frames.Where(f => f.Kind == FicsFrameKind.Line))
        {
            FicsMessage message = FicsMessageParser.Parse(frame.Text);
            Assert.NotNull(message);
        }
    }

    [Fact]
    public void FullGameTranscriptYieldsAllBoardUpdatesAndTheGameEnd()
    {
        byte[] wireBytes = LoadWireBytes("02 With OpenSeal on.txt");

        List<FicsFrame> frames = Tokenize(wireBytes, 4096);
        List<FicsMessage> messages = frames
            .Where(f => f.Kind == FicsFrameKind.Line)
            .Select(f => FicsMessageParser.Parse(f.Text))
            .ToList();

        // The transcript contains nine <12> lines (see the fixture).
        Assert.Equal(9, messages.OfType<Style12Message>().Count());
        GameEndedMessage ended = Assert.Single(messages.OfType<GameEndedMessage>());
        Assert.Equal("1-0", ended.Result);

        // The four [G] timeseal pings must surface as ping frames, not lines.
        Assert.Equal(4, frames.Count(f => f.Kind == FicsFrameKind.TimesealPing));
    }

    [Fact]
    public void TranscriptChunkSizeDoesNotChangeTheResultingFrames()
    {
        byte[] wireBytes = LoadWireBytes("03 Full Game.txt");

        List<FicsFrame> byOne = Tokenize(wireBytes, 1);
        List<FicsFrame> byBlock = Tokenize(wireBytes, 4096);

        Assert.Equal(byBlock.Count, byOne.Count);
        for (int i = 0; i < byBlock.Count; i++)
        {
            Assert.Equal(byBlock[i].Kind, byOne[i].Kind);
            Assert.Equal(byBlock[i].Text, byOne[i].Text);
        }
    }
}
