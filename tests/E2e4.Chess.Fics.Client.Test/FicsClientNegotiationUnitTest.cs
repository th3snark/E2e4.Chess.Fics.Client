using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Client.Test.Support;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

public class FicsClientNegotiationUnitTest
{
    private static readonly string[] Core =
    {
        "iset defprompt 1", "iset startpos 1", "iset ms 1", "iset pendinfo 1", "iset nowrap 1", "iset gameinfo 1",
    };

    private static readonly string[] SeekInfo = { "iset seekinfo 1", "iset seekremove 1", "iset showownseek 1" };

    private static readonly string[] Tail = { $"set interface {new FicsClientOptions().InterfaceName}", "set style 12", "set bell 0" };

    private static string[] Join(params string[][] parts) => parts.SelectMany(p => p).ToArray();

    public static TheoryData<string, bool, bool, bool> NegotiationMatrix() => new()
    {
        { "seek info off", false, false, false },
        { "pin on", true, true, false },
        { "lock on", true, false, true },
        { "everything", true, true, true },
    };

    [Theory]
    [MemberData(nameof(NegotiationMatrix))]
    public async Task NonBlockModeNegotiatesExactlyTheRequestedOptionsInOrder(string name, bool seekInfo, bool pin, bool lockIvars)
    {
        Assert.NotEmpty(name);
        FicsClientOptions options = new()
        {
            EnableSeekInfo = seekInfo,
            EnablePin = pin,
            LockIvars = lockIvars,
        };

        (_, FicsClient client, List<string> negotiation) = await LoginAsync(options);
        await using IAsyncDisposable clientScope = client;

        string[] expected = Join(
            Core,
            seekInfo ? SeekInfo : Array.Empty<string>(),
            pin ? new[] { "iset pin 1" } : Array.Empty<string>(),
            lockIvars ? new[] { "iset lock 1" } : Array.Empty<string>(),
            Tail);
        Assert.Equal(expected, negotiation);
        Assert.Equal(FicsClientState.Ready, client.State);
    }

    [Fact]
    public async Task BlockModeTurnsBlockOnLastWithoutAnIdAndNeedsNoLockLine()
    {
        (_, FicsClient client, List<string> negotiation) = await LoginAsync(
            new FicsClientOptions { UseBlockMode = true });
        await using IAsyncDisposable clientScope = client;

        Assert.Equal(Join(Core, SeekInfo, Tail, new[] { "iset block 1" }), negotiation);
        Assert.Equal(FicsClientState.Ready, client.State);
    }

    [Fact]
    public async Task BlockModeWithLockSendsTheLockLineWithAnIdAfterBlockIsOn()
    {
        (_, FicsClient client, List<string> negotiation) = await LoginAsync(
            new FicsClientOptions { UseBlockMode = true, LockIvars = true });
        await using IAsyncDisposable clientScope = client;

        string[] withoutLock = Join(Core, SeekInfo, Tail, new[] { "iset block 1" });
        Assert.Equal(withoutLock, negotiation.Take(withoutLock.Length));
        Assert.Matches(@"^[1-9] iset lock 1$", negotiation[^1]);
        Assert.Equal(withoutLock.Length + 1, negotiation.Count);
        Assert.Equal(FicsClientState.Ready, client.State);
    }

    [Fact]
    public async Task OpenSealEncryptedLoginNegotiatesTheSameCommands()
    {
        (_, _, List<string> negotiation) = await LoginAsync(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance });

