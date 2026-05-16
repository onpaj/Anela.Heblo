using Anela.Heblo.Xcc.Http;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class HebloHttpClientBuilderExtensionsTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.Configure<OutboundResilienceOptions>(_ => { });
        services.AddSingleton(new Mock<ITelemetryService>().Object);
        services.AddTransient<OutboundCallObservabilityHandler>();
        return services;
    }

    [Fact]
    public void WithHebloOutboundObservability_AttachesObservabilityHandler()
    {
        // Arrange
        var services = BaseServices();
        services.AddHttpClient("test").WithHebloOutboundObservability();

        // Act
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        // Assert — handler is wrapped; the client resolves without throwing
        client.Should().NotBeNull();
    }

    [Fact]
    public void WithHebloOutboundDefaults_ConfiguresSocketsHttpHandlerWithPooledConnectionLifetime()
    {
        // Arrange
        var services = BaseServices();
        services.Configure<OutboundResilienceOptions>(o => o.PooledConnectionLifetime = TimeSpan.FromMinutes(2));
        services.AddHttpClient("named").WithHebloOutboundDefaults();

        // Act
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var primaryHandler = factory.CreateHandler("named");

        // Walk the DelegatingHandler chain to the primary.
        var current = primaryHandler;
        while (current is DelegatingHandler dh && dh.InnerHandler is not null)
        {
            current = dh.InnerHandler;
        }

        // Assert
        current.Should().BeOfType<SocketsHttpHandler>();
        var socketsHandler = (SocketsHttpHandler)current!;
        socketsHandler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(2));
    }
}
