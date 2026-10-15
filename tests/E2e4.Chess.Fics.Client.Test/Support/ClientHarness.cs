using System.Globalization;
using Xunit;

namespace E2e4.Chess.Fics.Client.Test.Support;

/// <summary>Drives a <see cref="FicsClient"/> through connect and login against a <see cref="FakeFicsServer"/>.</summary>
internal static class ClientHarness
{
    public const char BlockStart = (char)0x15;
    public const char BlockSeparator = (char)0x16;
    public const char BlockEnd = (char)0x17;

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public static Task<T> Soon<T>(Task<T> task) => task.WaitAsync(Patience);

    public static Task Soon(Task task) => task.WaitAsync(Patience);

    /// <summary>A framed block-mode response, as the server sends it.</summary>
    public static string Block(int id, int code, string text) => $"{BlockStart}{id}{BlockSeparator}{code}{BlockSeparator}{text}{BlockEnd}";

    /// <summary>
    /// Connects and logs in as a guest, returning the commands the client sent during
    /// negotiation (in order, including the id-prefixed "iset lock 1" that follows
    /// "iset block 1" in block mode).
    /// </summary>
    public static async Task<(FakeFicsServer Server, FicsClient Client, List<string> Negotiation)> LoginAsync(FicsClientOptions options)
    {
        FakeFicsServer server = new();
        FicsClient client = new(options, server.Transport);

        await client.ConnectAsync();
        if (options.Timeseal is not null)
        {
            await Soon(server.ReadLineAsync(openSeal: true));
        }

        await server.SendAsync("login: ");
        Task<string> login = client.LoginAsGuestAsync();

        Assert.Equal("guest", await Soon(server.ReadLineAsync(options.Timeseal is not null)));
        await server.SendAsync("Press return to enter the server as \"GuestTEST\":\n\r");
        Assert.Equal("", await Soon(server.ReadLineAsync(options.Timeseal is not null)));
        await server.SendAsync("**** Starting FICS session as GuestTEST(U) ****\n\r");

        string last = options.UseBlockMode ? "iset block 1" : "set bell 0";
        List<string> negotiation = new();
        while (true)
        {
            string line = await Soon(server.ReadLineAsync(options.Timeseal is not null));
            negotiation.Add(line);
            if (line == last)
            {
                break;
            }
        }

        if (options.UseBlockMode)
        {
            if (options.LockIvars)
            {
                string locked = await Soon(server.ReadLineAsync(options.Timeseal is not null));
                negotiation.Add(locked);
                int id = int.Parse(locked.Split(' ')[0], CultureInfo.InvariantCulture);
                await server.SendAsync(Block(id, 0, ""));
            }
        }
        else
        {
            await server.SendAsync(string.Concat(Enumerable.Repeat("fics% ", negotiation.Count + 1)));
        }

        Assert.Equal("GuestTEST", await Soon(login));
        return (server, client, negotiation);
    }
}
