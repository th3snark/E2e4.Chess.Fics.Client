namespace E2e4.Chess.Fics.Client;

/// <summary>Where a <see cref="FicsClient"/> is in its single-use session, from connecting through to being ready for commands.</summary>
public enum FicsClientState
{
    Disconnected,
    Connecting,
    AwaitingLogin,
    LoggingIn,
    Negotiating,
    Ready,
}
