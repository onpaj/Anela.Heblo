using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class ManufactureTemplateCache : IManufactureTemplateCache
{
    private const string CacheKeyPrefix = "manufacture-template:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;
    private readonly ILogger<ManufactureTemplateCache> _logger;

    public ManufactureTemplateCache(IMemoryCache cache, ILogger<ManufactureTemplateCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ManufactureTemplate?> GetOrFetchAsync(
        string productCode,
        Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
        CancellationToken cancellationToken)
    {
        var key = CacheKeyPrefix + productCode;

        if (TryGet(key, out var cached) && cached is not null)
        {
            _logger.LogDebug("Manufacture template cache HIT for {ProductCode}", productCode);
            return ManufactureTemplateCloner.Clone(cached);
        }

        _logger.LogDebug("Manufacture template cache MISS for {ProductCode}", productCode);
        var fetched = await fetch(cancellationToken);
        if (fetched is null)
        {
            return null;
        }

        TrySet(key, fetched);
        _logger.LogDebug("Manufacture template cache STORE for {ProductCode}", productCode);
        return ManufactureTemplateCloner.Clone(fetched);
    }

    private bool TryGet(string key, out ManufactureTemplate? value)
    {
        value = null;
        try
        {
            return _cache.TryGetValue(key, out value);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void TrySet(string key, ManufactureTemplate value)
    {
        try
        {
            _cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });
        }
        catch (ObjectDisposedException)
        {
            // Cache disposed during shutdown — skip caching, return data anyway.
        }
    }

    public void Invalidate(string productCode)
    {
        var key = CacheKeyPrefix + productCode;
        try
        {
            _cache.Remove(key);
            _logger.LogDebug("Manufacture template cache INVALIDATED for {ProductCode}", productCode);
        }
        catch (ObjectDisposedException)
        {
            // Cache disposed during shutdown — ignore.
        }
    }
}
