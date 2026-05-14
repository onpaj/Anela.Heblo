# Decouple ProductEnrichmentCache from Catalog Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the cross-module dependency where `ProductEnrichmentCache` (KnowledgeBase) imports `Anela.Heblo.Domain.Features.Catalog` types and consumes `ICatalogRepository` directly — by introducing a narrow Catalog-owned read contract (`IProductCatalogQueryService`) and refactoring the cache to depend on it. Behavior, lifetimes, TTL, and downstream consumer signatures stay identical.

**Architecture:** A new contract pair (`IProductCatalogQueryService` + `ProductCatalogEntry` DTO) is added to `Anela.Heblo.Application/Features/Catalog/Contracts/`. The implementation `ProductCatalogQueryService` lives in `Anela.Heblo.Application/Features/Catalog/Services/`, wraps `ICatalogRepository`, owns the `Type == Product || Type == Goods` predicate, and projects each `CatalogAggregate` into the DTO. `ProductEnrichmentCache` is rewritten to resolve `IProductCatalogQueryService` (transient) via `IServiceScopeFactory` — the singleton-resolving-transient indirection is preserved because the cache itself is singleton. The predicate test that was previously in `ProductEnrichmentCacheTests` moves into a new `ProductCatalogQueryServiceTests` class — that file becomes the only place outside the Catalog module that still references `CatalogAggregate` and `ProductType`.

**Tech Stack:** .NET 8, C#, xUnit, Moq, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options. No new NuGet packages.

---

## File Structure

**New files:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductCatalogEntry.cs` — DTO (class, three string properties).
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/IProductCatalogQueryService.cs` — Catalog-owned read contract.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductCatalogQueryService.cs` — Implementation; wraps `ICatalogRepository`, owns the predicate and projection.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/ProductCatalogQueryServiceTests.cs` — Predicate-filter, projection, and cancellation-token tests; lives in Catalog test tree (the only allowed `CatalogAggregate` / `ProductType` reference site for this refactor).

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — One-line DI registration.
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs` — Replace `ICatalogRepository` with `IProductCatalogQueryService`; drop all Catalog-domain `using`s.
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs` — Mock `IProductCatalogQueryService` instead of `ICatalogRepository`; drop `CatalogAggregate`/`ProductType` usage; remove the predicate test (moved to Catalog tests); keep TTL tests.

**Unchanged (but verified by grep):**
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/IProductEnrichmentCache.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentEntry.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- Consumers: `PostAnswerEnrichmentMiddleware`, `AskQuestionHandler`, and their tests.

---

### Task 1: Add `ProductCatalogEntry` DTO

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductCatalogEntry.cs`

- [ ] **Step 1: Create the DTO file**

Per the project rule in `CLAUDE.md` (*"DTOs are classes, never C# records"*) and consistent with every other file under `Features/Catalog/Contracts/` (e.g., `CatalogItemDto.cs`), declare a `public class` with mutable get/set properties.

Write the following content to `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductCatalogEntry.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class ProductCatalogEntry
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Url { get; set; }
}
```

- [ ] **Step 2: Verify no `Anela.Heblo.Domain.*` reference**

Run: `grep -n "Anela.Heblo.Domain" backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductCatalogEntry.cs || echo OK`
Expected: `OK`

- [ ] **Step 3: Build to confirm the file compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductCatalogEntry.cs
git commit -m "feat(catalog): add ProductCatalogEntry contract DTO"
```

---

### Task 2: Add `IProductCatalogQueryService` contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/IProductCatalogQueryService.cs`

- [ ] **Step 1: Create the interface**

Method returns `IReadOnlyList<>` so consumers cannot mutate the producer's list. Optional `CancellationToken ct = default` matches the calling convention used by `ICatalogRepository.FindAsync`.

