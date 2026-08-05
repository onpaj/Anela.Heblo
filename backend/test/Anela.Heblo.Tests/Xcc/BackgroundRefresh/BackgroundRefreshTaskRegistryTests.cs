using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.BackgroundRefresh;

public class BackgroundRefreshTaskRegistryTests
{
    private static BackgroundRefreshTaskRegistry CreateRegistry(
        IConfiguration configuration,
        BackgroundRefreshTaskRegistrySetup setup)
    {
        var loggerMock = new Mock<ILogger<BackgroundRefreshTaskRegistry>>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        return new BackgroundRefreshTaskRegistry(
            loggerMock.Object,
            configuration,
            serviceProviderMock.Object,
            Options.Create(setup));
    }

    [Fact]
    public void ExplicitConfiguration_IsHonoured_NotOverwrittenByAppSettings()
    {
        // Arrange
        var explicitConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestOwner.TestMethod",
            InitialDelay = TimeSpan.FromSeconds(1),
            RefreshInterval = TimeSpan.FromMinutes(30),
            Enabled = false,
            HydrationTier = 3
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundRefresh:TestOwner:TestMethod:InitialDelay"] = "00:00:05",
                ["BackgroundRefresh:TestOwner:TestMethod:RefreshInterval"] = "00:10:00",
                ["BackgroundRefresh:TestOwner:TestMethod:Enabled"] = "true",
                ["BackgroundRefresh:TestOwner:TestMethod:HydrationTier"] = "2"
            })
            .Build();

        var setup = new BackgroundRefreshTaskRegistrySetup();
        setup.TaskRegistrations.Add(new TaskRegistrationInfo
        {
            TaskId = "TestOwner.TestMethod",
            RefreshMethod = (_, _) => Task.CompletedTask,
            Configuration = explicitConfig
        });

        // Act
        var registry = CreateRegistry(configuration, setup);

        // Assert
        var registered = Assert.Single(registry.GetRegisteredTasks());
        Assert.Equal(explicitConfig.InitialDelay, registered.InitialDelay);
        Assert.Equal(explicitConfig.RefreshInterval, registered.RefreshInterval);
        Assert.Equal(explicitConfig.Enabled, registered.Enabled);
        Assert.Equal(explicitConfig.HydrationTier, registered.HydrationTier);
    }

    [Fact]
    public void NoConfiguration_FallsBackToAppSettings_Unchanged()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundRefresh:TestOwner:TestMethod:InitialDelay"] = "00:00:05",
                ["BackgroundRefresh:TestOwner:TestMethod:RefreshInterval"] = "00:10:00",
                ["BackgroundRefresh:TestOwner:TestMethod:Enabled"] = "true",
                ["BackgroundRefresh:TestOwner:TestMethod:HydrationTier"] = "2"
            })
            .Build();

        var setup = new BackgroundRefreshTaskRegistrySetup();
        setup.TaskRegistrations.Add(new TaskRegistrationInfo
        {
            TaskId = "TestOwner.TestMethod",
            RefreshMethod = (_, _) => Task.CompletedTask,
            Configuration = null
        });

        var expected = RefreshTaskConfiguration.FromAppSettings(configuration, "TestOwner.TestMethod");

        // Act
        var registry = CreateRegistry(configuration, setup);

        // Assert
        var registered = Assert.Single(registry.GetRegisteredTasks());
        Assert.Equal(expected.InitialDelay, registered.InitialDelay);
        Assert.Equal(expected.RefreshInterval, registered.RefreshInterval);
        Assert.Equal(expected.Enabled, registered.Enabled);
        Assert.Equal(expected.HydrationTier, registered.HydrationTier);
    }

    [Fact]
    public void ExplicitConfiguration_NoAppSettingsSection_DoesNotThrow()
    {
        // Arrange
        var explicitConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestOwner.TestMethod",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(15),
            Enabled = true,
            HydrationTier = 1
        };

        var configuration = new ConfigurationBuilder().Build();

        var setup = new BackgroundRefreshTaskRegistrySetup();
        setup.TaskRegistrations.Add(new TaskRegistrationInfo
        {
            TaskId = "TestOwner.TestMethod",
            RefreshMethod = (_, _) => Task.CompletedTask,
            Configuration = explicitConfig
        });

        // Act
        var registry = CreateRegistry(configuration, setup);

        // Assert
        var registered = Assert.Single(registry.GetRegisteredTasks());
        Assert.Equal(explicitConfig.RefreshInterval, registered.RefreshInterval);
    }
}
