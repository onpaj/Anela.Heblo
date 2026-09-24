# Purchase Price Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the nightly `purchase-price-recalculation` job keep Flexi `cenik.nakupCena` correct: sync materials and goods from the stock valuation (`prumCena`), then recalculate semi-product BoMs, then product/set BoMs.

**Architecture:** The existing `RecalculatePurchasePriceHandler` (Purchase module) gets a three-phase `RecalculateAll` path. Phase 1 reads candidates through a new Purchase contract `IPurchasePriceSyncSource` (implemented in Catalog/Infrastructure from the catalog, the Flexi ceník and Flexi stock-to-date) and writes through `IPurchasePriceRecalculationService.SetPurchasePriceAsync`, which delegates to a new Domain port `IErpPurchasePriceWriter` implemented by `FlexiPurchasePriceWriter` (`PUT cenik/{id}.json { nakupCena }`). Phases 2/3 reuse the existing `prepocti-nakupni-cenu` call, split by `MaterialBomReference.IsSemiProduct`.

**Tech Stack:** .NET 8, MediatR, xUnit + Moq + FluentAssertions, `Microsoft.Extensions.TimeProvider.Testing`, Flexi REST (ABRA Flexi), Hangfire.

**Spec:** `docs/superpowers/specs/2026-09-23-purchase-price-sync-design.md`

## Global Constraints

- DTOs / response classes are **classes, never C# records** (OpenAPI generator).
- Purchase module reaches Catalog/Flexi **only through `Purchase/Contracts`** interfaces; adapters live in `Application/Features/Catalog/Infrastructure/`.
- Flexi ceník writes are addressed **by internal numeric id only** (`cenik/{id}.json`); a PUT by code creates a new item.
- Decimal values sent to Flexi use `CultureInfo.InvariantCulture`.
- Materials sync from warehouse **5**, goods from warehouse **4**; source is `prumCena` only (D2); no usable `prumCena` (missing or ≤ 0) → skip, never write (D3).
- `PurchasePriceTolerance` = `0.0001m` (named constant).
- Write safety: fully automatic, no threshold guard, no dry-run mode in the job (D6).
- Phase 1 input-load failure fails the job and **no** BoM recalculation runs.
- Cancellation: per-item `catch` blocks use `when (!cancellationToken.IsCancellationRequested)` so a cancelled run is not counted as item failures.
- Adapter HTTP tests **parse the request body as JSON** — no substring asserts.
- Commits: conventional (`feat:`, `test:`, `docs:`), ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Live Flexi calls (Task 6) only with the owner's explicit approval — there is no sandbox.

## Review Focus

- A catalog Material/Goods item with **no ceník row or `ErpItemId` 0** must never be written (and never by code) — it is excluded from candidates. Test in Task 2.
- **Duplicate product codes** in the ceník or stock-to-date rows must not crash the job (first row wins). Test in Task 2.
- A **material whose stock row exists only in the goods warehouse** (or vice versa) has no stock price and is skipped, not priced from the wrong warehouse. Test in Task 2.
- **`prumCena` of 0 or negative** must be skipped, not written. Test in Task 4.
- **Cancellation during phase 1** must propagate as cancellation, not be swallowed as N item failures. Test in Task 4.

## File Structure

| File | Responsibility |
|---|---|
| Create `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPurchasePriceWriter.cs` | Domain port: write a ceník purchase price |
| Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiPurchasePriceWriter.cs` | Flexi implementation (`PUT cenik/{id}.json`) |
| Modify `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs:71` | Register writer |
| Create `backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiPurchasePriceWriterTests.cs` | Writer tests |
| Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceSyncSource.cs` | Purchase contract: phase-1 candidates |
| Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchasePriceSyncCandidate.cs` | Candidate DTO |
| Modify `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs` | Add `SetPurchasePriceAsync` |
| Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapter.cs` | Builds candidates from catalog + ceník + stock |
| Modify `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs` | Implement `SetPurchasePriceAsync` |
| Modify `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:55-56` | Register sync source |
| Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapterTests.cs` | Source adapter tests |
| Modify `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs` | New ctor arg + delegation test |
| Modify `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs` | Add `IsSemiProduct` |
| Modify `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs:56-69` | Map `IsSemiProduct` |
| Modify `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapterTests.cs` | `IsSemiProduct` test |
| Modify `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs` | Three phases |
| Modify `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceResponse.cs` | Per-phase summaries |
| Modify `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/Jobs/PurchasePriceRecalculationJob.cs:55-65` | Log/telemetry phase-1 counts |
| Modify `backend/test/Anela.Heblo.Tests/Application/Purchase/RecalculatePurchasePriceHandlerTests.cs` | New ctor arg + phase tests |
| Modify `docs/superpowers/specs/2026-09-03-central-price-management-design.md:87-90` | Revise A2 |
| Create `scripts/flexi-purchase-price-report.py` | Read-only pre-deploy comparison report |
| Create `docs/integrations/flexi-api.md` | Live findings (Task 6) |

---

### Task 1: Flexi purchase price writer

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPurchasePriceWriter.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiPurchasePriceWriter.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` (after line 71)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiPurchasePriceWriterTests.cs`

**Interfaces:**
- Produces: `Anela.Heblo.Domain.Features.ProductPricing.IErpPurchasePriceWriter.SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken ct) : Task` — throws `ArgumentOutOfRangeException` for `erpItemId <= 0` or `purchasePrice <= 0` (no HTTP call), `HttpRequestException` on non-2xx.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiPurchasePriceWriterTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Flexi.Price;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Client;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiPurchasePriceWriterTests
{
    private static (FlexiPurchasePriceWriter Writer, List<HttpRequestMessage> Requests, List<string> Bodies) Create(
        IMemoryCache? cache = null, HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{}")
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new StubHandler(requests, bodies, status, responseBody);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var settings = new FlexiBeeSettings { Server = "https://petra-tesarikova.flexibee.eu", Company = "anela" };

        return (new FlexiPurchasePriceWriter(
                    factory, settings, cache ?? new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<FlexiPurchasePriceWriter>.Instance),
                requests, bodies);
    }

    [Fact]
    public async Task addresses_the_write_by_internal_cenik_id_with_put()
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        await writer.SetPurchasePriceAsync(789, 0.311m, CancellationToken.None);

        // Assert
        requests.Should().ContainSingle();
        requests[0].Method.Should().Be(HttpMethod.Put);
        requests[0].RequestUri!.ToString()
            .Should().Be("https://petra-tesarikova.flexibee.eu/c/anela/cenik/789.json");
    }

    [Fact]
    public async Task sends_only_nakupCena_in_invariant_format()
    {
        // Arrange
        var (writer, _, bodies) = Create();

        // Act
        await writer.SetPurchasePriceAsync(789, 0.311234m, CancellationToken.None);

        // Assert
        using var doc = JsonDocument.Parse(bodies.Single());
        var cenik = doc.RootElement.GetProperty("winstrom").GetProperty("cenik");
        cenik.GetProperty("nakupCena").GetString().Should().Be("0.311234");
        cenik.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "nakupCena" });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task rejects_a_non_positive_id_without_calling_flexi(int erpItemId)
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPurchasePriceAsync(erpItemId, 1m, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public async Task rejects_a_non_positive_price_without_calling_flexi(decimal price)
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPurchasePriceAsync(789, price, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task throws_with_the_flexi_body_when_the_write_is_rejected()
    {
        // Arrange
        var (writer, _, _) = Create(status: HttpStatusCode.BadRequest, responseBody: "{\"err\":\"nope\"}");

        // Act
        var act = () => writer.SetPurchasePriceAsync(789, 1m, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).And.Message.Should().Contain("nope");
    }

    [Fact]
    public async Task evicts_the_cached_flexi_price_read_after_a_successful_write()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(FlexiProductPriceErpClient.CacheKey, new List<ProductPriceFlexiDto>());
        var (writer, _, _) = Create(cache);

        // Act
        await writer.SetPurchasePriceAsync(789, 0.311m, CancellationToken.None);

        // Assert
        cache.TryGetValue(FlexiProductPriceErpClient.CacheKey, out _).Should().BeFalse();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests;
        private readonly List<string> _bodies;
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public StubHandler(List<HttpRequestMessage> requests, List<string> bodies,
                           HttpStatusCode status, string responseBody)
        {
            _requests = requests;
            _bodies = bodies;
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (request.Content is not null)
            {
                _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: build FAILS — `FlexiPurchasePriceWriter` does not exist.

- [ ] **Step 3: Add the domain port**

Create `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPurchasePriceWriter.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

