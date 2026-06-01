# Manufacture Template Performance Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `GET /api/manufacture-batch/template/{productCode}` p95 well under the 10 s App Insights threshold (target p50 < 2 s cold, < 200 ms warm; p95 < 5 s) by parallelising the three FlexiBee stock-snapshot calls inside `FlexiManufactureTemplateService`, projecting the snapshots to a `HasLots` dictionary, and wrapping the whole template fetch in a 5-minute in-memory cache.

**Architecture:** All changes live inside the `Anela.Heblo.Adapters.Flexi` assembly. A new `internal IManufactureTemplateCache` (singleton, backed by the already-registered `IMemoryCache`) wraps `FlexiManufactureTemplateService.GetManufactureTemplateAsync`. The cache returns a defensive deep clone on hit (handler-level `template.BatchSize` mutation in `CalculatedBatchSizeHandler` must not corrupt the cached copy), skips writes for `null`/exception fetches, and uses absolute expiration only. The three `IErpStockClient.StockToDateAsync` calls (Material, SemiProducts, Products warehouses) are dispatched concurrently via `Task.WhenAll` with the caller's `CancellationToken`, then projected to a single `Dictionary<string, bool>` keyed by product code. Existing `ITelemetryService` (singleton, `Anela.Heblo.Xcc.Telemetry`) emits a `manufacture_template_fetched` business event with timing metrics. No Domain, Application, API, persistence, or frontend changes.

**Tech Stack:** .NET 8, C#, `Microsoft.Extensions.Caching.Memory.IMemoryCache`, `Rem.FlexiBeeSDK` clients (`IBoMClient`, `IErpStockClient`), xUnit + Moq + FluentAssertions, `ITelemetryService` (Application Insights wrapper).

---

## Pre-flight: Context You Need

**The hot path before this change** (from `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs:31-102`):

1. `_bomClient.GetAsync(productCode, ct)` — 1 HTTP call to FlexiBee. Returns `IEnumerable<BoMItemFlexiDto>`. A 501 NotImplemented is logged and rethrown (`:38-44`). `bom.SingleOrDefault(s => s.Level == 1)` is the header; if null, the method returns `null` (`:47-49`).
2. **Three sequential** `_stockClient.StockToDateAsync(stockDate, warehouseId, ct)` calls (`:65-69`) for the warehouse IDs `FlexiStockClient.MaterialWarehouseId`, `SemiProductsWarehouseId`, `ProductsWarehouseId`. The results are concatenated into `allStockData` only to feed a `.FirstOrDefault` per ingredient (`:81`) reading exactly one boolean — `stockItem?.HasLots`.
3. The remaining projection builds `ManufactureTemplate` and `List<Ingredient>` with `ResolveProductType`. `ManufactureType.MultiPhase` if any ingredient has a sub-ingredient with `ProductTypeId == ProductType.SemiProduct`, otherwise `SinglePhase`.

**Why the cache must clone:** `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs:38` mutates `template.BatchSize`. Without a deep clone, the second cache hit would observe a value written by a prior caller.

**Precedent for cache + parallel dispatch in this same adapter:**
- Cache idiom: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs:50-98` (uses `IMemoryCache.TryGetValue` / `Set` with `ObjectDisposedException` defence, 5-minute absolute TTL).
- Parallel three-warehouse fetch: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs:33-46` (uses `Task.WhenAll` against `IStockToDateClient`).

**Registration anchor:** `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs:48` already calls `services.AddMemoryCache()`; line `:73` registers `IFlexiManufactureTemplateService` as scoped. We will add one new singleton registration for the cache wrapper.

**Telemetry anchor:** `ITelemetryService.TrackBusinessEvent(eventName, properties, metrics)` is the canonical entry point (`backend/src/Anela.Heblo.Xcc/Telemetry/ITelemetryService.cs:9`). The interface is registered globally in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:52` (singleton). It's safe to inject into the scoped `FlexiManufactureTemplateService`.

**Test conventions to follow:**
- Test assembly: `backend/test/Anela.Heblo.Adapters.Flexi.Tests`. Existing tests use **Moq** + **FluentAssertions** + **xUnit** (see `Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs:7-23`). Use Moq, not NSubstitute — overrides spec NFR-5 per arch-review.
- Test data helpers live in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/ManufactureTestData.cs`. Reuse `ManufactureTestData.Materials.*` codes for realistic product codes.

---

## File Structure

**New files** (all under `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/`):

| File | Responsibility |
|---|---|
| `IManufactureTemplateCache.cs` | `internal` interface — single `GetOrFetchAsync` method. |
| `ManufactureTemplateCache.cs` | `internal sealed` class — `IMemoryCache`-backed implementation. Skips nulls/exceptions. Returns deep clones. |
| `ManufactureTemplateCloner.cs` | `internal static` helper — `Clone(ManufactureTemplate)` returns a new `ManufactureTemplate` with a fresh `List<Ingredient>` and new `Ingredient` instances. |

