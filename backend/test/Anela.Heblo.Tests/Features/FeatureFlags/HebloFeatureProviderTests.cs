using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class HebloFeatureProviderTests
{
    private static (HebloFeatureProvider provider, IFeatureFlagOverrideRepository mockRepo, IMemoryCache cache)
        CreateProvider(Dictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var mockRepo = Substitute.For<IFeatureFlagOverrideRepository>();
        mockRepo.GetAllAsDictionaryAsync(default).ReturnsForAnyArgs(new Dictionary<string, bool>());

        var services = new ServiceCollection();
        services.AddSingleton<IFeatureFlagOverrideRepository>(mockRepo);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var logger = NullLogger<HebloFeatureProvider>.Instance;
        var provider = new HebloFeatureProvider(scopeFactory, cache, configuration, logger);
        return (provider, mockRepo, cache);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenDbOverrideTrue_ReturnsTrueWithTargetingMatchReason()
    {
        var (provider, mockRepo, _) = CreateProvider();
        mockRepo.GetAllAsDictionaryAsync(default).ReturnsForAnyArgs(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.StockTaking] = true
        });

        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Constant.Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenDbOverrideFalse_ReturnsFalseWithTargetingMatchReason()
    {
        var (provider, mockRepo, _) = CreateProvider();
        mockRepo.GetAllAsDictionaryAsync(default).ReturnsForAnyArgs(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.BackgroundRefresh] = false
        });

        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.BackgroundRefresh, true);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(OpenFeature.Constant.Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenNoDbOverride_FallsBackToConfig()
    {
        var config = new Dictionary<string, string?>
        {
            [$"FeatureManagement:{FeatureFlagKeys.StockTaking}"] = "true"
        };
        var (provider, _, _) = CreateProvider(config);

        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Constant.Reason.Static);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenNoDbOverrideNoConfig_ReturnsSuppliedDefault()
    {
        var (provider, _, _) = CreateProvider();

        var result = await provider.ResolveBooleanValueAsync("is-unknown-enabled", true);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Constant.Reason.Default);
    }

    [Fact]
    public async Task ResolveBooleanValue_WhenRepoThrows_ReturnsDefaultWithErrorReason()
    {
        var (provider, mockRepo, _) = CreateProvider();
        mockRepo.GetAllAsDictionaryAsync(default)
            .ThrowsForAnyArgs(new InvalidOperationException("DB down"));

        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        result.Value.Should().BeFalse();
        result.ErrorType.Should().Be(OpenFeature.Constant.ErrorType.General);
    }

    [Fact]
    public async Task ResolveBooleanValue_DbOverrideTakesPrecedenceOverConfig()
    {
        var config = new Dictionary<string, string?>
        {
            [$"FeatureManagement:{FeatureFlagKeys.StockTaking}"] = "false"
        };
        var (provider, mockRepo, _) = CreateProvider(config);
        mockRepo.GetAllAsDictionaryAsync(default).ReturnsForAnyArgs(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.StockTaking] = true
        });

        var result = await provider.ResolveBooleanValueAsync(FeatureFlagKeys.StockTaking, false);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(OpenFeature.Constant.Reason.TargetingMatch);
    }
}