public interface IErpPurchasePriceWriter
{
    /// <param name="erpItemId">Internal ceník id (<c>idcenik</c>). Addressing by code would create records.</param>
    /// <param name="purchasePrice">Purchase price excluding VAT, per the item's primary unit (<c>mj1</c>).</param>
    Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken ct);
}
```

- [ ] **Step 4: Implement the Flexi writer**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiPurchasePriceWriter.cs`:

```csharp
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;

namespace Anela.Heblo.Adapters.Flexi.Price;

/// <summary>
/// Writes a Flexi ceník item's purchase price (<c>nakupCena</c>, excluding VAT, per <c>mj1</c>).
///
/// Used by the nightly purchase price sync to set materials and goods to their average stock
/// price, so Flexi's BoM roll-up (<c>prepocti-nakupni-cenu</c>) sums real values.
///
/// Addressed by the internal numeric id only: Flexi does not distinguish create from
/// update, so a PUT to <c>cenik/code:UNKNOWN.json</c> silently creates a new item.
/// </summary>
public class FlexiPurchasePriceWriter : IErpPurchasePriceWriter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlexiBeeSettings _connection;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FlexiPurchasePriceWriter> _logger;

    public FlexiPurchasePriceWriter(
        IHttpClientFactory httpClientFactory,
        FlexiBeeSettings connection,
        IMemoryCache cache,
        ILogger<FlexiPurchasePriceWriter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connection = connection;
        _cache = cache;
        _logger = logger;
    }

    public async Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken ct)
    {
        if (erpItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(erpItemId),
                "A Flexi ceník id is required. Writing by code would create a new price list item.");
        }

        if (purchasePrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(purchasePrice),
                purchasePrice,
                "A Flexi ceník purchase price must be positive.");
        }

        var payload = new
        {
            winstrom = new
            {
                cenik = new
                {
                    nakupCena = purchasePrice.ToString("0.######", CultureInfo.InvariantCulture),
                },
            },
        };

        var url = $"{_connection.Server.TrimEnd('/')}/c/{_connection.Company}/cenik/{erpItemId}.json";

        // Same client naming and 5-minute timeout convention as FlexiProductPriceWriter.
        using var client = _httpClientFactory.CreateClient(nameof(FlexiPurchasePriceWriter));
        client.Timeout = TimeSpan.FromMinutes(5);
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_connection.Login}:{_connection.Password}")));

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Flexi ceník purchase price write failed for id {erpItemId} with {(int)response.StatusCode}: {body}");
        }

        InvalidateCachedErpPrices();

        _logger.LogInformation(
            "Updated Flexi ceník {ErpItemId} purchase price to {PurchasePrice}", erpItemId, purchasePrice);
    }

    private void InvalidateCachedErpPrices()
    {
        try
        {
            _cache.Remove(FlexiProductPriceErpClient.CacheKey);
        }
        catch (ObjectDisposedException)
        {
            // A disposed cache is not a reason to report a completed live write as a failure.
        }
    }
}
```

- [ ] **Step 5: Register it**

In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`, directly after
`services.AddScoped<IErpPriceWriter, FlexiProductPriceWriter>();` add:

```csharp
        services.AddScoped<IErpPurchasePriceWriter, FlexiPurchasePriceWriter>();
```

(`IErpPurchasePriceWriter` is in the same namespace as `IErpPriceWriter`, so no new `using` is needed.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~FlexiPurchasePriceWriterTests"`
Expected: all 8 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPurchasePriceWriter.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiPurchasePriceWriter.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiPurchasePriceWriterTests.cs
git commit -m "feat: Flexi writer for ceník purchase price (nakupCena)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Phase-1 candidate source and purchase price write contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchasePriceSyncCandidate.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceSyncSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (lines 55-56)
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs`

**Interfaces:**
- Consumes: `IErpPurchasePriceWriter.SetPurchasePriceAsync(int, decimal, CancellationToken)` (Task 1); `ICatalogRepository.GetAllAsync(CancellationToken) : Task<IEnumerable<CatalogAggregate>>`; `IProductPriceErpClient.GetAllAsync(bool forceReload, CancellationToken) : Task<IEnumerable<ProductPriceErp>>`; `IErpStockClient.StockToDateAsync(DateTime date, int warehouseId, CancellationToken) : Task<IReadOnlyList<ErpStock>>`; `TimeProvider`.
- Produces:
  - `PurchasePriceSyncCandidate { string ProductCode; MaterialProductType ProductType; int ErpItemId; decimal CurrentPurchasePrice; decimal? StockPrice }` (namespace `Anela.Heblo.Application.Features.Purchase.Contracts`)
  - `IPurchasePriceSyncSource.GetCandidatesAsync(CancellationToken) : Task<IReadOnlyList<PurchasePriceSyncCandidate>>`
  - `IPurchasePriceRecalculationService.SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken) : Task`

- [ ] **Step 1: Add the contracts**

Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchasePriceSyncCandidate.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// A material or goods item considered by the nightly purchase price sync.
/// </summary>
public sealed class PurchasePriceSyncCandidate
{
    public required string ProductCode { get; init; }
    public required MaterialProductType ProductType { get; init; }

    /// <summary>Internal Flexi ceník id (<c>idcenik</c>); always &gt; 0 for a candidate.</summary>
    public required int ErpItemId { get; init; }

    /// <summary>Current ceník <c>nakupCena</c> (excluding VAT).</summary>
    public required decimal CurrentPurchasePrice { get; init; }

    /// <summary>Today's average stock price (<c>prumCena</c>) in the item's warehouse; null when there is no stock row.</summary>
    public decimal? StockPrice { get; init; }
}
```

Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceSyncSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceSyncSource
{
    /// <summary>
    /// Every Material and Goods item that has a ceník row, with its current purchase price and
    /// today's average stock price. Throws when the ceník or stock cannot be loaded.
    /// </summary>
    Task<IReadOnlyList<PurchasePriceSyncCandidate>> GetCandidatesAsync(CancellationToken cancellationToken);
}
```

Replace `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs` with:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceRecalculationService
{
    Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken);

    Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the failing adapter tests**

In `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapterTests.cs`:
- add `using Anela.Heblo.Domain.Features.ProductPricing;`
- add a field `private readonly Mock<IErpPurchasePriceWriter> _purchasePriceWriterMock = new();`
- change `CreateAdapter()` to `new(_erpClientMock.Object, _purchasePriceWriterMock.Object);`
- add the test:

```csharp
    [Fact]
    public async Task SetPurchasePriceAsync_DelegatesToPurchasePriceWriter()
    {
        // Arrange
        var ct = CancellationToken.None;
        var adapter = CreateAdapter();

        // Act
        await adapter.SetPurchasePriceAsync(789, 0.311m, ct);

        // Assert
        _purchasePriceWriterMock.Verify(x => x.SetPurchasePriceAsync(789, 0.311m, ct), Times.Once);
    }
