using E2e4.Chess.Fics.Client.Transport;
using Microsoft.Extensions.Options;

namespace E2e4.Chess.Fics.Client;

/// <summary>The default <see cref="IFicsClientFactory"/>: builds <see cref="FicsClient"/> over a shared transport.</summary>
public sealed class FicsClientFactory : IFicsClientFactory
{
    private readonly IOptions<FicsClientOptions> _options;
    private readonly IFicsTransport _transport;

    /// <param name="options">The defaults every client starts from.</param>
    /// <param name="transport">Shared by all clients the factory creates, so it must be reusable.</param>
    public FicsClientFactory(IOptions<FicsClientOptions> options, IFicsTransport transport)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transport);
        _options = options;
        _transport = transport;
    }

    public IFicsClient Create() => new FicsClient(_options.Value.Clone(), _transport);

    public IFicsClient Create(Action<FicsClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        FicsClientOptions options = _options.Value.Clone();
        configure(options);
        return new FicsClient(options, _transport);
    }
}
