### task: switch-product-pairing-comparer-to-dqt-resilience-contract

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs:1-28`
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs:1-15`

This task switches `ProductPairingDqtComparer`'s constructor dependency from the Catalog-owned `ICatalogResilienceService` to the new DataQuality-owned `IDqtResilienceService`, and updates the existing unit test's mock type to match. No method body or test assertion changes — this is a pure type substitution enabled by the identical interface shape.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`. Replace the `using` block at the top of the file (lines 1-5):

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;
```

with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;
```

- [ ] Step 2: In the same file, replace the field declaration and constructor (lines 9-28):

```csharp
public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IEshopStockClient eshopStockClient,
        IErpStockClient erpStockClient,
        ICatalogResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
        _resilienceService = resilienceService;
        _logger = logger;
    }
```

with:

```csharp
public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly IDqtResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IEshopStockClient eshopStockClient,
        IErpStockClient erpStockClient,
        IDqtResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
        _resilienceService = resilienceService;
        _logger = logger;
    }
```

Do not change anything else in this file — `CompareAsync` and `IsSellable` are unchanged.

- [ ] Step 3: Open `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`. Replace the `using` block at the top of the file (lines 1-7):

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
```

with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
```

- [ ] Step 4: In the same file, replace this line (line 15):

```csharp
    private readonly Mock<ICatalogResilienceService> _resilienceMock = new();
```

with:

```csharp
    private readonly Mock<IDqtResilienceService> _resilienceMock = new();
```

Do not change anything else in this file — the constructor call in `CreateSut()`, all 5 test methods, and all assertions are unchanged since `IDqtResilienceService.ExecuteWithResilienceAsync<T>` has the identical signature to `ICatalogResilienceService.ExecuteWithResilienceAsync<T>`.

- [ ] Step 5: From the repository root, build the solution:

```bash
dotnet build Anela.Heblo.sln
```

Confirm the build succeeds with no errors and no warnings about unused `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` in either modified file (both files' Catalog-Infrastructure `using` directives were removed in steps 1 and 3).

- [ ] Step 6: Run the comparer's unit tests:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ProductPairingDqtComparerTests"
```

Confirm all 5 existing tests pass: `CompareAsync_ReturnsEmpty_WhenAllProductsPaired`, `CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp`, `CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp`, `CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts`, `CompareAsync_WrapsBothListCalls_WithResilience`.

- [ ] Step 7: Verify no file under DataQuality's `Services/` folder references the Catalog Application-layer namespace anymore (this scans the whole folder, not just the file changed in this task, to confirm the module-wide boundary claim from the spec — `IEshopStockClient`/`IErpStockClient` are Domain-layer, not Application-layer, so they are correctly excluded by this pattern):

```bash
grep -rn "using Anela.Heblo.Application.Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality/Services/
```

Confirm this returns no matches (empty output).

- [ ] Step 8: Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs
git commit -m "Switch ProductPairingDqtComparer to DataQuality-owned IDqtResilienceService"
```

---