```

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogPurchasePriceSyncSourceAdapterTests
{
    private const int MaterialWarehouseId = 5;
    private const int ProductsWarehouseId = 4;
    private static readonly DateTime Today = new(2026, 9, 23);

    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly Mock<IProductPriceErpClient> _prices = new();
    private readonly Mock<IErpStockClient> _stock = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Today.AddHours(2), TimeSpan.Zero));

    private CatalogPurchasePriceSyncSourceAdapter CreateAdapter() =>
        new(_catalog.Object, _prices.Object, _stock.Object, _time);

    private void GivenCatalog(params (string Code, ProductType Type)[] items) =>
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.Select(i => new CatalogAggregate { ProductCode = i.Code, Type = i.Type }).ToList());

    private void GivenCenik(params ProductPriceErp[] rows) =>
        _prices.Setup(p => p.GetAllAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    private void GivenStock(int warehouseId, params (string Code, decimal Price)[] rows) =>
        _stock.Setup(s => s.StockToDateAsync(Today, warehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.Select(r => new ErpStock { ProductCode = r.Code, Price = r.Price }).ToList());

    private static ProductPriceErp Cenik(string code, int id, decimal purchasePrice) =>
        new() { ProductCode = code, ErpItemId = id, PurchasePrice = purchasePrice };

    [Fact]
    public async Task material_takes_prumCena_from_the_material_warehouse()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3.048594m));
        GivenStock(MaterialWarehouseId, ("AKL097", 0.311m));
        GivenStock(ProductsWarehouseId);

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new PurchasePriceSyncCandidate
        {
            ProductCode = "AKL097",
            ProductType = MaterialProductType.Material,
            ErpItemId = 789,
            CurrentPurchasePrice = 3.048594m,
            StockPrice = 0.311m,
        });
    }

    [Fact]
    public async Task goods_take_prumCena_from_the_products_warehouse()
    {
        // Arrange
        GivenCatalog(("ZBO001", ProductType.Goods));
        GivenCenik(Cenik("ZBO001", 12, 100m));
        GivenStock(MaterialWarehouseId);
        GivenStock(ProductsWarehouseId, ("ZBO001", 80m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        var candidate = result.Should().ContainSingle().Subject;
        candidate.ProductType.Should().Be(MaterialProductType.Goods);
        candidate.StockPrice.Should().Be(80m);
    }

    [Fact]
    public async Task material_with_stock_only_in_the_goods_warehouse_has_no_stock_price()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m));
        GivenStock(MaterialWarehouseId);
        GivenStock(ProductsWarehouseId, ("AKL097", 0.3m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.StockPrice.Should().BeNull();
    }

    [Fact]
    public async Task ignores_products_semi_products_and_sets()
    {
        // Arrange
        GivenCatalog(("DEZ001100", ProductType.Product), ("DEZ001001M", ProductType.SemiProduct), ("SET001", ProductType.Set));
        GivenCenik(Cenik("DEZ001100", 818, 214m), Cenik("DEZ001001M", 900, 2m), Cenik("SET001", 901, 50m));
        GivenStock(MaterialWarehouseId);
        GivenStock(ProductsWarehouseId, ("DEZ001100", 65m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task excludes_items_without_a_cenik_row_or_with_erp_item_id_zero()
    {
        // Arrange
        GivenCatalog(("NO-CENIK", ProductType.Material), ("ZERO-ID", ProductType.Material));
        GivenCenik(Cenik("ZERO-ID", 0, 1m));
        GivenStock(MaterialWarehouseId, ("NO-CENIK", 1m), ("ZERO-ID", 1m));
        GivenStock(ProductsWarehouseId);

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task duplicate_codes_in_cenik_or_stock_do_not_throw_and_first_row_wins()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m), Cenik("AKL097", 790, 9m));
        GivenStock(MaterialWarehouseId, ("AKL097", 0.3m), ("AKL097", 0.9m));
        GivenStock(ProductsWarehouseId);

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        var candidate = result.Should().ContainSingle().Subject;
        candidate.ErpItemId.Should().Be(789);
        candidate.StockPrice.Should().Be(0.3m);
    }

    [Fact]
    public async Task propagates_a_stock_load_failure()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m));
        _stock.Setup(s => s.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("flexi down"));

        // Act
        var act = () => CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: build FAILS — `CatalogPurchasePriceSyncSourceAdapter` missing; `CatalogPurchasePriceRecalculationAdapter` has no 2-arg constructor / `SetPurchasePriceAsync`.

- [ ] **Step 4: Implement the recalculation adapter change**

Replace `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs` with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogPurchasePriceRecalculationAdapter : IPurchasePriceRecalculationService
{
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IErpPurchasePriceWriter _purchasePriceWriter;

    public CatalogPurchasePriceRecalculationAdapter(
        IProductPriceErpClient productPriceErpClient,
        IErpPurchasePriceWriter purchasePriceWriter)
    {
        _productPriceErpClient = productPriceErpClient;
        _purchasePriceWriter = purchasePriceWriter;
    }

    public Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken) =>
        _productPriceErpClient.RecalculatePurchasePrice(bomId, cancellationToken);

    public Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken cancellationToken) =>
        _purchasePriceWriter.SetPurchasePriceAsync(erpItemId, purchasePrice, cancellationToken);
}
```

- [ ] **Step 5: Implement the sync source adapter**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Builds the nightly purchase price sync candidates: Material and Goods catalog items that
/// have a ceník row, with their current <c>nakupCena</c> (fresh ceník read) and today's
/// <c>prumCena</c> from the item's own warehouse.
/// </summary>
internal sealed class CatalogPurchasePriceSyncSourceAdapter : IPurchasePriceSyncSource
{
    // Flexi warehouse ids — same values as FlexiStockClient / FinancialOverviewStockValueAdapter.
    private const int MaterialWarehouseId = 5; // MATERIAL
    private const int ProductsWarehouseId = 4; // ZBOZI (products and goods)

    private readonly ICatalogRepository _catalogRepository;
    private readonly IProductPriceErpClient _priceClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;

