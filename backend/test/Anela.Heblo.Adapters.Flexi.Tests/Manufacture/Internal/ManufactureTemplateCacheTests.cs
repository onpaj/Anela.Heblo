using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class ManufactureTemplateCacheTests
{
    private static ManufactureTemplate BuildTemplate(string productCode = "MAS001001M", int ingredientOrder = 0) => new()
    {
        TemplateId = 1,
        ProductCode = productCode,
        ProductName = "Test product",
        Amount = 10,
        OriginalAmount = 10,
        BatchSize = 0,
        ManufactureType = ManufactureType.SinglePhase,
        Ingredients = new List<Ingredient>
        {
            new()
            {
                TemplateId = 2,
                ProductCode = "AKL001",
                ProductName = "Ing",
                Amount = 1.0,
                ProductType = ProductType.Material,
                HasLots = true,
                Order = ingredientOrder
            }
        }
    };

    private static ManufactureTemplateCache CreateSut() =>
        new(new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ManufactureTemplateCache>.Instance);

    [Fact]
    public async Task GetOrFetchAsync_CacheMiss_InvokesFetcherOnce()
    {
        var sut = CreateSut();
        var fetcherCalls = 0;

        var result = await sut.GetOrFetchAsync(
            "MAS001001M",
            _ =>
            {
                fetcherCalls++;
                return Task.FromResult<ManufactureTemplate?>(BuildTemplate());
            },
            CancellationToken.None);

        fetcherCalls.Should().Be(1);
        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("MAS001001M");
    }

    [Fact]
    public async Task GetOrFetchAsync_SecondCallHits_DoesNotInvokeFetcher()
    {
        var sut = CreateSut();
        var fetcherCalls = 0;

        Func<CancellationToken, Task<ManufactureTemplate?>> fetch = _ =>
        {
            fetcherCalls++;
            return Task.FromResult<ManufactureTemplate?>(BuildTemplate());
        };

        await sut.GetOrFetchAsync("MAS001001M", fetch, CancellationToken.None);
        var second = await sut.GetOrFetchAsync("MAS001001M", fetch, CancellationToken.None);

        fetcherCalls.Should().Be(1);
        second!.ProductCode.Should().Be("MAS001001M");
    }

    [Fact]
    public async Task GetOrFetchAsync_CacheHit_ReturnsDeepClone_NotSharedReference()
    {
        var sut = CreateSut();
        var stored = BuildTemplate();

        var first = await sut.GetOrFetchAsync("MAS001001M",
            _ => Task.FromResult<ManufactureTemplate?>(stored),
            CancellationToken.None);
        first!.BatchSize = 555;

        var second = await sut.GetOrFetchAsync("MAS001001M",
            _ => throw new InvalidOperationException("fetcher must not be called on hit"),
            CancellationToken.None);

        second!.BatchSize.Should().Be(0, "the second hit must not see the mutation from the first caller");
        second.Should().NotBeSameAs(first);
        second.Ingredients.Should().NotBeSameAs(first!.Ingredients);
    }

    [Fact]
    public async Task GetOrFetchAsync_FetcherReturnsNull_DoesNotCacheResult()
    {
        var sut = CreateSut();
        var fetcherCalls = 0;

        Func<CancellationToken, Task<ManufactureTemplate?>> fetch = _ =>
        {
            fetcherCalls++;
            return Task.FromResult<ManufactureTemplate?>(null);
        };

        var first = await sut.GetOrFetchAsync("MISSING", fetch, CancellationToken.None);
        var second = await sut.GetOrFetchAsync("MISSING", fetch, CancellationToken.None);

        first.Should().BeNull();
        second.Should().BeNull();
        fetcherCalls.Should().Be(2, "null results must not pin a transient FlexiBee outage as 'not found'");
    }

    [Fact]
    public async Task GetOrFetchAsync_FetcherThrows_DoesNotCacheAndPropagates()
    {
        var sut = CreateSut();
        var fetcherCalls = 0;

        async Task<ManufactureTemplate?> Throwing(CancellationToken ct)
        {
            fetcherCalls++;
            await Task.Yield();
            throw new InvalidOperationException("flexi went boom");
        }

        var act1 = async () => await sut.GetOrFetchAsync("MAS001001M", Throwing, CancellationToken.None);
        await act1.Should().ThrowAsync<InvalidOperationException>();

        var act2 = async () => await sut.GetOrFetchAsync("MAS001001M", Throwing, CancellationToken.None);
        await act2.Should().ThrowAsync<InvalidOperationException>();

        fetcherCalls.Should().Be(2, "exceptions must not be cached as 'not found'");
    }

    [Fact]
    public async Task GetOrFetchAsync_PassesCancellationTokenToFetcher()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        CancellationToken? observed = null;

        await sut.GetOrFetchAsync(
            "MAS001001M",
            ct =>
            {
                observed = ct;
                return Task.FromResult<ManufactureTemplate?>(BuildTemplate());
            },
            cts.Token);

        observed.Should().NotBeNull();
        observed!.Value.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetOrFetchAsync_CacheMiss_PreservesIngredientOrder()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetOrFetchAsync(
            "MAS001001M",
            _ => Task.FromResult<ManufactureTemplate?>(BuildTemplate(ingredientOrder: 3)),
            CancellationToken.None);

        // Assert
        result!.Ingredients[0].Order.Should().Be(3,
            "Order must be preserved through the deep clone returned on cache miss");
    }

    [Fact]
    public async Task GetOrFetchAsync_CacheHit_PreservesIngredientOrder()
    {
        // Arrange
        var sut = CreateSut();

        // Populate cache
        await sut.GetOrFetchAsync(
            "MAS001001M",
            _ => Task.FromResult<ManufactureTemplate?>(BuildTemplate(ingredientOrder: 5)),
            CancellationToken.None);

        // Act – second call hits the cache
        var result = await sut.GetOrFetchAsync(
            "MAS001001M",
            _ => throw new InvalidOperationException("fetcher must not be called on hit"),
            CancellationToken.None);

        // Assert
        result!.Ingredients[0].Order.Should().Be(5,
            "Order must be preserved through the deep clone returned on cache hit");
    }

    [Fact]
    public async Task Invalidate_AfterCacheHit_NextGetOrFetchInvokesFetcherAgain()
    {
        // Arrange
        var sut = CreateSut();
        var fetcherCalls = 0;

        Func<CancellationToken, Task<ManufactureTemplate?>> fetch = _ =>
        {
            fetcherCalls++;
            return Task.FromResult<ManufactureTemplate?>(BuildTemplate());
        };

        // First call: populates cache
        await sut.GetOrFetchAsync("MAS001001M", fetch, CancellationToken.None);
        fetcherCalls.Should().Be(1);

        // Act
        sut.Invalidate("MAS001001M");

        // Second call: cache was invalidated, must invoke fetcher again
        await sut.GetOrFetchAsync("MAS001001M", fetch, CancellationToken.None);

        // Assert
        fetcherCalls.Should().Be(2, "cache was invalidated between calls");
    }
}
