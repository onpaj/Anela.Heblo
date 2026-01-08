using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Common;

public class BackgroundServicesConfigurationTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;

    public BackgroundServicesConfigurationTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void BackgroundServicesOptions_ShouldHaveHydrationDisabledInTestEnvironment()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<BackgroundServicesOptions>>();

        // Act
        var enableHydrationFromConfig = configuration.GetValue<bool>("BackgroundServices:EnableHydration", true);
        var enableHydrationFromOptions = options.Value.EnableHydration;

        // Assert
        Assert.False(enableHydrationFromConfig);
        Assert.False(enableHydrationFromOptions);
    }

    [Fact]
    public void HydrationOrchestratorWrapper_ShouldNotBeRegisteredInTestEnvironment()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var hostedServices = scope.ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        // Act
        var hydrationServiceExists = hostedServices.Any(s => s.GetType().Name == nameof(HydrationOrchestratorWrapper));

        // Assert
        Assert.False(hydrationServiceExists, "HydrationOrchestratorWrapper should not be registered in Test environment");
    }
}