    public CatalogPurchasePriceSyncSourceAdapter(
        ICatalogRepository catalogRepository,
        IProductPriceErpClient priceClient,
        IErpStockClient stockClient,
        TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _priceClient = priceClient;
        _stockClient = stockClient;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PurchasePriceSyncCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        var items = (await _catalogRepository.GetAllAsync(cancellationToken))
            .Where(a => a.Type is ProductType.Material or ProductType.Goods)
            .ToList();

        var prices = FirstByCode(await _priceClient.GetAllAsync(forceReload: true, cancellationToken), p => p.ProductCode);

        var today = _timeProvider.GetUtcNow().Date;
        var materialStock = FirstByCode(
            await _stockClient.StockToDateAsync(today, MaterialWarehouseId, cancellationToken), s => s.ProductCode);
        var goodsStock = FirstByCode(
            await _stockClient.StockToDateAsync(today, ProductsWarehouseId, cancellationToken), s => s.ProductCode);

        var candidates = new List<PurchasePriceSyncCandidate>(items.Count);
        foreach (var item in items)
        {
            if (!prices.TryGetValue(item.ProductCode, out var price) || price.ErpItemId <= 0)
                continue;

            var isMaterial = item.Type == ProductType.Material;
            var stock = isMaterial ? materialStock : goodsStock;

            candidates.Add(new PurchasePriceSyncCandidate
            {
                ProductCode = item.ProductCode,
                ProductType = isMaterial ? MaterialProductType.Material : MaterialProductType.Goods,
                ErpItemId = price.ErpItemId,
                CurrentPurchasePrice = price.PurchasePrice,
                StockPrice = stock.TryGetValue(item.ProductCode, out var stockRow) ? stockRow.Price : null,
            });
        }

        return candidates;
    }