Write the following to `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/IProductCatalogQueryService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public interface IProductCatalogQueryService
{
    Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Verify no `Anela.Heblo.Domain.*` reference**

Run: `grep -n "Anela.Heblo.Domain" backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/IProductCatalogQueryService.cs || echo OK`
Expected: `OK`

- [ ] **Step 3: Build to confirm the file compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/IProductCatalogQueryService.cs
git commit -m "feat(catalog): add IProductCatalogQueryService contract"
```

---

### Task 3: Write failing tests for `ProductCatalogQueryService`

This task is TDD-Red. We write the full test class before any implementation. The test file is the only place outside the Catalog module that still references `CatalogAggregate` and `ProductType`, and that is intentional — it lives in the Catalog test tree.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/ProductCatalogQueryServiceTests.cs`

- [ ] **Step 1: Write the test file**

The tests cover three behaviors:
1. Predicate filters to `Product` and `Goods` only (capture the `Expression`, compile it, assert per `ProductType`).
2. Projection maps `ProductCode`, `ProductName`, `Url` correctly and preserves `null` for `Url`.
3. The provided `CancellationToken` is propagated to the repository.

Note: `ICatalogRepository.FindAsync` returns `Task<IEnumerable<CatalogAggregate>>` (see `BaseRepository.FindAsync` and `MockCatalogRepository.FindAsync`), not `Task<IReadOnlyList<...>>`. The implementation will materialize to a list before returning.

Write the following to `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/ProductCatalogQueryServiceTests.cs`:

```csharp
using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

public class ProductCatalogQueryServiceTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private ProductCatalogQueryService Create() => new(_repository.Object);

    [Fact]
    public async Task GetActiveProductsAsync_PredicateFiltersToProductAndGoodsOnly()
    {
        Expression<Func<CatalogAggregate, bool>>? capturedPredicate = null;
        _repository
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<CatalogAggregate, bool>>, CancellationToken>((pred, _) => capturedPredicate = pred)
            .ReturnsAsync(Array.Empty<CatalogAggregate>());

        var service = Create();
        await service.GetActiveProductsAsync();

        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(new CatalogAggregate { Type = ProductType.Product }));
        Assert.True(compiled(new CatalogAggregate { Type = ProductType.Goods }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.Material }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.SemiProduct }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.Set }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.UNDEFINED }));
    }

    [Fact]
    public async Task GetActiveProductsAsync_MapsProductCodeNameAndUrl()
    {
        _repository
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product, Url = "https://example.com/prd001" },
                new CatalogAggregate { ProductCode = "GDS001", ProductName = "Krém XYZ", Type = ProductType.Goods, Url = null }
            });

        var service = Create();
        var result = await service.GetActiveProductsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("PRD001", result[0].ProductCode);
        Assert.Equal("Sérum ABC", result[0].ProductName);
        Assert.Equal("https://example.com/prd001", result[0].Url);
        Assert.Equal("GDS001", result[1].ProductCode);
        Assert.Equal("Krém XYZ", result[1].ProductName);
        Assert.Null(result[1].Url);
    }

    [Fact]
    public async Task GetActiveProductsAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var capturedToken = CancellationToken.None;
        _repository
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<CatalogAggregate, bool>>, CancellationToken>((_, t) => capturedToken = t)
            .ReturnsAsync(Array.Empty<CatalogAggregate>());

        var service = Create();
        await service.GetActiveProductsAsync(cts.Token);

        Assert.Equal(cts.Token, capturedToken);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductCatalogQueryServiceTests"`
