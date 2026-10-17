using System.Buffers;
using System.IO.Pipelines;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using E2e4.Chess.Fics.Protocol.Messages;
using E2e4.Chess.Fics.Protocol;
using E2e4.Chess.Fics.Client.Transport;

namespace E2e4.Chess.Fics.Client;

/// <summary>
/// A FICS session: connection, timeseal handshake, login, iset negotiation,
/// framing of the interleaved server output into typed messages, and (in block
/// mode) request/response correlation.
///
/// Events are raised on the read-loop thread; WPF hosts must marshal to their
/// Dispatcher. An exception thrown by an event subscriber is contained: it never
/// reaches the read loop or other subscribers. The same message stream is also
/// available as <see cref="ReadAllMessagesAsync"/> for await-foreach consumers.
///
/// A client is single-use: once a connection has been established (and later
/// closed) create a new <see cref="FicsClient"/>. A failed
/// <see cref="ConnectAsync"/> may be retried. A failed or cancelled login closes
/// the connection.
/// </summary>
public sealed partial class FicsClient : IFicsClient
{
    private const string TimesealHello = "TIMESTAMP|openseal|Running on an operating system|";

    // The reply to a "[G]" timeseal ping: the two bytes 0x02 '9', encrypted
    // by the configured timeseal codec.
    private const string TimesealPingReply = "\u00029";

    [GeneratedRegex("^Press return to enter the server as \"(?<name>[^\"]+)\":")]
    private static partial Regex GuestConfirmRegex();

    [GeneratedRegex(@"^\*\*\*\* Starting FICS session as (?<name>\S+) \*\*\*\*")]
    private static partial Regex SessionStartRegex();

    [GeneratedRegex(@"^\*\*\*\* Invalid password! \*\*\*\*")]
    private static partial Regex InvalidPasswordRegex();

    // Bounds the lines kept for a command whose response never completes.
    private const int MaxPendingLines = 10_000;