    private static Dictionary<string, T> FirstByCode<T>(IEnumerable<T> rows, Func<T, string> code) =>
        rows.Where(r => !string.IsNullOrEmpty(code(r)))
            .GroupBy(code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}
```

- [ ] **Step 6: Register the source**

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, after
`services.AddScoped<IPurchasePriceRecalculationService, CatalogPurchasePriceRecalculationAdapter>();` add:

```csharp
        services.AddScoped<IPurchasePriceSyncSource, CatalogPurchasePriceSyncSourceAdapter>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~CatalogPurchasePriceSyncSourceAdapterTests|FullyQualifiedName~CatalogPurchasePriceRecalculationAdapterTests"`
Expected: all PASS. (The handler still compiles: it does not call the new method yet.)

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/ \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapter.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
git commit -m "feat: purchase price sync candidates from catalog, ceník and stock

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Mark semi-product BoMs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs:56-69`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapterTests.cs`

**Interfaces:**
- Produces: `MaterialBomReference.IsSemiProduct : bool` (init, default `false`).

- [ ] **Step 1: Write the failing test**

Append to `PurchaseMaterialCatalogAdapterTests` (the existing `MakeMaterial` helper accepts `type`, `hasBoM`, `bomId`):

```csharp
    [Fact]
    public async Task GetMaterialsWithBomAsync_marks_semi_product_boms()
    {
        // Arrange
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("DEZ001001M", "Semi", type: ProductType.SemiProduct, hasBoM: true, bomId: 8),
                MakeMaterial("DEZ001100", "Product", type: ProductType.Product, hasBoM: true, bomId: 9),
            });

        // Act
        var result = await CreateAdapter().GetMaterialsWithBomAsync(ct);

        // Assert
        result.Single(r => r.ProductCode == "DEZ001001M").IsSemiProduct.Should().BeTrue();
        result.Single(r => r.ProductCode == "DEZ001100").IsSemiProduct.Should().BeFalse();
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: build FAILS — `MaterialBomReference` has no `IsSemiProduct`.

- [ ] **Step 3: Implement**

Replace `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs` with:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialBomReference
{
    public required string ProductCode { get; init; }
    public required int BoMId { get; init; }

    /// <summary>
    /// True when the BoM owner is a semi-product. Semi-product BoMs are recalculated before
    /// product BoMs, because Flexi's roll-up reads each component's stored purchase price.
    /// </summary>
    public bool IsSemiProduct { get; init; }
}
```

In `PurchaseMaterialCatalogAdapter.GetMaterialsWithBomAsync`, change the projection to:

```csharp
            .Select(item => new MaterialBomReference
            {
                ProductCode = item.ProductCode,
                BoMId = item.BoMId!.Value,
                IsSemiProduct = item.Type == ProductType.SemiProduct,
            })
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~PurchaseMaterialCatalogAdapterTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapterTests.cs
git commit -m "feat: flag semi-product BoMs for ordered recalculation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Three-phase handler, response and job telemetry

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/Jobs/PurchasePriceRecalculationJob.cs:55-65`
- Test: `backend/test/Anela.Heblo.Tests/Application/Purchase/RecalculatePurchasePriceHandlerTests.cs`

**Interfaces:**
- Consumes: `IPurchasePriceSyncSource.GetCandidatesAsync` and `PurchasePriceSyncCandidate` (Task 2); `IPurchasePriceRecalculationService.SetPurchasePriceAsync` (Task 2); `MaterialBomReference.IsSemiProduct` (Task 3).
- Produces: handler constructor `(IMaterialCatalogService, IPurchasePriceRecalculationService, IPurchasePriceSyncSource, ILogger<RecalculatePurchasePriceHandler>)`; `RecalculatePurchasePriceHandler.PurchasePriceTolerance = 0.0001m` (internal const); response properties `PriceSync : PurchasePriceSyncSummary`, `SemiProducts : BomRecalculationSummary`, `Products : BomRecalculationSummary`.

- [ ] **Step 1: Add response summaries**

In `RecalculatePurchasePriceResponse.cs`, add these properties to `RecalculatePurchasePriceResponse` (after `ProcessedProducts`), leaving `SuccessCount`/`FailedCount`/`TotalCount`/`IsSuccess`/`Message` unchanged (they keep counting BoM recalculations across phases 2 and 3):

```csharp
    /// <summary>
    /// Phase 1 of a RecalculateAll run: materials and goods synced to their average stock price.
    /// </summary>
    public PurchasePriceSyncSummary PriceSync { get; set; } = new();

    /// <summary>Phase 2 of a RecalculateAll run: semi-product BoM recalculations.</summary>
    public BomRecalculationSummary SemiProducts { get; set; } = new();

    /// <summary>Phase 3 of a RecalculateAll run: product and set BoM recalculations.</summary>
    public BomRecalculationSummary Products { get; set; } = new();
```

and append to the file:

```csharp
public class PurchasePriceSyncSummary
{
    public int Candidates { get; set; }
    public int Written { get; set; }
    public int Unchanged { get; set; }
    public int SkippedNoStockPrice { get; set; }
    public int Failed { get; set; }
}

public class BomRecalculationSummary
{
    public int Succeeded { get; set; }
    public int Failed { get; set; }
}
```

- [ ] **Step 2: Update the test fixture and write the failing tests**

In `RecalculatePurchasePriceHandlerTests`:
- add a field `private readonly Mock<IPurchasePriceSyncSource> _priceSyncSourceMock;`
- in the constructor, before creating the handler:

```csharp
        _priceSyncSourceMock = new Mock<IPurchasePriceSyncSource>();
        _priceSyncSourceMock
            .Setup(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PurchasePriceSyncCandidate>());
```

- construct the handler as:

```csharp
        _handler = new RecalculatePurchasePriceHandler(
            _materialCatalogMock.Object,
            _priceRecalculationServiceMock.Object,
            _priceSyncSourceMock.Object,
            _loggerMock.Object);
```

Then add these helpers and tests to the class:

```csharp
    private static PurchasePriceSyncCandidate Candidate(
        string code, int erpItemId, decimal current, decimal? stock,
        MaterialProductType type = MaterialProductType.Material) => new()
    {
        ProductCode = code,
        ProductType = type,
        ErpItemId = erpItemId,
        CurrentPurchasePrice = current,
        StockPrice = stock,
    };

    private void GivenCandidates(params PurchasePriceSyncCandidate[] candidates) =>
        _priceSyncSourceMock
            .Setup(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

    private void GivenBoms(params MaterialBomReference[] boms) =>
        _materialCatalogMock
            .Setup(x => x.GetMaterialsWithBomAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(boms);

    private static RecalculatePurchasePriceRequest RecalculateAll() => new() { RecalculateAll = true };

    [Fact]
    public async Task RecalculateAll_writes_stock_price_for_materials_and_goods_that_differ()
    {
        // Arrange
        GivenCandidates(
            Candidate("AKL097", 789, 3.048594m, 0.311m),
            Candidate("ZBO001", 12, 100m, 80m, MaterialProductType.Goods));
        GivenBoms();

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _priceRecalculationServiceMock.Verify(x => x.SetPurchasePriceAsync(789, 0.311m, It.IsAny<CancellationToken>()), Times.Once);
        _priceRecalculationServiceMock.Verify(x => x.SetPurchasePriceAsync(12, 80m, It.IsAny<CancellationToken>()), Times.Once);
        result.PriceSync.Candidates.Should().Be(2);
        result.PriceSync.Written.Should().Be(2);
    }

    [Fact]
    public async Task RecalculateAll_skips_prices_within_tolerance()
    {
        // Arrange
        GivenCandidates(Candidate("AKL097", 789, 0.31100m, 0.31105m));
        GivenBoms();

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _priceRecalculationServiceMock.Verify(
            x => x.SetPurchasePriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        result.PriceSync.Unchanged.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecalculateAll_skips_items_without_a_usable_stock_price(int? stockPrice)
    {
        // Arrange
        GivenCandidates(Candidate("AKL097", 789, 3m, stockPrice));
        GivenBoms();

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        _priceRecalculationServiceMock.Verify(
            x => x.SetPurchasePriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        result.PriceSync.SkippedNoStockPrice.Should().Be(1);
    }

    [Fact]
    public async Task RecalculateAll_runs_writes_then_semi_products_then_products()
    {
        // Arrange
        var calls = new List<string>();
        GivenCandidates(Candidate("AKL097", 789, 3m, 0.3m));
        GivenBoms(
            new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 },
            new MaterialBomReference { ProductCode = "DEZ001001M", BoMId = 5700, IsSemiProduct = true });
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback<int, decimal, CancellationToken>((id, _, _) => calls.Add($"write:{id}"))
            .Returns(Task.CompletedTask);
        _priceRecalculationServiceMock
            .Setup(x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((bomId, _) => calls.Add($"bom:{bomId}"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        calls.Should().Equal("write:789", "bom:5700", "bom:5654");
        result.SemiProducts.Succeeded.Should().Be(1);
        result.Products.Succeeded.Should().Be(1);
        result.SuccessCount.Should().Be(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task RecalculateAll_does_not_recalculate_boms_when_candidates_cannot_be_loaded()
    {
        // Arrange
        _priceSyncSourceMock
            .Setup(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("flexi down"));
        GivenBoms(new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 });

        // Act
        var act = () => _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _priceRecalculationServiceMock.Verify(
            x => x.RecalculatePurchasePriceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecalculateAll_continues_after_a_failed_write()
    {
        // Arrange
        GivenCandidates(Candidate("BAD", 1, 3m, 0.3m), Candidate("GOOD", 2, 3m, 0.3m));
        GivenBoms(new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 });
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(1, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rejected"));

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        result.PriceSync.Failed.Should().Be(1);
        result.PriceSync.Written.Should().Be(1);
        _priceRecalculationServiceMock.Verify(x => x.RecalculatePurchasePriceAsync(5654, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculateAll_runs_products_even_when_a_semi_product_fails()
    {
        // Arrange
        GivenBoms(
            new MaterialBomReference { ProductCode = "DEZ001001M", BoMId = 5700, IsSemiProduct = true },
            new MaterialBomReference { ProductCode = "DEZ001100", BoMId = 5654 });
        _priceRecalculationServiceMock
            .Setup(x => x.RecalculatePurchasePriceAsync(5700, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var result = await _handler.Handle(RecalculateAll(), CancellationToken.None);

        // Assert
        result.SemiProducts.Failed.Should().Be(1);
        result.Products.Succeeded.Should().Be(1);
        result.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task RecalculateAll_propagates_cancellation_during_price_sync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        GivenCandidates(Candidate("A", 1, 3m, 0.3m), Candidate("B", 2, 3m, 0.3m));
        GivenBoms();
        _priceRecalculationServiceMock
            .Setup(x => x.SetPurchasePriceAsync(1, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => _handler.Handle(RecalculateAll(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _priceRecalculationServiceMock.Verify(
            x => x.SetPurchasePriceAsync(2, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SingleProduct_does_not_sync_prices()
    {
        // Arrange
        _materialCatalogMock.Setup(x => x.GetByIdAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMaterialWithBoM("PROD001", 123));

        // Act
        await _handler.Handle(new RecalculatePurchasePriceRequest { ProductCode = "PROD001" }, CancellationToken.None);

        // Assert
        _priceSyncSourceMock.Verify(x => x.GetCandidatesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: build FAILS — the handler has no 4-arg constructor.

- [ ] **Step 4: Implement the handler**

Replace `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;

/// <summary>
/// Single product: recalculates that item's BoM purchase price in Flexi.
///
/// RecalculateAll (nightly job) runs three phases in strict order:
/// 1. materials and goods: ceník nakupCena := average stock price (prumCena);
/// 2. semi-product BoMs; 3. product and set BoMs.
/// Flexi's roll-up (prepocti-nakupni-cenu) sums each component's STORED nakupCena, so each
/// level must be current before the level above it is recalculated.
/// </summary>
public class RecalculatePurchasePriceHandler : IRequestHandler<RecalculatePurchasePriceRequest, RecalculatePurchasePriceResponse>
{
    /// <summary>Differences below this are rounding noise, not a price change worth writing to the ERP.</summary>
    internal const decimal PurchasePriceTolerance = 0.0001m;

    private const int MaxLoggedSkippedCodes = 10;

    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IPurchasePriceRecalculationService _priceRecalculationService;
    private readonly IPurchasePriceSyncSource _priceSyncSource;
    private readonly ILogger<RecalculatePurchasePriceHandler> _logger;

    public RecalculatePurchasePriceHandler(
        IMaterialCatalogService materialCatalog,
        IPurchasePriceRecalculationService priceRecalculationService,
        IPurchasePriceSyncSource priceSyncSource,
        ILogger<RecalculatePurchasePriceHandler> logger)
    {
        _materialCatalog = materialCatalog;
        _priceRecalculationService = priceRecalculationService;
        _priceSyncSource = priceSyncSource;
        _logger = logger;
    }

