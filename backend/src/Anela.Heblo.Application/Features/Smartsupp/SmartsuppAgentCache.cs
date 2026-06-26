using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp;

public interface ISmartsuppAgentCache
{
    Task<IReadOnlyDictionary<string, string>> GetAgentNamesAsync(CancellationToken cancellationToken = default);
}

public sealed class SmartsuppAgentCache : ISmartsuppAgentCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private sealed record CacheData(IReadOnlyDictionary<string, string> NamesByAgentId);

    // ISmartsuppApiClient is scoped; use IServiceScopeFactory so the singleton
    // cache can create a short-lived scope for each API call.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmartsuppAgentCache> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private CacheData? _cache;
    private DateTime _cachedAt = DateTime.MinValue;

    public SmartsuppAgentCache(IServiceScopeFactory scopeFactory, ILogger<SmartsuppAgentCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAgentNamesAsync(CancellationToken cancellationToken = default) =>
        (await GetCacheAsync(cancellationToken)).NamesByAgentId;

    private async Task<CacheData> GetCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl)
            return _cache;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl)
                return _cache;

            using var scope = _scopeFactory.CreateScope();
            var apiClient = scope.ServiceProvider.GetRequiredService<ISmartsuppApiClient>();
            var agents = await apiClient.GetAgentsAsync(cancellationToken);

            _cache = new CacheData(
                NamesByAgentId: agents
                    .Where(a => a.Name is not null)
                    .ToDictionary(a => a.Id, a => a.Name!));
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Smartsupp agent names; falling back to empty map");
            return _cache ?? new CacheData(new Dictionary<string, string>());
        }
        finally
        {
            _lock.Release();
        }
    }
}
