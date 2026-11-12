using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenFeature.Constant;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

/// <summary>
/// Covers the three-layer flag resolution (DB override → appsettings → default) and the
/// fail-open behavior the delivered-order dry-run gate depends on: any resolution failure
/// must yield the supplied default (false), never an exception.
/// </summary>
public class HebloFeatureProviderTests
{
    private const string FlagKey = FeatureFlagKeys.DeliveredOrderCompletion;

    private static HebloFeatureProvider MakeProvider(
        Dictionary<string, bool>? dbOverrides = null,
        Dictionary<string, string?>? configValues = null,
        bool repoThrows = false,
        Exception? thrownException = null,
        ILogger<HebloFeatureProvider>? logger = null)
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        if (repoThrows || thrownException is not null)
        {
            repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(thrownException ?? new InvalidOperationException("DB unavailable"));
        }
        else
        {
            repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(dbOverrides ?? []);
        }

        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? [])
            .Build();

        return new HebloFeatureProvider(
            scopeFactory,
            new MemoryCache(new MemoryCacheOptions()),
            configuration,
            logger ?? NullLogger<HebloFeatureProvider>.Instance);
    }

    private static HebloFeatureProvider MakeProviderWithRepo(
        Mock<IFeatureFlagOverrideRepository> repo,
        IMemoryCache? cache = null,
        ILogger<HebloFeatureProvider>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new HebloFeatureProvider(
            scopeFactory,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
            logger ?? NullLogger<HebloFeatureProvider>.Instance);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_DbOverrideWins_OverConfigAndDefault()
    {
        var provider = MakeProvider(
            dbOverrides: new Dictionary<string, bool> { [FlagKey] = true },
            configValues: new Dictionary<string, string?> { [$"FeatureManagement:{FlagKey}"] = "false" });

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_FallsBackToConfig_WhenNoDbOverride()
    {
        var provider = MakeProvider(
            configValues: new Dictionary<string, string?> { [$"FeatureManagement:{FlagKey}"] = "true" });

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(Reason.Static);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_FallsBackToDefault_WhenNoOverrideOrConfig()
    {
        var provider = MakeProvider();

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.Default);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_FailsOpenToDefault_WhenDbThrows()
    {
        var provider = MakeProvider(repoThrows: true);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.Error);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_DbOverrideCanDisable_WhenConfigEnables()
    {
        var provider = MakeProvider(
            dbOverrides: new Dictionary<string, bool> { [FlagKey] = false },
            configValues: new Dictionary<string, string?> { [$"FeatureManagement:{FlagKey}"] = "true" });

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: true);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_LogsDbRetryExhausted_WhenExceptionCarriesMultipleAttempts()
    {
        // Simulates what PollyExecutionStrategy tags on an exception that survived retries
        // (ex.Data["Anela.DbRetryAttempts"] = attempt) before rethrowing it up to this catch block.
        var thrown = new InvalidOperationException("DB unavailable after retries");
        thrown.Data["Anela.DbRetryAttempts"] = 3;
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();

        var provider = MakeProvider(thrownException: thrown, logger: loggerMock.Object);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.Error);
        VerifyWarningLogged(loggerMock, thrown, "dbRetryExhausted=True", "attempts=3");
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_LogsDbRetryNotExhausted_WhenExceptionHasNoAttemptTag()
    {
        // No "Anela.DbRetryAttempts" tag set — mirrors an immediate non-transient failure (attempts=1
        // implied), distinguishing it from a retry-exhausted failure in logs/dashboards.
        var thrown = new InvalidOperationException("immediate non-transient failure");
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();

        var provider = MakeProvider(thrownException: thrown, logger: loggerMock.Object);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.Error);
        VerifyWarningLogged(loggerMock, thrown, "dbRetryExhausted=False", "attempts=1");
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_ConcurrentCallersOnColdCache_QueryDbOnce()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => pending.Task);
        var provider = MakeProviderWithRepo(repo);

        var calls = Enumerable.Range(0, 20)
            .Select(_ => provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false))
            .ToArray();
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(r => r.Value && r.Reason == Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_ConcurrentCallersAfterCacheExpiry_QueryDbOnceMore()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => pending.Task);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = MakeProviderWithRepo(repo, cache);

        var first = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });
        await first;

        // Simulates TTL expiry (the entry disappears from the cache).
        cache.Remove(HebloFeatureProvider.CacheKey);
        pending = new TaskCompletionSource<Dictionary<string, bool>>();

        var calls = Enumerable.Range(0, 20)
            .Select(_ => provider.ResolveBooleanValueAsync(FlagKey, defaultValue: true))
            .ToArray();
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = false });
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(r => !r.Value && r.Reason == Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_CallerCancellation_DoesNotCancelLoadOrOtherCallers()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repoTokens = new List<CancellationToken>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                repoTokens.Add(token);
                return pending.Task;
            });
        var provider = MakeProviderWithRepo(repo);
        using var callerCts = new CancellationTokenSource();

        var cancelledCall = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false, context: null, callerCts.Token);
        var otherCall = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        callerCts.Cancel();

        var cancelledResult = await cancelledCall;
        cancelledResult.Value.Should().BeFalse();
        cancelledResult.Reason.Should().Be(Reason.Error);

        repoTokens.Should().ContainSingle();
        repoTokens[0].Should().NotBe(callerCts.Token);
        repoTokens[0].CanBeCanceled.Should().BeTrue("the load runs on its own timeout token");
        repoTokens[0].IsCancellationRequested.Should().BeFalse();
        otherCall.IsCompleted.Should().BeFalse();

        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });
        var otherResult = await otherCall;
        otherResult.Value.Should().BeTrue();
        otherResult.Reason.Should().Be(Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_LoadStartedByCancelledCaller_StillPopulatesCache()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => pending.Task);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = MakeProviderWithRepo(repo, cache);
        using var callerCts = new CancellationTokenSource();

        var cancelledCall = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false, context: null, callerCts.Token);
        callerCts.Cancel();
        (await cancelledCall).Reason.Should().Be(Reason.Error);

        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });

        // Whether the load's continuation (cache.Set) runs inline inside SetResult depends on the
        // synchronization context, so observe it through a second call: it either hits the cache
        // or joins the still-running load. Either way the repository is queried exactly once.
        var next = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        next.Value.Should().BeTrue();
        next.Reason.Should().Be(Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Once);

        cache.TryGetValue(HebloFeatureProvider.CacheKey, out Dictionary<string, bool>? cached).Should().BeTrue();
        cached.Should().ContainKey(FlagKey).WhoseValue.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_FailedLoadIsNotCached_NextCallReloads()
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.SetupSequence(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"))
            .ReturnsAsync(new Dictionary<string, bool> { [FlagKey] = true });
        var provider = MakeProviderWithRepo(repo);

        var failed = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        var recovered = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        failed.Value.Should().BeFalse();
        failed.Reason.Should().Be(Reason.Error);
        recovered.Value.Should().BeTrue();
        recovered.Reason.Should().Be(Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_CacheHit_DoesNotQueryDbAgain()
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, bool> { [FlagKey] = true });
        var provider = MakeProviderWithRepo(repo);

        await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        var second = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        second.Value.Should().BeTrue();
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_CallerCancelled_LogsDebugNotWarning()
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();
        var provider = MakeProviderWithRepo(repo, logger: loggerMock.Object);
        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false, context: null, callerCts.Token);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.Error);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Never);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cancelledBy=caller")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_RefreshCancelledWithoutCallerCancel_LogsWarningCancelledByRefresh()
    {
        // The load's own timeout (or the DB) cancelled the query; the caller's token is untouched.
        var thrown = new OperationCanceledException("refresh timed out");
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();
        var provider = MakeProvider(thrownException: thrown, logger: loggerMock.Object);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Reason.Should().Be(Reason.Error);
        VerifyWarningLogged(loggerMock, thrown, "cancelledBy=refresh", "dbRetryExhausted=False", "attempts=1");
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_NonCancellationFailure_LogsWarningCancelledByNone()
    {
        var thrown = new InvalidOperationException("DB unavailable");
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();
        var provider = MakeProvider(thrownException: thrown, logger: loggerMock.Object);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Reason.Should().Be(Reason.Error);
        VerifyWarningLogged(loggerMock, thrown, "cancelledBy=none", "dbRetryExhausted=False", "attempts=1");
    }

    private static void VerifyWarningLogged(Mock<ILogger<HebloFeatureProvider>> loggerMock, Exception expectedException, params string[] expectedFragments)
    {
        loggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => expectedFragments.All(f => v.ToString()!.Contains(f))),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
