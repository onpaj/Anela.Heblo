using Anela.Heblo.Xcc.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class OutboundResilienceOptionsTests
{
    [Fact]
    public void Defaults_AreSafe_WhenSectionIsAbsent()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.Configure<OutboundResilienceOptions>(configuration.GetSection(OutboundResilienceOptions.SectionName));

        // Act
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboundResilienceOptions>>().Value;

        // Assert
        options.LoggingEnabled.Should().BeTrue();
        options.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(4));
        options.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void BindsPerDependencySection_FromConfiguration()
    {
        // Arrange
        var data = new Dictionary<string, string?>
        {
            ["OutboundResilience:LoggingEnabled"] = "true",
            ["OutboundResilience:PooledConnectionLifetime"] = "00:02:30",
            ["OutboundResilience:Dependencies:Shoptet:RetryEnabled"] = "true",
            ["OutboundResilience:Dependencies:Shoptet:MaxRetryAttempts"] = "5",
            ["OutboundResilience:Dependencies:Shoptet:RetryBaseDelay"] = "00:00:00.500",
            ["OutboundResilience:Dependencies:Shoptet:Timeout"] = "00:01:00",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var services = new ServiceCollection();
        services.Configure<OutboundResilienceOptions>(configuration.GetSection(OutboundResilienceOptions.SectionName));

        // Act
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<OutboundResilienceOptions>>().Value;

        // Assert
        options.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(2.5));
        options.Dependencies.Should().ContainKey("Shoptet");
        var shoptet = options.Dependencies["Shoptet"];
        shoptet.RetryEnabled.Should().BeTrue();
        shoptet.MaxRetryAttempts.Should().Be(5);
        shoptet.RetryBaseDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        shoptet.Timeout.Should().Be(TimeSpan.FromMinutes(1));
    }
}
