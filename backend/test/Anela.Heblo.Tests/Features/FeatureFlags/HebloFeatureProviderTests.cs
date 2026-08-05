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
