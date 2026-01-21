using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.BackgroundRefresh;

public class BackgroundRefreshSchedulerServiceTests
{
    private readonly Mock<ILogger<BackgroundRefreshSchedulerService>> _loggerMock;
    private readonly Mock<ILogger<TierBasedHydrationOrchestrator>> _orchestratorLoggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly BackgroundRefreshTaskRegistry _taskRegistry;
    private readonly TierBasedHydrationOrchestrator _orchestrator;

    public BackgroundRefreshSchedulerServiceTests()
    {
        _loggerMock = new Mock<ILogger<BackgroundRefreshSchedulerService>>();
        _orchestratorLoggerMock = new Mock<ILogger<TierBasedHydrationOrchestrator>>();

        var registryLoggerMock = new Mock<ILogger<BackgroundRefreshTaskRegistry>>();
        var configurationMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

        // Setup proper service provider with scope support for task registry
        var registryScopeMock = new Mock<IServiceScope>();
        var registryScopeProviderMock = new Mock<IServiceProvider>();
        registryScopeMock.Setup(s => s.ServiceProvider).Returns(registryScopeProviderMock.Object);

        var registryScopeFactoryMock = new Mock<IServiceScopeFactory>();
        registryScopeFactoryMock.Setup(f => f.CreateScope()).Returns(registryScopeMock.Object);

        var registryServiceProviderMock = new Mock<IServiceProvider>();
        registryServiceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(registryScopeFactoryMock.Object);

        var setupMock = Microsoft.Extensions.Options.Options.Create(new BackgroundRefreshTaskRegistrySetup());

        _taskRegistry = new BackgroundRefreshTaskRegistry(
            registryLoggerMock.Object,
            configurationMock.Object,
            registryServiceProviderMock.Object,
            setupMock);

        _orchestrator = new TierBasedHydrationOrchestrator(_orchestratorLoggerMock.Object, _taskRegistry);

        // Setup service provider mocks for scheduler service
        _serviceScopeMock = new Mock<IServiceScope>();
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var mockScopeServiceProvider = new Mock<IServiceProvider>();
        mockScopeServiceProvider
            .Setup(sp => sp.GetService(typeof(IBackgroundRefreshTaskRegistry)))
            .Returns(_taskRegistry);

        _serviceScopeMock.Setup(s => s.ServiceProvider).Returns(mockScopeServiceProvider.Object);
        _serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(_serviceScopeMock.Object);

        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_serviceScopeFactoryMock.Object);
    }

    [Fact]
    public async Task ShouldNotStartLoopForDisabledTasks()
    {
        // Arrange
        var enabledTaskLoopStarted = false;
        var disabledTaskLoopStarted = false;
        var enabledTaskExecutionCount = 0;
        var disabledTaskExecutionCount = 0;

        var enabledConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Enabled",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = true,
            HydrationTier = 1
        };

        var disabledConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = false,
            HydrationTier = 1
        };

        _taskRegistry.RegisterTask("TestTask.Enabled", (_, _) =>
        {
            enabledTaskLoopStarted = true;
            Interlocked.Increment(ref enabledTaskExecutionCount);
            return Task.CompletedTask;
        }, enabledConfig);

        _taskRegistry.RegisterTask("TestTask.Disabled", (_, _) =>
        {
            disabledTaskLoopStarted = true;
            Interlocked.Increment(ref disabledTaskExecutionCount);
            return Task.CompletedTask;
        }, disabledConfig);

        var scheduler = new BackgroundRefreshSchedulerService(
            _loggerMock.Object,
            _serviceProviderMock.Object,
            _orchestrator);

        var cts = new CancellationTokenSource();

        // Act
        var orchestratorTask = _orchestrator.StartAsync(cts.Token);
        await _orchestrator.WaitForHydrationCompletionAsync();

        var schedulerTask = scheduler.StartAsync(cts.Token);

        // Wait for scheduler to start and execute at least one loop
        await Task.Delay(300);

        cts.Cancel();
        await Task.WhenAll(orchestratorTask, schedulerTask);

        // Assert
        Assert.True(enabledTaskLoopStarted, "Enabled task loop should have started");
        Assert.False(disabledTaskLoopStarted, "Disabled task loop should NOT have started");

        // Enabled task should execute during hydration + at least once in periodic loop
        Assert.True(enabledTaskExecutionCount >= 2, $"Enabled task should have executed at least twice (hydration + periodic), but executed {enabledTaskExecutionCount} times");
        Assert.Equal(0, disabledTaskExecutionCount);
    }

    [Fact]
    public async Task ShouldOnlyStartLoopsForEnabledTasks()
    {
        // Arrange
        var task1ExecutionCount = 0;
        var task2ExecutionCount = 0;
        var task3ExecutionCount = 0;

        var task1Config = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Task1",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = true,
            HydrationTier = 1
        };

        var task2Config = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Task2",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = false,
            HydrationTier = 1
        };

        var task3Config = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Task3",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = true,
            HydrationTier = 1
        };

        _taskRegistry.RegisterTask("TestTask.Task1", (_, _) =>
        {
            Interlocked.Increment(ref task1ExecutionCount);
            return Task.CompletedTask;
        }, task1Config);

        _taskRegistry.RegisterTask("TestTask.Task2", (_, _) =>
        {
            Interlocked.Increment(ref task2ExecutionCount);
            return Task.CompletedTask;
        }, task2Config);

        _taskRegistry.RegisterTask("TestTask.Task3", (_, _) =>
        {
            Interlocked.Increment(ref task3ExecutionCount);
            return Task.CompletedTask;
        }, task3Config);

        var scheduler = new BackgroundRefreshSchedulerService(
            _loggerMock.Object,
            _serviceProviderMock.Object,
            _orchestrator);

        var cts = new CancellationTokenSource();

        // Act
        var orchestratorTask = _orchestrator.StartAsync(cts.Token);
        await _orchestrator.WaitForHydrationCompletionAsync();

        var schedulerTask = scheduler.StartAsync(cts.Token);

        // Wait for scheduler to execute periodic loops
        await Task.Delay(300);

        cts.Cancel();
        await Task.WhenAll(orchestratorTask, schedulerTask);

        // Assert
        Assert.True(task1ExecutionCount >= 2, $"Task1 should have executed at least twice, but executed {task1ExecutionCount} times");
        Assert.True(task3ExecutionCount >= 2, $"Task3 should have executed at least twice, but executed {task3ExecutionCount} times");
        Assert.Equal(0, task2ExecutionCount);
    }

    [Fact]
    public async Task ShouldNotStartAnyLoopWhenAllTasksDisabled()
    {
        // Arrange
        var anyTaskExecuted = false;

        var disabledConfig1 = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled1",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = false,
            HydrationTier = 1
        };

        var disabledConfig2 = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled2",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = false,
            HydrationTier = 1
        };

        _taskRegistry.RegisterTask("TestTask.Disabled1", (_, _) =>
        {
            anyTaskExecuted = true;
            return Task.CompletedTask;
        }, disabledConfig1);

        _taskRegistry.RegisterTask("TestTask.Disabled2", (_, _) =>
        {
            anyTaskExecuted = true;
            return Task.CompletedTask;
        }, disabledConfig2);

        var scheduler = new BackgroundRefreshSchedulerService(
            _loggerMock.Object,
            _serviceProviderMock.Object,
            _orchestrator);

        var cts = new CancellationTokenSource();

        // Act
        var orchestratorTask = _orchestrator.StartAsync(cts.Token);
        await _orchestrator.WaitForHydrationCompletionAsync();

        var schedulerTask = scheduler.StartAsync(cts.Token);

        // Wait to ensure no loops start
        await Task.Delay(300);

        cts.Cancel();
        await Task.WhenAll(orchestratorTask, schedulerTask);

        // Assert
        Assert.False(anyTaskExecuted, "No tasks should have executed when all are disabled");
    }

    [Fact]
    public async Task ShouldLogSkippedDisabledTasks()
    {
        // Arrange
        var disabledConfig = new RefreshTaskConfiguration
        {
            TaskId = "TestTask.Disabled",
            InitialDelay = TimeSpan.Zero,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Enabled = false,
            HydrationTier = 1
        };

        _taskRegistry.RegisterTask("TestTask.Disabled", (_, _) => Task.CompletedTask, disabledConfig);

        var scheduler = new BackgroundRefreshSchedulerService(
            _loggerMock.Object,
            _serviceProviderMock.Object,
            _orchestrator);

        var cts = new CancellationTokenSource();

        // Act
        var orchestratorTask = _orchestrator.StartAsync(cts.Token);
        await _orchestrator.WaitForHydrationCompletionAsync();

        var schedulerTask = scheduler.StartAsync(cts.Token);
        await Task.Delay(100);

        cts.Cancel();
        await Task.WhenAll(orchestratorTask, schedulerTask);

        // Assert - verify logging occurred for skipped task
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipping disabled task") && v.ToString()!.Contains("TestTask.Disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
