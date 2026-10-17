using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Client.Test.Support;
using E2e4.Chess.Fics.Client.Transport;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

public class BlockCommandTrackerUnitTest
{
    private static TaskCompletionSource<FicsResponse> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static FicsResponse Response(int id) => new(id, 0, "text", Array.Empty<FicsMessage>());

    [Fact]
    public async Task HandsOutIdsOneToNineInOrder()
    {
        BlockCommandTracker tracker = new();

        List<int> ids = new();
        for (int i = 0; i < 9; i++)
        {
            ids.Add(await tracker.RentAsync(NewCompletion(), CancellationToken.None));
        }

        Assert.Equal(Enumerable.Range(1, 9), ids);
    }

    [Fact]
    public async Task TenthRentWaitsUntilAnIdIsCompleted()
    {
        BlockCommandTracker tracker = new();
        for (int i = 0; i < 9; i++)
        {
            await tracker.RentAsync(NewCompletion(), CancellationToken.None);
        }

        Task<int> tenth = tracker.RentAsync(NewCompletion(), CancellationToken.None);
        await Task.Delay(150);
        Assert.False(tenth.IsCompleted);

        Assert.True(tracker.TryComplete(4, Response(4)));

        Assert.Equal(4, await Soon(tenth));
    }

    [Fact]
    public async Task IdsKeepCyclingAfterTheyAreFreed()
    {
        BlockCommandTracker tracker = new();

        List<int> ids = new();
        for (int i = 0; i < 20; i++)
        {
            int id = await tracker.RentAsync(NewCompletion(), CancellationToken.None);
            ids.Add(id);
            Assert.True(tracker.TryComplete(id, Response(id)));
        }

        Assert.Equal(Enumerable.Range(0, 20).Select(i => i % 9 + 1), ids);
    }

    [Fact]
    public async Task CompletingDeliversTheResponseToTheRentersCompletionOnce()
    {
        BlockCommandTracker tracker = new();
        TaskCompletionSource<FicsResponse> completion = NewCompletion();
        int id = await tracker.RentAsync(completion, CancellationToken.None);
        FicsResponse response = Response(id);

        Assert.True(tracker.TryComplete(id, response));
        Assert.Same(response, await Soon(completion.Task));
        Assert.False(tracker.TryComplete(id, response));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(500)]
    public void OutOfRangeAndUnrentedIdsAreNotCompleted(int id)
    {
        BlockCommandTracker tracker = new();

        Assert.False(tracker.TryComplete(id, Response(id)));
    }

    [Fact]
    public async Task FaultAllFaultsEveryPendingCommandAndFreesTheirIds()
    {
        BlockCommandTracker tracker = new();
        List<TaskCompletionSource<FicsResponse>> completions = new();
        for (int i = 0; i < 9; i++)
        {
            TaskCompletionSource<FicsResponse> completion = NewCompletion();
            completions.Add(completion);
            await tracker.RentAsync(completion, CancellationToken.None);
        }

        tracker.FaultAll(new FicsException("closed"));

        foreach (TaskCompletionSource<FicsResponse> completion in completions)
        {
            await Assert.ThrowsAsync<FicsException>(() => Soon(completion.Task));
        }

        for (int i = 0; i < 9; i++)
        {
            await Soon(tracker.RentAsync(NewCompletion(), CancellationToken.None));
        }
    }

    [Fact]
    public async Task ReleaseFreesAnIdOnlyForItsOwnCompletion()
    {
        BlockCommandTracker tracker = new();
        TaskCompletionSource<FicsResponse> owner = NewCompletion();
        int id = await tracker.RentAsync(owner, CancellationToken.None);
        for (int i = 0; i < 8; i++)
        {
            await tracker.RentAsync(NewCompletion(), CancellationToken.None);
        }

        tracker.Release(id, NewCompletion());
        Task<int> blocked = tracker.RentAsync(NewCompletion(), CancellationToken.None);
        await Task.Delay(150);
        Assert.False(blocked.IsCompleted, "Releasing with someone else's completion must not free the id.");

        tracker.Release(id, owner);

        Assert.Equal(id, await Soon(blocked));
    }

    [Fact]
    public async Task ACancelledRentDoesNotConsumeASlot()
    {
        BlockCommandTracker tracker = new();
        for (int i = 0; i < 9; i++)
        {
            await tracker.RentAsync(NewCompletion(), CancellationToken.None);
        }

        using CancellationTokenSource cancel = new();
        Task<int> waiting = tracker.RentAsync(NewCompletion(), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Soon(waiting));

        Assert.True(tracker.TryComplete(1, Response(1)));
        Assert.Equal(1, await Soon(tracker.RentAsync(NewCompletion(), CancellationToken.None)));
    }
}

public class FicsLineEncoderUnitTest
{
    [Fact]
    public void PlaintextGetsExactlyOneLineFeed()
    {
        Assert.Equal(Encoding.Latin1.GetBytes("finger\n"), FicsLineEncoder.Encode("finger", timeseal: null));
    }

    [Fact]
    public void EmptyPlaintextIsJustTheLineFeed()
    {
        Assert.Equal(new byte[] { 0x0A }, FicsLineEncoder.Encode("", timeseal: null));
    }

