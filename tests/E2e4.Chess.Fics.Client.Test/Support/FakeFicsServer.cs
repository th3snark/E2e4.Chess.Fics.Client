using System.IO.Pipelines;
using System.Text;
using E2e4.Chess.Fics.Client.Transport;

namespace E2e4.Chess.Fics.Client.Test.Support;

/// <summary>
/// An in-memory FICS server: two cross-wired pipes standing in for the TCP
/// connection, plus helpers to script server output and to read (and openseal-
/// decode) what the client sent.
/// </summary>
internal sealed class FakeFicsServer
{
    private readonly Pipe _toClient = new();
    private readonly Pipe _fromClient = new();
    private readonly List<byte> _readBuffer = new();

    public FakeFicsServer()
    {
        Transport = new FakeTransport(new FakeConnection(_toClient.Reader, new FaultableWriter(_fromClient.Writer, this)));
    }

    public IFicsTransport Transport { get; }

    /// <summary>While true, every client write fails with an <see cref="IOException"/>, like a broken socket.</summary>
    public bool FailClientWrites { get; set; }

    /// <summary>Writes server output exactly as it would appear on the wire (Latin1).</summary>
    public async Task SendAsync(string wireText)
    {
        await _toClient.Writer.WriteAsync(Encoding.Latin1.GetBytes(wireText));
    }

    /// <summary>Simulates the server closing the connection.</summary>
    public void Close()
    {
        _toClient.Writer.Complete();
    }

    /// <summary>Simulates the connection failing with an error, as a reset socket would.</summary>
    public void Fault(Exception exception)
    {
        _toClient.Writer.Complete(exception);
    }

    /// <summary>Reads one '\n'-terminated line sent by the client, without the terminator.</summary>
    public async Task<string> ReadRawLineAsync()
    {
        while (true)
        {
            int newline = _readBuffer.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                string line = Encoding.Latin1.GetString(_readBuffer.ToArray(), 0, newline);
                _readBuffer.RemoveRange(0, newline + 1);
                return line;
            }

            ReadResult result = await _fromClient.Reader.ReadAsync();
            foreach (ReadOnlyMemory<byte> segment in result.Buffer)
            {
                _readBuffer.AddRange(segment.ToArray());
            }

            _fromClient.Reader.AdvanceTo(result.Buffer.End);

            if (result.IsCompleted && _readBuffer.IndexOf((byte)'\n') < 0)
            {
                throw new InvalidOperationException("Client closed the connection mid-line.");
            }
        }
    }

    /// <summary>Reads one client line and openseal-decodes it when requested.</summary>
    public async Task<string> ReadLineAsync(bool openSeal = false)
    {
        string raw = await ReadRawLineAsync();
        return openSeal ? DecodeOpenSeal(raw) : raw;
    }

    /// <summary>
    /// Reverses <see cref="FakeTimesealCodec"/> for one wire line (the text before '\n',
    /// still carrying the trailing '\x80') and returns the plaintext message.
    /// </summary>
    public static string DecodeOpenSeal(string wireLine)
    {
        if (wireLine.Length == 0 || wireLine[^1] != '\u0080' || !wireLine.StartsWith(FakeTimesealCodec.Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Not a timeseal line.");
        }

        return wireLine[FakeTimesealCodec.Prefix.Length..^1];
    }

    private sealed class FakeTransport : IFicsTransport
    {
        private readonly IFicsConnection _connection;

        public FakeTransport(IFicsConnection connection)
        {
            _connection = connection;
        }

        public Task<IFicsConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_connection);
        }
    }

    private sealed class FaultableWriter : PipeWriter
    {
        private readonly PipeWriter _inner;
        private readonly FakeFicsServer _server;

        public FaultableWriter(PipeWriter inner, FakeFicsServer server)
        {
            _inner = inner;
            _server = server;
        }

        private void ThrowIfFailing()
        {
            if (_server.FailClientWrites)
            {
                throw new IOException("Simulated write failure.");
            }
        }

        public override void Advance(int bytes) => _inner.Advance(bytes);

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            ThrowIfFailing();
            return _inner.GetMemory(sizeHint);
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            ThrowIfFailing();
            return _inner.GetSpan(sizeHint);
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            return _inner.FlushAsync(cancellationToken);
        }

        public override void CancelPendingFlush() => _inner.CancelPendingFlush();

        public override void Complete(Exception? exception = null) => _inner.Complete(exception);
    }

    private sealed class FakeConnection : IFicsConnection
    {
        public FakeConnection(PipeReader input, PipeWriter output)
        {
            Input = input;
            Output = output;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        public ValueTask DisposeAsync()
        {
            Input.Complete();
            Output.Complete();
            return ValueTask.CompletedTask;
        }
    }
}