**Modified files:**

| File | Change |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` | Inject `IManufactureTemplateCache` and `ITelemetryService`. Wrap fetch in `cache.GetOrFetchAsync`. Inside fetch, run the three stock calls in parallel and project to a single `Dictionary<string,bool>`. Emit telemetry. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` | Add `services.AddSingleton<IManufactureTemplateCache, ManufactureTemplateCache>();`. |

**New test files** (under `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/`):

| File | Coverage |
|---|---|
| `ManufactureTemplateClonerTests.cs` | Deep-equal but reference-distinct semantics; nested `Ingredient` instances are also new. |
| `ManufactureTemplateCacheTests.cs` | Miss invokes fetch + stores. Hit returns clone without invoking fetch. Null fetch result is not cached. Exception in fetch is not cached. TTL set via `AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)`. Mutation of returned instance does not corrupt cache. |

**Extended test file:**

| File | Coverage added |
|---|---|
| `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs` | Parallel dispatch of three stock calls. `HasLots` aggregation from concurrent results. Telemetry emitted with correct dimensions. Cache delegated to (verify by mocking `IManufactureTemplateCache`). Existing 501 test preserved. |

---

## Task 1: Add the deep-clone helper

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCloner.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateClonerTests.cs`

- [ ] **Step 1: Write the failing test for clone returning a new `ManufactureTemplate` instance with the same scalar values**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateClonerTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class ManufactureTemplateClonerTests
{
    private static ManufactureTemplate BuildTemplate() => new()
    {
        TemplateId = 42,
        ProductCode = "MAS001001M",
        ProductName = "Hedvábný pan Jasmín",
        Amount = 10,
        OriginalAmount = 10,
        BatchSize = 100,
        ManufactureType = ManufactureType.MultiPhase,
        Ingredients = new List<Ingredient>
        {
            new()
            {
                TemplateId = 100,
                ProductCode = "AKL001",
                ProductName = "Bisabolol",
                Amount = 1.5,
                OriginalAmount = 1.5,
                Price = 12.34m,
                ProductType = ProductType.Material,
                HasLots = true,
                HasExpiration = false
            },
            new()
            {
                TemplateId = 101,
                ProductCode = "AKL003",
                ProductName = "Dermosoft Eco 1388",
                Amount = 2.0,
                OriginalAmount = 2.0,
                Price = 5.5m,
                ProductType = ProductType.Material,
                HasLots = false,
                HasExpiration = false
            }
        }
    };

    [Fact]
    public void Clone_ReturnsTemplateWithSameScalarValues()
    {
        var original = BuildTemplate();

        var clone = ManufactureTemplateCloner.Clone(original);

        clone.Should().BeEquivalentTo(original);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateClonerTests"
```

Expected: FAIL — `ManufactureTemplateCloner` does not exist.

