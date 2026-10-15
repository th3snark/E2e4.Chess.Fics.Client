using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Client.Test.Support;
using Microsoft.Extensions.Logging;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>The logging and time seams on <see cref="FicsClientOptions"/>.</summary>
public class FicsClientDiagnosticsUnitTest
{
    [Fact]
    public async Task ASubscriberExceptionIsLoggedAsAWarningAndContained()
    {
        CapturingLogger logger = new();
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(
            new FicsClientOptions { Logger = logger });
        await using IAsyncDisposable clientScope = client;
        TaskCompletionSource<string> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += (_, _) => throw new InvalidOperationException("subscriber bug");
        client.MessageReceived += (_, e) =>
        {
            if (e.Message is TellMessage tell)
            {
                delivered.TrySetResult(tell.Text);
            }
        };

        await server.SendAsync("GuestAAAA(U) tells you: hello\n\r");

        Assert.Equal("hello", await Soon(delivered.Task));
        (LogLevel level, Exception? error, _) = Assert.Single(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal("subscriber bug", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Fact]
    public async Task EveryStateChangeIsLoggedAtDebugInOrder()
    {
        CapturingLogger logger = new();
        (_, FicsClient client, _) = await LoginAsync(new FicsClientOptions { Logger = logger });
        await using IAsyncDisposable clientScope = client;

        string[] states = ["Connecting", "AwaitingLogin", "LoggingIn", "Negotiating", "Ready"];
        string[] logged = logger.Entries
            .Where(e => e.Level == LogLevel.Debug && e.Message.StartsWith("State ", StringComparison.Ordinal))
            .Select(e => e.Message.Split(' ')[^1])
            .ToArray();
        Assert.Equal(states, logged);
    }

    [Fact]
    public async Task AFailedConnectIsLoggedWithItsException()
    {
        CapturingLogger logger = new();
        await using FicsClient client = new(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance, Logger = logger }, new RefusingTransport());

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Error is not null);
    }

    [Fact]
    public async Task NothingLoggedContainsThePasswordOrChatText()
    {
        CapturingLogger logger = new();
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions { Logger = logger }, server.Transport);
        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsync("alice", "hunter2");
        await Soon(server.ReadLineAsync());
        await server.SendAsync("password: ");
        await Soon(server.ReadLineAsync());
        await server.SendAsync("GuestAAAA(U) tells you: secretchat\n\r");
        server.Close();
        await Assert.ThrowsAnyAsync<Exception>(() => Soon(login));

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("hunter2") || e.Message.Contains("secretchat"));
    }

    [Fact]
    public async Task TheNegotiationTimeoutRunsOnTheOptionsTimeProvider()
    {
        ManualTimeProvider time = new();
        FakeFicsServer server = new();
        await using FicsClient client = new(
            new FicsClientOptions { NegotiationTimeout = TimeSpan.FromHours(1), TimeProvider = time },
            server.Transport);
        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsGuestAsync();
        await Soon(server.ReadLineAsync());
        await server.SendAsync("Press return to enter the server as \"GuestTEST\":\n\r");
        await Soon(server.ReadLineAsync());
        await server.SendAsync("**** Starting FICS session as GuestTEST(U) ****\n\r");
        for (int i = 0; i < 12; i++)
        {
            await Soon(server.ReadLineAsync());
        }

        // None of the owed prompts arrive, so login is waiting on the (manual) timeout timer.
        Assert.True(SpinWait.SpinUntil(() => time.PendingTimers > 0, Patience));
        Assert.False(login.IsCompleted);

        time.FireAll();

        Assert.Equal("GuestTEST", await Soon(login));
        Assert.Equal(FicsClientState.Ready, client.State);
    }

    private sealed class RefusingTransport : Transport.IFicsTransport
    {
        public Task<Transport.IFicsConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default) =>
            throw new IOException("refused");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Error, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, exception, formatter(state, exception)));
            }
        }
    }

    /// <summary>A clock whose timers fire only when the test says so.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();

        public int PendingTimers
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count(t => !t.Disposed);
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(callback, state);
            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        public void FireAll()
        {
            ManualTimer[] timers;
            lock (_timers)
            {
                timers = _timers.Where(t => !t.Disposed).ToArray();
            }

            foreach (ManualTimer timer in timers)
            {
                timer.Fire();
            }
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(TimerCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        public bool Disposed { get; private set; }

        public void Fire() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
