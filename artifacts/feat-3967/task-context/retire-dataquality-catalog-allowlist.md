### task: retire-dataquality-catalog-allowlist

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:128-144`

- [ ] **Step 1: Empty the resolved allowlist**

Find this block (the comment plus the `DataQualityCatalogAllowlist` declaration):

```csharp
    // Allowlist for DataQuality -> Catalog. Pre-existing ProductPairingDqtComparer references
    // are out of scope for the 2026-06-03 StockWriteBackDqtComparer decoupling.
    // Track follow-up: introduce DataQuality-owned IProductPairingQuery contract and Catalog-side
    // adapter that surfaces eshop/erp product snapshots without leaking Catalog types.
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

Replace it with (mirroring the "Empty — ..." comment style already used for `LeafletAllowlist`/`ArticleAllowlist`):

```csharp
    // Allowlist for DataQuality -> Catalog. Empty — ProductPairingDqtComparer now consumes
    // the DataQuality-owned IDqtEshopStockSource/IDqtErpStockSource contracts; the Catalog
    // adapters (DataQualityEshopStockSourceAdapter, DataQualityErpStockSourceAdapter) live in
    // Catalog.Infrastructure and implement them there, so no DataQuality type needs to
    // reference Catalog directly.
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Run the architecture test to verify it still passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: `Passed!` — all `ModuleBoundaryRule` theory cases pass, including `"DataQuality -> Catalog"` with the now-empty allowlist (confirms `ProductPairingDqtComparer` no longer references any Catalog-namespaced type).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Retire resolved DataQuality -> Catalog allowlist entries for ProductPairingDqtComparer"
```

---