        Assert.Equal(Join(Core, SeekInfo, Tail), negotiation);
    }

    [Fact]
    public async Task LoginCarriesOnAfterTheNegotiationTimeoutWhenFewerPromptsArrive()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(
            new FicsClientOptions { NegotiationTimeout = TimeSpan.FromMilliseconds(100) },
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

        // Only one of the thirteen owed prompts ever arrives.
        await server.SendAsync("fics% ");

        Assert.Equal("GuestTEST", await login.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(FicsClientState.Ready, client.State);
    }

    [Fact]
    public async Task LoginBeforeConnectIsRejected()
    {
        await using FicsClient client = new(new FicsClientOptions(), new FakeFicsServer().Transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LoginAsGuestAsync());
    }

    [Fact]
    public async Task SecondLoginWhileTheFirstIsRunningIsRejected()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();
        Task<string> first = client.LoginAsGuestAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LoginAsGuestAsync());

        first.ContinueWith(_ => { }, TaskScheduler.Default).Ignore();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ServerClosingDuringLoginFaultsTheLoginAndDisconnects()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsGuestAsync();
        Assert.Equal("guest", await Soon(server.ReadLineAsync()));

        server.Close();

        await Assert.ThrowsAsync<FicsException>(() => Soon(login));
        Assert.Equal(FicsClientState.Disconnected, client.State);
    }

    [Fact]
    public async Task AccountNeedingAPasswordWithoutOneFailsAndDisconnects()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsync("somebody");
        Assert.Equal("somebody", await Soon(server.ReadLineAsync()));

        await server.SendAsync("password: ");

        FicsException error = await Assert.ThrowsAsync<FicsException>(() => Soon(login));
        Assert.Contains("requires a password", error.Message);
        Assert.Equal(FicsClientState.Disconnected, client.State);
    }

    [Fact]
    public async Task UnconsumedMessageStreamDropsTheOldestWithoutStallingTheSession()
    {
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(new FicsClientOptions());
        await using IAsyncDisposable clientScope = client;
        TaskCompletionSource allSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int seen = 0;
        client.MessageReceived += (_, _) =>
        {
            if (Interlocked.Increment(ref seen) == 1500)
            {
                allSeen.TrySetResult();
            }
        };
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => ended.TrySetResult();

        await server.SendAsync(string.Concat(Enumerable.Range(0, 1500).Select(i => $"GuestAAAA(U) tells you: message {i}\n\r")));
        await Soon(allSeen.Task);
        server.Close();

        // The event fires before the message is written to the stream, so wait for the session to end
        // (every write done) before reading; otherwise the reader frees a slot and 1025 come out.
        await Soon(ended.Task);

        List<string> texts = new();
        await foreach (FicsMessage message in client.ReadAllMessagesAsync().WithCancellation(new CancellationTokenSource(Patience).Token))
        {
            texts.Add(((TellMessage)message).Text);
        }

        Assert.Equal(1024, texts.Count);
        Assert.Equal("message 476", texts[0]);
        Assert.Equal("message 1499", texts[^1]);
    }

    [Fact]
    public async Task EventsAndTheMessageStreamSeeTheSameMessagesInOrder()
    {
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(new FicsClientOptions());
        await using IAsyncDisposable clientScope = client;
        List<string> viaEvent = new();
        client.MessageReceived += (_, e) =>
        {
            if (e.Message is TellMessage tell)
            {
                lock (viaEvent)
                {
                    viaEvent.Add(tell.Text);
                }
            }
        };

        await server.SendAsync("A(U) tells you: one\n\rB(U) tells you: two\n\rC(U) tells you: three\n\r");
        server.Close();

        List<string> viaStream = new();
        await foreach (FicsMessage message in client.ReadAllMessagesAsync().WithCancellation(new CancellationTokenSource(Patience).Token))
        {
            viaStream.Add(((TellMessage)message).Text);
        }

        Assert.Equal(new[] { "one", "two", "three" }, viaStream);
        lock (viaEvent)
        {
            Assert.Equal(viaStream, viaEvent);
        }
    }

    [Fact]
    public async Task TimesealPingIsAnsweredWithTheEncryptedReplyWhenOpenSealIsOn()
    {
        (FakeFicsServer server, _, _) = await LoginAsync(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance });

        await server.SendAsync("[G]\n\r");

        Assert.Equal("\u00029", await Soon(server.ReadLineAsync(openSeal: true)));
    }

    [Fact]
    public async Task TimesealPingIsIgnoredWhenOpenSealIsOff()
    {
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(new FicsClientOptions());
        await using IAsyncDisposable clientScope = client;

        await server.SendAsync("[G]\n\r");
        Task<FicsResponse> command = client.SendCommandAsync("finger");

        Assert.Equal("finger", await Soon(server.ReadLineAsync()));
        await server.SendAsync("out\n\rfics% ");
        await Soon(command);
    }
}

internal static class TaskExtensions
{
    /// <summary>Marks a task as deliberately not awaited.</summary>
    public static void Ignore(this Task task)
    {
    }
}
