using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace E2e4.Chess.Fics.Client;

/// <summary>
/// Fills the options a container can supply (logger, clock, timeseal codec) when the host has not
/// set them. Everything is optional: with nothing registered the defaults stay.
/// </summary>
internal sealed class FicsClientOptionsSetup : IPostConfigureOptions<FicsClientOptions>
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly TimeProvider? _timeProvider;
    private readonly ITimesealCodec? _timeseal;

    public FicsClientOptionsSetup(ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null, ITimesealCodec? timeseal = null)
    {
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _timeseal = timeseal;
    }

    public void PostConfigure(string? name, FicsClientOptions options)
    {
        if (_loggerFactory is not null && ReferenceEquals(options.Logger, NullLogger.Instance))
        {
            options.Logger = _loggerFactory.CreateLogger<FicsClient>();
        }

        if (_timeProvider is not null && ReferenceEquals(options.TimeProvider, TimeProvider.System))
        {
            options.TimeProvider = _timeProvider;
        }

        options.Timeseal ??= _timeseal;
    }
}
