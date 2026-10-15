using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Client.Test.Support;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

public class FicsClientUnitTest
{
    private static async Task<(FakeFicsServer Server, FicsClient Client)> CreateLoggedInClientAsync(
        FicsClientOptions? options = null)
    {
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(options ?? new FicsClientOptions());
        Assert.Equal(FicsClientState.Ready, client.State);
        return (server, client);
    }

    [Fact]
    public async Task GuestLoginNegotiatesTheExpectedIsetSequence()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);

        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsGuestAsync();

        Assert.Equal("guest", await Soon(server.ReadLineAsync()));
        await server.SendAsync("Press return to enter the server as \"GuestTEST\":\n\r");
        Assert.Equal("", await Soon(server.ReadLineAsync()));
        await server.SendAsync("**** Starting FICS session as GuestTEST(U) ****\n\r");

        List<string> negotiation = new();
        for (int i = 0; i < 12; i++)
        {
            negotiation.Add(await Soon(server.ReadLineAsync()));
        }

        await server.SendAsync(string.Concat(Enumerable.Repeat("fics% ", 13)));

        Assert.Equal("GuestTEST", await Soon(login));
        Assert.Equal(FicsClientState.Ready, client.State);
        Assert.Equal("GuestTEST", client.UserName);
        Assert.Equal(
            new[]
            {
                "iset defprompt 1",
                "iset startpos 1",
                "iset ms 1",
                "iset pendinfo 1",
                "iset nowrap 1",
                "iset gameinfo 1",
                "iset seekinfo 1",
                "iset seekremove 1",
                "iset showownseek 1",
                $"set interface {new FicsClientOptions().InterfaceName}",
                "set style 12",
                "set bell 0",
            },
            negotiation);
    }

    [Fact]
    public async Task RegisteredLoginSendsThePasswordAtThePasswordPrompt()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);

        await client.ConnectAsync();
        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsync("somebody", "hunter2");

        Assert.Equal("somebody", await Soon(server.ReadLineAsync()));
        await server.SendAsync("password: ");
        Assert.Equal("hunter2", await Soon(server.ReadLineAsync()));
        await server.SendAsync("**** Starting FICS session as somebody ****\n\r");

        for (int i = 0; i < 12; i++)
        {
            await Soon(server.ReadLineAsync());
        }

        await server.SendAsync(string.Concat(Enumerable.Repeat("fics% ", 13)));

        Assert.Equal("somebody", await Soon(login));
    }

    [Fact]
    public async Task WrongPasswordFailsTheLoginTaskWithFicsException()
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
    }

    [Fact]
    public async Task OpenSealHelloIsTheFirstLineOnTheWireAndDecryptsToTheTimestampBanner()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance }, server.Transport);

        await client.ConnectAsync();

        string hello = await Soon(server.ReadLineAsync(openSeal: true));
        Assert.Equal("TIMESTAMP|openseal|Running on an operating system|", hello);
    }

    [Fact]
    public async Task OpenSealCommandsDecryptOnTheServerSide()
    {
        (FakeFicsServer server, FicsClient client) =
            await CreateLoggedInClientAsync(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance });
        await using IAsyncDisposable clientScope = client;

        Task send = client.SendLineAsync("sought");

        Assert.Equal("sought", await Soon(server.ReadLineAsync(openSeal: true)));
        await Soon(send);
    }

    [Fact]
    public async Task TimesealPingIsAnsweredImmediatelyWithTheEncryptedControlSequence()
    {
        (FakeFicsServer server, FicsClient client) =
            await CreateLoggedInClientAsync(new FicsClientOptions { Timeseal = FakeTimesealCodec.Instance });
        await using IAsyncDisposable clientScope = client;

        await server.SendAsync("[G]\n\r");

        string reply = await Soon(server.ReadLineAsync(openSeal: true));
        Assert.Equal("\u0002" + "9", reply);
    }

    [Fact]
    public void DecodeOpenSealReversesTheFakeCodec()
    {
        string wire = FakeTimesealCodec.Instance.Encode("iset ms 1");

        // The wire text ends "\x80\n"; the reader strips the '\n'.
        Assert.Equal("iset ms 1", FakeFicsServer.DecodeOpenSeal(wire[..^1]));
    }

    [Fact]
    public async Task BlockModeCommandCompletesWithTheCorrelatedFrame()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync(
            new FicsClientOptions { UseBlockMode = true });
        await using IAsyncDisposable clientScope = client;

        Task<FicsResponse> command = client.SendCommandAsync("sought");

        Assert.Equal("1 sought", await Soon(server.ReadLineAsync()));
        await server.SendAsync("\u00151\u0016157\u00161 ad displayed.\u0017");

        FicsResponse response = await Soon(command);
        Assert.Equal(1, response.CommandId);
        Assert.Equal(157, response.Code);
        Assert.Equal("1 ad displayed.", response.Text);
        Assert.False(response.IsError);
    }

    [Fact]
    public async Task BlockModeResponsesArrivingOutOfOrderCompleteTheRightCommands()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync(
            new FicsClientOptions { UseBlockMode = true });
        await using IAsyncDisposable clientScope = client;

        Task<FicsResponse> first = client.SendCommandAsync("finger");
        Assert.Equal("1 finger", await Soon(server.ReadLineAsync()));
        Task<FicsResponse> second = client.SendCommandAsync("sought");
        Assert.Equal("2 sought", await Soon(server.ReadLineAsync()));

        await server.SendAsync("\u00152\u0016157\u0016sought output\u0017");
        await server.SendAsync("\u00151\u001637\u0016finger output\u0017");

        FicsResponse secondResponse = await Soon(second);
        FicsResponse firstResponse = await Soon(first);
        Assert.Equal("sought output", secondResponse.Text);
        Assert.Equal("finger output", firstResponse.Text);
    }

    [Fact]
    public async Task BlockModeErrorCodeMarksTheResponseAsError()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync(
            new FicsClientOptions { UseBlockMode = true });
        await using IAsyncDisposable clientScope = client;

        Task<FicsResponse> command = client.SendCommandAsync("nosuchcommand");
        await Soon(server.ReadLineAsync());
        await server.SendAsync("\u00151\u0016512\u0016nosuchcommand: Command not found.\u0017");

        FicsResponse response = await Soon(command);
        Assert.True(response.IsError);
        Assert.Equal(512, response.Code);
    }

    [Fact]
    public async Task NonBlockCommandCollectsOutputUpToTheNextPrompt()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync();
        await using IAsyncDisposable clientScope = client;

        Task<FicsResponse> command = client.SendCommandAsync("finger");

        Assert.Equal("finger", await Soon(server.ReadLineAsync()));
        await server.SendAsync("Finger of GuestTEST:\n\rOn for: 5 mins\n\rfics% ");

        FicsResponse response = await Soon(command);
        Assert.Equal(0, response.CommandId);
        Assert.False(response.IsError);
        Assert.Contains("Finger of GuestTEST:", response.Text);
        Assert.Contains("On for: 5 mins", response.Text);
    }

    [Fact]
    public async Task Style12LineRaisesBoardUpdatedWithTheParsedBoard()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync();
        await using IAsyncDisposable clientScope = client;

        TaskCompletionSource<Style12> board = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.BoardUpdated += (_, e) => board.TrySetResult(e.Board);

        await server.SendAsync(FicsTestData.Style12StartPosition + "\n\r");

        Style12 style12 = await Soon(board.Task);
        Assert.Equal(314, style12.GameNumber);
        Assert.Equal("GuestWHTE", style12.WhiteName);
    }

    [Fact]
    public async Task ReadAllMessagesAsyncYieldsTheSameStreamAsTheEvents()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync();
        await using IAsyncDisposable clientScope = client;

        Task<FicsMessage> firstMessage = ReadFirstAsync(client);
        await server.SendAsync("GuestBLCK(U) tells you: hello there\n\r");

        TellMessage tell = Assert.IsType<TellMessage>(await Soon(firstMessage));
        Assert.Equal("GuestBLCK", tell.Handle);
        Assert.Equal("hello there", tell.Text);

        static async Task<FicsMessage> ReadFirstAsync(FicsClient client)
        {
            await foreach (FicsMessage message in client.ReadAllMessagesAsync())
            {
                return message;
            }

            throw new InvalidOperationException("Message stream ended unexpectedly.");
        }
    }

    [Fact]
    public async Task ServerClosingTheConnectionRaisesDisconnectedAndFaultsPendingCommands()
    {
        (FakeFicsServer server, FicsClient client) = await CreateLoggedInClientAsync(
            new FicsClientOptions { UseBlockMode = true });
        await using IAsyncDisposable clientScope = client;

        TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => disconnected.TrySetResult();

        Task<FicsResponse> command = client.SendCommandAsync("finger");
        await Soon(server.ReadLineAsync());

        server.Close();

        await Soon(disconnected.Task);
        Assert.Equal(FicsClientState.Disconnected, client.State);
        await Assert.ThrowsAsync<FicsException>(() => Soon(command));
    }

    [Fact]
    public async Task SendCommandBeforeLoginThrowsInvalidOperation()
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);

        await client.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendCommandAsync("finger"));
    }
}