- [ ] **Step 3: Implement the cloner**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCloner.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal static class ManufactureTemplateCloner
{
    public static ManufactureTemplate Clone(ManufactureTemplate source)
    {
        return new ManufactureTemplate
        {
            TemplateId = source.TemplateId,
            ProductCode = source.ProductCode,
            ProductName = source.ProductName,
            Amount = source.Amount,
            OriginalAmount = source.OriginalAmount,
            BatchSize = source.BatchSize,
            ManufactureType = source.ManufactureType,
            Ingredients = source.Ingredients
                .Select(i => new Ingredient
                {
                    TemplateId = i.TemplateId,
                    ProductCode = i.ProductCode,
                    ProductName = i.ProductName,
                    Amount = i.Amount,
                    OriginalAmount = i.OriginalAmount,
                    Price = i.Price,
                    ProductType = i.ProductType,
                    HasLots = i.HasLots,
                    HasExpiration = i.HasExpiration
                })
                .ToList()
        };
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateClonerTests.Clone_ReturnsTemplateWithSameScalarValues"
```

Expected: PASS.

- [ ] **Step 5: Add a test that asserts the clone is reference-distinct (root and nested list)**

Append to `ManufactureTemplateClonerTests.cs`:

```csharp
    [Fact]
    public void Clone_ReturnsReferenceDistinctRootAndIngredientList()
    {
        var original = BuildTemplate();

        var clone = ManufactureTemplateCloner.Clone(original);

        clone.Should().NotBeSameAs(original);
        clone.Ingredients.Should().NotBeSameAs(original.Ingredients);
        for (var i = 0; i < clone.Ingredients.Count; i++)
        {
            clone.Ingredients[i].Should().NotBeSameAs(original.Ingredients[i]);
        }
    }

    [Fact]
    public void Clone_MutationOnCloneDoesNotAffectOriginal()
    {
        var original = BuildTemplate();
        var clone = ManufactureTemplateCloner.Clone(original);

        clone.BatchSize = 999;
        clone.Ingredients[0].Amount = 99.9;
        clone.Ingredients.Add(new Ingredient
        {
            TemplateId = 999,
            ProductCode = "X",
            ProductName = "X",
            ProductType = ProductType.Material
        });

        original.BatchSize.Should().Be(100);
        original.Ingredients[0].Amount.Should().Be(1.5);
        original.Ingredients.Count.Should().Be(2);
    }
```

- [ ] **Step 6: Run both new tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateClonerTests"
```

Expected: 3/3 PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCloner.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateClonerTests.cs
git commit -m "feat: add ManufactureTemplateCloner for defensive cache cloning"
```

---

## Task 2: Define the cache interface

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IManufactureTemplateCache.cs`

- [ ] **Step 1: Create the internal interface**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IManufactureTemplateCache.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IManufactureTemplateCache
{
    Task<ManufactureTemplate?> GetOrFetchAsync(
        string productCode,
        Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify it compiles**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

Expected: build succeeds (no implementations yet, only the interface).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IManufactureTemplateCache.cs
git commit -m "feat: add IManufactureTemplateCache interface"
```

---

## Task 3: Implement the cache — miss path

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCache.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`

- [ ] **Step 1: Write the failing test — cache miss invokes the fetcher and returns its value**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`:

```csharp
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
    private static ManufactureTemplate BuildTemplate(string productCode = "MAS001001M") => new()
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
                HasLots = true
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
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateCacheTests"
```

Expected: FAIL — `ManufactureTemplateCache` does not exist.

- [ ] **Step 3: Implement the cache (miss path only — full implementation lands incrementally)**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCache.cs`:

```csharp
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
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateCacheTests.GetOrFetchAsync_CacheMiss_InvokesFetcherOnce"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCache.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs
git commit -m "feat: add ManufactureTemplateCache miss path"
```

---

## Task 4: Cache hit returns clone without invoking fetcher

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `ManufactureTemplateCacheTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateCacheTests"
```

Expected: 3/3 PASS (the prior test plus these two).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs
git commit -m "test: cache hit returns deep clone without invoking fetcher"
```

---

## Task 5: Null fetch results are not cached

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `ManufactureTemplateCacheTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it passes**

The current implementation already returns early on `null` without storing. Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateCacheTests.GetOrFetchAsync_FetcherReturnsNull_DoesNotCacheResult"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs
git commit -m "test: null fetch results skip cache write"
```

---

## Task 6: Exceptions in fetch are not cached

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `ManufactureTemplateCacheTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it passes**

The current implementation lets exceptions propagate naturally (`await fetch(...)` rethrows; the `TrySet` block is unreached). Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateCacheTests.GetOrFetchAsync_FetcherThrows_DoesNotCacheAndPropagates"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs
git commit -m "test: fetch exceptions skip cache write"
```

---

## Task 7: Cache passes cancellation token through to fetcher

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `ManufactureTemplateCacheTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureTemplateCacheTests.GetOrFetchAsync_PassesCancellationTokenToFetcher"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs
git commit -m "test: cache forwards cancellation token to fetcher"
```

---

## Task 8: Register the cache in DI

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the singleton registration after the existing `IFlexiManufactureTemplateService` line**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`. Find the line:

```csharp
        services.AddScoped<IFlexiManufactureTemplateService, FlexiManufactureTemplateService>();
```

Replace with:

```csharp
        services.AddSingleton<IManufactureTemplateCache, ManufactureTemplateCache>();
        services.AddScoped<IFlexiManufactureTemplateService, FlexiManufactureTemplateService>();
```

- [ ] **Step 2: Verify the assembly compiles**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
git commit -m "feat: register IManufactureTemplateCache as singleton"
```

---

## Task 9: Rewrite `FlexiManufactureTemplateService` to delegate to the cache (no behaviour change yet)

This task delegates through the cache without changing the inner fetch logic — keeping the rewrite reviewable. Parallelisation lands in Task 10. Telemetry lands in Task 11.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs`

- [ ] **Step 1: Update the existing 501 test to inject a fake `IManufactureTemplateCache` that always calls through**

Open `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs`. Replace the entire file with:

```csharp
using System.Net;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Adapters.Flexi.Tests.Manufacture;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
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

    /// <summary>
    /// Test cache that always invokes the fetcher (acts as no-op so we test
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
    }
}
```

- [ ] **Step 2: Run the test — it will fail to compile because the new constructor signature does not exist yet**

Run:
```bash
cd backend && dotnet build test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: compile error — `FlexiManufactureTemplateService` constructor does not accept `IManufactureTemplateCache` or `ITelemetryService`.

- [ ] **Step 3: Modify `FlexiManufactureTemplateService` — add the two new dependencies and wrap the existing body in `_cache.GetOrFetchAsync`**

Replace the entire content of `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` with:

```csharp
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;
using System.Net;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiManufactureTemplateService : IFlexiManufactureTemplateService
{
    private readonly IBoMClient _bomClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;
    private readonly IManufactureTemplateCache _cache;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<FlexiManufactureTemplateService> _logger;

    public FlexiManufactureTemplateService(
        IBoMClient bomClient,
        IErpStockClient stockClient,
        TimeProvider timeProvider,
        IManufactureTemplateCache cache,
        ITelemetryService telemetry,
        ILogger<FlexiManufactureTemplateService> logger)
    {
        _bomClient = bomClient ?? throw new ArgumentNullException(nameof(bomClient));
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    {
        return _cache.GetOrFetchAsync(productCode, ct => FetchAsync(productCode, ct), cancellationToken);
    }

    private async Task<ManufactureTemplate?> FetchAsync(string productCode, CancellationToken cancellationToken)
    {
        IEnumerable<BoMItemFlexiDto> bom;
        try
        {
            bom = await _bomClient.GetAsync(productCode, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee kusovnik returned 501 NotImplemented while fetching BoM template — " +
                "endpoint may be disabled or unsupported on this instance. ProductCode: {ProductCode}",
                productCode);
            throw;
        }

        var header = bom.SingleOrDefault(s => s.Level == 1);
        if (header == null)
            return null;

        var ingredients = bom.Where(w => w.Level != 1);

        // Get stock data to determine HasLots for each ingredient (sequential — will be parallelised in the next task).
        var stockDate = _timeProvider.GetLocalNow().DateTime;
        var allStockData = new List<ErpStock>();

        var warehouseIds = new[]
        {
            FlexiStockClient.MaterialWarehouseId,
            FlexiStockClient.SemiProductsWarehouseId,
            FlexiStockClient.ProductsWarehouseId
        };

        foreach (var warehouseId in warehouseIds)
        {
            var stockItems = await _stockClient.StockToDateAsync(stockDate, warehouseId, cancellationToken);
            allStockData.AddRange(stockItems);
        }

        var template = new ManufactureTemplate()
        {
            TemplateId = header.Id,
            ProductCode = header.IngredientCode.RemoveCodePrefix(),
            ProductName = header.IngredientFullName,
            Amount = header.Amount,
            OriginalAmount = header.Amount,
            Ingredients = ingredients.Select(s =>
            {
                var code = s.IngredientCode.RemoveCodePrefix();
                var stockItem = allStockData.FirstOrDefault(stock => stock.ProductCode == code);

                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = code,
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s),
                    HasLots = stockItem?.HasLots ?? false,
                    HasExpiration = false
                };
            }).ToList(),
        };

        if (ingredients.Any(a => a.Ingredient.Any(b => b.ProductTypeId == (int)ProductType.SemiProduct)))
            template.ManufactureType = ManufactureType.MultiPhase;
        else
            template.ManufactureType = ManufactureType.SinglePhase;

        return template;
    }

    private static ProductType ResolveProductType(BoMItemFlexiDto boMItemFlexiDto)
    {
        try
        {
            var productTypeId = boMItemFlexiDto.Ingredient?.FirstOrDefault()?.ProductTypeId;
            if (!productTypeId.HasValue)
            {
                return ProductType.UNDEFINED;
            }
            if (Enum.IsDefined(typeof(ProductType), productTypeId.Value))
            {
                return (ProductType)productTypeId.Value;
            }
            return ProductType.UNDEFINED;
        }
        catch
        {
            return ProductType.UNDEFINED;
        }
    }
}
```

- [ ] **Step 4: Run the existing 501 test to verify behaviour is preserved**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FlexiManufactureTemplateServiceTests"
```

Expected: PASS.

- [ ] **Step 5: Run the whole Flexi adapter test project to catch regressions in dependent tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs
git commit -m "refactor: route FlexiManufactureTemplateService through IManufactureTemplateCache"
```

---

## Task 10: Parallelise the three stock-snapshot calls and narrow projection to `HasLots`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs`

- [ ] **Step 1: Add a failing test that asserts the three stock calls run concurrently**

The test injects per-warehouse `Task.Delay`s; if the dispatch is sequential, wall-clock time ≈ 600 ms; if parallel, ≈ 200 ms. We assert < 400 ms.

Append to `FlexiManufactureTemplateServiceTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task GetManufactureTemplateAsync_DispatchesThreeStockCallsConcurrently()
    {
        const string productCode = "MAS001001M";

        _mockBomClient
            .Setup(x => x.GetAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto>
            {
                new()
                {
                    Id = 1, Level = 1, Amount = 10,
                    IngredientCode = "code:" + productCode,
                    IngredientFullName = "Header"
                },
                new()
                {
                    Id = 2, Level = 2, Amount = 1,
                    IngredientCode = "code:AKL001",
                    IngredientFullName = "Ing",
                    Ingredient = new List<Rem.FlexiBeeSDK.Client.Clients.Products.BoM.BomProductFlexiDto>
                    {
                        new() { Code = "code:AKL001", Name = "Ing", ProductTypeId = 1 }
                    }
                }
            });

        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (DateTime _, int _, CancellationToken ct) =>
            {
                await Task.Delay(200, ct);
                return (IReadOnlyList<ErpStock>)new List<ErpStock>
                {
                    new() { ProductCode = "AKL001", HasLots = true }
                };
            });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var template = await _service.GetManufactureTemplateAsync(productCode, CancellationToken.None);
        stopwatch.Stop();

        template.Should().NotBeNull();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(400,
            "three 200 ms stock calls must run in parallel, not sequentially");

        _mockStockClient.Verify(
            x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_MergesHasLotsAcrossWarehouses()
    {
        const string productCode = "MAS001001M";

        _mockBomClient
            .Setup(x => x.GetAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto>
            {
                new()
                {
                    Id = 1, Level = 1, Amount = 10,
                    IngredientCode = "code:" + productCode,
                    IngredientFullName = "Header"
                },
                new()
                {
                    Id = 2, Level = 2, Amount = 1,
                    IngredientCode = "code:AKL001",
                    IngredientFullName = "Bisabolol",
                    Ingredient = new List<Rem.FlexiBeeSDK.Client.Clients.Products.BoM.BomProductFlexiDto>
                    {
                        new() { Code = "code:AKL001", Name = "Bisabolol", ProductTypeId = 1 }
                    }
                },
                new()
                {
                    Id = 3, Level = 2, Amount = 1,
                    IngredientCode = "code:AKL003",
                    IngredientFullName = "Dermosoft",
                    Ingredient = new List<Rem.FlexiBeeSDK.Client.Clients.Products.BoM.BomProductFlexiDto>
                    {
                        new() { Code = "code:AKL003", Name = "Dermosoft", ProductTypeId = 1 }
                    }
                }
            });

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

        var template = await _service.GetManufactureTemplateAsync(productCode, CancellationToken.None);

        template.Should().NotBeNull();
        template!.Ingredients.Should().HaveCount(2);
        template.Ingredients.Single(i => i.ProductCode == "AKL001").HasLots.Should().BeTrue();
        template.Ingredients.Single(i => i.ProductCode == "AKL003").HasLots.Should().BeFalse();
    }
```

Add this `using` to the top of the file if not present:

```csharp
using Anela.Heblo.Adapters.Flexi.Stock;
```

- [ ] **Step 2: Run the tests to verify they fail (parallel-timing assertion will fail with the current sequential code)**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_DispatchesThreeStockCallsConcurrently"
```

Expected: FAIL — wall-clock ≈ 600 ms, exceeds 400 ms threshold.

- [ ] **Step 3: Parallelise the three stock calls and narrow the projection inside `FetchAsync`**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`. Replace the `// Get stock data ... } ` block (the `foreach` over `warehouseIds` and the `allStockData` list) with the parallel + dictionary projection:

```csharp
        var stockDate = _timeProvider.GetLocalNow().DateTime;

        var materialStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.MaterialWarehouseId, cancellationToken);
        var semiProductsStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.SemiProductsWarehouseId, cancellationToken);
        var productsStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.ProductsWarehouseId, cancellationToken);

        await Task.WhenAll(materialStockTask, semiProductsStockTask, productsStockTask);

        // Narrow the three full snapshots to a single dictionary of HasLots flags
        // before the larger DTOs go out of scope (FR-4).
        var hasLotsByProductCode = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var snapshot in new[] { materialStockTask.Result, semiProductsStockTask.Result, productsStockTask.Result })
        {
            foreach (var stockItem in snapshot)
            {
                hasLotsByProductCode[stockItem.ProductCode] = stockItem.HasLots;
            }
        }
```

Then update the ingredient projection to read from the dictionary instead of `allStockData.FirstOrDefault`:

Replace:

```csharp
            Ingredients = ingredients.Select(s =>
            {
                var code = s.IngredientCode.RemoveCodePrefix();
                var stockItem = allStockData.FirstOrDefault(stock => stock.ProductCode == code);

                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = code,
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s),
                    HasLots = stockItem?.HasLots ?? false,
                    HasExpiration = false
                };
            }).ToList(),
```

With:

```csharp
            Ingredients = ingredients.Select(s =>
            {
                var code = s.IngredientCode.RemoveCodePrefix();
                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = code,
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s),
                    HasLots = hasLotsByProductCode.TryGetValue(code, out var hasLots) && hasLots,
                    HasExpiration = false
                };
            }).ToList(),
```

Remove the now-unused `using` for `ErpStock` if the IDE flags it. The `ErpStock` type is still referenced via task result types, so likely still needed.

- [ ] **Step 4: Run the two new tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_DispatchesThreeStockCallsConcurrently|FullyQualifiedName~FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_MergesHasLotsAcrossWarehouses"
```

Expected: 2/2 PASS.

- [ ] **Step 5: Run the whole Flexi adapter test project**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs
git commit -m "perf: parallelise three stock snapshots and project to HasLots dictionary"
```

---

## Task 11: Emit telemetry for the manufacture-template fetch

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs`

Telemetry distinguishes cache hits from misses. To do this cleanly inside the cache-routed flow, the service measures the inner-fetch durations itself and the *outer* call captures `cache_hit` by observing whether `FetchAsync` actually ran. We track this with a per-call state object.

- [ ] **Step 1: Add the failing telemetry test for a cache miss**

Append to `FlexiManufactureTemplateServiceTests.cs`:

```csharp
    [Fact]
    public async Task GetManufactureTemplateAsync_OnCacheMiss_EmitsTelemetryEvent()
    {
        const string productCode = "MAS001001M";

        _mockBomClient
            .Setup(x => x.GetAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto>
            {
                new()
                {
                    Id = 1, Level = 1, Amount = 10,
                    IngredientCode = "code:" + productCode,
                    IngredientFullName = "Header"
                },
                new()
                {
                    Id = 2, Level = 2, Amount = 1,
                    IngredientCode = "code:AKL001",
                    IngredientFullName = "Ing",
                    Ingredient = new List<Rem.FlexiBeeSDK.Client.Clients.Products.BoM.BomProductFlexiDto>
                    {
                        new() { Code = "code:AKL001", Name = "Ing", ProductTypeId = 1 }
                    }
                }
            });
        _mockStockClient
            .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>());

        await _service.GetManufactureTemplateAsync(productCode, CancellationToken.None);

        _mockTelemetry.Verify(
            t => t.TrackBusinessEvent(
                "manufacture_template_fetched",
                It.Is<Dictionary<string, string>>(p =>
                    p["product_code"] == productCode &&
                    p["cache_hit"] == "false" &&
                    p["ingredient_count"] == "1"),
                It.Is<Dictionary<string, double>>(m =>
                    m.ContainsKey("bom_duration_ms") &&
                    m.ContainsKey("stock_duration_ms") &&
                    m.ContainsKey("total_duration_ms"))),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_OnCacheMiss_EmitsTelemetryEvent"
```

Expected: FAIL — `TrackBusinessEvent` is never invoked.

- [ ] **Step 3: Add telemetry emission in `FlexiManufactureTemplateService`**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`.

Add at the top of the file (using directives):

```csharp
using System.Diagnostics;
```

Replace the `GetManufactureTemplateAsync` method body so the outer call measures total + tracks cache hit/miss:

```csharp
    public async Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var fetchTimings = new FetchTimings();

        var template = await _cache.GetOrFetchAsync(
            productCode,
            ct => FetchAsync(productCode, ct, fetchTimings),
            cancellationToken);

        totalStopwatch.Stop();
        EmitTelemetry(productCode, fetchTimings, totalStopwatch.ElapsedMilliseconds, template);
        return template;
    }
```

Change the `FetchAsync` signature to:

```csharp
    private async Task<ManufactureTemplate?> FetchAsync(string productCode, CancellationToken cancellationToken, FetchTimings timings)
```

Inside `FetchAsync`, wrap the BoM call and the parallel block with `Stopwatch`s that write into `timings`:

```csharp
        var bomStopwatch = Stopwatch.StartNew();
        IEnumerable<BoMItemFlexiDto> bom;
        try
        {
            bom = await _bomClient.GetAsync(productCode, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee kusovnik returned 501 NotImplemented while fetching BoM template — " +
                "endpoint may be disabled or unsupported on this instance. ProductCode: {ProductCode}",
                productCode);
            throw;
        }
        bomStopwatch.Stop();
        timings.BomMs = bomStopwatch.ElapsedMilliseconds;

        var header = bom.SingleOrDefault(s => s.Level == 1);
        if (header == null)
            return null;

        var ingredients = bom.Where(w => w.Level != 1);

        var stockStopwatch = Stopwatch.StartNew();
        var stockDate = _timeProvider.GetLocalNow().DateTime;

        var materialStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.MaterialWarehouseId, cancellationToken);
        var semiProductsStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.SemiProductsWarehouseId, cancellationToken);
        var productsStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.ProductsWarehouseId, cancellationToken);

        await Task.WhenAll(materialStockTask, semiProductsStockTask, productsStockTask);
        stockStopwatch.Stop();
        timings.StockMs = stockStopwatch.ElapsedMilliseconds;
        timings.FetchInvoked = true;

        var hasLotsByProductCode = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var snapshot in new[] { materialStockTask.Result, semiProductsStockTask.Result, productsStockTask.Result })
        {
            foreach (var stockItem in snapshot)
            {
                hasLotsByProductCode[stockItem.ProductCode] = stockItem.HasLots;
            }
        }
```

Add the private state class and the emission helper inside `FlexiManufactureTemplateService`:

```csharp
    private sealed class FetchTimings
    {
        public bool FetchInvoked { get; set; }
        public long BomMs { get; set; }
        public long StockMs { get; set; }
    }

    private void EmitTelemetry(string productCode, FetchTimings timings, long totalMs, ManufactureTemplate? template)
    {
        try
        {
            var properties = new Dictionary<string, string>
            {
                ["product_code"] = productCode,
                ["cache_hit"] = (!timings.FetchInvoked).ToString().ToLowerInvariant(),
                ["ingredient_count"] = (template?.Ingredients.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var metrics = new Dictionary<string, double>
            {
                ["bom_duration_ms"] = timings.BomMs,
                ["stock_duration_ms"] = timings.StockMs,
                ["total_duration_ms"] = totalMs
            };
            _telemetry.TrackBusinessEvent("manufacture_template_fetched", properties, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to emit manufacture_template_fetched telemetry for {ProductCode}", productCode);
        }
    }
```

- [ ] **Step 4: Run the telemetry test to verify it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_OnCacheMiss_EmitsTelemetryEvent"
```

Expected: PASS.

- [ ] **Step 5: Add a test for the cache-hit telemetry path**

Append to `FlexiManufactureTemplateServiceTests.cs`:

```csharp
    [Fact]
    public async Task GetManufactureTemplateAsync_OnCacheHit_EmitsTelemetryWithCacheHitTrue()
    {
        // Replace passthrough cache with a stub that simulates a hit (does not invoke fetcher).
        var hitCache = new HitOnlyCache(new ManufactureTemplate
        {
            TemplateId = 1,
            ProductCode = "MAS001001M",
            ProductName = "Cached",
            Amount = 5,
            OriginalAmount = 5,
            Ingredients = new List<Ingredient>
            {
                new() { TemplateId = 2, ProductCode = "AKL001", ProductName = "Ing", Amount = 1 }
            },
            ManufactureType = ManufactureType.SinglePhase
        });

        var service = new FlexiManufactureTemplateService(
            _mockBomClient.Object,
            _mockStockClient.Object,
            TimeProvider.System,
            hitCache,
            _mockTelemetry.Object,
            _mockLogger.Object);

        await service.GetManufactureTemplateAsync("MAS001001M", CancellationToken.None);

        _mockTelemetry.Verify(
            t => t.TrackBusinessEvent(
                "manufacture_template_fetched",
                It.Is<Dictionary<string, string>>(p =>
                    p["product_code"] == "MAS001001M" &&
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
    }
```

- [ ] **Step 6: Run the hit-path test to verify it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_OnCacheHit_EmitsTelemetryWithCacheHitTrue"
```

Expected: PASS.

- [ ] **Step 7: Run the whole adapter test project**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs
git commit -m "feat: emit manufacture_template_fetched telemetry with cache_hit dimension"
```

---

## Task 12: Whole-solution build + regression suite

**Files:** none modified.

- [ ] **Step 1: Build the entire backend solution**

Run:
```bash
cd backend && dotnet build
```

Expected: build succeeds with no errors. Warnings unchanged from baseline.

- [ ] **Step 2: Run the entire backend test suite**

Run:
```bash
cd backend && dotnet test
```

Expected: all tests pass. The handler tests for `CalculatedBatchSizeHandler`, `CalculateBatchByIngredientHandler`, `CalculateBatchPlanHandler`, `GetProductCompositionHandler` (and any others that indirectly call `IManufactureClient.GetManufactureTemplateAsync`) must continue passing without modification — proves backward compatibility (FR-1, NFR-4).

- [ ] **Step 3: Run `dotnet format`**

Run:
```bash
cd backend && dotnet format
```

Expected: no diff (or only whitespace fixes to the new files).

- [ ] **Step 4: Commit formatting if any**

```bash
git status
# If formatting produced changes:
git add -u && git commit -m "chore: dotnet format"
# Otherwise skip.
```

---

## Task 13: PR description with App Insights baseline query

**Files:** none in the repo.

- [ ] **Step 1: Capture the App Insights baseline KQL query for the endpoint**

Add to the PR description (do not commit to the repo) the following baseline KQL — required by spec FR-5 acceptance criterion:

```kusto
// Baseline vs post-deploy p50/p95 for GET ManufactureBatch/GetBatchTemplate
requests
| where timestamp > ago(7d)
| where name has "ManufactureBatch" and name has "template"
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    count_ = count()
  by bin(timestamp, 1h)
| order by timestamp asc
```

And the cache-hit/miss split (uses the new custom event):

```kusto
customEvents
| where timestamp > ago(7d)
| where name == "manufacture_template_fetched"
| extend cache_hit = tostring(customDimensions["cache_hit"])
| summarize
    p50_total = percentile(todouble(customMeasurements["total_duration_ms"]), 50),
    p95_total = percentile(todouble(customMeasurements["total_duration_ms"]), 95),
    p50_bom   = percentile(todouble(customMeasurements["bom_duration_ms"]), 50),
    p50_stock = percentile(todouble(customMeasurements["stock_duration_ms"]), 50),
    count_    = count()
  by cache_hit
```

- [ ] **Step 2: Document the explicit non-goal in the PR description**

Note in the PR description: "v1 does not deduplicate concurrent cache misses for the same product code (no single-flight). Worst-case is N redundant FlexiBee calls during cache stampede. The BoM data is read-only, so this is acceptable for v1."

Note also: "Beneficiary call sites that now also use caching + parallel stock fetch: `CalculateBatchByIngredientHandler`, `CalculateBatchPlanHandler`, `GetProductCompositionHandler`, `ResidueDistributionCalculator`, `FlexiIngredientRequirementAggregator`. None depend on cache-miss semantics — templates are read-only data."

---

## Self-Review Notes

**Spec coverage check:**

| Spec requirement | Task(s) |
|---|---|
| FR-1 — preserve response contract | Task 9 keeps existing 501 test; Task 12 runs full handler test suite. |
| FR-2 — parallelise three stock calls | Task 10 (timing test + `Task.WhenAll`). |
| FR-3 — 5-minute in-memory cache, defensive clone, no null caching, scoped to adapter, singleton wrapper, debug logging | Tasks 1–8 (cloner, interface, miss, hit/clone, null skip, exception skip, cancellation, DI registration). |
| FR-4 — narrow stock projection to `HasLots` dictionary, release larger DTOs | Task 10 (replace `allStockData` list with `hasLotsByProductCode` dict). |
| FR-5 — telemetry custom event with all required dimensions and metrics | Task 11 (both miss and hit paths). |
| NFR-1 — perf targets | Verified via Task 13 KQL queries post-deploy. |
| NFR-2 — security (no new endpoints/auth) | Unchanged — no surface changes. |
| NFR-3 — reliability (no caching of nulls/exceptions, cancellation) | Tasks 5, 6, 7. |
| NFR-4 — backward compatibility | Task 12 (full backend test suite). |
| NFR-5 — testability ≥80% coverage with xUnit + Moq | Tasks 1–11 add tests for every new code path. |

**Placeholder scan:** None. Every code step contains full code blocks; every command shows expected output.

**Type-consistency check:**
- Interface `IManufactureTemplateCache.GetOrFetchAsync(productCode, fetch, ct)` — used identically in Tasks 2, 3, 9, 11.
- `ManufactureTemplateCloner.Clone(ManufactureTemplate)` — used identically in Tasks 1, 3, 11.
- `ITelemetryService.TrackBusinessEvent(string, Dictionary<string,string>, Dictionary<string,double>)` — matches the existing interface signature verified in `backend/src/Anela.Heblo.Xcc/Telemetry/ITelemetryService.cs:9`.
- `FlexiManufactureTemplateService` constructor order (`bom, stock, time, cache, telemetry, logger`) — used identically in Tasks 9 and 11.
- Stock client method `IErpStockClient.StockToDateAsync(DateTime date, int warehouseId, CancellationToken)` — matches `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs:48`.
- Warehouse constants `FlexiStockClient.MaterialWarehouseId / SemiProductsWarehouseId / ProductsWarehouseId` — match `FlexiStockClient.cs:12-14`.
