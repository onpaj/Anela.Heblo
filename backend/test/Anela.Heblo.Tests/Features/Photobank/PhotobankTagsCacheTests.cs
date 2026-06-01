using Anela.Heblo.Application.Features.Photobank.Configuration;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankTagsCacheTests
{
    private static PhotobankTagsCache CreateCache(int ttlSeconds = 60)
    {
        var memory = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new PhotobankTagsCacheOptions { TtlSeconds = ttlSeconds });
        return new PhotobankTagsCache(memory, options);
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenCacheIsEmpty()
    {
        var cache = CreateCache();

        var hit = cache.TryGet(out var tags);

        hit.Should().BeFalse();
        tags.Should().BeNull();
    }

    [Fact]
    public void TryGet_ReturnsCachedPayload_AfterSet()
    {
        var cache = CreateCache();
        var payload = new List<TagWithCountDto>
        {
            new() { Id = 1, Name = "summer", Count = 10 },
        };

        cache.Set(payload);
        var hit = cache.TryGet(out var tags);

        hit.Should().BeTrue();
        tags.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Invalidate_RemovesCachedPayload()
    {
        var cache = CreateCache();
        cache.Set(new List<TagWithCountDto> { new() { Id = 1, Name = "x", Count = 1 } });

        cache.Invalidate();
        var hit = cache.TryGet(out var tags);

        hit.Should().BeFalse();
        tags.Should().BeNull();
    }
}
