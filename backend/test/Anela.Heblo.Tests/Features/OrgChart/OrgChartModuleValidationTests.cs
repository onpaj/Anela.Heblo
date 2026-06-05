using System.Collections.Generic;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.OrgChart;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public sealed class OrgChartModuleValidationTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IHost BuildHost(Dictionary<string, string?> configValues)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddInMemoryCollection(configValues);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOrgChartServices(context.Configuration);
            })
            .Build();
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_throws_when_OrgChart_DataSourceUrl_is_missing()
    {
        // Arrange
        using var host = BuildHost(new Dictionary<string, string?>());

        // Act
        var act = async () => await host.StartAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Message.Should().Contain(nameof(OrgChartOptions.DataSourceUrl));
    }

    [Fact]
    public async Task StartAsync_throws_when_OrgChart_DataSourceUrl_is_empty_string()
    {
        // Arrange
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["OrgChart:DataSourceUrl"] = string.Empty,
        });

        // Act
        var act = async () => await host.StartAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Message.Should().Contain(nameof(OrgChartOptions.DataSourceUrl));
    }

    [Fact]
    public async Task StartAsync_succeeds_when_OrgChart_DataSourceUrl_is_configured()
    {
        // Arrange
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["OrgChart:DataSourceUrl"] = "https://example.test/organization-structure.json",
        });

        // Act
        await host.StartAsync();

        // Assert
        // If StartAsync didn't throw, validation passed. Stop the host cleanly.
        await host.StopAsync();
    }
}
