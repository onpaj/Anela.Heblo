using Anela.Heblo.Xcc.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class HebloResiliencePipelineExtensionsTests
{
    private static IServiceProvider BuildProvider(Dictionary<string, string?> configData)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OutboundResilienceOptions>(config.GetSection(OutboundResilienceOptions.SectionName));
        services.AddHebloOutboundResiliencePipelines();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NoDependenciesConfigured_CanResolveProvider()
    {
        // Arrange + Act
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["OutboundResilience:LoggingEnabled"] = "true",
        });

        // Assert — provider resolves without throwing
        var pipelineProvider = provider.GetRequiredService<ResiliencePipelineProvider<string>>();
        pipelineProvider.Should().NotBeNull();
    }

    [Fact]
    public void RetryDisabledForDependency_DoesNotRegisterPipeline()
    {
        // Arrange + Act
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["OutboundResilience:Dependencies:Shoptet:RetryEnabled"] = "false",
            ["OutboundResilience:Dependencies:Shoptet:MaxRetryAttempts"] = "3",
        });
        var pipelineProvider = provider.GetRequiredService<ResiliencePipelineProvider<string>>();

        // Assert — unknown key throws
        Action act = () => pipelineProvider.GetPipeline("Shoptet");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void RetryEnabledForDependency_RegistersPipelineKeyedByName()
    {
        // Arrange + Act
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["OutboundResilience:Dependencies:Shoptet:RetryEnabled"] = "true",
            ["OutboundResilience:Dependencies:Shoptet:MaxRetryAttempts"] = "2",
            ["OutboundResilience:Dependencies:Shoptet:RetryBaseDelay"] = "00:00:00.100",
            ["OutboundResilience:Dependencies:Shoptet:Timeout"] = "00:00:05",
        });
        var pipelineProvider = provider.GetRequiredService<ResiliencePipelineProvider<string>>();

        // Assert
        var pipeline = pipelineProvider.GetPipeline("Shoptet");
        pipeline.Should().NotBeNull();
    }
}