    public async Task<RecalculatePurchasePriceResponse> Handle(RecalculatePurchasePriceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting price recalculation - ProductCode: {ProductCode}, RecalculateAll: {RecalculateAll}",
            request.ProductCode, request.RecalculateAll);

        if (string.IsNullOrEmpty(request.ProductCode) && !request.RecalculateAll)
        {
            _logger.LogWarning("Invalid request: Either ProductCode must be specified or RecalculateAll must be true");
            return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "Message", "Either ProductCode must be specified or RecalculateAll must be true" } });
        }

        return request.RecalculateAll
            ? await RecalculateAllAsync(cancellationToken)
            : await RecalculateSingleAsync(request.ProductCode!, cancellationToken);
    }

    private async Task<RecalculatePurchasePriceResponse> RecalculateSingleAsync(string productCode, CancellationToken cancellationToken)
    {
        var product = await _materialCatalog.GetByIdAsync(productCode, cancellationToken);
        if (product == null)
        {
            _logger.LogWarning("Product with code '{ProductCode}' not found", productCode);
            return new RecalculatePurchasePriceResponse(ErrorCodes.CatalogItemNotFound, new Dictionary<string, string> { { "ProductCode", productCode } });
        }

        if (!product.HasBoM || !product.BoMId.HasValue)
        {
            _logger.LogWarning("Product '{ProductCode}' does not have BoM", productCode);
            return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "ProductCode", productCode }, { "Message", $"Product {productCode} does not have BoM" } });
        }

        var response = new RecalculatePurchasePriceResponse { TotalCount = 1 };
        await RecalculateBomsAsync(
            new[] { new MaterialBomReference { ProductCode = product.ProductCode, BoMId = product.BoMId.Value } },
            response, cancellationToken);

        LogCompletion(response);
        return response;
    }

    private async Task<RecalculatePurchasePriceResponse> RecalculateAllAsync(CancellationToken cancellationToken)
    {
        var response = new RecalculatePurchasePriceResponse();

        // Phase 1. A load failure propagates: no BoM is recalculated on partial data.
        response.PriceSync = await SyncStockPricesAsync(cancellationToken);

        var boms = await _materialCatalog.GetMaterialsWithBomAsync(cancellationToken);
        response.TotalCount = boms.Count;

        response.SemiProducts = await RecalculateBomsAsync(boms.Where(b => b.IsSemiProduct).ToList(), response, cancellationToken);
        _logger.LogInformation("Purchase price phase 2 (semi-products): {Succeeded} succeeded, {Failed} failed",
            response.SemiProducts.Succeeded, response.SemiProducts.Failed);

        response.Products = await RecalculateBomsAsync(boms.Where(b => !b.IsSemiProduct).ToList(), response, cancellationToken);
        _logger.LogInformation("Purchase price phase 3 (products and sets): {Succeeded} succeeded, {Failed} failed",
            response.Products.Succeeded, response.Products.Failed);

        LogCompletion(response);
        return response;
    }

    private async Task<PurchasePriceSyncSummary> SyncStockPricesAsync(CancellationToken cancellationToken)
    {
        var candidates = await _priceSyncSource.GetCandidatesAsync(cancellationToken);
        var summary = new PurchasePriceSyncSummary { Candidates = candidates.Count };
        var skippedCodes = new List<string>();

        foreach (var candidate in candidates)
        {
            if (candidate.StockPrice is not > 0m)
            {
                summary.SkippedNoStockPrice++;
                skippedCodes.Add(candidate.ProductCode);
                continue;
            }

            var stockPrice = candidate.StockPrice.Value;
            if (Math.Abs(stockPrice - candidate.CurrentPurchasePrice) < PurchasePriceTolerance)
            {
                summary.Unchanged++;
                continue;
            }

            await WritePurchasePriceAsync(candidate, stockPrice, summary, cancellationToken);
        }

        if (skippedCodes.Count > 0)
        {
            _logger.LogWarning(
                "Purchase price sync skipped {Count} items without a usable stock price (e.g. {ProductCodes})",
                skippedCodes.Count, string.Join(", ", skippedCodes.Take(MaxLoggedSkippedCodes)));
        }

        _logger.LogInformation(
            "Purchase price phase 1 (materials and goods): {Candidates} candidates, {Written} written, {Unchanged} unchanged, {Skipped} skipped, {Failed} failed",
            summary.Candidates, summary.Written, summary.Unchanged, summary.SkippedNoStockPrice, summary.Failed);

        return summary;
    }

    private async Task WritePurchasePriceAsync(
        PurchasePriceSyncCandidate candidate, decimal stockPrice, PurchasePriceSyncSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            await _priceRecalculationService.SetPurchasePriceAsync(candidate.ErpItemId, stockPrice, cancellationToken);
            summary.Written++;
            _logger.LogInformation(
                "Purchase price sync: {ProductCode} ({ProductType}) nakupCena {OldPrice} -> {NewPrice} (prumCena)",
                candidate.ProductCode, candidate.ProductType, candidate.CurrentPurchasePrice, stockPrice);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            summary.Failed++;
            _logger.LogError(ex, "Purchase price sync failed for {ProductCode} (ceník {ErpItemId})",
                candidate.ProductCode, candidate.ErpItemId);
        }
    }

    private async Task<BomRecalculationSummary> RecalculateBomsAsync(
        IReadOnlyList<MaterialBomReference> boms, RecalculatePurchasePriceResponse response, CancellationToken cancellationToken)
    {
        var summary = new BomRecalculationSummary();

        foreach (var bom in boms)
        {
            try
            {
                await _priceRecalculationService.RecalculatePurchasePriceAsync(bom.BoMId, cancellationToken);
                response.ProcessedProducts.Add(new ProductRecalculationResult { ProductCode = bom.ProductCode, Success = true });
                response.SuccessCount++;
                summary.Succeeded++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = bom.ProductCode,
                    Success = false,
                    ErrorCode = ErrorCodes.Exception,
                    Params = new Dictionary<string, string>
                    {
                        { "message", ex.Message },
                        { "exceptionType", ex.GetType().Name }
                    }
                });
                response.FailedCount++;
                summary.Failed++;
                _logger.LogError(ex, "Failed to recalculate price for product {ProductCode}: {ErrorMessage}",
                    bom.ProductCode, ex.Message);
            }
        }

        return summary;
    }

    private void LogCompletion(RecalculatePurchasePriceResponse response) =>
        _logger.LogInformation("Price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
            response.SuccessCount, response.FailedCount, response.TotalCount);
}
```

Note: the old per-BoM `catch (Exception)` had no filter; the new `when (!cancellationToken.IsCancellationRequested)` makes a cancelled run stop instead of recording every remaining BoM as failed. Check the existing tests in this file still pass — none of them cancels.

- [ ] **Step 5: Add phase-1 counts to the job's log and telemetry**

In `PurchasePriceRecalculationJob.cs`, replace the completion log and the telemetry dictionary with:

```csharp
            _logger.LogInformation("{JobName} completed - Prices written: {Written}, BoMs success: {SuccessCount}, failed: {FailedCount}, total: {TotalCount}",
                Metadata.JobName, response.PriceSync.Written, response.SuccessCount, response.FailedCount, response.TotalCount);

            _telemetryService.TrackBusinessEvent("PurchasePriceRecalculation", new Dictionary<string, string>
            {
                ["Status"] = "Success",
                ["PriceSyncWritten"] = response.PriceSync.Written.ToString(),
                ["PriceSyncSkippedNoStockPrice"] = response.PriceSync.SkippedNoStockPrice.ToString(),
                ["PriceSyncFailed"] = response.PriceSync.Failed.ToString(),
                ["SuccessCount"] = response.SuccessCount.ToString(),
                ["FailedCount"] = response.FailedCount.ToString(),
                ["TotalCount"] = response.TotalCount.ToString(),
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
```

- [ ] **Step 6: Run the handler tests (new and existing)**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~RecalculatePurchasePrice|FullyQualifiedName~PurchasePriceRecalculationJob"`
Expected: all PASS, including the 9 pre-existing handler tests.

- [ ] **Step 7: Full backend gate**

Run from repo root:
```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln --verify-no-changes || dotnet format Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "Category!=Integration"
```
Expected: build succeeds (the OpenAPI TS client regenerates — the response gained properties, additive only); all non-integration tests PASS.

Then the frontend build, because the generated client changed:
```bash
cd frontend && CI=false npm run build && npm run lint
```
Expected: success.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/ \
        backend/test/Anela.Heblo.Tests/Application/Purchase/RecalculatePurchasePriceHandlerTests.cs \
        frontend/src/api/generated/
git commit -m "feat: nightly purchase price sync - stock prices, then semi-products, then products

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Docs and the pre-deploy report script

**Files:**
- Modify: `docs/superpowers/specs/2026-09-03-central-price-management-design.md` (assumption A2, lines 87-90)
- Create: `scripts/flexi-purchase-price-report.py`

**Interfaces:** none (docs and an ops script).

- [ ] **Step 1: Revise A2**

In `docs/superpowers/specs/2026-09-03-central-price-management-design.md`, replace the A2 bullet with:

```markdown
- **A2 — Heblo writes purchase price only for materials and goods, only from the stock
  valuation.** The nightly `purchase-price-recalculation` job sets `cenaNakup` =
  `prumCena` for `Material` and `Goods`, then triggers Flexi's BoM roll-up for
  semi-products and then products (see `2026-09-23-purchase-price-sync-design.md`).
  Shoptet's `buyPrice` is never written.
```

- [ ] **Step 2: Write the read-only report script**

Create `scripts/flexi-purchase-price-report.py`:

```python
#!/usr/bin/env python3
"""Read-only report: Flexi ceník nakupCena vs average stock price (prumCena).

Lists every material (warehouse 5) and goods item (warehouse 4) with what the nightly
purchase price sync would do to it: write / unchanged / skip-no-stock-price.
Only GET requests are made.

Usage:
  FLEXI_SERVER=https://petra-tesarikova.flexibee.eu \
  FLEXI_COMPANY=$(az keyvault secret show --vault-name kv-heblo-prod -n FlexiBeeSettings--Company --query value -o tsv) \
  FLEXI_LOGIN=$(az keyvault secret show --vault-name kv-heblo-prod -n FlexiBeeSettings--Login --query value -o tsv) \
  FLEXI_PASSWORD=$(az keyvault secret show --vault-name kv-heblo-prod -n FlexiBeeSettings--Password --query value -o tsv) \
  scripts/flexi-purchase-price-report.py > purchase-price-report.csv
"""
import base64
import csv
import datetime
import json
import math
import os
import sys
import urllib.parse
import urllib.request

TOLERANCE = 0.0001  # same as RecalculatePurchasePriceHandler.PurchasePriceTolerance
WAREHOUSE_BY_TYPE = {"typZasoby.material": 5, "typZasoby.zbozi": 4}

SERVER = os.environ["FLEXI_SERVER"].rstrip("/")
COMPANY = os.environ["FLEXI_COMPANY"]
AUTH = base64.b64encode(f"{os.environ['FLEXI_LOGIN']}:{os.environ['FLEXI_PASSWORD']}".encode()).decode()


def get(resource, params):
    url = f"{SERVER}/c/{COMPANY}/{resource}.json?{urllib.parse.urlencode(params)}"
    request = urllib.request.Request(url, headers={"Authorization": f"Basic {AUTH}"})
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.load(response)["winstrom"][resource]


def stock_prices(warehouse_id, date):
    rows = get("stav-skladu-k-datu", {
        "datum": date,
        "sklad": warehouse_id,
        "detail": "custom:cenik(kod),prumCena,stavMJ",
        "includes": "/stav-skladu-k-datu/cenik",
        "limit": 0,
    })
    prices = {}
    for row in rows:
        cenik = row.get("cenik") or []
        code = (cenik[0].get("kod") if isinstance(cenik, list) and cenik else "") or ""
        code = code.strip()
        if code and code not in prices:
            prices[code] = (float(row.get("prumCena") or 0), float(row.get("stavMJ") or 0))
    return prices


def main():
    today = datetime.date.today().isoformat()
    cenik = get("cenik", {"detail": "custom:id,kod,nazev,nakupCena,typZasobyK,mj1", "limit": 0})
    stock = {wh: stock_prices(wh, today) for wh in set(WAREHOUSE_BY_TYPE.values())}

    rows = []
    for item in cenik:
        warehouse = WAREHOUSE_BY_TYPE.get(item.get("typZasobyK"))
        if warehouse is None:
            continue
        code = item["kod"].strip()
        current = float(item.get("nakupCena") or 0)
        prum, qty = stock[warehouse].get(code, (None, None))
        if prum is None or prum <= 0:
            action, ratio = "skip-no-stock-price", ""
        elif abs(prum - current) < TOLERANCE:
            action, ratio = "unchanged", 1.0
        else:
            action = "write"
            ratio = round(current / prum, 3) if prum else ""
        rows.append({
            "kod": code, "nazev": item.get("nazev", ""), "typ": item.get("typZasobyK"),
            "mj": item.get("mj1@showAs", item.get("mj1", "")), "nakupCena": current,
            "prumCena": prum if prum is not None else "", "stavMJ": qty if qty is not None else "",
            "nakupCena/prumCena": ratio, "action": action,
        })

    def distance(row):
        ratio = row["nakupCena/prumCena"]
        return abs(math.log(ratio)) if isinstance(ratio, float) and ratio > 0 else -1

    rows.sort(key=distance, reverse=True)
    writer = csv.DictWriter(sys.stdout, fieldnames=list(rows[0].keys()) if rows else ["kod"])
    writer.writeheader()
    writer.writerows(rows)
    print(f"{len(rows)} items, {sum(r['action'] == 'write' for r in rows)} would be written", file=sys.stderr)


if __name__ == "__main__":
    main()
```

Run: `chmod +x scripts/flexi-purchase-price-report.py && python3 -m py_compile scripts/flexi-purchase-price-report.py`
Expected: no output (compiles). Do NOT run it against Flexi in this task — that is Task 6 Step 1.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-09-03-central-price-management-design.md scripts/flexi-purchase-price-report.py
git commit -m "docs: revise A2 for purchase price sync; add read-only nakupCena vs prumCena report

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Live verification and findings (owner-gated)

**STOP before each live step and get the owner's explicit OK. Every call hits the live Flexi company.**

**Files:**
- Create: `docs/integrations/flexi-api.md`

**Interfaces:** none. Uses the `FLEXI_*` environment variables from Task 5 and `curl`.

- [ ] **Step 1: Run the read-only report (GET only) — owner OK required**

```bash
scripts/flexi-purchase-price-report.py > /tmp/purchase-price-report.csv
```

Check first that the `stav-skladu-k-datu` rows carry `cenik[0].kod` and `prumCena` (if the script prints `0 would be written`, inspect one raw row with `curl -s -u "$FLEXI_LOGIN:$FLEXI_PASSWORD" "$FLEXI_SERVER/c/$FLEXI_COMPANY/stav-skladu-k-datu.json?datum=$(date +%F)&sklad=5&limit=1&detail=custom:cenik(kod),prumCena&includes=/stav-skladu-k-datu/cenik"` and adjust the parsing). Expected: `AKL097` near the top with `nakupCena/prumCena ≈ 9.8` and action `write`. Hand the CSV to the owner.

- [ ] **Step 2: Verify `PUT cenik { nakupCena }` on AKL097 — owner OK required**

AKL097 is ceník id `789` (verified 2026-09-23). Take its `prumCena` from the report, then:

```bash
curl -s -u "$FLEXI_LOGIN:$FLEXI_PASSWORD" -X PUT -H "Content-Type: application/json" \
  "$FLEXI_SERVER/c/$FLEXI_COMPANY/cenik/789.json" \
  -d '{"winstrom":{"cenik":{"nakupCena":"<prumCena from report>"}}}'
curl -s -u "$FLEXI_LOGIN:$FLEXI_PASSWORD" \
  "$FLEXI_SERVER/c/$FLEXI_COMPANY/cenik/789.json?detail=custom:kod,nakupCena,mj1"
```

Expected: `"success":"true"` and the read-back `nakupCena` equals the value sent (per gram, `mj1 = G`). This is the intended correction for AKL097 anyway.

Check the stored precision: if Flexi rounds `nakupCena` to fewer than 4 decimals, the 0.0001
tolerance will re-write per-gram materials every night — record it and raise
`PurchasePriceTolerance` accordingly.

- [ ] **Step 3: Verify whether `prepocti-nakupni-cenu` recurses — owner OK required**

DEZ001100's BoM root row is kusovník id `5654`; its semi-product is `DEZ001001M`. Record `nakupCena` of both (GET `cenik/(kod='DEZ001100' or kod='DEZ001001M').json?detail=custom:kod,nakupCena`), then recalculate DEZ001100 only:

```bash
curl -s -u "$FLEXI_LOGIN:$FLEXI_PASSWORD" -X PUT -H "Content-Type: application/json" \
  "$FLEXI_SERVER/c/$FLEXI_COMPANY/kusovnik.json" \
  -d '{"winstrom":{"kusovnik":{"id":5654,"@action":"prepocti-nakupni-cenu"}}}'
```

Read both `nakupCena` values again.
- DEZ001001M unchanged and DEZ001100 still ≈ 214 → roll-up reads the semi-product's **stored** price (expected; the phase 2 → 3 order is required).
- DEZ001001M changed, or DEZ001100 dropped to ≈ 65 → roll-up recurses; ordering is harmless but not required. Record it.

Then recalculate DEZ001001M's own BoM (find its root id: GET `kusovnik/(cenik='code:DEZ001001M' and hladina=1).json?detail=custom:id`), then DEZ001100 again, and confirm DEZ001100 ends near its manufacture cost.

- [ ] **Step 4: Check for semi-products nested in semi-products (GET only)**

```bash
curl -s -u "$FLEXI_LOGIN:$FLEXI_PASSWORD" \
  "$FLEXI_SERVER/c/$FLEXI_COMPANY/kusovnik.json?limit=0&detail=custom:id,cenik,otecCenik,hladina" > /tmp/kusovnik.json
curl -s -u "$FLEXI_LOGIN:$FLEXI_PASSWORD" \
  "$FLEXI_SERVER/c/$FLEXI_COMPANY/cenik/(typZasobyK='typZasoby.polotovar').json?limit=0&detail=custom:kod" > /tmp/semi.json
python3 - <<'EOF'
import json
semi = {r["kod"].strip() for r in json.load(open("/tmp/semi.json"))["winstrom"]["cenik"]}
code = lambda ref: (ref or "").replace("code:", "").strip()
rows = json.load(open("/tmp/kusovnik.json"))["winstrom"]["kusovnik"]
nested = [(code(r.get("otecCenik")), code(r.get("cenik"))) for r in rows
          if code(r.get("cenik")) in semi and code(r.get("otecCenik")) in semi and code(r.get("cenik")) != code(r.get("otecCenik"))]
print(len(nested), "nested semi-product rows"); print(nested[:20])
EOF
```

Expected: `0 nested semi-product rows`. If any exist, STOP: phase 2 needs dependency ordering, which is a spec change — report the list to the owner before merging.

- [ ] **Step 5: Check one SET kusovník (GET only)**

Check one SET kusovník (GET `kusovnik` rows for a BAL*/SET* item) and confirm its components
are Products; record it.

- [ ] **Step 6: Record findings**

Create `docs/integrations/flexi-api.md`:

```markdown
# ABRA Flexi API findings

No sandbox — every call hits the live company. Record verified behaviour here before relying on it.

## Ceník purchase price (`cenik.nakupCena`)

- Verified <date>: `PUT /c/{company}/cenik/{id}.json` with `{"winstrom":{"cenik":{"nakupCena":"<decimal>"}}}`
  updates the purchase price, excluding VAT, in the item's primary unit (`mj1`). Address by numeric id only —
  a PUT by `code:` creates a new item.
- Written nightly for materials and goods by `purchase-price-recalculation` (see
  `docs/superpowers/specs/2026-09-23-purchase-price-sync-design.md`).

## BoM purchase price roll-up (`prepocti-nakupni-cenu`)

- `PUT /c/{company}/kusovnik.json` with `{"winstrom":{"kusovnik":{"id":<root row id>,"@action":"prepocti-nakupni-cenu"}}}`.
- Sums components' **ceník `nakupCena`** — not the stock valuation.
- Recursion into sub-BoMs: <recurses | reads the stored semi-product price>, verified <date> on DEZ001100 / DEZ001001M.
- Semi-products nested in semi-products: <count> found on <date>.
- Sets contain: <products | …>, verified <date>.

## Stock to date (`stav-skladu-k-datu`)

- `prumCena` is the average stock price per `mj1` for the requested `sklad` and `datum`.
  Warehouses: 5 = materials, 20 = semi-products, 4 = products and goods.
```

Fill each `<…>` with the result of Steps 2–5 before committing.

- [ ] **Step 7: Commit**

```bash
git add docs/integrations/flexi-api.md
git commit -m "docs: record verified Flexi purchase price and BoM roll-up behaviour

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Rollout (after merge)

1. The owner has the Task 6 report; the first nightly run (02:00) will perform those writes.
2. Next morning: App Insights `PurchasePriceRecalculation` event (`PriceSyncWritten`, `PriceSyncFailed`, `FailedCount`) and the job's phase log lines.
3. Spot-check in Flexi and Heblo: `AKL097.nakupCena ≈ prumCena`; `DEZ001100.nakupCena` near its manufacture cost (≈ 65).
