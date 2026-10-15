using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Protocol.Messages;

namespace E2e4.Chess.Fics.Client;

/// <summary>
/// The surface of <see cref="FicsClient"/> that applications program against, so view models
/// and other consumers can be tested with a fake session.
/// </summary>
public interface IFicsClient : IAsyncDisposable
{
    FicsClientState State { get; }

    /// <summary>The actual session user name (e.g. "GuestKRNP"), known once logged in.</summary>
    string? UserName { get; }

    event EventHandler<FicsMessageEventArgs>? MessageReceived;

    event EventHandler<Style12EventArgs>? BoardUpdated;

    event EventHandler<FicsRawTextEventArgs>? RawTextReceived;

    event EventHandler<FicsDisconnectedEventArgs>? Disconnected;

    IAsyncEnumerable<FicsMessage> ReadAllMessagesAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<string> LoginAsGuestAsync(CancellationToken cancellationToken = default);

    Task<string> LoginAsync(string user, string? password = null, CancellationToken cancellationToken = default);

    Task<FicsResponse> SendCommandAsync(string command, CancellationToken cancellationToken = default);

    Task SendLineAsync(string line, CancellationToken cancellationToken = default);
}
