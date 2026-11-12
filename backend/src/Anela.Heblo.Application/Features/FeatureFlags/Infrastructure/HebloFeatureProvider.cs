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
    internal static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(5);

    // Guards _inflight so that concurrent cache misses share one DB load (single-flight).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<Dictionary<string, bool>>? _inflight;

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
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                // The caller (typically an aborted HTTP request) gave up waiting. The shared
                // override refresh is not affected, so this is not a DB problem — keep it quiet.
                _logger.LogDebug(
                    "Feature flag resolution cancelled for key {FlagKey}; using default {DefaultValue}; cancelledBy=caller",
                    flagKey, defaultValue);
            }
            else
            {
                var attempts = ex.Data["Anela.DbRetryAttempts"] as int? ?? 1;
                var dbRetryExhausted = attempts > 1;
                var cancelledBy = ex is OperationCanceledException ? "refresh" : "none";
                _logger.LogWarning(
                    ex,
                    "Feature flag resolution failed for key {FlagKey}; using default {DefaultValue}; dbRetryExhausted={DbRetryExhausted} attempts={Attempts} cancelledBy={CancelledBy}",
                    flagKey, defaultValue, dbRetryExhausted, attempts, cancelledBy);
            }

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

        // Hit path: no lock.
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, bool>? hit) && hit is not null)
            return hit;

        Task<Dictionary<string, bool>> load;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out hit) && hit is not null)
                return hit;

            // A completed task here is either a failed load (failures are never cached, so retry)
            // or a successful one whose cache entry has since expired or been invalidated.
            if (_inflight is null || _inflight.IsCompleted)
                _inflight = LoadOverridesAsync();

            load = _inflight;
        }
        finally
        {
            _gate.Release();
        }

        // A cancelled caller abandons only its own wait; the shared load keeps running.
        return await load.WaitAsync(ct);
    }

    /// <summary>
    /// Loads all overrides and caches them. Shared by every caller that missed the cache, so it
    /// must not observe any caller's CancellationToken — it is bounded by RefreshTimeout instead.
    /// </summary>
    private async Task<Dictionary<string, bool>> LoadOverridesAsync()
    {
        using var cts = new CancellationTokenSource(RefreshTimeout);
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFeatureFlagOverrideRepository>();
        var overrides = await repo.GetAllAsDictionaryAsync(cts.Token) ?? [];
        _cache.Set(CacheKey, overrides, CacheTtl);
        return overrides;
    }
}
