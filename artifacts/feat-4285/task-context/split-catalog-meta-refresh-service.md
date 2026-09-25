### task: split-catalog-meta-refresh-service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs`

- [ ] **Step 1: Create `CatalogMetaRefreshService.cs`**

```csharp
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes catalog metadata: product attributes, lots, eshop/ERP prices, and eshop URLs.
/// </summary>
public sealed class CatalogMetaRefreshService
{
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IProductEshopUrlClient _productEshopUrlClient;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogMetaRefreshService> _logger;

    public CatalogMetaRefreshService(
        ICatalogAttributesClient attributesClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogMetaRefreshService> logger)
    {
        _attributesClient = attributesClient ?? throw new ArgumentNullException(nameof(attributesClient));
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _productPriceEshopClient = productPriceEshopClient ?? throw new ArgumentNullException(nameof(productPriceEshopClient));
        _productPriceErpClient = productPriceErpClient ?? throw new ArgumentNullException(nameof(productPriceErpClient));
        _productEshopUrlClient = productEshopUrlClient ?? throw new ArgumentNullException(nameof(productEshopUrlClient));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        _cacheStore.SetCatalogAttributesData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct));
    }

    public async Task RefreshLotsData(CancellationToken ct)
    {
        _cacheStore.SetLotsData((await _lotsClient.GetAsync(cancellationToken: ct)).ToList());
    }

    public async Task RefreshEshopPricesData(CancellationToken ct)
    {
        _cacheStore.SetEshopPriceData((await _productPriceEshopClient.GetAllAsync(ct)).ToList());
    }

    public async Task RefreshErpPricesData(CancellationToken ct)
    {
        _cacheStore.SetErpPriceData((await _productPriceErpClient.GetAllAsync(false, ct)).ToList());
    }

    public async Task RefreshEshopUrlData(CancellationToken ct)
    {
        _cacheStore.SetEshopUrlData((await _productEshopUrlClient.GetAllAsync(ct)).ToList());
    }
}
```

- [ ] **Step 2: Build to confirm it compiles standalone**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Note on tests** — `CatalogDataRefreshServiceTests.cs` contains no test cases for `RefreshAttributesData`, `RefreshLotsData`, `RefreshEshopPricesData`, `RefreshErpPricesData`, or `RefreshEshopUrlData` today. Per spec FR-4 ("do not add new test cases beyond what's needed to preserve 1:1 coverage"), do **not** create a `CatalogMetaRefreshServiceTests.cs` file in this task — there is nothing to migrate. (Adding new coverage for these methods is legitimate future work but is out of scope for this SRP-only refactor.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs
git commit -m "feat(catalog): extract CatalogMetaRefreshService from CatalogDataRefreshService"
```

---

