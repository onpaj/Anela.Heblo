using System.Net;
using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Adapters.Flexi.Tests.Manufacture;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class FlexiManufactureTemplateServiceTests
{
    private readonly Mock<IBoMClient> _mockBomClient = new();
    private readonly Mock<IErpStockClient> _mockStockClient = new();
    private readonly Mock<ILogger<FlexiManufactureTemplateService>> _mockLogger = new();
    private readonly Mock<ITelemetryService> _mockTelemetry = new();
    private readonly PassthroughTemplateCache _passthroughCache = new();
    private readonly FlexiManufactureTemplateService _service;

    public FlexiManufactureTemplateServiceTests()
    {
        _service = new FlexiManufactureTemplateService(
            _mockBomClient.Object,
            _mockStockClient.Object,
            TimeProvider.System,
            _passthroughCache,
            _mockTelemetry.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_When501Returned_LogsErrorAndRethrows()
    {
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockBomClient
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var act = async () => await _service.GetManufactureTemplateAsync("PROD-001", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("501") && v.ToString()!.Contains("kusovnik")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_DispatchesThreeStockCallsConcurrently()
    {
        var header = ManufactureTestData.CreateBoMItem(1, 1, 10, ManufactureTestData.SemiProducts.SilkBar);
        var ingredient = ManufactureTestData.CreateBoMItem(2, 2, 1, ManufactureTestData.Materials.Bisabolol);

        _mockBomClient
            .Setup(x => x.GetAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto> { header, ingredient });

        // Each StockToDateAsync call registers as in flight and then waits until all three calls have started.
        // Run concurrently, all three reach the barrier immediately and maxInFlight is 3. Run sequentially, the
        // first call can never see the others start, gives up at the barrier timeout and maxInFlight stays 1.
        // No assertion depends on elapsed wall-clock time, so a loaded runner cannot make this flaky.
        const int ExpectedConcurrentCalls = 3;
        var barrierTimeout = TimeSpan.FromSeconds(2);
        var inFlight = 0;
        var maxInFlight = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IReadOnlyList<ErpStock>> StockCallAsync(IReadOnlyList<ErpStock> result, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref inFlight);
            int observed;
            while ((observed = Volatile.Read(ref maxInFlight)) < current
                && Interlocked.CompareExchange(ref maxInFlight, current, observed) != observed)
            {
            }

            if (current >= ExpectedConcurrentCalls)
            {
                allStarted.TrySetResult();
            }

            try
            {
                await allStarted.Task.WaitAsync(barrierTimeout, ct);
            }
            catch (TimeoutException)
            {
                // Calls were not concurrent; maxInFlight records that and the assertion below fails.
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }

            return result;
        }

        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), FlexiStockClient.MaterialWarehouseId, It.IsAny<CancellationToken>()))
            .Returns((DateTime _, int _, CancellationToken ct) =>
                StockCallAsync(new List<ErpStock> { new() { ProductCode = "AKL001", HasLots = true } }, ct));

        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), FlexiStockClient.SemiProductsWarehouseId, It.IsAny<CancellationToken>()))
            .Returns((DateTime _, int _, CancellationToken ct) => StockCallAsync(new List<ErpStock>(), ct));

        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), FlexiStockClient.ProductsWarehouseId, It.IsAny<CancellationToken>()))
            .Returns((DateTime _, int _, CancellationToken ct) => StockCallAsync(new List<ErpStock>(), ct));

        var template = await _service.GetManufactureTemplateAsync(ManufactureTestData.SemiProducts.SilkBar.Code, CancellationToken.None);

        template.Should().NotBeNull();
        maxInFlight.Should().Be(ExpectedConcurrentCalls,
            "the three stock calls must be in flight at the same time, not dispatched sequentially");

        _mockStockClient.Verify(
            x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_MergesHasLotsAcrossWarehouses()
    {
        var header = ManufactureTestData.CreateBoMItem(1, 1, 10, ManufactureTestData.SemiProducts.SilkBar);
        var ingredient1 = ManufactureTestData.CreateBoMItem(2, 2, 1, ManufactureTestData.Materials.Bisabolol);
        var ingredient2 = ManufactureTestData.CreateBoMItem(3, 2, 1, ManufactureTestData.Materials.DermosoftEco);

        _mockBomClient
            .Setup(x => x.GetAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto> { header, ingredient1, ingredient2 });

        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), FlexiStockClient.MaterialWarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>
            {
                new() { ProductCode = "AKL001", HasLots = true }
            });
        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), FlexiStockClient.SemiProductsWarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>());
        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), FlexiStockClient.ProductsWarehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>
            {
                new() { ProductCode = "AKL003", HasLots = false }
            });

        var template = await _service.GetManufactureTemplateAsync(ManufactureTestData.SemiProducts.SilkBar.Code, CancellationToken.None);

        template.Should().NotBeNull();
        template!.Ingredients.Should().HaveCount(2);
        template.Ingredients.Single(i => i.ProductCode == "AKL001").HasLots.Should().BeTrue();
        template.Ingredients.Single(i => i.ProductCode == "AKL003").HasLots.Should().BeFalse();
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_OnCacheMiss_EmitsTelemetryEvent()
    {
        var header = ManufactureTestData.CreateBoMItem(1, 1, 10, ManufactureTestData.SemiProducts.SilkBar);
        var ingredient = ManufactureTestData.CreateBoMItem(2, 2, 1, ManufactureTestData.Materials.Bisabolol);

        _mockBomClient
            .Setup(x => x.GetAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto> { header, ingredient });
        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>());

        await _service.GetManufactureTemplateAsync(ManufactureTestData.SemiProducts.SilkBar.Code, CancellationToken.None);

        _mockTelemetry.Verify(
            t => t.TrackBusinessEvent(
                "manufacture_template_fetched",
                It.Is<Dictionary<string, string>>(p =>
                    p["product_code"] == ManufactureTestData.SemiProducts.SilkBar.Code &&
                    p["cache_hit"] == "false" &&
                    p["ingredient_count"] == "1"),
                It.Is<Dictionary<string, double>>(m =>
                    m.ContainsKey("bom_duration_ms") &&
                    m.ContainsKey("stock_duration_ms") &&
                    m.ContainsKey("total_duration_ms"))),
            Times.Once);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_OnCacheHit_EmitsTelemetryWithCacheHitTrue()
    {
        var cachedTemplate = ManufactureTestData.CreateTemplate(
            ManufactureTestData.SemiProducts.SilkBar,
            templateAmount: 10,
            (ManufactureTestData.Materials.Bisabolol, amount: 1, hasLots: false));

        var hitCache = new HitOnlyCache(cachedTemplate);

        var service = new FlexiManufactureTemplateService(
            _mockBomClient.Object,
            _mockStockClient.Object,
            TimeProvider.System,
            hitCache,
            _mockTelemetry.Object,
            _mockLogger.Object);

        await service.GetManufactureTemplateAsync(ManufactureTestData.SemiProducts.SilkBar.Code, CancellationToken.None);

        _mockTelemetry.Verify(
            t => t.TrackBusinessEvent(
                "manufacture_template_fetched",
                It.Is<Dictionary<string, string>>(p =>
                    p["product_code"] == ManufactureTestData.SemiProducts.SilkBar.Code &&
                    p["cache_hit"] == "true" &&
                    p["ingredient_count"] == "1"),
                It.Is<Dictionary<string, double>>(m =>
                    m["bom_duration_ms"] == 0 &&
                    m["stock_duration_ms"] == 0 &&
                    m.ContainsKey("total_duration_ms"))),
            Times.Once);

        _mockBomClient.Verify(
            x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockStockClient.Verify(
            x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_MapsIngredientOrder_FromBoMItemFlexiDto()
    {
        // Arrange
        var header = ManufactureTestData.CreateBoMItem(1, 1, 10, ManufactureTestData.SemiProducts.SilkBar);

        var ing1 = ManufactureTestData.CreateBoMItem(10, 2, 5, ManufactureTestData.Materials.Bisabolol);
        ing1.Order = 2;
        var ing2 = ManufactureTestData.CreateBoMItem(20, 2, 3, ManufactureTestData.Materials.Glycerol);
        ing2.Order = 1;

        _mockBomClient
            .Setup(x => x.GetAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto> { header, ing1, ing2 });

        SetupEmptyStock();

        // Act
        var template = await _service.GetManufactureTemplateAsync(
            ManufactureTestData.SemiProducts.SilkBar.Code, CancellationToken.None);

        // Assert
        template.Should().NotBeNull();
        var bisabolol = template!.Ingredients.Single(i => i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code);
        var glycerol = template.Ingredients.Single(i => i.ProductCode == ManufactureTestData.Materials.Glycerol.Code);
        bisabolol.Order.Should().Be(2);
        glycerol.Order.Should().Be(1);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_WhenHeaderNotFound_EmitsTelemetryAsCacheMiss()
    {
        // BoM returns no Level-1 header → FetchAsync returns null
        _mockBomClient
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto>());

        var result = await _service.GetManufactureTemplateAsync("PROD-UNKNOWN", CancellationToken.None);

        result.Should().BeNull();
        _mockTelemetry.Verify(
            t => t.TrackBusinessEvent(
                "manufacture_template_fetched",
                It.Is<Dictionary<string, string>>(p =>
                    p["product_code"] == "PROD-UNKNOWN" &&
                    p["cache_hit"] == "false" &&
                    p["ingredient_count"] == "0"),
                It.Is<Dictionary<string, double>>(m =>
                    m["stock_duration_ms"] == 0 &&
                    m.ContainsKey("total_duration_ms"))),
            Times.Once);
    }

    private void SetupEmptyStock()
    {
        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>());
    }

    /// <summary>
    /// Test cache that always invokes the fetcher (acts as pass-through so we test
    /// the inner fetch logic directly).
    /// </summary>
    private sealed class PassthroughTemplateCache : IManufactureTemplateCache
    {
        public int Calls { get; private set; }

        public async Task<ManufactureTemplate?> GetOrFetchAsync(
            string productCode,
            Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
            CancellationToken cancellationToken)
        {
            Calls++;
            return await fetch(cancellationToken);
        }

        public void Invalidate(string productCode) { }
    }

    private sealed class HitOnlyCache : IManufactureTemplateCache
    {
        private readonly ManufactureTemplate _cached;
        public HitOnlyCache(ManufactureTemplate cached) => _cached = cached;

        public Task<ManufactureTemplate?> GetOrFetchAsync(
            string productCode,
            Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
            CancellationToken cancellationToken)
        {
            // Simulate a hit: do not invoke fetch.
            return Task.FromResult<ManufactureTemplate?>(ManufactureTemplateCloner.Clone(_cached));
        }

        public void Invalidate(string productCode) { }
    }
}
