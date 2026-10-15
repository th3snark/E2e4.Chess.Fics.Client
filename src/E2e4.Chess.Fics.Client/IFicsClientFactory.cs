namespace E2e4.Chess.Fics.Client;

/// <summary>
/// Creates <see cref="IFicsClient"/> sessions from the registered defaults. A client is single-use
/// (one connection, then dispose), so long-lived services should inject this factory and create
/// one client per session rather than inject <see cref="IFicsClient"/> itself.
/// The caller owns, and must dispose, every client the factory returns.
/// </summary>
public interface IFicsClientFactory
{
    /// <summary>Creates a client from the configured <see cref="FicsClientOptions"/>.</summary>
    IFicsClient Create();

    /// <summary>
    /// Creates a client from a copy of the configured <see cref="FicsClientOptions"/> after
    /// <paramref name="configure"/> has changed it (for example a different host or login-specific settings).
    /// </summary>
    IFicsClient Create(Action<FicsClientOptions> configure);
}
