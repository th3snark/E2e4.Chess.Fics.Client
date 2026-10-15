using System.Globalization;
using System.IO.Pipelines;
using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Client.Test.Support;
using E2e4.Chess.Fics.Client.Transport;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>Failure, cancellation and lifecycle behaviour of <see cref="FicsClient"/>.</summary>
public class FicsClientResilienceUnitTest
{
    private static async Task<(FakeFicsServer Server, FicsClient Client)> LoggedInAsync(bool blockMode = false)
    {
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(new FicsClientOptions { UseBlockMode = blockMode });
        return (server, client);
    }

    private static async Task AssertStaysPendingAsync(Task task)
    {
        await Task.Delay(300);
        Assert.False(task.IsCompleted, "The task completed but should still be waiting.");
    }

    [Fact]
    public async Task ThrowingEventHandlerDoesNotKillTheSession()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;
        int calls = 0;
        TaskCompletionSource<string> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("subscriber bug");
        };
        client.MessageReceived += (_, e) =>
        {
            if (e.Message is TellMessage tell)
            {
                if (tell.Text == "two")
                {
                    second.TrySetResult(tell.Text);
                }
            }
        };

        await server.SendAsync("GuestAAAA(U) tells you: one\n\r");
        await server.SendAsync("GuestAAAA(U) tells you: two\n\r");

        Assert.Equal("two", await Soon(second.Task));
        Assert.Equal(2, calls);
        Assert.Equal(FicsClientState.Ready, client.State);
    }

    [Fact]
    public async Task BoardUpdatedSubscriberExceptionDoesNotStopLaterHandlersOrTheStream()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;
        TaskCompletionSource<int> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.BoardUpdated += (_, _) => throw new InvalidOperationException("subscriber bug");
        client.BoardUpdated += (_, e) => seen.TrySetResult(e.Board.GameNumber);

        await server.SendAsync(FicsTestData.Style12StartPosition + "\n\r");

        Assert.Equal(314, await Soon(seen.Task));
    }

    [Fact]
    public async Task DisconnectedCarriesTheErrorThatEndedTheReadLoop()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;
        TaskCompletionSource<FicsDisconnectedEventArgs> disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, e) => disconnected.TrySetResult(e);

        server.Fault(new IOException("connection reset"));

        FicsDisconnectedEventArgs args = await Soon(disconnected.Task);
        Assert.IsType<IOException>(args.Error);
    }

    [Fact]
    public async Task DisconnectedHasNoErrorWhenTheServerClosesCleanly()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;
        TaskCompletionSource<FicsDisconnectedEventArgs> disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, e) => disconnected.TrySetResult(e);

        server.Close();

        Assert.Null((await Soon(disconnected.Task)).Error);
    }

    [Fact]
    public async Task FailedHelloDisposesTheConnectionAndLeavesTheClientRetryable()
    {
        CountingTransport transport = new(failWrites: true);
        await using FicsClient client = new(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance }, transport);

        await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync());

        Assert.Equal(FicsClientState.Disconnected, client.State);
        Assert.Equal(1, transport.Connections[0].DisposeCount);

        transport.FailWrites = false;
        await client.ConnectAsync();

        Assert.Equal(FicsClientState.AwaitingLogin, client.State);
        Assert.Equal(2, transport.Connections.Count);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        (_, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;

        await Soon(client.DisposeAsync().AsTask());
        await Soon(client.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposingWithACommandInFlightFailsItWithoutAnObjectDisposedException(bool blockMode)
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync(blockMode);
        await using IAsyncDisposable clientScope = client;
        Task<FicsResponse> command = client.SendCommandAsync("finger");
        await Soon(server.ReadLineAsync());

        await Soon(client.DisposeAsync().AsTask());

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => Soon(command));
        Assert.IsNotType<ObjectDisposedException>(error);
    }

    [Fact]
    public async Task DisposeAsyncWithoutEverConnectingCompletesTheMessageStream()
    {
        await using FicsClient client = new(new FicsClientOptions(), new FakeFicsServer().Transport);
        Task<int> drained = Task.Run(async () =>
        {
            int count = 0;
            await foreach (FicsMessage _ in client.ReadAllMessagesAsync())
            {
                count++;
            }

            return count;
        });

        await client.DisposeAsync();

        Assert.Equal(0, await Soon(drained));
    }

    [Fact]
    public async Task DisposingFromInsideTheDisconnectedHandlerDoesNotDeadlock()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += async (_, _) =>
        {
            await client.DisposeAsync();
            disposed.TrySetResult();
        };

        server.Close();

        await Soon(disposed.Task);
    }

    [Fact]
    public async Task FailedBlockModeSendsDoNotLeakCommandIds()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync(blockMode: true);
        await using IAsyncDisposable clientScope = client;

        server.FailClientWrites = true;
        for (int i = 0; i < 12; i++)
        {
            await Assert.ThrowsAsync<IOException>(() => Soon(client.SendCommandAsync("finger")));
        }

        server.FailClientWrites = false;
        Task<FicsResponse> command = client.SendCommandAsync("finger");
        string sent = await Soon(server.ReadLineAsync());
        Assert.EndsWith(" finger", sent);
        int id = int.Parse(sent.Split(' ')[0], CultureInfo.InvariantCulture);
        await server.SendAsync("\u0015" + id + "\u00160\u0016ok\u0017");

        Assert.Equal("ok", (await Soon(command)).Text);
    }

    [Fact]
    public async Task FailedRawSendDoesNotLeavePromptDebtThatSwallowsTheNextResponse()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;

        server.FailClientWrites = true;
        await Assert.ThrowsAsync<IOException>(() => Soon(client.SendLineAsync("tell 1 hi")));
        server.FailClientWrites = false;

        Task<FicsResponse> command = client.SendCommandAsync("finger");
        Assert.Equal("finger", await Soon(server.ReadLineAsync()));
        await server.SendAsync("Finger of GuestTEST\n\rfics% ");

        Assert.Contains("Finger of GuestTEST", (await Soon(command)).Text);
    }

    [Fact]
    public async Task CancelledCommandDoesNotLetItsLatePromptCompleteTheNextCommand()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;

        using CancellationTokenSource cancel = new();
        Task<FicsResponse> first = client.SendCommandAsync("finger", cancel.Token);
        Assert.Equal("finger", await Soon(server.ReadLineAsync()));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Soon(first));

        Task<FicsResponse> second = client.SendCommandAsync("who");
        Assert.Equal("who", await Soon(server.ReadLineAsync()));

        // The prompt for the abandoned "finger" must be absorbed, not taken as the end of "who".
        await server.SendAsync("fics% ");
        await AssertStaysPendingAsync(second);

        await server.SendAsync("Who output\n\rfics% ");
        Assert.Contains("Who output", (await Soon(second)).Text);
    }

    [Fact]
    public async Task RawLineSendWaitsForTheCommandInFlightInNonBlockMode()
    {
        (FakeFicsServer server, FicsClient client) = await LoggedInAsync();
        await using IAsyncDisposable clientScope = client;

        Task<FicsResponse> command = client.SendCommandAsync("finger");
        Assert.Equal("finger", await Soon(server.ReadLineAsync()));
        Task raw = client.SendLineAsync("tell 1 hi");
        await AssertStaysPendingAsync(raw);

        await server.SendAsync("Finger output\n\rfics% ");
        await Soon(command);
        await Soon(raw);

        Assert.Equal("tell 1 hi", await Soon(server.ReadLineAsync()));
    }

    [Fact]
    public async Task FailedLoginLeavesTheClientDisconnected()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsync("somebody", "wrong");
        Assert.Equal("somebody", await Soon(server.ReadLineAsync()));
        await server.SendAsync("password: ");
        Assert.Equal("wrong", await Soon(server.ReadLineAsync()));
        await server.SendAsync("**** Invalid password! ****\n\r");

        await Assert.ThrowsAsync<FicsException>(() => Soon(login));

        Assert.Equal(FicsClientState.Disconnected, client.State);
    }

    [Fact]
    public async Task CancelledLoginLeavesTheClientDisconnected()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();
        using CancellationTokenSource cancel = new();
        Task<string> login = client.LoginAsGuestAsync(cancel.Token);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Soon(login));
        Assert.Equal(FicsClientState.Disconnected, client.State);
    }

    [Fact]
    public async Task ConnectingTwiceIsRejected()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
    }

    /// <summary>A transport that hands out fresh in-memory connections and records their disposal.</summary>
    private sealed class CountingTransport : IFicsTransport
    {
        public CountingTransport(bool failWrites)
        {
            FailWrites = failWrites;
        }

        public bool FailWrites { get; set; }

        public List<CountingConnection> Connections { get; } = new();

        public Task<IFicsConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            CountingConnection connection = new(FailWrites);
            Connections.Add(connection);
            return Task.FromResult<IFicsConnection>(connection);
        }
    }

    private sealed class CountingConnection : IFicsConnection
    {
        private readonly Pipe _input = new();

        public CountingConnection(bool failWrites)
        {
            Output = failWrites ? PipeWriter.Create(new ThrowingStream()) : new Pipe().Writer;
        }

        public int DisposeCount { get; private set; }

        public PipeReader Input => _input.Reader;

        public PipeWriter Output { get; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _input.Writer.Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Simulated write failure.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("Simulated write failure.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated write failure.");
    }
}
