using Anela.Heblo.Domain.Features.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Tests.Features.FileStorage.Configuration;

public sealed class ProductExportOptionsTests
{
    [Fact]
    public void Defaults_HeadTimeout_Is10Seconds()
    {
        // Arrange / Act
        var options = new ProductExportOptions();

        // Assert
        options.HeadTimeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Defaults_DownloadTimeout_Is120Seconds()
    {
        // Arrange / Act
        var options = new ProductExportOptions();

        // Assert
        options.DownloadTimeout.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Defaults_MaxRetryAttempts_Is3()
    {
        // Arrange / Act
        var options = new ProductExportOptions();

        // Assert
        options.MaxRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void Defaults_RetryBaseDelay_Is2Seconds()
    {
        // Arrange / Act
        var options = new ProductExportOptions();

        // Assert
        options.RetryBaseDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Configuration_BindsTimeSpanFromString()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "HeadTimeout", "00:00:30" },
            })
            .Build();

        // Act
        var options = configuration.Get<ProductExportOptions>()!;

        // Assert
        options.HeadTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Configuration_BindsMaxRetryAttempts_FromInteger()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "MaxRetryAttempts", "5" },
            })
            .Build();

        // Act
        var options = configuration.Get<ProductExportOptions>()!;

        // Assert
        options.MaxRetryAttempts.Should().Be(5);
    }
}
