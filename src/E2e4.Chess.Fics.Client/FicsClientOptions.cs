using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace E2e4.Chess.Fics.Client;

public sealed class FicsClientOptions
{
    public string Host { get; set; } = "freechess.org";

    public int Port { get; set; } = 5000;

    /// <summary>
    /// Encodes outbound lines with the timeseal cipher (server-side lag compensation).
    /// Null, the default, sends plain lines and no timeseal hello.
    /// </summary>
    public ITimesealCodec? Timeseal { get; set; }

    /// <summary>
    /// Negotiate "iset block 1" so command responses are framed and correlated by id.
    /// Off by default: FICS documents block mode as incompatible with timeseal.
    /// </summary>
    public bool UseBlockMode { get; set; }

    /// <summary>Send "iset lock 1" at the end of negotiation so the ivars cannot change mid-session.</summary>
    public bool LockIvars { get; set; }

    /// <summary>Value for "set interface", shown to other players by "finger".</summary>
    public string InterfaceName { get; set; } = "E2e4.Chess.Fics.Client 1.0";

    /// <summary>Negotiate seekinfo/seekremove/showownseek so seek ads arrive as &lt;s&gt;/&lt;sn&gt;/&lt;sr&gt;/&lt;sc&gt; lines.</summary>
    public bool EnableSeekInfo { get; set; } = true;

    /// <summary>
    /// How long login waits for the server to finish answering the negotiation commands before
    /// carrying on regardless. Some servers answer fewer prompts than commands.
    /// </summary>
    public TimeSpan NegotiationTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long <see cref="FicsClient.LoginAsync"/> may take in all (prompts, password and negotiation) before it
    /// fails with a <see cref="TimeoutException"/> and closes the connection. Use
    /// <see cref="Timeout.InfiniteTimeSpan"/> to wait for ever.
    /// </summary>
    public TimeSpan LoginTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Negotiate "iset pin 1" for &lt;wa&gt;/&lt;wd&gt; player arrival/departure notifications.</summary>
    public bool EnablePin { get; set; }

    /// <summary>
    /// The clock behind the negotiation timeout. Replace it to control time in tests.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Receives diagnostics for failures the client contains rather than throws, such as an
    /// exception thrown by an event subscriber. Defaults to discarding them.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>A copy that can be changed without affecting this instance.</summary>
    internal FicsClientOptions Clone() => (FicsClientOptions)MemberwiseClone();
}
