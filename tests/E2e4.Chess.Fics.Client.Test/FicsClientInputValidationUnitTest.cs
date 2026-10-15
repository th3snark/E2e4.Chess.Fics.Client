using E2e4.Chess.Fics.Client.Test.Support;
using E2e4.Chess.Fics.Protocol;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>A caller-supplied string must never reach the server as more than one line.</summary>
public class FicsClientInputValidationUnitTest
{
    [Fact]
    public async Task ACommandWithALineBreakIsRejectedAndTheSessionStaysUsable()
    {
        (FakeFicsServer server, FicsClient client, _) = await LoginAsync(new FicsClientOptions());
        await using FicsClient disposable = client;

        await Assert.ThrowsAsync<ArgumentException>("command", () => client.SendCommandAsync("tell 1 hi\nquit"));
        await Assert.ThrowsAsync<ArgumentException>("line", () => client.SendLineAsync("tell 1 hi\r\nquit"));

        Assert.Equal(FicsClientState.Ready, client.State);

        // Nothing from the rejected calls reached the wire: the next line the server sees is the next command.
        Task<FicsResponse> next = client.SendCommandAsync("finger");
        Assert.Equal("finger", await Soon(server.ReadLineAsync()));
        await server.SendAsync("fics% ");
        await Soon(next);
    }

    [Theory]
    [InlineData("guest\nquit", null, "user")]
    [InlineData("someone", "secret\nquit", "password")]
    public async Task ALoginWithALineBreakIsRejectedWithoutClosingTheConnection(string user, string? password, string parameter)
    {
        FakeFicsServer server = new();
        await using FicsClient client = new(new FicsClientOptions(), server.Transport);
        await client.ConnectAsync();
        await server.SendAsync("login: ");

        await Assert.ThrowsAsync<ArgumentException>(parameter, () => client.LoginAsync(user, password));

        Assert.Equal(FicsClientState.AwaitingLogin, client.State);
    }
}
