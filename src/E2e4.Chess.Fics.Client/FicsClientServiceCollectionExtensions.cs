using E2e4.Chess.Fics.Client;
using E2e4.Chess.Fics.Client.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the FICS client with a dependency injection container.</summary>
public static class FicsClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFicsClientFactory"/> (singleton), <see cref="IFicsClient"/> (transient) and the
    /// <see cref="IFicsTransport"/> they share (singleton <see cref="TcpFicsTransport"/> unless already registered).
    /// </summary>
    /// <remarks>
    /// A client is a single-use session. Prefer injecting <see cref="IFicsClientFactory"/> into long-lived services:
    /// a transient <see cref="IFicsClient"/> resolved from the root provider is kept alive until the provider is
    /// disposed, so resolve it from a scope that ends with the session. <see cref="FicsClient"/> is only
    /// <see cref="IAsyncDisposable"/>, so dispose the provider or scope with <c>DisposeAsync</c> (a generic host does).
    /// The logger (from <c>ILoggerFactory</c>), clock (<c>TimeProvider</c>) and timeseal codec (<see cref="ITimesealCodec"/>)
    /// are taken from the container when registered and not already set on the options.
    /// Bind settings from configuration with <c>services.Configure&lt;FicsClientOptions&gt;(section)</c>.
    /// </remarks>
    /// <param name="services">The service collection to register the client in.</param>
    /// <param name="configure">Optional defaults for every client.</param>
    public static IServiceCollection AddFicsClient(this IServiceCollection services, Action<FicsClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<FicsClientOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<FicsClientOptions>, FicsClientOptionsSetup>());
        services.TryAddSingleton<IFicsTransport, TcpFicsTransport>();
        services.TryAddSingleton<IFicsClientFactory, FicsClientFactory>();
        services.TryAddTransient(static provider => provider.GetRequiredService<IFicsClientFactory>().Create());
        return services;
    }
}
