using System.IO.Pipelines;
using System.Net.Sockets;

namespace E2e4.Chess.Fics.Client.Transport;

/// <summary>TCP transport wrapping a <see cref="NetworkStream"/> in a pipe pair.</summary>
public sealed class TcpFicsTransport : IFicsTransport
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _connectTimeout;

    /// <param name="connectTimeout">How long to wait for the TCP connection; 30 seconds if omitted.</param>
    public TcpFicsTransport(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
    }

    public async Task<IFicsConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        // Small writes (moves, timeseal ping replies) must not wait for Nagle's algorithm. Keep-alive
        // lets the operating system eventually notice a silently dropped connection, at its default
        // (slow) pace.
        TcpClient tcpClient = new() { NoDelay = true };

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout);

        try
        {
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            await tcpClient.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            tcpClient.Dispose();
            throw new TimeoutException($"Timed out connecting to {host}:{port} after {_connectTimeout.TotalSeconds:0.#} s.");
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }

        return new TcpFicsConnection(tcpClient);
    }

    private sealed class TcpFicsConnection : IFicsConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;

        public TcpFicsConnection(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            Input = PipeReader.Create(_stream);
            Output = PipeWriter.Create(_stream);
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await Input.CompleteAsync().ConfigureAwait(false);
                await Output.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                _stream.Dispose();
                _tcpClient.Dispose();
            }
        }
    }
}
