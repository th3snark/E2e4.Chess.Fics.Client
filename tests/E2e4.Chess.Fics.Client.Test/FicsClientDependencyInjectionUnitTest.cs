using E2e4.Chess.Fics.Client.Test.Support;
using E2e4.Chess.Fics.Client.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static E2e4.Chess.Fics.Client.Test.Support.ClientHarness;

namespace E2e4.Chess.Fics.Client.Test;

/// <summary>The container registration from <c>AddFicsClient</c>.</summary>
public class FicsClientDependencyInjectionUnitTest
{
    private sealed class FixedTimeProvider : TimeProvider
    {
    }

    private sealed class PassThroughCodec : ITimesealCodec
    {
        public string Encode(string line) => line + "\n";
    }

    private static ServiceProvider Build(Action<IServiceCollection>? arrange = null, Action<FicsClientOptions>? configure = null)
    {
        ServiceCollection services = new();
        arrange?.Invoke(services);
        services.AddFicsClient(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Factory_is_a_singleton_and_clients_are_transient()
    {
        await using ServiceProvider provider = Build();

        Assert.Same(provider.GetRequiredService<IFicsClientFactory>(), provider.GetRequiredService<IFicsClientFactory>());
        Assert.IsType<TcpFicsTransport>(provider.GetRequiredService<IFicsTransport>());
        Assert.NotSame(provider.GetRequiredService<IFicsClient>(), provider.GetRequiredService<IFicsClient>());
        Assert.IsType<FicsClient>(provider.GetRequiredService<IFicsClient>());
    }

    [Fact]
    public async Task Configured_defaults_reach_every_client()
    {
        await using ServiceProvider provider = Build(configure: o =>
        {
            o.Host = "example.test";
            o.Port = 5001;
            o.UseBlockMode = true;
        });

        FicsClient client = Assert.IsType<FicsClient>(provider.GetRequiredService<IFicsClientFactory>().Create());

        Assert.Equal("example.test", client.Options.Host);
        Assert.Equal(5001, client.Options.Port);
        Assert.True(client.Options.UseBlockMode);
    }

    [Fact]
    public async Task Create_with_overrides_changes_only_that_client()
    {
        await using ServiceProvider provider = Build(configure: o => o.Host = "example.test");
        IFicsClientFactory factory = provider.GetRequiredService<IFicsClientFactory>();

        FicsClient overridden = Assert.IsType<FicsClient>(factory.Create(o => o.Host = "other.test"));
        FicsClient plain = Assert.IsType<FicsClient>(factory.Create());

        Assert.Equal("other.test", overridden.Options.Host);
        Assert.Equal("example.test", plain.Options.Host);
        Assert.NotSame(overridden.Options, plain.Options);
    }

    [Fact]
    public async Task Container_services_fill_logger_clock_and_timeseal()
    {
        FixedTimeProvider clock = new();
        PassThroughCodec codec = new();
        await using ServiceProvider provider = Build(services =>
        {
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ITimesealCodec>(codec);
        });

        FicsClient client = Assert.IsType<FicsClient>(provider.GetRequiredService<IFicsClient>());

        Assert.NotSame(NullLogger.Instance, client.Options.Logger);
        Assert.Same(clock, client.Options.TimeProvider);
        Assert.Same(codec, client.Options.Timeseal);
    }

    [Fact]
    public async Task Nothing_registered_keeps_the_option_defaults()
    {
        await using ServiceProvider provider = Build();

        FicsClient client = Assert.IsType<FicsClient>(provider.GetRequiredService<IFicsClient>());

        Assert.Same(NullLogger.Instance, client.Options.Logger);
        Assert.Same(TimeProvider.System, client.Options.TimeProvider);
        Assert.Null(client.Options.Timeseal);
    }

    [Fact]
    public async Task Explicit_options_win_over_container_services()
    {
        PassThroughCodec explicitCodec = new();
        await using ServiceProvider provider = Build(
            services => services.AddSingleton<ITimesealCodec>(new PassThroughCodec()),
            o => o.Timeseal = explicitCodec);

        FicsClient client = Assert.IsType<FicsClient>(provider.GetRequiredService<IFicsClient>());

        Assert.Same(explicitCodec, client.Options.Timeseal);
    }

    [Fact]
    public async Task A_transport_registered_first_is_the_one_used()
    {
        FakeFicsServer server = new();
        await using ServiceProvider provider = Build(services => services.AddSingleton(server.Transport));

        await using IFicsClient client = provider.GetRequiredService<IFicsClient>();
        await Soon(client.ConnectAsync());

        Assert.Equal(FicsClientState.AwaitingLogin, client.State);
    }

    [Fact]
    public async Task Options_can_be_configured_through_the_options_pattern()
    {
        ServiceCollection services = new();
        services.AddFicsClient();
        services.Configure<FicsClientOptions>(o => o.Port = 6000);
        await using ServiceProvider provider = services.BuildServiceProvider();

        FicsClient client = Assert.IsType<FicsClient>(provider.GetRequiredService<IFicsClient>());

        Assert.Equal(6000, client.Options.Port);
    }

    [Fact]
    public void Registering_twice_does_not_duplicate_services()
    {
        ServiceCollection services = new();
        services.AddFicsClient();
        services.AddFicsClient();

        Assert.Single(services, d => d.ServiceType == typeof(IFicsClientFactory));
        Assert.Single(services, d => d.ServiceType == typeof(IFicsClient));
        Assert.Single(services, d => d.ServiceType == typeof(IFicsTransport));
    }
}
