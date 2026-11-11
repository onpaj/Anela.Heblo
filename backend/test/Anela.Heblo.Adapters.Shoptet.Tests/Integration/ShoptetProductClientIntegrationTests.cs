using System.Diagnostics;
using System.Net.Http.Json;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Domain.Features.Catalog.Eshop;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

/// <summary>Read-only sweep against the Shoptet TEST store (780175). Never point at production.</summary>
[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetProductClientIntegrationTests
{
    private const int ExpectedBestsellerCount = 12;
    private static readonly TimeSpan MaxSweepDuration = TimeSpan.FromSeconds(120);

    private readonly ShoptetIntegrationTestFixture _fixture;

    public ShoptetProductClientIntegrationTests(ShoptetIntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListAsync_TestStore_ReturnsAllSupplyCodes()
    {
        // Arrange
        var client = _fixture.ServiceProvider.GetRequiredService<IEshopProductClient>();
        var settings = _fixture.ServiceProvider.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
        using var supplyHttp = new HttpClient { BaseAddress = new Uri(settings.BaseUrl) };
        supplyHttp.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        var supplies = await supplyHttp.GetFromJsonAsync<GetSuppliesResponse>(
            $"/api/stocks/{settings.StockId}/supplies?itemsPerPage=1000");
        var supplyCodes = supplies!.Data!.Supplies.Select(s => s.Code).ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var products = await client.ListAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert
        Console.WriteLine($"ShoptetProductClient sweep: {products.Count} variants, {supplyCodes.Count} supply codes, {stopwatch.Elapsed.TotalSeconds:F1}s");
        products.Select(p => p.Code).Should().Contain(supplyCodes);
        products.Where(p => p.Flags.Any(f => f.Code == "bestseller")).Select(p => p.ProductGuid).Distinct().Count().Should().Be(ExpectedBestsellerCount);
        stopwatch.Elapsed.Should().BeLessThan(MaxSweepDuration);
    }
}