    private readonly FicsClientOptions _options;
    private readonly IFicsTransport _transport;
    private readonly FicsFrameTokenizer _tokenizer = new();
    private readonly BlockCommandTracker _blockTracker = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Channel<FicsMessage> _messageChannel = Channel.CreateBounded<FicsMessage>(
        new BoundedChannelOptions(1024)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    // True on the async flow of the read loop, so DisposeAsync can tell it is being
    // called from an event handler and must not wait for the loop it is running on.
    private readonly AsyncLocal<bool> _onReadLoop = new();

    private int _state;
    private int _disposed;
    private volatile string? _userName;

    private IFicsConnection? _connection;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    // Login-flow signals, completed by the read loop.
    private TaskCompletionSource? _loginPrompt;
    private TaskCompletionSource? _passwordPrompt;
    private TaskCompletionSource<string>? _guestConfirm;
    private TaskCompletionSource<string>? _sessionStart;

    // Non-block mode bookkeeping, all guarded by _pendingGate. The read loop and
    // callers both touch it, and the "command abandoned" and "prompt arrives"
    // decisions must be atomic with respect to each other.
    //
    // _pending: the command whose output is being collected up to the next "fics% " prompt.
    // _promptDebt: the number of "fics% " prompts still owed to lines we sent without
    // collecting their responses (login + negotiation + raw sends + abandoned
    // commands). Those prompts must not complete a pending SendCommandAsync.
    private readonly object _pendingGate = new();
    private PendingCommand? _pending;
    private int _promptDebt;
    private TaskCompletionSource? _negotiationSettled;

    public FicsClient(FicsClientOptions? options = null, IFicsTransport? transport = null)
    {
        _options = options ?? new FicsClientOptions();
        _transport = transport ?? new TcpFicsTransport();
    }

    public FicsClientState State
    {
        get => (FicsClientState)Volatile.Read(ref _state);
        private set
        {
            if (Interlocked.Exchange(ref _state, (int)value) != (int)value)
            {
                _options.Logger.LogDebug("State -> {State}", value);
            }
        }
    }

    /// <summary>Moves from <paramref name="from"/> to <paramref name="to"/> only if the client is in <paramref name="from"/>, logging the change.</summary>
    private bool TryTransition(FicsClientState from, FicsClientState to)
    {
        if (Interlocked.CompareExchange(ref _state, (int)to, (int)from) != (int)from)
        {
            return false;
        }

        _options.Logger.LogDebug("State -> {State}", to);
        return true;
    }

    /// <summary>The actual session user name (e.g. "GuestKRNP"), known once logged in.</summary>
    public string? UserName => _userName;

    public FicsClientOptions Options => _options;

    /// <summary>Every parsed server message not consumed by the login flow. Raised on the read-loop thread.</summary>
    public event EventHandler<FicsMessageEventArgs>? MessageReceived;

    /// <summary>Raised for every style-12 board update. Raised on the read-loop thread.</summary>
    public event EventHandler<Style12EventArgs>? BoardUpdated;

    /// <summary>Every frame as it comes off the wire, for transcript display. Raised on the read-loop thread.</summary>
    public event EventHandler<FicsRawTextEventArgs>? RawTextReceived;

    /// <summary>
    /// Raised once when the connection ends, on the read-loop thread. The event data carries
    /// the error that ended it, or null for a clean close or a local dispose.
    /// </summary>
    public event EventHandler<FicsDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// The same stream of messages as <see cref="MessageReceived"/> as an async
    /// sequence. The backing channel is bounded (oldest dropped), so an
    /// unconsumed enumerator never blocks the session. There is a single shared
    /// stream: concurrent enumerators each receive only some of the messages.
    /// </summary>
    public IAsyncEnumerable<FicsMessage> ReadAllMessagesAsync(CancellationToken cancellationToken = default)
    {
        return _messageChannel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>Connects, sends the timeseal hello (when a codec is set) and starts the read loop. Follow with a login call.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_connection is not null || !TryTransition(FicsClientState.Disconnected, FicsClientState.Connecting))
        {
            throw new InvalidOperationException(
                "The client is single-use: create a new FicsClient for a new connection.");
        }

        _loginPrompt = NewSignal();
        _passwordPrompt = NewSignal();
        _guestConfirm = NewSignal<string>();
        _sessionStart = NewSignal<string>();

        try
        {
            _connection = await _transport.ConnectAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false);

            if (_options.Timeseal is not null)
            {
                // The hello must be the first thing on the wire, before any server output.
                await SendWireBytesAsync(FicsLineEncoder.Encode(TimesealHello, _options.Timeseal), cancellationToken).ConfigureAwait(false);
            }

            State = FicsClientState.AwaitingLogin;
            _readLoopCts = new CancellationTokenSource();
            _readLoopTask = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _options.Logger.LogDebug(ex, "Connect to {Host}:{Port} failed", _options.Host, _options.Port);

            // Do not strand a half-open connection (and the "already connected" guard) behind a failed hello.
            IFicsConnection? failed = _connection;
            _connection = null;
            State = FicsClientState.Disconnected;
            if (failed is not null)
            {
                try
                {
                    await failed.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposeError)
                {
                    _options.Logger.LogDebug(disposeError, "Disposing the failed connection also failed");
                }
            }

            throw;
        }
    }

    public Task<string> LoginAsGuestAsync(CancellationToken cancellationToken = default)
    {
        return LoginAsync("guest", password: null, cancellationToken);
    }

    /// <summary>
    /// Logs in and performs iset/style negotiation. Returns the actual session
    /// user name. Pass a null password for guest-style logins. If login fails or is
    /// cancelled the connection is closed: the login conversation cannot be resumed.
    /// </summary>
    public async Task<string> LoginAsync(string user, string? password = null, CancellationToken cancellationToken = default)
    {
        // Checked before the state changes: a bad argument must not close a connection that is still usable.
        FicsLineEncoder.ThrowIfNotSingleLine(user, nameof(user));
        if (password is not null)
        {
            FicsLineEncoder.ThrowIfNotSingleLine(password, nameof(password));
        }

        if (!TryTransition(FicsClientState.AwaitingLogin, FicsClientState.LoggingIn))
        {
            throw new InvalidOperationException($"Cannot log in while {State}.");
        }

        using CancellationTokenSource timeout = _options.LoginTimeout == Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource()
            : new CancellationTokenSource(_options.LoginTimeout, _options.TimeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            return await LoginCoreAsync(user, password, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _options.Logger.LogDebug("Login timed out after {Timeout}; closing the connection", _options.LoginTimeout);
            await CloseAfterFailureAsync().ConfigureAwait(false);
            throw new TimeoutException($"Login did not complete within {_options.LoginTimeout}.");
        }
        catch (Exception ex)
        {
            _options.Logger.LogDebug(ex, "Login failed; closing the connection");
            await CloseAfterFailureAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<string> LoginCoreAsync(string user, string? password, CancellationToken cancellationToken)
    {
        // Pre-count the "fics% " prompts the login and negotiation will produce
        // (one after the session-start banner, one per negotiation command) so
        // they cannot complete a later SendCommandAsync prematurely.
        List<string> negotiationCommands = BuildNegotiationCommands();
        if (!_options.UseBlockMode)
        {
            _negotiationSettled = NewSignal();
            lock (_pendingGate)
            {
                _promptDebt = negotiationCommands.Count + 1;
            }
        }

        await WaitAsync(_loginPrompt!.Task, cancellationToken).ConfigureAwait(false);
        await SendLineCoreAsync(user, cancellationToken).ConfigureAwait(false);

        Task<string> sessionStart = _sessionStart!.Task;
        Task completed = await Task.WhenAny(sessionStart, _passwordPrompt!.Task, _guestConfirm!.Task)
            .WaitAsync(cancellationToken).ConfigureAwait(false);

        if (completed == _passwordPrompt.Task)
        {
            if (password is null)
            {
                throw new FicsException($"The account \"{user}\" requires a password.");
            }

            await SendLineCoreAsync(password, cancellationToken).ConfigureAwait(false);
        }
        else if (completed == _guestConfirm.Task)
        {
            // "Press return to enter the server as "GuestXXXX":" - confirm with an empty line.
            await SendLineCoreAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        }

        string name = await WaitAsync(sessionStart, cancellationToken).ConfigureAwait(false);
        _userName = name;

        State = FicsClientState.Negotiating;
        await NegotiateAsync(negotiationCommands, cancellationToken).ConfigureAwait(false);
        if (!TryTransition(FicsClientState.Negotiating, FicsClientState.Ready))
        {
            throw new FicsException("The connection closed during login.");
        }

        return name;
    }

    /// <summary>Tears the connection down after a failed login and waits for the read loop to finish.</summary>
    private async Task CloseAfterFailureAsync()
    {
        _readLoopCts?.Cancel();

        IFicsConnection? connection = _connection;
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _options.Logger.LogDebug(ex, "Disposing the connection after a failed login also failed");
            }
        }

        if (_readLoopTask is not null)
        {
            try
            {
                await _readLoopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _options.Logger.LogDebug(ex, "The read loop ended with an error after a failed login");
            }
        }

        State = FicsClientState.Disconnected;
    }

    /// <summary>
    /// The negotiation commands sent right after login. In block mode
    /// "iset block 1" must come last: every subsequent command needs an id.
    /// </summary>
    private List<string> BuildNegotiationCommands()
    {
        List<string> commands = new()
        {
            "iset defprompt 1",
            "iset startpos 1",
            "iset ms 1",
            "iset pendinfo 1",
            "iset nowrap 1",
            "iset gameinfo 1",
        };

        if (_options.EnableSeekInfo)
        {
            commands.Add("iset seekinfo 1");
            commands.Add("iset seekremove 1");
            commands.Add("iset showownseek 1");
        }

        if (_options.EnablePin)
        {
            commands.Add("iset pin 1");
        }

        if (_options.LockIvars && !_options.UseBlockMode)
        {
            commands.Add("iset lock 1");
        }

        commands.Add($"set interface {_options.InterfaceName}");
        commands.Add("set style 12");
        commands.Add("set bell 0");

        if (_options.UseBlockMode)
        {
            commands.Add("iset block 1");
        }

        return commands;
    }

    private async Task NegotiateAsync(List<string> commands, CancellationToken cancellationToken)
    {
        foreach (string command in commands)
        {
            await SendLineCoreAsync(command, cancellationToken).ConfigureAwait(false);
        }

        if (_options.UseBlockMode)
        {
            if (_options.LockIvars)
            {
                // Block mode is active now, so even this must carry an id.
                TaskCompletionSource<FicsResponse> completion = NewSignal<FicsResponse>();
                int id = await _blockTracker.RentAsync(completion, cancellationToken).ConfigureAwait(false);
                try
                {
                    await SendLineCoreAsync($"{id} iset lock 1", cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _blockTracker.Release(id, completion);
                    throw;
                }

                try
                {
                    await completion.Task.WaitAsync(_options.NegotiationTimeout, _options.TimeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // The server never confirmed "iset lock"; carry on, it only affects ivar locking.
                }
            }
        }
        else
        {
            // Wait for the negotiation chatter to drain so its prompts cannot leak
            // into the first real command's response.
            try
            {
                await _negotiationSettled!.Task.WaitAsync(_options.NegotiationTimeout, _options.TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Some servers answer fewer prompts than commands; the session is usable regardless.
            }
        }
    }

    /// <summary>
    /// Sends a command and returns its response. In block mode the response is
    /// exactly the framed output correlated by id; otherwise it is everything the
    /// server sent up to the next "fics% " prompt (one command in flight at a
    /// time), which can include unrelated asynchronous output.
    /// </summary>
    public async Task<FicsResponse> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        FicsLineEncoder.ThrowIfNotSingleLine(command, nameof(command));

        if (State != FicsClientState.Ready)
        {
            throw new InvalidOperationException($"Cannot send commands while {State}.");
        }

        if (_options.UseBlockMode)
        {
            TaskCompletionSource<FicsResponse> blockCompletion = NewSignal<FicsResponse>();
            int id = await _blockTracker.RentAsync(blockCompletion, cancellationToken).ConfigureAwait(false);
            try
            {
                await SendLineCoreAsync($"{id} {command}", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Nothing reached the server, so no response will ever free this id.
                _blockTracker.Release(id, blockCompletion);
                throw;
            }

            return await WaitAsync(blockCompletion.Task, cancellationToken).ConfigureAwait(false);
        }

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PendingCommand pending = new();
            lock (_pendingGate)
            {
                _pending = pending;
            }

            bool sent = false;
            try
            {
                await SendLineCoreAsync(command, cancellationToken).ConfigureAwait(false);
                sent = true;
                return await WaitAsync(pending.Completion.Task, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_pendingGate)
                {
                    _pending = null;

                    // The server still answers an abandoned (cancelled) command with a prompt;
                    // that prompt must not complete the next command.
                    if (sent && !pending.Completion.Task.IsCompleted)
                    {
                        _promptDebt++;
                    }
                }
            }
        }
        finally
        {
            ReleaseQuietly(_commandLock);
        }
    }

    /// <summary>
    /// Sends one raw line (encoded by the timeseal codec, if any) with no response correlation.
    /// In non-block mode the line is sequenced after any command in flight, so its
    /// "fics% " prompt cannot be mistaken for that command's. In block mode a raw line carries
    /// no command id, so the server ignores it; use <see cref="SendCommandAsync"/> there.
    /// In non-block mode the output of an earlier raw line can be attributed to the next command.
    /// </summary>
    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        FicsLineEncoder.ThrowIfNotSingleLine(line, nameof(line));

        if (_options.UseBlockMode || State is not (FicsClientState.Negotiating or FicsClientState.Ready))
        {
            await SendLineCoreAsync(line, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // This line's eventual "fics% " prompt belongs to it, not to any command sent later.
            lock (_pendingGate)
            {
                _promptDebt++;
            }

            try
            {
                await SendLineCoreAsync(line, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_pendingGate)
                {
                    _promptDebt--;
                }

                throw;
            }
        }
        finally
        {
            ReleaseQuietly(_commandLock);
        }
    }

    private Task SendLineCoreAsync(string line, CancellationToken cancellationToken)
    {
        return SendWireBytesAsync(FicsLineEncoder.Encode(line, _options.Timeseal), cancellationToken);
    }

    private async Task SendWireBytesAsync(byte[] wireBytes, CancellationToken cancellationToken)
    {
        IFicsConnection connection = _connection ?? throw new InvalidOperationException("Not connected.");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.Output.WriteAsync(wireBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseQuietly(_sendLock);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        _onReadLoop.Value = true;
        PipeReader input = _connection!.Input;
        Exception? error = null;
        try
        {
            while (true)
            {
                ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (_tokenizer.TryReadFrame(ref buffer, out FicsFrame? frame))
                {
                    await DispatchAsync(frame!, cancellationToken).ConfigureAwait(false);
                }

                input.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Local shutdown: not an error.
        }
        catch (Exception ex)
        {
            // The connection failed; report it through Disconnected below.
            error = ex;
            _options.Logger.LogDebug(ex, "The read loop ended with an error");
        }
        finally
        {
            OnConnectionClosed(error);
        }
    }

    private async Task DispatchAsync(FicsFrame frame, CancellationToken cancellationToken)
    {
        Raise(RawTextReceived, new FicsRawTextEventArgs(frame.Kind, frame.Text));

        switch (frame.Kind)
        {
            case FicsFrameKind.TimesealPing:
                if (_options.Timeseal is not null)
                {
                    // Must be answered at once; the server uses it to measure lag.
                    await SendWireBytesAsync(
                        FicsLineEncoder.Encode(TimesealPingReply, _options.Timeseal),
                        cancellationToken).ConfigureAwait(false);
                }
                break;

            case FicsFrameKind.Prompt:
                DispatchPrompt(frame.Text);
                break;

            case FicsFrameKind.Block:
                DispatchBlock(frame);
                break;

            case FicsFrameKind.Line:
                DispatchLine(frame.Text);
                break;
        }
    }

    private void DispatchPrompt(string prompt)
    {
        switch (prompt)
        {
            case FicsPrompts.Login:
                _loginPrompt?.TrySetResult();
                break;

            case FicsPrompts.Password:
                _passwordPrompt?.TrySetResult();
                break;

            case FicsPrompts.Command:
                lock (_pendingGate)
                {
                    if (_promptDebt > 0)
                    {
                        if (--_promptDebt == 0)
                        {
                            _negotiationSettled?.TrySetResult();
                        }
                    }
                    else
                    {
                        CompletePendingCommand();
                    }
                }
                break;
        }
    }

    /// <summary>Completes the command in flight with the lines collected so far. Caller holds _pendingGate.</summary>
    private void CompletePendingCommand()
    {
        PendingCommand? pending = _pending;
        if (pending is null)
        {
            return;
        }

        string text = string.Join("\n", pending.Lines);
        List<FicsMessage> messages = pending.Lines
            .Where(line => line.Length > 0)
            .Select(ParseLine)
            .ToList();
        pending.Completion.TrySetResult(new FicsResponse(0, 0, text, messages));
    }

    private void DispatchBlock(FicsFrame frame)
    {
        List<FicsMessage> messages = new();
        foreach (string line in frame.Text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                messages.Add(ParseLine(trimmed));
            }
        }

        FicsResponse response = new(frame.BlockCommandId, frame.BlockCode, frame.Text, messages);

        if (frame.BlockCommandId == 0 || !_blockTracker.TryComplete(frame.BlockCommandId, response))
        {
            // Server-posed output (id 0) or an id we no longer track: surface the
            // contents as ordinary unsolicited messages.
            foreach (FicsMessage message in messages)
            {
                PublishMessage(message);
            }

            return;
        }

        // The response reached its awaiting command, but structured state updates
        // inside it (board positions from our own moves, seek ads from "sought",
        // ...) must still reach event/stream consumers.
        foreach (FicsMessage message in messages)
        {
            if (message is not UnknownLineMessage)
            {
                PublishMessage(message);
            }
        }
    }

    /// <summary>Parses one line; a parser fault becomes an unknown line instead of ending the session.</summary>
    private FicsMessage ParseLine(string line)
    {
        try
        {
            return FicsMessageParser.Parse(line);
        }
        catch (Exception ex)
        {
            _options.Logger.LogDebug(ex, "The parser failed on a line; treating it as unknown");
            return new UnknownLineMessage { RawText = line };
        }
    }

    private void DispatchLine(string line)
    {
        if ((State is FicsClientState.AwaitingLogin or FicsClientState.LoggingIn) && TryHandleLoginLine(line))
        {
            return;
        }

        lock (_pendingGate)
        {
            if (_pending is { } pending && pending.Lines.Count < MaxPendingLines)
            {
                pending.Lines.Add(line);
            }
        }

        if (line.Length > 0)
        {
            PublishMessage(ParseLine(line));
        }
    }

    private bool TryHandleLoginLine(string line)
    {
        Match guestConfirm = GuestConfirmRegex().Match(line);
        if (guestConfirm.Success)
        {
            _guestConfirm?.TrySetResult(guestConfirm.Groups["name"].Value);
            return true;
        }

        Match sessionStart = SessionStartRegex().Match(line);
        if (sessionStart.Success)
        {
            // The name may carry tags, e.g. "GuestKRNP(U)".
            string name = sessionStart.Groups["name"].Value;
            int tagStart = name.IndexOf('(');
            if (tagStart > 0)
            {
                name = name[..tagStart];
            }

            _sessionStart?.TrySetResult(name);
            return true;
        }

        if (InvalidPasswordRegex().IsMatch(line))
        {
            Fault(_sessionStart, new FicsException("Invalid password."));
            return true;
        }

        return false;
    }

    private void PublishMessage(FicsMessage message)
    {
        Raise(MessageReceived, new FicsMessageEventArgs(message));

        if (message is Style12Message style12)
        {
            Raise(BoardUpdated, new Style12EventArgs(style12.Board));
        }

        _messageChannel.Writer.TryWrite(message);
    }

    /// <summary>
    /// Raises an event, isolating subscribers from each other and from the read loop:
    /// a throwing subscriber must not end the session or starve later subscribers.
    /// </summary>
    private void Raise<TEventArgs>(EventHandler<TEventArgs>? handler, TEventArgs args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TEventArgs>)subscriber)(this, args);
            }
            catch (Exception ex)
            {
                // Contained: a subscriber's bug must not end the read loop or starve other subscribers.
                _options.Logger.LogWarning(ex, "A {EventArgs} subscriber threw; the exception was contained.", typeof(TEventArgs).Name);
            }
        }
    }

    private void OnConnectionClosed(Exception? error)
    {
        State = FicsClientState.Disconnected;

        const string closedMessage = "The connection to the server was closed.";
        FicsException closed = error is null ? new(closedMessage) : new(closedMessage, error);
        Fault(_loginPrompt, closed);
        Fault(_passwordPrompt, closed);
        Fault(_guestConfirm, closed);
        Fault(_sessionStart, closed);
        Fault(_negotiationSettled, closed);

        PendingCommand? pending;
        lock (_pendingGate)
        {
            pending = _pending;
        }

        if (pending is not null)
        {
            Fault(pending.Completion, closed);
        }

        _blockTracker.FaultAll(closed);
        _messageChannel.Writer.TryComplete();

        Raise(Disconnected, new FicsDisconnectedEventArgs(error));
    }

    /// <summary>
    /// Closes the connection and stops the read loop. Safe to call more than once, and from
    /// inside an event handler (the read loop is then not awaited, as it is the caller).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _readLoopCts?.Cancel();

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Dispose is once-only (_disposed is already set), so the rest of the cleanup must still run.
                _options.Logger.LogDebug(ex, "The connection failed to close cleanly during dispose");
            }
        }

        if (_readLoopTask is not null && !_onReadLoop.Value)
        {
            try
            {
                await _readLoopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _options.Logger.LogDebug(ex, "The read loop ended with an error during dispose");
            }

            _readLoopCts?.Dispose();
        }

        // If the client never connected there is no read loop to end the stream.
        _messageChannel.Writer.TryComplete();

        // Commands still in flight release these as they unwind, which ReleaseQuietly tolerates.
        _sendLock.Dispose();
        _commandLock.Dispose();
        _blockTracker.Dispose();
    }

    /// <summary>Releases a lock that <see cref="DisposeAsync"/> may already have disposed under a command that was still unwinding.</summary>
    private static void ReleaseQuietly(SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<T> NewSignal<T>()
    {
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Faults a signal and marks the exception observed, so an unawaited signal never raises UnobservedTaskException.</summary>
    private static void Fault(TaskCompletionSource? signal, Exception exception)
    {
        if (signal is not null && signal.TrySetException(exception))
        {
            _ = signal.Task.Exception;
        }
    }

    private static void Fault<T>(TaskCompletionSource<T>? signal, Exception exception)
    {
        if (signal is not null && signal.TrySetException(exception))
        {
            _ = signal.Task.Exception;
        }
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitAsync(Task task, CancellationToken cancellationToken)
    {
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A non-block command waiting for the next prompt, and the lines collected for it.</summary>
    private sealed class PendingCommand
    {
        public TaskCompletionSource<FicsResponse> Completion { get; } = NewSignal<FicsResponse>();

        public List<string> Lines { get; } = new();
    }
}