Expected: Build fails with "The type or namespace name 'ProductCatalogQueryService' could not be found" (the implementation doesn't exist yet). This is the correct TDD-Red state.

- [ ] **Step 3: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/ProductCatalogQueryServiceTests.cs
git commit -m "test(catalog): add failing tests for ProductCatalogQueryService"
```

---

### Task 4: Implement `ProductCatalogQueryService`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductCatalogQueryService.cs`

- [ ] **Step 1: Write the implementation**

Per the architecture review (Decision 3), this is `public class` matching sibling implementations in `Features/Catalog/Services/` (`MarginCalculationService`, `StockUpProcessingService`, `ProductWeightRecalculationService`, `EshopStockDomainService`). The architecture review's Amendment §1 overrides the spec's `internal sealed` wording. We mark `sealed` because the class is not designed for inheritance.

Write the following to `backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductCatalogQueryService.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public sealed class ProductCatalogQueryService : IProductCatalogQueryService
{
    private readonly ICatalogRepository _repository;

    public ProductCatalogQueryService(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(CancellationToken ct = default)
    {
        var products = await _repository.FindAsync(
            p => p.Type == ProductType.Product || p.Type == ProductType.Goods,
            ct);

        return products
            .Select(p => new ProductCatalogEntry
            {
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                Url = p.Url
            })
            .ToList();
    }
}
```

- [ ] **Step 2: Run the new tests — they should now pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductCatalogQueryServiceTests"`
Expected: 3 tests passed, 0 failed.

- [ ] **Step 3: Run `dotnet format` on the touched files**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductCatalogQueryService.cs backend/test/Anela.Heblo.Tests/Features/Catalog/Services/ProductCatalogQueryServiceTests.cs`
Expected: No errors, formatting applied.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductCatalogQueryService.cs
git commit -m "feat(catalog): implement ProductCatalogQueryService"
```

---

### Task 5: Register `IProductCatalogQueryService` in `CatalogModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (insert after line 62)

- [ ] **Step 1: Add the DI registration**

The architecture review's Amendment §3 specifies the exact placement: after the `IEshopStockDomainService` registration on line 62, at the end of the "Register catalog-specific services" block. Lifetime is `Transient` to match `ICatalogRepository` (line 39) and avoid captive-dependency warnings.

Apply this edit to `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`:

Replace:
```csharp
        services.AddScoped<IEshopStockDomainService, EshopStockDomainService>();

        // Configure feature flags from configuration
```

With:
```csharp
        services.AddScoped<IEshopStockDomainService, EshopStockDomainService>();
        services.AddTransient<IProductCatalogQueryService, ProductCatalogQueryService>();

        // Configure feature flags from configuration
```

- [ ] **Step 2: Add the `using` directive**

The `Anela.Heblo.Application.Features.Catalog.Services` namespace is already imported (line 8) — `ProductCatalogQueryService` is reachable. The `Anela.Heblo.Application.Features.Catalog.Contracts` namespace is **not** in the file's existing usings. Add it.

Apply this edit to `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`:

Replace:
```csharp
using Anela.Heblo.Application.Features.Catalog.Cache;
```

With:
```csharp
using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.Contracts;
```

- [ ] **Step 3: Build to confirm registration compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full Catalog test slice to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Catalog"`
Expected: All tests pass (existing Catalog tests + the three new `ProductCatalogQueryServiceTests`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(catalog): register IProductCatalogQueryService in CatalogModule"
```

---

### Task 6: Rewrite `ProductEnrichmentCacheTests` to consume the contract (TDD-Red)

We rewrite the KB tests first — they will fail to compile because the cache still references `ICatalogRepository`. Then Task 7 makes them pass.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs` (full rewrite)

- [ ] **Step 1: Replace the test file content**

Drop `using Anela.Heblo.Domain.Features.Catalog;` and the predicate test (it now lives in `ProductCatalogQueryServiceTests`). Replace `ICatalogRepository` setup with `IProductCatalogQueryService`. Keep the existing `IServiceScopeFactory` → `IServiceScope` → `IServiceProvider` chain (lines 13–25 of the original) — that's still the pattern under singleton-resolves-transient.

Overwrite `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class ProductEnrichmentCacheTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IProductCatalogQueryService> _queryService = new();

    public ProductEnrichmentCacheTests()
    {
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider
            .Setup(sp => sp.GetService(typeof(IProductCatalogQueryService)))
            .Returns(_queryService.Object);
    }

    private ProductEnrichmentCache Create(int ttlMinutes = 60) =>
        new(_scopeFactory.Object, Options.Create(new KnowledgeBaseOptions
        {
            ProductEnrichmentCacheTtlMinutes = ttlMinutes
        }));

    private void SetupQueryService(params ProductCatalogEntry[] entries)
    {
        _queryService
            .Setup(s => s.GetActiveProductsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
    }

    [Fact]
    public async Task GetProductLookupAsync_MapsEntriesIntoLookup()
    {
        SetupQueryService(
            new ProductCatalogEntry { ProductCode = "PRD001", ProductName = "Sérum ABC", Url = null },
            new ProductCatalogEntry { ProductCode = "GDS001", ProductName = "Krém XYZ", Url = "https://example.com/gds001" });

        var cache = Create();
        var lookup = await cache.GetProductLookupAsync();

        Assert.Equal(2, lookup.Count);
        Assert.True(lookup.ContainsKey("PRD001"));
        Assert.Equal("Sérum ABC", lookup["PRD001"].ProductName);
        Assert.Null(lookup["PRD001"].Url);
        Assert.Equal("https://example.com/gds001", lookup["GDS001"].Url);
    }

    [Fact]
    public async Task GetProductLookupAsync_WithinTtl_ReturnsCachedResult_QueryServiceCalledOnce()
    {
        SetupQueryService(new ProductCatalogEntry { ProductCode = "PRD001", ProductName = "Sérum ABC" });
        var cache = Create(ttlMinutes: 60);

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _queryService.Verify(
            s => s.GetActiveProductsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductLookupAsync_AfterTtlExpiry_RefreshesFromQueryService()
    {
        SetupQueryService(new ProductCatalogEntry { ProductCode = "PRD001", ProductName = "Sérum ABC" });
        var cache = Create(ttlMinutes: 0); // TTL = 0 → always expired

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _queryService.Verify(
            s => s.GetActiveProductsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductEnrichmentCacheTests"`
Expected: Build fails because `ProductEnrichmentCache` still uses `ICatalogRepository` internally, so the production code still compiles, but the test mock chain returns the new service. Concretely, the cache's runtime call `GetRequiredService<ICatalogRepository>()` will throw `InvalidOperationException` (the mock `_serviceProvider` no longer responds to `typeof(ICatalogRepository)`). Tests will execute (compile passes) but the three behavior tests will throw at runtime. That is the correct TDD-Red state for this task.

Note: If the compiler did fail (it shouldn't because the test file only uses Contracts types now), Task 7 below restores green by changing the production code.

- [ ] **Step 3: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs
git commit -m "test(knowledge-base): switch ProductEnrichmentCacheTests to IProductCatalogQueryService"
```

---

### Task 7: Refactor `ProductEnrichmentCache` to depend on the contract (TDD-Green)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs` (full rewrite)

- [ ] **Step 1: Rewrite the cache to use `IProductCatalogQueryService`**

Drop `using Anela.Heblo.Domain.Features.Catalog;`. Add `using Anela.Heblo.Application.Features.Catalog.Contracts;`. Replace the repository resolution and predicate call. Keep the singleton-safe `IServiceScopeFactory` indirection, the double-check locking, the `SemaphoreSlim`, and the `_cache` / `_lastLoaded` fields exactly as they are. The `_cache` dictionary semantics (`ProductCode` → `ProductEnrichmentEntry`) and its shape are unchanged so downstream consumers don't notice.

Overwrite `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs` with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class ProductEnrichmentCache : IProductEnrichmentCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KnowledgeBaseOptions> _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyDictionary<string, ProductEnrichmentEntry> _cache =
        new Dictionary<string, ProductEnrichmentEntry>();
    private DateTime _lastLoaded = DateTime.MinValue;

    public ProductEnrichmentCache(
        IServiceScopeFactory scopeFactory,
        IOptions<KnowledgeBaseOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, ProductEnrichmentEntry>> GetProductLookupAsync(
        CancellationToken ct = default)
    {
        var ttl = TimeSpan.FromMinutes(_options.Value.ProductEnrichmentCacheTtlMinutes);

        if (DateTime.UtcNow - _lastLoaded < ttl)
            return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastLoaded < ttl)
                return _cache;

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IProductCatalogQueryService>();

            var products = await service.GetActiveProductsAsync(ct);

            _cache = products.ToDictionary(
                p => p.ProductCode,
                p => new ProductEnrichmentEntry
                {
                    ProductCode = p.ProductCode,
                    ProductName = p.ProductName,
                    Url = p.Url
                });

            _lastLoaded = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

- [ ] **Step 2: Run the cache tests — they should now pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductEnrichmentCacheTests"`
Expected: 3 tests passed, 0 failed.

- [ ] **Step 3: Verify zero Catalog-domain references remain under `Features/KnowledgeBase/`**

Run: `grep -rn "Anela.Heblo.Domain.Features.Catalog\|ICatalogRepository\|CatalogAggregate\|ProductType" backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ || echo OK`
Expected: `OK` (no matches).

- [ ] **Step 4: Run `dotnet format` on touched files**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs`
Expected: No errors, formatting applied.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs
git commit -m "refactor(knowledge-base): consume IProductCatalogQueryService instead of ICatalogRepository"
```

---

### Task 8: Final verification — full build, full test suite, downstream consumers green

**Files:** none modified — verification only.

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors, 0 warnings (or only pre-existing warnings unrelated to this change).

- [ ] **Step 2: Full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests pass. The downstream consumers of `IProductEnrichmentCache` (`AskQuestionHandlerTests`, `PostAnswerEnrichmentMiddlewareTests`) must remain green without changes — their contract surface (`IProductEnrichmentCache.GetProductLookupAsync`) was not modified.

- [ ] **Step 3: Verify downstream consumer signatures untouched**

Run: `git diff main -- backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/IProductEnrichmentCache.cs backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentEntry.cs`
Expected: empty output (no changes to either file).

- [ ] **Step 4: Verify the architectural invariant**

Run: `grep -rn "Anela.Heblo.Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ || echo OK`
Expected: `OK`.

Run: `grep -rn "Anela.Heblo.Domain.Features.Catalog\|CatalogAggregate\|ProductType" backend/test/Anela.Heblo.Tests/KnowledgeBase/ || echo OK`
Expected: `OK`.

- [ ] **Step 5: Final `dotnet format` over the whole solution**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: No diagnostics that block the build.

- [ ] **Step 6: Final commit if formatter touched anything**

```bash
git status
# If there are remaining changes from `dotnet format`:
git add -u backend/
git commit -m "chore: dotnet format"
```

If `git status` reports a clean tree, skip the commit.

---

## Self-Review Checklist (already applied)

- **Spec coverage:** FR-1 → Tasks 1, 2. FR-2 → Tasks 3, 4. FR-3 → Task 5. FR-4 → Task 7. FR-5 → Tasks 3 (new Catalog tests), 6 (KB tests rewrite). FR-6 → Task 8 step 3 (verify untouched files). NFR-1/2/3/4 → preserved by design; verified by step 3/4 of Task 8.
- **Arch-review amendments applied:** §1 (`public sealed class` not `internal sealed`) → Task 4 step 1. §2 (test path under `Features/Catalog/Services/`) → Task 3 file path. §3 (registration placement after line 62) → Task 5 step 1. §4 (predicate-capture pattern) → Task 3 test 1. §5 (KB test mocking chain preserved) → Task 6 test class constructor.
- **Placeholders:** none. Every code step shows exact code; every command shows the exact invocation.
- **Type consistency:** `ProductCatalogEntry` properties (`ProductCode`, `ProductName`, `Url`) are spelled identically in Tasks 1, 3, 4, 6, 7. `GetActiveProductsAsync` signature is identical across Tasks 2, 3, 4, 6, 7. `IProductEnrichmentCache.GetProductLookupAsync` is unchanged (verified Task 8 step 3).
