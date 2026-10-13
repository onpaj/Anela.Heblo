using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolCacheTests
{
    private static CostPoolCache CreateCache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetCachedDataAsync_ReturnsEmptyUnhydratedData_WhenNothingStored()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var data = await cache.GetCachedDataAsync();

        // Assert
        data.IsHydrated.Should().BeFalse();
        data.Pools.Should().BeEmpty();
    }

    [Fact]
    public void IsHydrated_IsFalse_BeforeAnythingIsStored()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        cache.IsHydrated.Should().BeFalse();
    }

    [Fact]
    public async Task SetCachedDataAsync_RoundTripsStoredData()
    {
        // Arrange
        var cache = CreateCache();
        var stored = new CostPoolCacheData
        {
            Pools = new[] { new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M3, 903_000m) },
            LastUpdated = new DateTime(2026, 9, 21, 3, 0, 0, DateTimeKind.Utc),
            DataFrom = new DateOnly(2026, 1, 1),
            DataTo = new DateOnly(2026, 9, 30),
            IsHydrated = true
        };

        // Act
        await cache.SetCachedDataAsync(stored);
        var loaded = await cache.GetCachedDataAsync();

        // Assert
        loaded.Should().BeEquivalentTo(stored);
        cache.IsHydrated.Should().BeTrue();
    }

    [Fact]
    public async Task SetCachedDataAsync_OverwritesPreviousData()
    {
        // Arrange
        var cache = CreateCache();
        await cache.SetCachedDataAsync(new CostPoolCacheData
        {
            Pools = new[] { new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M2, 1m) },
            IsHydrated = true
        });

        // Act
        await cache.SetCachedDataAsync(new CostPoolCacheData
        {
            Pools = new[] { new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M2, 2m) },
            IsHydrated = true
        });
        var loaded = await cache.GetCachedDataAsync();

        // Assert
        loaded.Pools.Should().ContainSingle()
            .Which.Should().Be(new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M2, 2m));
    }
}
