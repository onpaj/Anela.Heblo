using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;

/// <summary>
/// OpenFeature provider: resolves flags in order — DB override → appsettings.json → registry default.
/// Registered as singleton; uses IServiceScopeFactory for scoped DB access.
/// Cache TTL: 30 seconds. Invalidated immediately on admin writes.
/// </summary>
internal sealed class HebloFeatureProvider : FeatureProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HebloFeatureProvider> _logger;

    internal const string CacheKey = "feature_flag_overrides";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public HebloFeatureProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<HebloFeatureProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    public override Metadata GetMetadata() => new("Heblo.FeatureManagement");

    public override async Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey,
        bool defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var overrides = await GetOverridesAsync(cancellationToken);
            if (overrides.TryGetValue(flagKey, out var dbValue))
                return new ResolutionDetails<bool>(flagKey, dbValue, reason: Reason.TargetingMatch);

            var configSection = _configuration.GetSection($"FeatureManagement:{flagKey}");
            if (configSection.Exists() && bool.TryParse(configSection.Value, out var configValue))
                return new ResolutionDetails<bool>(flagKey, configValue, reason: Reason.Static);

            return new ResolutionDetails<bool>(flagKey, defaultValue, reason: Reason.Default);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature flag resolution failed for key {FlagKey}; using default {DefaultValue}", flagKey, defaultValue);
            return new ResolutionDetails<bool>(
                flagKey,
                defaultValue,
                errorType: ErrorType.General,
                errorMessage: ex.Message,
                reason: Reason.Error);
        }
    }

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey, string defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("String flags not supported in v1.");

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey, int defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Integer flags not supported in v1.");

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey, double defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Double flags not supported in v1.");

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey, Value defaultValue, EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Structure flags not supported in v1.");

    private async Task<Dictionary<string, bool>> GetOverridesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            return await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IFeatureFlagOverrideRepository>();
                return await repo.GetAllAsDictionaryAsync(ct);
            }) ?? [];
        }
        catch (OperationCanceledException)
        {
            _cache.Remove(CacheKey);
            throw;
        }
    }
}
