using Anela.Heblo.Domain.Features.Configuration;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Configuration;

public class ApplicationConfigurationTests
{
    [Fact]
    public void CreateWithDefaults_WithNullVersionAndEnvironment_FallsBackToConfigurationConstantsDefaults()
    {
        // Act
        var config = ApplicationConfiguration.CreateWithDefaults(null, null, false);

        // Assert
        config.Version.Should().Be(ConfigurationConstants.DEFAULT_VERSION);
        config.Environment.Should().Be(ConfigurationConstants.DEFAULT_ENVIRONMENT);
    }

    [Fact]
    public void CreateWithDefaults_WithProvidedVersionAndEnvironment_PassesThroughUnchanged()
    {
        // Act
        var config = ApplicationConfiguration.CreateWithDefaults("9.9.9", "Staging", true);

        // Assert
        config.Version.Should().Be("9.9.9");
        config.Environment.Should().Be("Staging");
        config.UseMockAuth.Should().BeTrue();
    }
}
