using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.BackgroundRefresh;

public class TierBasedHydrationOrchestratorTests
{
    private readonly Mock<ILogger<TierBasedHydrationOrchestrator>> _loggerMock;
    private readonly BackgroundRefreshTaskRegistry _taskRegistry;

    public TierBasedHydrationOrchestratorTests()
    {
        _loggerMock = new Mock<ILogger<TierBasedHydrationOrchestrator>>();

        var registryLoggerMock = new Mock<ILogger<BackgroundRefreshTaskRegistry>>();
        var configurationMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

        // Setup proper service provider with scope support
        var scopeMock = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        var scopeProviderMock = new Mock<IServiceProvider>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(scopeProviderMock.Object);

        var scopeFactoryMock = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);

        var setupMock = Microsoft.Extensions.Options.Options.Create(new BackgroundRefreshTaskRegistrySetup());

        _taskRegistry = new BackgroundRefreshTaskRegistry(
            registryLoggerMock.Object,
            configurationMock.Object,
            serviceProviderMock.Object,
            setupMock);
    }

    [Fact]
    public async Task ShouldNotExecuteDisabledTasks()
    {
        // Arrange
        var enabledTaskExecuted = false;
        var disabledTaskExecuted = false;

        var enabledConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Enabled",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = true,
            HydrationTier = 1
        };

        var disabledConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = false,
            HydrationTier = 1
        };

        _taskRegistry.RegisterTask("TestTask.Enabled", (_, _) =>
        {
            enabledTaskExecuted = true;
            return Task.CompletedTask;
        }, enabledConfig);

        _taskRegistry.RegisterTask("TestTask.Disabled", (_, _) =>
        {
            disabledTaskExecuted = true;
            return Task.CompletedTask;
        }, disabledConfig);

        var orchestrator = new TierBasedHydrationOrchestrator(_loggerMock.Object, _taskRegistry);
        var cts = new CancellationTokenSource();

        // Act
        var hydrationTask = orchestrator.StartAsync(cts.Token);
        await orchestrator.WaitForHydrationCompletionAsync();
        cts.Cancel();
        await hydrationTask;

        // Assert
        Assert.True(enabledTaskExecuted, "Enabled task should have been executed");
        Assert.False(disabledTaskExecuted, "Disabled task should NOT have been executed");
    }

    [Fact]
    public async Task ShouldOnlyExecuteEnabledTasks()
    {
        // Arrange
        var executedTasks = new List<string>();
        var lockObject = new object();

        var task1Config = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Task1",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = true,
            HydrationTier = 1
        };

        var task2Config = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Task2",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = false,
            HydrationTier = 1
        };

        var task3Config = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Task3",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = true,
            HydrationTier = 2
        };

        _taskRegistry.RegisterTask("TestTask.Task1", (_, _) =>
        {
            lock (lockObject)
            {
                executedTasks.Add("Task1");
            }
            return Task.CompletedTask;
        }, task1Config);

        _taskRegistry.RegisterTask("TestTask.Task2", (_, _) =>
        {
            lock (lockObject)
            {
                executedTasks.Add("Task2");
            }
            return Task.CompletedTask;
        }, task2Config);

        _taskRegistry.RegisterTask("TestTask.Task3", (_, _) =>
        {
            lock (lockObject)
            {
                executedTasks.Add("Task3");
            }
            return Task.CompletedTask;
        }, task3Config);

        var orchestrator = new TierBasedHydrationOrchestrator(_loggerMock.Object, _taskRegistry);
        var cts = new CancellationTokenSource();

        // Act
        var hydrationTask = orchestrator.StartAsync(cts.Token);
        await orchestrator.WaitForHydrationCompletionAsync();
        cts.Cancel();
        await hydrationTask;

        // Assert
        Assert.Equal(2, executedTasks.Count);
        Assert.Contains("Task1", executedTasks);
        Assert.Contains("Task3", executedTasks);
        Assert.DoesNotContain("Task2", executedTasks);
    }

    [Fact]
    public async Task ShouldNotExecuteAnyTasksWhenAllDisabled()
    {
        // Arrange
        var taskExecuted = false;

        var disabledConfig1 = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled1",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = false,
            HydrationTier = 1
        };

        var disabledConfig2 = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled2",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = false,
            HydrationTier = 2
        };

        _taskRegistry.RegisterTask("TestTask.Disabled1", (_, _) =>
        {
            taskExecuted = true;
            return Task.CompletedTask;
        }, disabledConfig1);

        _taskRegistry.RegisterTask("TestTask.Disabled2", (_, _) =>
        {
            taskExecuted = true;
            return Task.CompletedTask;
        }, disabledConfig2);

        var orchestrator = new TierBasedHydrationOrchestrator(_loggerMock.Object, _taskRegistry);
        var cts = new CancellationTokenSource();

        // Act
        var hydrationTask = orchestrator.StartAsync(cts.Token);
        await orchestrator.WaitForHydrationCompletionAsync();
        cts.Cancel();
        await hydrationTask;

        // Assert
        Assert.False(taskExecuted, "No tasks should have been executed when all are disabled");
    }

    [Fact]
    public async Task ShouldExecuteEnabledTasksInCorrectTierOrder()
    {
        // Arrange
        var executionOrder = new List<(int Tier, string TaskId)>();
        var lockObject = new object();

        var tier1Task = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Tier1",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = true,
            HydrationTier = 1
        };

        var tier2Task = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Tier2",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = true,
            HydrationTier = 2
        };

        var tier2DisabledTask = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Tier2Disabled",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = false,
            HydrationTier = 2
        };

        var tier3Task = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Tier3",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMinutes(1),
            Enabled = true,
            HydrationTier = 3
        };

        _taskRegistry.RegisterTask("TestTask.Tier1", async (_, _) =>
        {
            await Task.Delay(50); // Simulate work
            lock (lockObject)
            {
                executionOrder.Add((1, "Tier1"));
            }
        }, tier1Task);

        _taskRegistry.RegisterTask("TestTask.Tier2", async (_, _) =>
        {
            await Task.Delay(50); // Simulate work
            lock (lockObject)
            {
                executionOrder.Add((2, "Tier2"));
            }
        }, tier2Task);

        _taskRegistry.RegisterTask("TestTask.Tier2Disabled", (_, _) =>
        {
            lock (lockObject)
            {
                executionOrder.Add((2, "Tier2Disabled"));
            }
            return Task.CompletedTask;
        }, tier2DisabledTask);

        _taskRegistry.RegisterTask("TestTask.Tier3", async (_, _) =>
        {
            await Task.Delay(50); // Simulate work
            lock (lockObject)
            {
                executionOrder.Add((3, "Tier3"));
            }
        }, tier3Task);

        var orchestrator = new TierBasedHydrationOrchestrator(_loggerMock.Object, _taskRegistry);
        var cts = new CancellationTokenSource();

        // Act
        var hydrationTask = orchestrator.StartAsync(cts.Token);
        await orchestrator.WaitForHydrationCompletionAsync();
        cts.Cancel();
        await hydrationTask;

        // Assert
        Assert.Equal(3, executionOrder.Count);
        Assert.DoesNotContain(executionOrder, item => item.TaskId == "Tier2Disabled");

        // Verify tier ordering (Tier 1 should complete before Tier 2, Tier 2 before Tier 3)
        var tier1Index = executionOrder.FindIndex(item => item.TaskId == "Tier1");
        var tier2Index = executionOrder.FindIndex(item => item.TaskId == "Tier2");
        var tier3Index = executionOrder.FindIndex(item => item.TaskId == "Tier3");

        Assert.True(tier1Index < tier2Index, "Tier 1 should execute before Tier 2");
        Assert.True(tier2Index < tier3Index, "Tier 2 should execute before Tier 3");
    }
}
