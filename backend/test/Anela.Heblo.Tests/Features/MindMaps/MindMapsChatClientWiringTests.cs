using Anela.Heblo.Adapters.Anthropic;
using Anela.Heblo.Application.Features.MindMaps;
using Anela.Heblo.Application.Features.MindMaps.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

/// <summary>
/// Proves the MindMaps module's DI wiring actually resolves from a built container:
/// the keyed "mindmap-updater" IChatClient registered by AddAnthropicAdapter, and the
/// IMindMapUpdater implementation swap driven by MindMaps:UseStubUpdater — the switch
/// that makes staging/E2E deterministic. A typo in either would otherwise only surface
/// on the first real request in production.
/// </summary>
public class MindMapsChatClientWiringTests
{
    private static ServiceProvider BuildProvider(bool useStubUpdater = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MindMaps:UseStubUpdater"] = useStubUpdater ? "true" : "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAnthropicAdapter(configuration);
        services.AddMindMapsModule(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void MindMapUpdater_keyed_chat_client_resolves()
    {
        using var provider = BuildProvider();

        var chatClient = provider.GetRequiredKeyedService<IChatClient>(AnthropicAdapterServiceCollectionExtensions.MindMapUpdaterClientKey);

        chatClient.Should().NotBeNull();
    }

    [Fact]
    public void IMindMapUpdater_resolves_to_ClaudeMindMapUpdater_by_default()
    {
        using var provider = BuildProvider(useStubUpdater: false);

        var updater = provider.GetRequiredService<IMindMapUpdater>();

        updater.Should().BeOfType<ClaudeMindMapUpdater>();
    }

    [Fact]
    public void IMindMapUpdater_resolves_to_StubMindMapUpdater_when_UseStubUpdater_is_true()
    {
        using var provider = BuildProvider(useStubUpdater: true);

        var updater = provider.GetRequiredService<IMindMapUpdater>();

        updater.Should().BeOfType<StubMindMapUpdater>();
    }
}
