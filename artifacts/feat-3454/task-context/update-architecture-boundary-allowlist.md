### task: update-architecture-boundary-allowlist

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:151-157`

This task removes the now-stale `ProductPairingDqtComparer -> ICatalogResilienceService` entry from the `DataQuality -> Catalog` architecture-boundary allowlist, since that dependency no longer exists after the previous task. The four remaining, genuinely out-of-scope entries (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`, `EshopStock`) stay untouched. This file enforces module-boundary rules via a reflection-based xUnit test (`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`) with a `"DataQuality -> Catalog"` rule driven by the `DataQualityCatalogAllowlist` set — leaving a stale entry would contradict that file's own documented convention that entries are removed once the underlying violation is fixed.

- [ ] Step 1: Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Find this block (currently at lines 149-163):

```csharp
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing,
        // wrapped in ICatalogResilienceService for transient-fault protection.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService",

        // Compiler-generated async state machines and lambdas for CompareAsync capture EshopStock.
        // The declaring-type check covers nested types (<CompareAsync>d__6, <<CompareAsync>b__6_1>d)
        // via this single parent entry.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock",
    };
```

Replace it with (the `ICatalogResilienceService` line removed, and the explanatory comment above the first four entries trimmed so it no longer references resilience wrapping that no longer applies):

```csharp
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",

        // Compiler-generated async state machines and lambdas for CompareAsync capture EshopStock.
        // The declaring-type check covers nested types (<CompareAsync>d__6, <<CompareAsync>b__6_1>d)
        // via this single parent entry.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock",
    };
```

Do not change the block comment above this set (currently at lines 145-148, describing the `DataQualityCatalogAllowlist` follow-up tracking) — it still accurately describes the four remaining out-of-scope entries and the tracked `IProductPairingQuery` follow-up.

- [ ] Step 2: From the repository root, build the solution:

```bash
dotnet build Anela.Heblo.sln
```

Confirm the build succeeds with no errors.

- [ ] Step 3: Run the architecture boundary tests:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Confirm all tests in this file pass, including the `"DataQuality -> Catalog"` rule — this proves `ProductPairingDqtComparer` no longer references `Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService` (or any other non-allowlisted Catalog Application-layer type) via reflection, independent of the earlier manual `grep` check.

- [ ] Step 4: Confirm no remaining reference to `ICatalogResilienceService` exists anywhere in the allowlist file:

```bash
grep -n "ICatalogResilienceService" backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
```

Confirm this returns no matches (empty output).

- [ ] Step 5: Run the full test suite one final time to confirm nothing else regressed from the full set of changes across all three tasks:

```bash
dotnet test Anela.Heblo.sln
```

Confirm the run completes with 0 failures.

- [ ] Step 6: Run `dotnet format` to confirm formatting is clean across all changed files:

```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

If this reports formatting issues, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to auto-fix, then re-stage the affected files before committing.

- [ ] Step 7: Commit:

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Remove stale ICatalogResilienceService entry from DataQuality->Catalog allowlist"
```
