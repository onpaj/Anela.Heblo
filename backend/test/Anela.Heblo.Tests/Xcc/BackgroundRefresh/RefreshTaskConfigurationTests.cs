using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.BackgroundRefresh;

public class RefreshTaskConfigurationTests
{
    [Fact]
    public void FromAppSettings_ShouldCorrectlyParseEnabledFalse()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["BackgroundRefresh:TestRepository:TestMethod:InitialDelay"] = "00:00:00",
            ["BackgroundRefresh:TestRepository:TestMethod:RefreshInterval"] = "00:05:00",
            ["BackgroundRefresh:TestRepository:TestMethod:Enabled"] = "false",
            ["BackgroundRefresh:TestRepository:TestMethod:HydrationTier"] = "1"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "TestRepository.TestMethod";

        // Act
        var config = RefreshTaskConfiguration.FromAppSettings(configuration, taskId);

        // Assert
        Assert.Equal(taskId, config.TaskId);
        Assert.False(config.Enabled);
        Assert.Equal(TimeSpan.Zero, config.InitialDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), config.RefreshInterval);
        Assert.Equal(1, config.HydrationTier);
    }

    [Fact]
    public void FromAppSettings_ShouldCorrectlyParseEnabledTrue()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["BackgroundRefresh:TestRepository:TestMethod:InitialDelay"] = "00:00:00",
            ["BackgroundRefresh:TestRepository:TestMethod:RefreshInterval"] = "00:05:00",
            ["BackgroundRefresh:TestRepository:TestMethod:Enabled"] = "true",
            ["BackgroundRefresh:TestRepository:TestMethod:HydrationTier"] = "1"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "TestRepository.TestMethod";

        // Act
        var config = RefreshTaskConfiguration.FromAppSettings(configuration, taskId);

        // Assert
        Assert.Equal(taskId, config.TaskId);
        Assert.True(config.Enabled);
    }

    [Fact]
    public void FromAppSettings_ShouldDefaultToEnabledTrueWhenNotSpecified()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["BackgroundRefresh:TestRepository:TestMethod:InitialDelay"] = "00:00:00",
            ["BackgroundRefresh:TestRepository:TestMethod:RefreshInterval"] = "00:05:00",
            ["BackgroundRefresh:TestRepository:TestMethod:HydrationTier"] = "1"
            // Note: Enabled is NOT specified
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "TestRepository.TestMethod";

        // Act
        var config = RefreshTaskConfiguration.FromAppSettings(configuration, taskId);

        // Assert
        Assert.Equal(taskId, config.TaskId);
        Assert.True(config.Enabled, "Should default to Enabled=true when not specified");
    }

    [Fact]
    public void FromAppSettings_ShouldDefaultToEnabledTrueWhenInvalid()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["BackgroundRefresh:TestRepository:TestMethod:InitialDelay"] = "00:00:00",
            ["BackgroundRefresh:TestRepository:TestMethod:RefreshInterval"] = "00:05:00",
            ["BackgroundRefresh:TestRepository:TestMethod:Enabled"] = "invalid-value",
            ["BackgroundRefresh:TestRepository:TestMethod:HydrationTier"] = "1"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "TestRepository.TestMethod";

        // Act
        var config = RefreshTaskConfiguration.FromAppSettings(configuration, taskId);

        // Assert
        Assert.Equal(taskId, config.TaskId);
        Assert.True(config.Enabled, "Should default to Enabled=true when parsing fails");
    }

    [Fact]
    public void FromAppSettings_ShouldHandleCaseInsensitiveEnabledValues()
    {
        // Arrange - Test various case combinations
        var testCases = new[]
        {
            ("False", false),
            ("FALSE", false),
            ("false", false),
            ("True", true),
            ("TRUE", true),
            ("true", true)
        };

        foreach (var (enabledValue, expectedResult) in testCases)
        {
            var configData = new Dictionary<string, string>
            {
                ["BackgroundRefresh:TestRepository:TestMethod:InitialDelay"] = "00:00:00",
                ["BackgroundRefresh:TestRepository:TestMethod:RefreshInterval"] = "00:05:00",
                ["BackgroundRefresh:TestRepository:TestMethod:Enabled"] = enabledValue,
                ["BackgroundRefresh:TestRepository:TestMethod:HydrationTier"] = "1"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData!)
                .Build();

            var taskId = "TestRepository.TestMethod";

            // Act
            var config = RefreshTaskConfiguration.FromAppSettings(configuration, taskId);

            // Assert
            Assert.Equal(expectedResult, config.Enabled);
        }
    }

    [Fact]
    public void FromAppSettings_ShouldThrowWhenConfigurationSectionNotFound()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            // Empty configuration
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "NonExistent.Method";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RefreshTaskConfiguration.FromAppSettings(configuration, taskId));

        Assert.Contains("Configuration section", exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public void FromAppSettings_ShouldThrowWhenRefreshIntervalInvalid()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["BackgroundRefresh:TestRepository:TestMethod:InitialDelay"] = "00:00:00",
            ["BackgroundRefresh:TestRepository:TestMethod:RefreshInterval"] = "invalid",
            ["BackgroundRefresh:TestRepository:TestMethod:Enabled"] = "true",
            ["BackgroundRefresh:TestRepository:TestMethod:HydrationTier"] = "1"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "TestRepository.TestMethod";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RefreshTaskConfiguration.FromAppSettings(configuration, taskId));

        Assert.Contains("Invalid RefreshInterval", exception.Message);
    }

    [Fact]
    public void FromAppSettings_ShouldParseComplexConfiguration()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["BackgroundRefresh:ICatalogRepository:RefreshTransportData:InitialDelay"] = "00:02:30",
            ["BackgroundRefresh:ICatalogRepository:RefreshTransportData:RefreshInterval"] = "00:15:00",
            ["BackgroundRefresh:ICatalogRepository:RefreshTransportData:Enabled"] = "false",
            ["BackgroundRefresh:ICatalogRepository:RefreshTransportData:HydrationTier"] = "3"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var taskId = "ICatalogRepository.RefreshTransportData";

        // Act
        var config = RefreshTaskConfiguration.FromAppSettings(configuration, taskId);

        // Assert
        Assert.Equal(taskId, config.TaskId);
        Assert.False(config.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)), config.InitialDelay);
        Assert.Equal(TimeSpan.FromMinutes(15), config.RefreshInterval);
        Assert.Equal(3, config.HydrationTier);
    }
}