    [Fact]
    public void PlaintextLatin1CharactersAreSentAsSingleBytes()
    {
        Assert.Equal(new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9, 0x0A }, FicsLineEncoder.Encode("caf" + (char)0xE9, timeseal: null));
    }

    [Theory]
    [InlineData("tell 1 hi\nquit")]
    [InlineData("tell 1 hi\rquit")]
    [InlineData("finger\0")]
    [InlineData("\n")]
    [InlineData("say hi \u00FF")]   // 0xFF on the wire is Telnet IAC
    public void ALineBreakingOutOfItsLineIsRejectedBeforeAnythingIsSent(string line)
    {
        Assert.Throws<ArgumentException>(nameof(line), () => FicsLineEncoder.Encode(line, timeseal: null));
        Assert.Throws<ArgumentException>(nameof(line), () => FicsLineEncoder.Encode(line, timeseal: FakeTimesealCodec.Instance));
    }

    [Fact]
    public void ANullLineIsRejected()
    {
        Assert.Throws<ArgumentNullException>("line", () => FicsLineEncoder.Encode(null!, timeseal: null));
    }

    [Theory]
    [InlineData("finger")]
    [InlineData("tell 1 hello there")]
    [InlineData("")]
    public void OpenSealOutputDecodesBackToTheOriginalLine(string line)
    {
        byte[] wire = FicsLineEncoder.Encode(line, timeseal: FakeTimesealCodec.Instance);

        Assert.Equal(0x0A, wire[^1]);
        Assert.Equal(0x80, wire[^2]);
        Assert.Equal(1, wire.Count(b => b == 0x0A));
        Assert.Equal(line, FakeFicsServer.DecodeOpenSeal(Encoding.Latin1.GetString(wire, 0, wire.Length - 1)));
    }
}

public class TcpFicsTransportUnitTest
{
    private static TcpListener StartListener()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static int PortOf(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    [Fact]
    public async Task BytesFlowBothWaysOverARealSocket()
    {
        TcpListener listener = StartListener();
        try
        {
            Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
            await using IFicsConnection connection = await Soon(new TcpFicsTransport().ConnectAsync("127.0.0.1", PortOf(listener)));
            using TcpClient server = await Soon(accepted);
            NetworkStream serverStream = server.GetStream();

            await connection.Output.WriteAsync(Encoding.Latin1.GetBytes("hello\n"));
            byte[] received = new byte[6];
            await serverStream.ReadExactlyAsync(received).AsTask().WaitAsync(Patience);
            Assert.Equal("hello\n", Encoding.Latin1.GetString(received));

            await serverStream.WriteAsync(Encoding.Latin1.GetBytes("fics% "));
            ReadResult result = await Soon(connection.Input.ReadAsync().AsTask());
            Assert.Equal("fics% ", Encoding.Latin1.GetString(result.Buffer));
            connection.Input.AdvanceTo(result.Buffer.End);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DisposingTheConnectionIsIdempotentAndEndsTheStreamForThePeer()
    {
        TcpListener listener = StartListener();
        try
        {
            Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
            IFicsConnection connection = await Soon(new TcpFicsTransport().ConnectAsync("127.0.0.1", PortOf(listener)));
            using TcpClient server = await Soon(accepted);

            await connection.DisposeAsync();
            await connection.DisposeAsync();

            byte[] buffer = new byte[1];
            Assert.Equal(0, await server.GetStream().ReadAsync(buffer).AsTask().WaitAsync(Patience));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectingToAClosedPortFails()
    {
        TcpListener listener = StartListener();
        int port = PortOf(listener);
        listener.Stop();

        await Assert.ThrowsAnyAsync<SocketException>(() => Soon(new TcpFicsTransport().ConnectAsync("127.0.0.1", port)));
    }

    [Fact]
    public async Task ACancelledTokenCancelsTheConnectAndIsNotReportedAsATimeout()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TcpFicsTransport().ConnectAsync("127.0.0.1", 9, cancel.Token));
    }

    [Fact]
    public async Task ClientWorksEndToEndOverLoopbackTcp()
    {
        TcpListener listener = StartListener();
        try
        {
            Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
            await using FicsClient client = new(
                new FicsClientOptions { Host = "127.0.0.1", Port = PortOf(listener) },
                new TcpFicsTransport());
            await client.ConnectAsync();
            using TcpClient server = await Soon(accepted);
            NetworkStream stream = server.GetStream();

            await stream.WriteAsync(Encoding.Latin1.GetBytes("login: "));
            Task<string> login = client.LoginAsGuestAsync();
            Assert.Equal("guest\n", await ReadLineAsync(stream));
            await stream.WriteAsync(Encoding.Latin1.GetBytes("Press return to enter the server as \"GuestNET\":\n\r"));
            Assert.Equal("\n", await ReadLineAsync(stream));
            await stream.WriteAsync(Encoding.Latin1.GetBytes("**** Starting FICS session as GuestNET ****\n\r"));
            int commands = 0;
            while (await ReadLineAsync(stream) != "set bell 0\n")
            {
                commands++;
            }

            await stream.WriteAsync(Encoding.Latin1.GetBytes(string.Concat(Enumerable.Repeat("fics% ", commands + 2))));

            Assert.Equal("GuestNET", await Soon(login));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        StringBuilder line = new();
        byte[] one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one).AsTask().WaitAsync(Patience) == 0)
            {
                throw new EndOfStreamException();
            }

            line.Append((char)one[0]);
            if (one[0] == (byte)'\n')
            {
                return line.ToString();
            }
        }
    }
}
