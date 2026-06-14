using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class ProductionConnectionStringDefaultsTests
{
    [Fact]
    public void Production_DatabaseMaxPoolSize_IsTwenty()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("Database:MaxPoolSize").Should().Be(20,
            "spec FR-2 raises production EF pool from 15 to 20");
    }

    [Fact]
    public void Production_AnalyticsMaxPoolSize_IsCapped()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("AnalyticsDatabase:MaxPoolSize").Should().Be(10,
            "AnalyticsDbContext must not consume the server's connection budget");
    }

    [Fact]
    public void Production_ResilienceOptions_MatchSpec()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("Database:Resilience:MaxRetryAttempts").Should().Be(3);
        config.GetValue<TimeSpan>("Database:Resilience:TotalTimeBudget").Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Production_HangfireConnectionLimit_StaysAtFive()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("Hangfire:ConnectionLimit").Should().Be(5,
            "spec FR-2 keeps Hangfire pool at 5 to preserve total connection budget");
    }

    private static IConfigurationRoot LoadProductionConfig()
    {
        // Navigate from test binary output up to the API project directory.
        // Test bin path: backend/test/Anela.Heblo.Tests/bin/Release/net8.0/
        // API project: backend/src/Anela.Heblo.API/
        var basePath = AppContext.BaseDirectory;
        var apiDir = FindApiDirectory(basePath);

        return new ConfigurationBuilder()
            .SetBasePath(apiDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();
    }

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
