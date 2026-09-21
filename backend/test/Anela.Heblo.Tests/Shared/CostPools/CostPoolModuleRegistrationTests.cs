using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolModuleRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddOptions<DataSourceOptions>();
        services.AddSingleton(new Mock<ILedgerService>().Object);

        services.AddSharedCostPoolsModule();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSharedCostPoolsModule_ResolvesCostPoolService()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var service = provider.GetService<ICostPoolService>();

        // Assert
        service.Should().BeOfType<CostPoolService>();
    }

    [Fact]
    public void AddSharedCostPoolsModule_ResolvesCostPoolCache()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var cache = provider.GetService<ICostPoolCache>();

        // Assert
        cache.Should().BeOfType<CostPoolCache>();
    }

    /// <summary>
    /// RegisterRefreshTask derives the task id from the interface name, and
    /// RefreshTaskConfiguration.FromAppSettings throws when no matching
    /// BackgroundRefresh section exists. Nothing in the test host builds the
    /// refresh registry (appsettings.Test.json disables hydration), so without
    /// this test a rename or a typo in either half would surface first as a
    /// staging startup crash.
    /// </summary>
    [Fact]
    public void RefreshCacheTask_HasAMatchingBackgroundRefreshSection_InAppSettings()
    {
        // Arrange
        var configuration = LoadApiAppSettings();

        // Act
        var config = RefreshTaskConfiguration.FromAppSettings(
            configuration, $"{nameof(ICostPoolService)}.RefreshCache");

        // Assert
        config.Enabled.Should().BeTrue();
        config.RefreshInterval.Should().Be(TimeSpan.FromHours(4));
        config.InitialDelay.Should().Be(TimeSpan.FromMinutes(5));
        config.HydrationTier.Should().Be(3);
    }

    private static IConfigurationRoot LoadApiAppSettings() =>
        new ConfigurationBuilder()
            .SetBasePath(FindApiDirectory(AppContext.BaseDirectory))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

    private static string FindApiDirectory(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src", "Anela.Heblo.API");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate backend/src/Anela.Heblo.API from {startPath}");
    }
}
