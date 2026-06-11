using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Domain.Features.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Anela.Heblo.Tests.Features.Configuration;

public class GetConfigurationHandlerTests
{
    private static GetConfigurationHandler CreateHandler(Dictionary<string, string?> configData)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");

        return new GetConfigurationHandler(configuration, environment, NullLogger<GetConfigurationHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet()
    {
        // Arrange
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = "2.5.1-ci.42"
        });

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.Version.Should().Be("2.5.1-ci.42");
    }

    [Fact]
    public async Task Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty()
    {
        // Arrange
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = ""
        });

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        // When APP_VERSION is empty, should fall back to assembly version (non-null, not the hardcoded "1.0.0" default)
        response.Version.Should().NotBeNullOrEmpty().And.NotBe(ConfigurationConstants.DEFAULT_VERSION);
    }

    [Fact]
    public async Task Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent()
    {
        // Arrange
        var handler = CreateHandler(new Dictionary<string, string?>());

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        // When APP_VERSION is absent, should fall back to assembly informational version or assembly version
        response.Version.Should().NotBeNullOrEmpty().And.NotBe(ConfigurationConstants.DEFAULT_VERSION);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet()
    {
        // Arrange — regression guard: surgical change must not break UseMockAuth wiring
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = "1.2.3",
            [ConfigurationConstants.USE_MOCK_AUTH] = "true"
        });

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.Version.Should().Be("1.2.3");
        response.UseMockAuth.Should().BeTrue();
    }
}
