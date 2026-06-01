using System.Diagnostics.CodeAnalysis;
using Anela.Heblo.Application.Features.Photobank.Configuration;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Photobank.Services;

public sealed class PhotobankTagsCache : IPhotobankTagsCache
{
    private const string CacheKey = "Photobank:Tags:WithCounts";

    private readonly IMemoryCache _memoryCache;
    private readonly PhotobankTagsCacheOptions _options;

    public PhotobankTagsCache(IMemoryCache memoryCache, IOptions<PhotobankTagsCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _options = options.Value;
    }

    public bool TryGet([NotNullWhen(true)] out IReadOnlyList<TagWithCountDto>? tags)
    {
        if (_memoryCache.TryGetValue(CacheKey, out IReadOnlyList<TagWithCountDto>? cached) && cached is not null)
        {
            tags = cached;
            return true;
        }

        tags = null;
        return false;
    }

    public void Set(IReadOnlyList<TagWithCountDto> tags)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.TtlSeconds),
        };
        _memoryCache.Set(CacheKey, tags, entryOptions);
    }

    public void Invalidate() => _memoryCache.Remove(CacheKey);
}
