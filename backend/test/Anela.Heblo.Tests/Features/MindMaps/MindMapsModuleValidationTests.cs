using Anela.Heblo.Application.Features.MindMaps;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

/// <summary>
/// Proves that MindMapsModule's `.ValidateDataAnnotations().ValidateOnStart()` wiring is
/// actually enforced by the host, not just declared: the [Range(1024, 64000)] on
/// MindMapsOptions.UpdaterMaxOutputTokens is inert unless ValidateOnStart is wired up, and
/// a typo dropping it would only surface as a silently-ignored config value in production.
/// Uses UseStubUpdater=true so the host never needs a real keyed IChatClient.
/// </summary>
public sealed class MindMapsModuleValidationTests
{
    private static IHost BuildHost(Dictionary<string, string?> configValues)
    {
        var merged = new Dictionary<string, string?>(configValues)
        {
            ["MindMaps:UseStubUpdater"] = "true"
        };

        return new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(merged))
            .ConfigureServices((context, services) => services.AddMindMapsModule(context.Configuration))
            .Build();
    }

    [Fact]
    public async Task StartAsync_throws_when_UpdaterMaxOutputTokens_is_below_range()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["MindMaps:UpdaterMaxOutputTokens"] = "1023"
        });

        var act = async () => await host.StartAsync();

        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Message.Should().Contain(nameof(MindMapsOptions.UpdaterMaxOutputTokens));
    }

    [Fact]
    public async Task StartAsync_throws_when_UpdaterMaxOutputTokens_is_above_range()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["MindMaps:UpdaterMaxOutputTokens"] = "64001"
        });

        var act = async () => await host.StartAsync();

        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Message.Should().Contain(nameof(MindMapsOptions.UpdaterMaxOutputTokens));
    }

    [Fact]
    public async Task StartAsync_succeeds_when_UpdaterMaxOutputTokens_is_within_range()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["MindMaps:UpdaterMaxOutputTokens"] = "16384"
        });

        await host.StartAsync();

        // If StartAsync didn't throw, validation passed. Stop the host cleanly.
        await host.StopAsync();
    }
}
