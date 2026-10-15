using System.IO.Pipelines;

namespace E2e4.Chess.Fics.Client.Transport;

/// <summary>
/// A duplex byte connection to a FICS server exposed as a pipe pair.
/// Production code uses <see cref="TcpFicsTransport"/>; tests substitute
/// in-memory pipes.
/// </summary>
public interface IFicsConnection : IAsyncDisposable
{
    PipeReader Input { get; }

    PipeWriter Output { get; }
}

public interface IFicsTransport
{
    Task<IFicsConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
}
