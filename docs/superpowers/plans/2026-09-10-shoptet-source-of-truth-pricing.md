# Shoptet as the Price Source of Truth — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Shoptet the authoritative retail price, so Heblo compares Shoptet against Flexi, alerts on divergence, and writes an edit through to Shoptet and then Flexi — with no master price of its own.

**Architecture:** Delete the three-way sync reconciliation (master table, per-product state machine, conflict resolution). The comparison becomes a live read of Shoptet + Flexi, surfaced as a new `IDriftDqtComparer` in the existing DataQuality framework and a dashboard tile. Price edits become an ordered write-through: pre-flight Flexi's ceník id and VAT rate, write Shoptet, then write Flexi, appending an append-only change-log row on every outcome.

**Tech Stack:** .NET 8, MediatR, EF Core + PostgreSQL, xUnit + FluentAssertions + Moq, React 18 + TanStack Query + Tailwind, NSwag-generated TypeScript client.

**Spec:** [docs/superpowers/specs/2026-09-10-shoptet-source-of-truth-pricing-design.md](../specs/2026-09-10-shoptet-source-of-truth-pricing-design.md)

## Global Constraints

Every task's requirements implicitly include these.

- **DTOs are classes, never C# records.** OpenAPI client generators mishandle record parameter order.
- **Every Application `*Response` must inherit `BaseResponse`.** A reflection contract test fails in CI otherwise.
- **Validators are registered manually**, per module, in `ProductPricingModule` / `DataQualityModule`. There is no `AddValidatorsFromAssembly` in this project.
- **Money rounds to 2 decimals with `MidpointRounding.AwayFromZero`** — the convention for every money value in this codebase.
- **Backend test loop:** `dotnet build <testproj>` then `dotnet test <testproj> --no-build -p:UseSharedCompilation=false --filter "<filter>"`. Never run bare `dotnet test` — it hangs when another worktree is building concurrently.
- **Frontend build gate is `CI=false npm run build`, run from `frontend/`.** `npx tsc --noEmit` false-greens on this repo (react-i18next `.d.ts` parse errors abort the whole check).
- **Frontend test runner is `npx react-scripts test`**, never `npx jest` (TypeScript parse errors).
- **Regenerate the TypeScript client** after any controller or DTO change: `dotnet msbuild -t:GenerateFrontendClientManual`.
- **The generated client throws on non-2xx.** `if (!response.success)` branches are dead code; read `errorCode` from the caught `SwaggerException` — it arrives as a **string**, not a number.
- **New error codes need a Czech string in `frontend/src/i18n.ts`** or the error-handling test fails. The `36XX` Product Pricing bucket already exists in `ErrorHandlingTests.cs`, so no new bucket line is needed.
- **Adapter tests assert request bodies structurally** with `JsonDocument`, never with `Should().Contain(...)`. There is no Shoptet sandbox; these assertions are the only wire contract.
- **Never write to Flexi by product code.** Flexi does not distinguish create from update — a write by code silently creates a new ceník item. Always the numeric `ErpItemId`.

---

## File Structure

**Deleted (Task 1)**

| Path | Was responsible for |
|---|---|
| `Domain/Features/ProductPricing/PriceSyncDecider.cs` + `PriceSyncDecision.cs` + `PriceSyncAction.cs` + `PriceSyncStatus.cs` + `PriceSyncTarget.cs` | Three-way "who moved" decision |
| `Domain/Features/ProductPricing/ProductPrice.cs` + `ProductPriceSyncState.cs` + `IProductPriceRepository.cs` | Master price + per-product sync state |
| `Application/.../Services/ProductPriceSyncService.cs` + `IProductPriceSyncService.cs` + `PriceSyncRunResult.cs` | The sync run |
| `Application/.../Infrastructure/Jobs/ProductPriceSyncJob.cs` | Hourly push |
| `Application/.../UseCases/{TriggerPriceSync,GetPriceSyncConflicts,ResolvePriceSyncConflict,GetProductPrices}/` | Sync-era use cases |
| `Application/.../Contracts/{PriceConflictResolution,PriceSyncConflictDto,ProductPriceDto}.cs` | Sync-era DTOs |
| `Persistence/ProductPricing/{ProductPriceConfiguration,ProductPriceSyncStateConfiguration,ProductPriceRepository}.cs` | Master persistence |
| `frontend/src/components/pricing/{ProductPriceGrid,PriceConflictBanner}.tsx` | Master grid + conflict UI |

**Created**

| Path | Responsibility |
|---|---|
| `Domain/Features/ProductPricing/ProductPriceChangeLog.cs` | Append-only edit history entity |
| `Domain/Features/ProductPricing/IProductPriceChangeLogRepository.cs` | `AppendAsync` only |
| `Persistence/ProductPricing/ProductPriceChangeLogConfiguration.cs` | EF mapping |
| `Persistence/ProductPricing/ProductPriceChangeLogRepository.cs` | EF implementation |
| `Application/.../Contracts/IPriceComparisonSource.cs` **in DataQuality** | Consumer-owned cross-module contract |
| `Application/Features/ProductPricing/Infrastructure/PriceComparisonDqtAdapter.cs` | Provider adapter |
| `Application/Features/DataQuality/Services/PriceComparisonDqtComparer.cs` | `IDriftDqtComparer` implementation |
| `Application/Features/DataQuality/Infrastructure/Jobs/PriceComparisonDqtJob.cs` | Nightly run |
| `Application/Features/DataQuality/DashboardTiles/PriceComparisonStatusTile.cs` | Tile |
| `frontend/src/components/dashboard/tiles/PriceComparisonTile.tsx` | Tile renderer |

**Modified**

| Path | Change |
|---|---|
| `Application/.../Services/PriceDivergenceReportService.cs` → `PriceComparisonService.cs` | Drop master column + repository dep; add Flexi tolerance |
| `Application/.../UseCases/SetProductPrice/SetProductPriceHandler.cs` | Becomes write-through |
| `Application/Shared/ErrorCodes.cs` | Four new 36XX codes; `ProductPriceConflictNotFound` removed |
| `API/Controllers/ProductPricingController.cs` | Drop four endpoints |
| `Persistence/ApplicationDbContext.cs` | Swap two DbSets for one |
| `Domain/Features/DataQuality/DqtTestType.cs` | `PriceComparison = 5` |
| `frontend/src/pages/ProductPricingPage.tsx` | Single screen, inline edit |
| `frontend/src/api/hooks/useProductPricing.ts` | Drop three hooks |
| `frontend/src/components/dashboard/drillDownRoutes.ts` | New `productPricing` route key |
| `frontend/src/i18n.ts` | Four Czech error strings |

---

## Task 1: Remove Heblo's master price ownership

Everything here is inseparable: `PriceDivergenceReportService` and `SetProductPriceHandler` both depend on `IProductPriceRepository`, so the repository cannot be deleted without them. The deliverable is a working read-only comparison screen with no edit path — a coherent intermediate state.

**Files:**
- Delete: the 8 rows in the "Deleted" table above (all paths listed there)
- Delete: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SetProductPrice/` (all 4 files — reintroduced in Task 4)
- Delete: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/{ProductPriceRepositoryTests,ResolvePriceSyncConflictHandlerTests,GetProductPricesHandlerTests,SetProductPriceHandlerTests,ProductPriceSyncJobTests,ProductPriceSyncServiceTests}.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Domain/ProductPricing/PriceSyncDeciderTests.cs`
- Delete: `frontend/src/components/pricing/{ProductPriceGrid,PriceConflictBanner}.tsx`
- Modify: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceDivergenceReportService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/PriceDivergenceRowDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ProductPricing/ProductPricingModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:62-63`
- Modify: `frontend/src/pages/ProductPricingPage.tsx`
- Modify: `frontend/src/api/hooks/useProductPricing.ts`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/PriceDivergenceReportServiceTests.cs`

**Interfaces:**
- Consumes: nothing (first task)
- Produces: `PriceDivergenceReportService.BuildReportAsync(CancellationToken) -> Task<PriceDivergenceReportResult>` with constructor `(ICatalogRepository, IEshopPriceListClient, IProductPriceErpClient)` — the `IProductPriceRepository` parameter is gone. `PriceDivergenceRowDto` no longer has `HebloMasterPriceWithVat`.

- [ ] **Step 1: Update the report service test to the new constructor and drop master-price assertions**

This file already has its own fixture style — `CreateService()` plus `GivenCatalog` / `GivenShoptetPrices` / `GivenErpPrices` / `GivenMasterPrices` helpers. **Use those helpers; do not introduce a different style.**

Remove the `Mock<IProductPriceRepository> _priceRepository` field, the whole `GivenMasterPrices` helper and every call to it, and the fourth argument of `CreateService()`. Delete any assertion referencing `HebloMasterPriceWithVat`. Then add this test, which pins the behaviour that must survive the deletion:

```csharp
[Fact]
public async Task classifies_a_product_as_in_agreement_when_shoptet_and_flexi_match()
{
    // Arrange
    GivenCatalog(("A", ProductType.Product, "Alpha"));
    GivenShoptetPrices(("A", 390.00m));
    GivenErpPrices(new ProductPriceErp
    {
        ProductCode = "A", PriceWithVat = 390.00m, PriceWithoutVat = 322.31m,
        ErpItemId = 11, ErpPriceType = "bezDph",
    });

    // Act
    var result = await CreateService().BuildReportAsync(CancellationToken.None);

    // Assert
    result.Rows.Should().ContainSingle()
        .Which.Kind.Should().Be(PriceDivergenceKind.InAgreement);
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
```
Expected: FAIL — `PriceDivergenceReportService` still requires four constructor arguments.

- [ ] **Step 3: Delete the sync machinery**

```bash
cd /Users/pajgrtondrej/orca/workspaces/Anela.Heblo/knifefish
git rm -r \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncDecider.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncDecision.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncAction.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncStatus.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncTarget.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPrice.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPriceSyncState.cs \
  backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductPriceRepository.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/ProductPriceSyncService.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/IProductPriceSyncService.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceSyncRunResult.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/Jobs/ProductPriceSyncJob.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/PriceConflictResolution.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/PriceSyncConflictDto.cs \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/ProductPriceDto.cs \
  backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceConfiguration.cs \
  backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceSyncStateConfiguration.cs \
  backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceRepository.cs
git rm -r \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/TriggerPriceSync \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/GetPriceSyncConflicts \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/ResolvePriceSyncConflict \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/GetProductPrices \
  backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SetProductPrice
git rm \
  backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceRepositoryTests.cs \
  backend/test/Anela.Heblo.Tests/Features/ProductPricing/ResolvePriceSyncConflictHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/ProductPricing/GetProductPricesHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/ProductPricing/SetProductPriceHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncJobTests.cs \
  backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncServiceTests.cs \
  backend/test/Anela.Heblo.Tests/Domain/ProductPricing/PriceSyncDeciderTests.cs \
  frontend/src/components/pricing/ProductPriceGrid.tsx \
  frontend/src/components/pricing/PriceConflictBanner.tsx
```

- [ ] **Step 4: Drop the master column from the report service and DTO**

In `PriceDivergenceReportService.cs`: remove the `IProductPriceRepository _priceRepository` field, its constructor parameter, the `masterPrices` dictionary and its `GetAllAsync` call, the `masterPrices` parameter of `BuildRow`, and the `HebloMasterPriceWithVat` assignment.

In `PriceDivergenceRowDto.cs`, delete:

```csharp
    /// <summary>The current <c>ProductPrices</c> master row, if any.</summary>
    public decimal? HebloMasterPriceWithVat { get; set; }
```

and change the class summary to `<summary>One in-scope product's price comparison across Shoptet and Flexi.</summary>`.

- [ ] **Step 5: Clean the module, controller and DbContext**

In `ProductPricingModule.cs` remove the `IProductPriceRepository`, `IProductPriceSyncService` and `ProductPriceSyncJob` registrations and both validator/behavior blocks, leaving only:

```csharp
services.AddScoped<IPriceDivergenceReportService, PriceDivergenceReportService>();
```

In `ProductPricingController.cs` delete the `GetPrices`, `SetPrice`, `TriggerSync`, `GetConflicts` and `ResolveConflict` actions and their now-unused usings, leaving only `GetDivergenceReport`.

In `ApplicationDbContext.cs` delete lines 62-63 (`ProductPrices`, `ProductPriceSyncStates` DbSets) and the corresponding `ApplyConfiguration` calls if present.

- [ ] **Step 6: Strip the frontend to the comparison tab**

In `ProductPricingPage.tsx`: remove the tab bar, the `useProductPrices` / `useTriggerPriceSync` usage, the sync button, the success and error banners, and render `<PriceDivergenceReport />` directly under the header.

In `useProductPricing.ts`, delete exactly these exported hooks — **the names matter, verify each against the file before deleting**: `useProductPrices`, `useSetProductPrice`, `usePriceSyncConflicts`, `useResolvePriceConflict`, `useTriggerPriceSync`. Also delete the `QUERY_KEYS.prices` / `.conflicts` entries, the `SetProductPriceInput` / `ResolvePriceConflictInput` interfaces, the `GENERIC_*` constants for those flows, and every re-export of a deleted generated type (`ProductPriceDto`, `PriceSyncConflictDto`, `PriceSyncStatus`, `PriceSyncTarget`, `PriceConflictResolution`).

**Keep `usePriceDivergenceReport`** (note the full name) and the divergence re-exports (`PriceDivergenceRowDto`, `PriceDivergenceSummaryDto`, `PriceDivergenceKind`).

Delete `frontend/src/pages/__tests__/ProductPricingPage.test.tsx` assertions covering the grid, sync button and conflicts; keep only those asserting the header and the comparison table render.

- [ ] **Step 7: Add the drop migration**

```bash
dotnet ef migrations add DropProductPriceMasterTables \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Review the generated file: it must contain exactly two `DropTable` calls, for `ProductPrices` and `ProductPriceSyncStates`. Migrations are applied manually in this project — do not run `database update` against any shared database.

- [ ] **Step 8: Regenerate the TypeScript client and verify everything builds**

```bash
dotnet msbuild -t:GenerateFrontendClientManual
dotnet build Anela.Heblo.sln -v q --nologo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPricing"
cd frontend && CI=false npm run build && npx react-scripts test --watchAll=false --testPathPattern="pricing|Pricing"
```
Expected: 0 errors, all pricing tests pass including the new `in_agreement` test.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: drop Heblo's master retail price and its sync machinery

Shoptet becomes the source of truth, so the three-way sync decider, the
per-product sync state machine, the conflict resolution flow and the
master price table all lose their reason to exist. What remains is the
read-only Shoptet-vs-Flexi comparison."
```

---

## Task 2: Rename to PriceComparisonService and add the Flexi rounding tolerance

Flexi stores `cenaZakl` excluding VAT and reconstructs the with-VAT price on read, which does not round-trip: 190.00 stores as 157.02 and reads back as 189.99. Without a tolerance a large share of the catalogue reports as divergent by one haléř forever, and the Task 6 tile is permanently orange.

**Files:**
- Rename: `Services/PriceDivergenceReportService.cs` → `Services/PriceComparisonService.cs`
- Rename: `Services/IPriceDivergenceReportService.cs` → `Services/IPriceComparisonService.cs`
- Rename: `Services/PriceDivergenceReportResult.cs` → `Services/PriceComparisonResult.cs`
- Modify: `ProductPricingModule.cs`, `UseCases/GetPriceDivergenceReport/GetPriceDivergenceReportHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/PriceComparisonServiceTests.cs` (renamed from `PriceDivergenceReportServiceTests.cs`)

**Interfaces:**
- Consumes: `PriceDivergenceReportService` from Task 1 (3-arg constructor)
- Produces: `IPriceComparisonService.BuildReportAsync(CancellationToken) -> Task<PriceComparisonResult>`; `PriceComparisonResult { List<PriceDivergenceRowDto> Rows; PriceDivergenceSummaryDto Summary; }`. `PriceDivergenceKind` and `PriceDivergenceRowDto` keep their names.

- [ ] **Step 1: Write the failing tolerance tests**

**The theory parameter must be `double`, not `decimal`.** C# forbids `decimal` as an attribute argument, so `[InlineData(189.99)]` on a `decimal` parameter is a compile error. Take a `double` and cast at the use site.

```csharp
[Theory]
[InlineData(189.99)] // Flexi's round-trip loss on a 190.00 with-VAT price
[InlineData(190.01)]
public async Task treats_a_flexi_price_within_one_hundredth_as_in_agreement(double flexiPriceWithVat)
{
    // Arrange
    ArrangeSingleProduct(shoptetPriceWithVat: 190.00m, flexiPriceWithVat: (decimal)flexiPriceWithVat);

    // Act
    var result = await CreateService().BuildReportAsync(CancellationToken.None);

    // Assert
    result.Rows.Should().ContainSingle()
        .Which.Kind.Should().Be(PriceDivergenceKind.InAgreement);
}

[Theory]
[InlineData(189.97)]
[InlineData(190.03)]
public async Task reports_a_flexi_price_beyond_the_tolerance_as_divergent(double flexiPriceWithVat)
{
    // Arrange
    ArrangeSingleProduct(shoptetPriceWithVat: 190.00m, flexiPriceWithVat: (decimal)flexiPriceWithVat);

    // Act
    var result = await CreateService().BuildReportAsync(CancellationToken.None);

    // Assert
    result.Rows.Should().ContainSingle()
        .Which.Kind.Should().Be(PriceDivergenceKind.FlexiDiffers);
}
```

Add the helper the tests share, built on the file's existing `Given*` helpers:

```csharp
private void ArrangeSingleProduct(decimal shoptetPriceWithVat, decimal flexiPriceWithVat)
{
    GivenCatalog(("A", ProductType.Product, "Alpha"));
    GivenShoptetPrices(("A", shoptetPriceWithVat));
    GivenErpPrices(new ProductPriceErp
    {
        ProductCode = "A", PriceWithVat = flexiPriceWithVat, PriceWithoutVat = 157.02m,
        ErpItemId = 11, ErpPriceType = "bezDph",
    });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~PriceComparisonServiceTests"
```
Expected: the two `within_one_hundredth` cases FAIL, reporting `FlexiDiffers` where `InAgreement` is expected.

- [ ] **Step 3: Add the tolerance and rename**

Rename the three service files and the interface/class/result names, updating `ProductPricingModule` and `GetPriceDivergenceReportHandler` to match. In the renamed `PriceComparisonService.cs`, add the constant and use it in the classifier:

```csharp
/// <summary>
/// Flexi stores <c>cenaZakl</c> excluding VAT and reconstructs the with-VAT price as
/// <c>cena * (100 + vat) / 100</c> on read, which does not round-trip exactly
/// (190.00 -> cenaZakl 157.02 -> 189.99). Without this tolerance a large share of the
/// catalogue would report as divergent by one haléř and the dashboard tile would be
/// permanently orange. Shoptet stores the with-VAT price directly and gets no tolerance.
/// </summary>
private const decimal FlexiRoundTripTolerance = 0.01m;

private static bool PricesAgree(decimal shoptetPriceWithVat, decimal flexiPriceWithVat) =>
    Math.Abs(Math.Round(shoptetPriceWithVat, PriceDecimals, MidpointRounding.AwayFromZero)
           - Math.Round(flexiPriceWithVat, PriceDecimals, MidpointRounding.AwayFromZero))
    <= FlexiRoundTripTolerance;
```

Replace the equality check inside `ClassifyRow` with `PricesAgree(...)`. Leave the `MissingInShoptet` → `MissingInFlexi` → `FlexiPriceTypeUnknown` precedence exactly as it is.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~PriceComparisonServiceTests"
```
Expected: PASS, all four theory cases plus the existing classification tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename to PriceComparisonService and absorb Flexi's rounding loss

Flexi's with-VAT price is reconstructed from a without-VAT store and
loses a haléř on the round trip. Comparing without a tolerance would
report most of the catalogue as divergent."
```

---

## Task 3: Price change log

Append-only history of edits made through Heblo. Never read to decide anything — it exists to record who changed a price and, critically, to record the partial-failure state where Shoptet was updated and Flexi was not.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPriceChangeLog.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductPriceChangeLogRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceChangeLogConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceChangeLogRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, `ProductPricingModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceChangeLogRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks
- Produces: `IProductPriceChangeLogRepository.AppendAsync(ProductPriceChangeLog entry, CancellationToken) -> Task` — persists immediately (calls `SaveChangesAsync` itself), so callers need no second call.

- [ ] **Step 1: Write the failing repository test**

```csharp
public class ProductPriceChangeLogRepositoryTests
{
    [Fact]
    public async Task appends_an_entry_and_persists_it()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"changelog-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var sut = new ProductPriceChangeLogRepository(context);

        // Act
        await sut.AppendAsync(new ProductPriceChangeLog
        {
            ProductCode = "DEO007005",
            OldPriceWithVat = 190.00m,
            NewPriceWithVat = 210.00m,
            ChangedAt = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            ChangedBy = "ondra@anela.cz",
            ShoptetSucceeded = true,
            FlexiSucceeded = false,
            ErrorMessage = "Flexi timeout",
        }, CancellationToken.None);

        // Assert
        var stored = context.ProductPriceChangeLogs.Single();
        stored.ProductCode.Should().Be("DEO007005");
        stored.ShoptetSucceeded.Should().BeTrue();
        stored.FlexiSucceeded.Should().BeFalse();
        stored.ErrorMessage.Should().Be("Flexi timeout");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
```
Expected: FAIL — `ProductPriceChangeLog` and `ProductPriceChangeLogRepository` do not exist.

- [ ] **Step 3: Write the entity, contract, configuration and repository**

`ProductPriceChangeLog.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// One price edit made through Heblo, recorded whatever the outcome. Append-only and never
/// read to decide anything — it is history, and the only record of the partial-failure state
/// where Shoptet accepted a write and Flexi did not. Edits made directly in Shoptet's own
/// admin do not appear here; the comparison, not this log, is what catches those.
/// </summary>
public class ProductPriceChangeLog
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Shoptet's price immediately before the write; null when it had none.</summary>
    public decimal? OldPriceWithVat { get; set; }
    public decimal NewPriceWithVat { get; set; }

    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = string.Empty;

    public bool ShoptetSucceeded { get; set; }
    public bool FlexiSucceeded { get; set; }

    /// <summary>Null on full success. Truncated to the column length by the repository.</summary>
    public string? ErrorMessage { get; set; }
}
```

`IProductPriceChangeLogRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Append-only. No read method exists until something needs one.</summary>
public interface IProductPriceChangeLogRepository
{
    Task AppendAsync(ProductPriceChangeLog entry, CancellationToken ct);
}
```

`ProductPriceChangeLogConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceChangeLogConfiguration : IEntityTypeConfiguration<ProductPriceChangeLog>
{
    public void Configure(EntityTypeBuilder<ProductPriceChangeLog> builder)
    {
        builder.ToTable("ProductPriceChangeLogs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ProductCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OldPriceWithVat).HasPrecision(18, 2);
        builder.Property(e => e.NewPriceWithVat).HasPrecision(18, 2);
        builder.Property(e => e.ChangedBy).HasMaxLength(256).IsRequired();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(e => new { e.ProductCode, e.ChangedAt });
    }
}
```

`ProductPriceChangeLogRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceChangeLogRepository : IProductPriceChangeLogRepository
{
    /// <summary>Column is varchar(2000); an untruncated remote error body would fail the insert.</summary>
    private const int ErrorMessageMaxLength = 2000;

    private readonly ApplicationDbContext _context;

    public ProductPriceChangeLogRepository(ApplicationDbContext context) => _context = context;

    public async Task AppendAsync(ProductPriceChangeLog entry, CancellationToken ct)
    {
        if (entry.ErrorMessage is { Length: > ErrorMessageMaxLength })
        {
            entry.ErrorMessage = entry.ErrorMessage[..ErrorMessageMaxLength];
        }

        _context.ProductPriceChangeLogs.Add(entry);
        await _context.SaveChangesAsync(ct);
    }
}
```

In `ApplicationDbContext.cs`, where the two deleted DbSets were:

```csharp
public DbSet<ProductPriceChangeLog> ProductPriceChangeLogs { get; set; } = null!;
```

In `ProductPricingModule.cs`:

```csharp
services.AddScoped<IProductPriceChangeLogRepository, ProductPriceChangeLogRepository>();
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPriceChangeLogRepositoryTests"
```
Expected: PASS.

- [ ] **Step 5: Add the migration and commit**

```bash
dotnet ef migrations add AddProductPriceChangeLog \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
git add -A
git commit -m "feat: append-only price change log"
```

---

## Task 4: Write-through price edit

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SetProductPrice/{SetProductPriceRequest,SetProductPriceResponse,SetProductPriceRequestValidator,SetProductPriceHandler}.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IEshopPriceListClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs`
- Modify: `ProductPricingModule.cs`, `frontend/src/i18n.ts`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/SetProductPriceHandlerTests.cs`, `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetPriceListClientTests.cs`

**Interfaces:**
- Consumes: `IProductPriceChangeLogRepository.AppendAsync` (Task 3); `IEshopPriceListClient.SetPriceWithVatAsync`; `IErpPriceWriter.SetPriceWithoutVatAsync(int erpItemId, decimal priceWithoutVat, CancellationToken)`; `IProductPriceErpClient.GetAllAsync(bool forceReload, CancellationToken)`; `IProductVatRateProvider.GetVatRatesAsync(CancellationToken)`; `ICurrentUserService.GetCurrentUser().Email`
- Produces: `SetProductPriceRequest { string ProductCode; decimal PriceWithVat; }` (IRequest<SetProductPriceResponse>); `SetProductPriceResponse : BaseResponse { decimal PriceWithVat; }`; new `IEshopPriceListClient.GetPriceWithVatAsync(string productCode, CancellationToken) -> Task<decimal?>`

- [ ] **Step 1: Write the failing single-product read test**

Add to `ShoptetPriceListClientTests.cs`:

```csharp
[Fact]
public async Task reads_one_products_price_by_code()
{
    // Arrange
    var recorded = new List<HttpRequestMessage>();
    var client = CreateClient(_ => Json("""
        {"data":{"pricelist":[{"code":"DEO007005","includingVat":true,"vatRate":"21.00",
         "price":{"price":"390.00"}}],"paginator":{"page":1,"pageCount":1}},"errors":null}
        """), recorded);

    // Act
    var price = await client.GetPriceWithVatAsync("DEO007005", CancellationToken.None);

    // Assert
    price.Should().Be(390.00m);
    recorded[0].RequestUri!.Query.Should().Contain("code=DEO007005");
}

[Fact]
public async Task returns_null_when_the_product_is_absent_from_the_price_list()
{
    // Arrange
    var client = CreateClient(_ => Json("""
        {"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}
        """));

    // Act
    var price = await client.GetPriceWithVatAsync("NOPE", CancellationToken.None);

    // Assert
    price.Should().BeNull();
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
```
Expected: FAIL — `GetPriceWithVatAsync` is not defined on `IEshopPriceListClient`.

- [ ] **Step 3: Implement the single-product read**

Add to `IEshopPriceListClient`:

```csharp
/// <summary>This product's current price including VAT, or null when it has none in the list.</summary>
Task<decimal?> GetPriceWithVatAsync(string productCode, CancellationToken ct);
```

In `ShoptetPriceListClient`, reusing the existing `TryComputePriceWithVat` helper:

```csharp
public async Task<decimal?> GetPriceWithVatAsync(string productCode, CancellationToken ct)
{
    var priceListId = ResolvePriceListId();

    // `code=` (singular) is supported and returns totalCount 1; `codes=` is rejected outright.
    var url = $"/api/pricelists/{priceListId}?code={Uri.EscapeDataString(productCode)}";
    var snapshot = await GetAsync<PriceListSnapshotResponse>(url, ct);

    var data = snapshot.Data
        ?? throw new HttpRequestException($"Shoptet returned no data block for {url}");

    var item = data.Items.FirstOrDefault(i =>
        string.Equals(i.Code, productCode, StringComparison.OrdinalIgnoreCase));

    var rawPrice = item?.Price?.Price;
    if (item is null || rawPrice is null)
    {
        return null;
    }

    return TryComputePriceWithVat(item, rawPrice, out var priceWithVat) ? priceWithVat : null;
}
```

- [ ] **Step 4: Run to verify it passes**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ShoptetPriceListClientTests"
```
Expected: PASS.

- [ ] **Step 5: Add the three error codes and their Czech strings**

In `ErrorCodes.cs`, in the `36XX` block. **Delete both existing codes** — `ProductPriceNotFound = 3601` and `ProductPriceConflictNotFound = 3604` — because Task 1 deleted their only two users (`SetProductPriceHandler` and `ResolvePriceSyncConflictHandler`); verify with `grep -rn "ProductPriceNotFound\b\|ProductPriceConflictNotFound" backend/src` that nothing references them. The four new codes keep the 36XX bucket non-empty, so `ErrorHandlingTests` still passes. Add:

```csharp
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ProductPriceNotFoundInShoptet = 3602,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ProductPriceFlexiItemIdUnknown = 3603,
    [HttpStatusCode(HttpStatusCode.BadGateway)]
    ProductPriceShoptetWriteFailed = 3605,
    [HttpStatusCode(HttpStatusCode.BadGateway)]
    ProductPriceFlexiWriteFailed = 3606,
```

In `frontend/src/i18n.ts`, replacing **both** the `ProductPriceNotFound` and `ProductPriceConflictNotFound` lines:

```ts
        ProductPriceNotFoundInShoptet: "Produkt není v maloobchodním ceníku Shoptetu",
        ProductPriceFlexiItemIdUnknown: "Produkt nemá ceníkovou položku ve Flexi, cena nebyla nikde změněna",
        ProductPriceShoptetWriteFailed: "Zápis ceny do Shoptetu selhal, cena nebyla nikde změněna",
        ProductPriceFlexiWriteFailed: "Cena byla změněna v Shoptetu, ale zápis do Flexi selhal — ceny se nyní liší",
```

- [ ] **Step 6: Write the failing handler tests**

```csharp
public class SetProductPriceHandlerTests
{
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IErpPriceWriter> _erpWriter = new();
    private readonly Mock<IProductPriceErpClient> _erpReader = new();
    private readonly Mock<IProductVatRateProvider> _vatRates = new();
    private readonly Mock<IProductPriceChangeLogRepository> _changeLog = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public SetProductPriceHandlerTests()
    {
        _eshop.Setup(c => c.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(190.00m);
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 11, PriceWithVat = 190m, PriceWithoutVat = 157.02m },
            });
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 21m });
        // CurrentUser is a positional record: (Id, Name, Email, IsAuthenticated).
        _currentUser.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Ondra", "ondra@anela.cz", true));
    }

    private SetProductPriceHandler CreateSut() => new(
        _eshop.Object, _erpWriter.Object, _erpReader.Object,
        _vatRates.Object, _changeLog.Object, _currentUser.Object);

    private static SetProductPriceRequest Request(decimal price = 210.00m) =>
        new() { ProductCode = "A", PriceWithVat = price };

    [Fact]
    public async Task writes_shoptet_then_flexi_with_the_price_excluding_vat()
    {
        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(11, 173.55m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task aborts_before_touching_shoptet_when_flexi_has_no_cenik_id()
    {
        // Arrange
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 0 },
            });

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceFlexiItemIdUnknown);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task never_writes_flexi_when_the_shoptet_write_fails()
    {
        // Arrange
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceShoptetWriteFailed);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task reports_the_partial_failure_when_only_flexi_fails()
    {
        // Arrange
        _erpWriter.Setup(w => w.SetPriceWithoutVatAsync(11, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi timeout"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceFlexiWriteFailed);
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e => e.ShoptetSucceeded && !e.FlexiSucceeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task returns_not_found_when_the_product_has_no_shoptet_price()
    {
        // Arrange
        _eshop.Setup(c => c.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceNotFoundInShoptet);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task records_the_old_and_new_price_on_success()
    {
        // Act
        await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e =>
                e.OldPriceWithVat == 190.00m && e.NewPriceWithVat == 210.00m &&
                e.ChangedBy == "ondra@anela.cz" && e.ShoptetSucceeded && e.FlexiSucceeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

*(173.55 = 210.00 / 1.21, rounded to 2 decimals away from zero.)*

- [ ] **Step 7: Run to verify they fail**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
```
Expected: FAIL — `SetProductPriceHandler` does not exist.

- [ ] **Step 8: Write the request, response, validator and handler**

`SetProductPriceRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequest : IRequest<SetProductPriceResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
}
```

`SetProductPriceResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceResponse : BaseResponse
{
    public SetProductPriceResponse() { }

    public SetProductPriceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }

    public decimal PriceWithVat { get; set; }
}
```

`SetProductPriceRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequestValidator : AbstractValidator<SetProductPriceRequest>
{
    public SetProductPriceRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty().MaximumLength(50);

        // Shoptet treats a literal 0 as a genuine free price, not "clear the price".
        RuleFor(r => r.PriceWithVat).GreaterThan(0m);
    }
}
```

`SetProductPriceHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

/// <summary>
/// Writes a retail price through to Shoptet (the source of truth) and then to Flexi.
///
/// The Flexi pre-flight runs BEFORE the Shoptet write on purpose: a missing ceník id or VAT
/// rate is knowable up front and guarantees the Flexi leg cannot succeed, so discovering it
/// afterwards would manufacture an avoidable divergence. The only partial failure this can
/// produce is a genuine Flexi write error, which is not knowable in advance.
/// </summary>
public class SetProductPriceHandler : IRequestHandler<SetProductPriceRequest, SetProductPriceResponse>
{
    private const int PriceDecimals = 2;

    private readonly IEshopPriceListClient _eshopClient;
    private readonly IErpPriceWriter _erpWriter;
    private readonly IProductPriceErpClient _erpReader;
    private readonly IProductVatRateProvider _vatRateProvider;
    private readonly IProductPriceChangeLogRepository _changeLog;
    private readonly ICurrentUserService _currentUserService;

    public SetProductPriceHandler(
        IEshopPriceListClient eshopClient,
        IErpPriceWriter erpWriter,
        IProductPriceErpClient erpReader,
        IProductVatRateProvider vatRateProvider,
        IProductPriceChangeLogRepository changeLog,
        ICurrentUserService currentUserService)
    {
        _eshopClient = eshopClient;
        _erpWriter = erpWriter;
        _erpReader = erpReader;
        _vatRateProvider = vatRateProvider;
        _changeLog = changeLog;
        _currentUserService = currentUserService;
    }

    public async Task<SetProductPriceResponse> Handle(
        SetProductPriceRequest request, CancellationToken cancellationToken)
    {
        var code = request.ProductCode;

        // 1. Shoptet is authoritative: a product with no row in the retail list cannot be priced.
        var oldPrice = await _eshopClient.GetPriceWithVatAsync(code, cancellationToken);
        if (oldPrice is null)
        {
            return await FailAsync(request, null, false, false,
                ErrorCodes.ProductPriceNotFoundInShoptet, $"{code} is not in the Shoptet retail price list.",
                cancellationToken);
        }

        // 2. Pre-flight the Flexi leg while nothing has been written yet.
        var erpItemId = await ResolveErpItemIdAsync(code, cancellationToken);
        var vatRate = await ResolveVatRateAsync(code, cancellationToken);
        if (erpItemId is null || vatRate is null)
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceFlexiItemIdUnknown,
                $"No Flexi ceník id or VAT rate for {code}; nothing was written.",
                cancellationToken);
        }

        var priceWithoutVat = Math.Round(
            request.PriceWithVat / (1 + vatRate.Value / 100m), PriceDecimals, MidpointRounding.AwayFromZero);

        // 3. Write Shoptet.
        try
        {
            await _eshopClient.SetPriceWithVatAsync(code, request.PriceWithVat, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceShoptetWriteFailed, ex.Message, cancellationToken);
        }

        // 4. Write Flexi. A failure here leaves the two systems divergent by design (D2):
        //    no rollback, no retry queue — the comparison screen is the safety net.
        try
        {
            await _erpWriter.SetPriceWithoutVatAsync(erpItemId.Value, priceWithoutVat, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, true, false,
                ErrorCodes.ProductPriceFlexiWriteFailed, ex.Message, cancellationToken);
        }

        await AppendAsync(request, oldPrice, true, true, null, cancellationToken);
        return new SetProductPriceResponse { PriceWithVat = request.PriceWithVat };
    }

    private async Task<int?> ResolveErpItemIdAsync(string code, CancellationToken ct)
    {
        var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);
        var match = erpPrices.FirstOrDefault(p =>
            string.Equals(p.ProductCode, code, StringComparison.OrdinalIgnoreCase));

        return match is { ErpItemId: > 0 } ? match.ErpItemId : null;
    }

    private async Task<decimal?> ResolveVatRateAsync(string code, CancellationToken ct)
    {
        var rates = await _vatRateProvider.GetVatRatesAsync(ct);
        return rates.TryGetValue(code, out var rate) && rate >= 0 ? rate : null;
    }

    private async Task<SetProductPriceResponse> FailAsync(
        SetProductPriceRequest request, decimal? oldPrice, bool shoptetOk, bool flexiOk,
        ErrorCodes errorCode, string error, CancellationToken ct)
    {
        await AppendAsync(request, oldPrice, shoptetOk, flexiOk, error, ct);

        return new SetProductPriceResponse(
            errorCode, new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
    }

    private Task AppendAsync(
        SetProductPriceRequest request, decimal? oldPrice, bool shoptetOk, bool flexiOk,
        string? error, CancellationToken ct) =>
        _changeLog.AppendAsync(new ProductPriceChangeLog
        {
            ProductCode = request.ProductCode,
            OldPriceWithVat = oldPrice,
            NewPriceWithVat = request.PriceWithVat,
            ChangedAt = DateTime.UtcNow,
            ChangedBy = _currentUserService.GetCurrentUser().Email,
            ShoptetSucceeded = shoptetOk,
            FlexiSucceeded = flexiOk,
            ErrorMessage = error,
        }, ct);
}
```

- [ ] **Step 9: Register the validator and restore the endpoint**

In `ProductPricingModule.cs`:

```csharp
services.AddScoped<IValidator<SetProductPriceRequest>, SetProductPriceRequestValidator>();
services.AddScoped<
    IPipelineBehavior<SetProductPriceRequest, SetProductPriceResponse>,
    ValidationBehavior<SetProductPriceRequest, SetProductPriceResponse>>();
```

In `ProductPricingController.cs`, restore the action with the same route as before:

```csharp
[HttpPut("prices/{productCode}")]
[FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
public async Task<ActionResult<SetProductPriceResponse>> SetPrice(
    string productCode,
    [FromBody] SetProductPriceRequest request,
    CancellationToken cancellationToken = default)
{
    request.ProductCode = productCode;
    return HandleResponse(await _mediator.Send(request, cancellationToken));
}
```

Note both conventions this controller uses: authorization is `[FeatureAuthorize(Feature, AccessLevel)]`, not `[Authorize(Policy = ...)]`, and every action wraps its result in `HandleResponse(...)` so a `BaseResponse` carrying an `ErrorCode` maps to the right HTTP status.

- [ ] **Step 10: Run to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~SetProductPriceHandlerTests|FullyQualifiedName~ErrorHandlingTests"
```
Expected: PASS — all six handler tests and the error-code contract test.

- [ ] **Step 11: Regenerate the client and commit**

```bash
dotnet msbuild -t:GenerateFrontendClientManual
dotnet build Anela.Heblo.sln -v q --nologo
cd frontend && CI=false npm run build && cd ..
git add -A
git commit -m "feat: write price edits through to Shoptet then Flexi

Pre-flights Flexi's cenik id and VAT rate before touching Shoptet, so
the only partial failure possible is a genuine Flexi write error. Every
outcome appends a change-log row."
```

---

## Task 5: Price comparison as a DataQuality drift check

DataQuality already has an `IDriftDqtComparer` abstraction with three implementations driven by a shared `DriftDqtJobRunner`, which handles run lifecycle, per-mismatch result persistence and failure recording. Price comparison plugs in as a fourth.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/DataQuality/PriceComparisonMismatch.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IPriceComparisonSource.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/PriceComparisonDqtComparer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/PriceComparisonDqtJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/PriceComparisonDqtAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtTestType.cs`, `DataQualityModule.cs`, `ProductPricingModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/DataQuality/PriceComparisonDqtComparerTests.cs`

**Interfaces:**
- Consumes: `IPriceComparisonService.BuildReportAsync` (Task 2); `PriceDivergenceKind`, `PriceDivergenceRowDto`
- Produces: `IPriceComparisonSource.GetDivergencesAsync(CancellationToken) -> Task<IReadOnlyList<PriceDivergence>>` where `PriceDivergence { string ProductCode; decimal? ShoptetPriceWithVat; decimal? FlexiPriceWithVat; string Kind; bool IsMismatch; }`

- [ ] **Step 1: Write the failing comparer test**

```csharp
public class PriceComparisonDqtComparerTests
{
    private readonly Mock<IPriceComparisonSource> _source = new();

    private PriceComparisonDqtComparer CreateSut() => new(_source.Object);

    [Fact]
    public void handles_the_price_comparison_test_type()
    {
        CreateSut().TestType.Should().Be(DqtTestType.PriceComparison);
    }

    [Fact]
    public async Task counts_every_product_checked_but_reports_only_mismatches()
    {
        // Arrange
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "A", ShoptetPriceWithVat = 190m, FlexiPriceWithVat = 190m,
                        Kind = "InAgreement", IsMismatch = false },
                new() { ProductCode = "B", ShoptetPriceWithVat = 250m, FlexiPriceWithVat = 200m,
                        Kind = "FlexiDiffers", IsMismatch = true },
            });

        // Act
        var result = await CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(2);
        result.Mismatches.Should().ContainSingle().Which.EntityKey.Should().Be("B");
    }

    [Fact]
    public async Task records_the_shoptet_and_flexi_prices_on_a_mismatch()
    {
        // Arrange
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "B", ShoptetPriceWithVat = 250m, FlexiPriceWithVat = 200m,
                        Kind = "FlexiDiffers", IsMismatch = true },
            });

        // Act
        var result = await CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        var mismatch = result.Mismatches.Single();
        mismatch.MismatchCode.Should().Be((int)PriceComparisonMismatch.PriceDiffers);
        mismatch.ShoptetValue.Should().Be("250.00");
        mismatch.HebloValue.Should().Be("200.00");
        mismatch.Details.Should().Be("FlexiDiffers");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
```
Expected: FAIL — none of these types exist.

- [ ] **Step 3: Add the test type and the consumer-owned contract**

In `DqtTestType.cs`:

```csharp
    LotSumVsErpStock = 4,
    PriceComparison = 5
```

`DataQuality/Contracts/IPriceComparisonSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// Consumer-owned contract (see development_guidelines.md, ILeafletKnowledgeSource pattern):
/// DataQuality declares what it needs, ProductPricing supplies the adapter and registers the
/// binding. Only the operations this check actually consumes are exposed.
/// </summary>
public interface IPriceComparisonSource
{
    Task<IReadOnlyList<PriceDivergence>> GetDivergencesAsync(CancellationToken ct);
}

public class PriceDivergence
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal? ShoptetPriceWithVat { get; set; }
    public decimal? FlexiPriceWithVat { get; set; }

    /// <summary>The classification name, e.g. "FlexiDiffers" — carried as text so DataQuality
    /// does not depend on ProductPricing's enum.</summary>
    public string Kind { get; set; } = string.Empty;

    public bool IsMismatch { get; set; }
}
```

- [ ] **Step 4: Write the comparer**

`DataQuality/Services/PriceComparisonDqtComparer.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Shoptet-vs-Flexi retail price drift, as a DQT check.
///
/// A price comparison is a snapshot, not a date-ranged query, so the from/to bounds the
/// framework supplies are ignored — the same accommodation the other drift comparers make.
/// <c>DqtDriftResult</c>'s columns are named for the Heblo-vs-Shoptet checks that came first;
/// here <c>ShoptetValue</c> carries the Shoptet price and <c>HebloValue</c> the Flexi price.
/// </summary>
public class PriceComparisonDqtComparer : IDriftDqtComparer
{
    private readonly IPriceComparisonSource _source;

    public PriceComparisonDqtComparer(IPriceComparisonSource source) => _source = source;

    public DqtTestType TestType => DqtTestType.PriceComparison;

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var divergences = await _source.GetDivergencesAsync(ct);

        var mismatches = divergences
            .Where(d => d.IsMismatch)
            .Select(d => new DriftMismatch
            {
                EntityKey = d.ProductCode,
                MismatchCode = (int)MapMismatch(d.Kind),
                ShoptetValue = Format(d.ShoptetPriceWithVat),
                HebloValue = Format(d.FlexiPriceWithVat),
                Details = d.Kind,
            })
            .ToList();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = divergences.Count };
    }

    /// <summary>
    /// Maps ProductPricing's classification name onto this check's own mismatch enum. The
    /// string crosses the module boundary so DataQuality never depends on ProductPricing's
    /// enum; the mapping back to an int lives here because MismatchCode is this module's
    /// vocabulary, exactly as the sibling drift comparers do it.
    /// </summary>
    private static PriceComparisonMismatch MapMismatch(string kind) => kind switch
    {
        "FlexiDiffers" => PriceComparisonMismatch.PriceDiffers,
        "MissingInFlexi" => PriceComparisonMismatch.MissingInFlexi,
        "FlexiPriceTypeUnknown" => PriceComparisonMismatch.FlexiPriceTypeUnknown,
        _ => PriceComparisonMismatch.Unknown,
    };

    private static string? Format(decimal? value) =>
        value?.ToString("F2", CultureInfo.InvariantCulture);
}
```

Every DQT check owns a mismatch enum beside its siblings (`ProductPairingMismatch`, `StockWriteBackMismatch`, `LotStockReconciliationMismatch`), and `MismatchCode` is `(int)` of that enum. Create `backend/src/Anela.Heblo.Domain/Features/DataQuality/PriceComparisonMismatch.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

public enum PriceComparisonMismatch
{
    Unknown = 0,
    PriceDiffers = 1,
    MissingInFlexi = 2,
    FlexiPriceTypeUnknown = 3
}
```

- [ ] **Step 5: Write the provider adapter**

`ProductPricing/Infrastructure/PriceComparisonDqtAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;

namespace Anela.Heblo.Application.Features.ProductPricing.Infrastructure;

/// <summary>Supplies DataQuality's price check without exposing ProductPricing's internals.</summary>
public class PriceComparisonDqtAdapter : IPriceComparisonSource
{
    /// <summary>
    /// FlexiPriceTypeUnknown counts as a mismatch: Flexi's with-VAT figure was derived from an
    /// assumed price type, so any agreement it shows is untrustworthy. MissingInShoptet does
    /// not — Shoptet is the source of truth, and a product it has never priced has no
    /// comparison to fail.
    /// </summary>
    private static readonly PriceDivergenceKind[] MismatchKinds =
    {
        PriceDivergenceKind.FlexiDiffers,
        PriceDivergenceKind.MissingInFlexi,
        PriceDivergenceKind.FlexiPriceTypeUnknown,
    };

    private readonly IPriceComparisonService _comparisonService;

    public PriceComparisonDqtAdapter(IPriceComparisonService comparisonService) =>
        _comparisonService = comparisonService;

    public async Task<IReadOnlyList<PriceDivergence>> GetDivergencesAsync(CancellationToken ct)
    {
        var report = await _comparisonService.BuildReportAsync(ct);

        return report.Rows
            .Select(row => new PriceDivergence
            {
                ProductCode = row.ProductCode,
                ShoptetPriceWithVat = row.ShoptetPriceWithVat,
                FlexiPriceWithVat = row.FlexiPriceWithVat,
                Kind = row.Kind.ToString(),
                IsMismatch = MismatchKinds.Contains(row.Kind),
            })
            .ToList();
    }
}
```

- [ ] **Step 6: Write the job**

`DataQuality/Infrastructure/Jobs/PriceComparisonDqtJob.cs` — modelled exactly on `ProductPairingDqtJob`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class PriceComparisonDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceComparisonDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-price-comparison-dqt",
        DisplayName = "Daily Price Comparison Data Quality Test",
        Description = "Compares retail prices between Shoptet (source of truth) and ABRA Flexi",
        CronExpression = "0 6 * * *", // Daily at 6:00 AM, alongside the other DQT checks
        DefaultIsEnabled = true
    };

    public PriceComparisonDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        TimeProvider timeProvider,
        ILogger<PriceComparisonDqtJob> logger)
    {
        _repository = repository;
        _jobRunner = jobRunner;
        _statusChecker = statusChecker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);

        _logger.LogInformation("Starting {JobName} for {Date}", Metadata.JobName, today);

        var run = DqtRun.Start(
            DqtTestType.PriceComparison, today, today, DqtTriggerType.Scheduled,
            _timeProvider.GetUtcNow().DateTime);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
```

This job is read-only against both remote systems, so unlike the deleted `ProductPriceSyncJob` it ships enabled.

- [ ] **Step 7: Register both sides**

In `DataQualityModule.cs`, beside the other comparers:

```csharp
services.AddScoped<IDriftDqtComparer, PriceComparisonDqtComparer>();
```

In `ProductPricingModule.cs` — the provider registers the binding, per the documented pattern:

```csharp
services.AddScoped<IPriceComparisonSource, PriceComparisonDqtAdapter>();
```

Register `PriceComparisonDqtJob` wherever the other DQT jobs are registered — find it with:

```bash
grep -rn "ProductPairingDqtJob" backend/src --include="*.cs" | grep -v "Jobs/ProductPairingDqtJob.cs"
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~PriceComparisonDqtComparerTests"
```
Expected: PASS, all three tests.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: price comparison as a DataQuality drift check

Plugs into the existing IDriftDqtComparer framework, so run lifecycle,
per-product result persistence and failure recording come for free."
```

---

## Task 6: Dashboard tile

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/PriceComparisonStatusTile.cs`
- Create: `frontend/src/components/dashboard/tiles/PriceComparisonTile.tsx`
- Modify: `DataQualityModule.cs`, `frontend/src/components/dashboard/drillDownRoutes.ts`
- Test: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/PriceComparisonStatusTileTests.cs` (tile tests live under `Features/<Module>/DashboardTiles/` in this repo), `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx`

**Interfaces:**
- Consumes: `IDqtRunRepository.GetLatestByTestTypeAsync(DqtTestType, CancellationToken)`; `DqtTestType.PriceComparison` (Task 5)
- Produces: tile id `"pricecomparisonstatus"`, drill-down route key `"productPricing"` → `/products/pricing`

- [ ] **Step 1: Write the failing tile test**

```csharp
public class PriceComparisonStatusTileTests
{
    private readonly Mock<IDqtRunRepository> _repository = new();

    private PriceComparisonStatusTile CreateSut() =>
        new(_repository.Object, NullLogger<PriceComparisonStatusTile>.Instance);

    [Fact]
    public async Task reports_no_data_before_the_first_run()
    {
        // Arrange
        _repository.Setup(r => r.GetLatestByTestTypeAsync(DqtTestType.PriceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        // Act
        var data = await CreateSut().LoadDataAsync();

        // Assert
        StatusOf(data).Should().Be("no_data");
    }

    [Theory]
    [InlineData(0, "success")]
    [InlineData(7, "warning")]
    public async Task maps_the_mismatch_count_to_a_status(int mismatches, string expected)
    {
        // Arrange
        var run = DqtRun.Start(DqtTestType.PriceComparison, new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 10), DqtTriggerType.Scheduled, DateTime.UtcNow);
        run.Complete(totalChecked: 400, totalMismatches: mismatches, DateTime.UtcNow);
        _repository.Setup(r => r.GetLatestByTestTypeAsync(DqtTestType.PriceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act
        var data = await CreateSut().LoadDataAsync();

        // Assert
        StatusOf(data).Should().Be(expected);
    }

    private static string StatusOf(object data) =>
        (string)data.GetType().GetProperty("status")!.GetValue(data)!;
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
```
Expected: FAIL — `PriceComparisonStatusTile` does not exist.

- [ ] **Step 3: Write the tile**

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

[TileId("pricecomparisonstatus")]
public class PriceComparisonStatusTile : ITile
{
    private const string DrillDownRouteKey = "productPricing";

    private readonly IDqtRunRepository _repository;
    private readonly ILogger<PriceComparisonStatusTile> _logger;

    public string Title => "Kontrola cen";
    public string Description => "Rozdíly cen mezi Shoptetem a Flexi";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public string[] RequiredPermissions => Array.Empty<string>();

    public PriceComparisonStatusTile(
        IDqtRunRepository repository,
        ILogger<PriceComparisonStatusTile> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        var drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true };

        try
        {
            var run = await _repository.GetLatestByTestTypeAsync(
                DqtTestType.PriceComparison, cancellationToken);

            if (run is null)
            {
                return new { status = "no_data", data = (object?)null, drillDown };
            }

            var status = run.Status == DqtRunStatus.Failed
                ? "error"
                : run.TotalMismatches > 0 ? "warning" : "success";

            return new
            {
                status,
                data = new
                {
                    runId = run.Id,
                    totalChecked = run.TotalChecked,
                    totalMismatches = run.TotalMismatches,
                    completedAt = run.CompletedAt,
                },
                drillDown
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the price comparison tile");
            return new { status = "error", data = (object?)null, drillDown };
        }
    }
}
```

Register it in `DataQualityModule.cs`:

```csharp
services.RegisterTile<PriceComparisonStatusTile>();
```

- [ ] **Step 4: Add the frontend drill-down route key**

In `drillDownRoutes.ts`:

```ts
export type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs' | 'productPricing';
```

and in `DASHBOARD_DRILLDOWN_ROUTES`:

```ts
  productPricing: { type: 'react-router', path: '/products/pricing' },
```

Add to `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx`:

```tsx
  it('resolves the price comparison tile to the pricing screen', () => {
    expect(DASHBOARD_DRILLDOWN_ROUTES.productPricing).toEqual({
      type: 'react-router',
      path: '/products/pricing',
    });
  });
```

- [ ] **Step 5: Write the tile renderer**

Copy `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` to `PriceComparisonTile.tsx`, changing the title to "Kontrola cen", the body to render `totalMismatches` of `totalChecked`, and registering it under the tile id `pricecomparisonstatus` wherever `DataQualityTile` is registered. Find that registry with:

```bash
grep -rn "dataqualitystatus" frontend/src --include="*.ts" --include="*.tsx"
```

- [ ] **Step 6: Run all tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~PriceComparisonStatusTileTests"
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="drillDownRoutes|PriceComparison"
```
Expected: PASS on both sides.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: price comparison dashboard tile"
```

---

## Task 7: Single-screen frontend with inline editing

**Files:**
- Modify: `frontend/src/pages/ProductPricingPage.tsx`
- Modify: `frontend/src/components/pricing/PriceDivergenceReport.tsx`
- Modify: `frontend/src/api/hooks/useProductPricing.ts`
- Test: `frontend/src/components/pricing/__tests__/PriceDivergenceReport.test.tsx`, `frontend/src/pages/__tests__/ProductPricingPage.test.tsx`

**Interfaces:**
- Consumes: `PUT /api/product-pricing/prices/{productCode}` (Task 4); `PriceDivergenceRowDto` without `HebloMasterPriceWithVat` (Task 1)
- Produces: `useSetProductPrice()` mutation returning `SetProductPriceResponse`, invalidating `QUERY_KEYS.divergence` on success

- [ ] **Step 1: Write the failing inline-edit test**

`PriceDivergenceReport.test.tsx` currently calls `render(<PriceDivergenceReport />)` with no props and no helper. **Introduce the `renderReport` helper these tests use** — it mocks `usePriceDivergenceReport` to return the given rows, mocks `useSetProductPrice` to return the given `setPrice` mutation, and renders the component with `canWrite`. Convert the existing tests in the file to it so there is one render path, not two.

```tsx
it('saves an edited Shoptet price and reloads the comparison', async () => {
  // Arrange
  const setPrice = jest.fn().mockResolvedValue({ priceWithVat: 210 });
  renderReport({ canWrite: true, setPrice, rows: [
    { productCode: 'A', productName: 'Alpha', shoptetPriceWithVat: 190,
      flexiPriceWithVat: 190, kind: PriceDivergenceKind.InAgreement },
  ]});

  // Act
  await userEvent.click(screen.getByRole('button', { name: 'Upravit cenu Alpha', exact: true }));
  const input = screen.getByRole('spinbutton', { name: 'Cena s DPH', exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, '210');
  await userEvent.click(screen.getByRole('button', { name: 'Uložit', exact: true }));

  // Assert
  await waitFor(() =>
    expect(setPrice).toHaveBeenCalledWith({ productCode: 'A', priceWithVat: 210 }));
});

it('keeps the partial-failure warning visible on the row', async () => {
  // Arrange
  const setPrice = jest.fn().mockRejectedValue(
    Object.assign(new Error('fail'), { errorCode: 'ProductPriceFlexiWriteFailed' }));
  renderReport({ canWrite: true, setPrice, rows: [
    { productCode: 'A', productName: 'Alpha', shoptetPriceWithVat: 190,
      flexiPriceWithVat: 190, kind: PriceDivergenceKind.InAgreement },
  ]});

  // Act
  await userEvent.click(screen.getByRole('button', { name: 'Upravit cenu Alpha', exact: true }));
  await userEvent.click(screen.getByRole('button', { name: 'Uložit', exact: true }));

  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent(/Flexi/);
});
```

Note `exact: true` on every `getByRole` name: Playwright and Testing Library both match the accessible name as a **substring**, and short Czech labels collide with longer aria-labels on the same row.

- [ ] **Step 2: Run to verify they fail**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="PriceDivergenceReport"
```
Expected: FAIL — no edit button is rendered.

- [ ] **Step 3: Add the mutation hook**

In `useProductPricing.ts`:

```ts
export interface SetProductPriceInput {
  productCode: string;
  priceWithVat: number;
}

const GENERIC_SET_PRICE_ERROR = "Cenu se nepodařilo uložit.";

export const useSetProductPrice = () => {
  const queryClient = useQueryClient();

  return useMutation<SetProductPriceResponse, Error, SetProductPriceInput>({
    mutationFn: ({ productCode, priceWithVat }: SetProductPriceInput) =>
      callApi(
        () =>
          getAuthenticatedApiClient().productPricing_SetPrice(
            productCode,
            new SetProductPriceRequest({ productCode, priceWithVat }),
          ),
        GENERIC_SET_PRICE_ERROR,
      ),
    onSuccess: () => {
      // Reload from live Shoptet/Flexi data rather than trusting the local value.
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.divergence });
    },
  });
};
```

- [ ] **Step 4: Add inline editing to the comparison table**

In `PriceDivergenceReport.tsx`, accept a `canWrite` prop, and for each row render an edit button (`aria-label={\`Upravit cenu ${row.productName}\`}`) that swaps the Shoptet cell for a number input plus Uložit / Zrušit buttons. On save call `useSetProductPrice().mutate`.

Render the failure as a persistent row-level `role="alert"` — not a toast — because it describes a state of the world that outlives a notification:

```tsx
{rowError && (
  <tr>
    <td colSpan={6} className="px-4 py-2">
      <div role="alert" className="text-sm text-red-800 dark:text-red-300">
        {rowError}
      </div>
    </td>
  </tr>
)}
```

Read the code from the caught `SwaggerException`, remembering it arrives as a **string**:

```ts
const errorCode = (error as { errorCode?: string })?.errorCode;
const rowError = errorCode ? t(errorCode) : GENERIC_SET_PRICE_ERROR;
```

In `ProductPricingPage.tsx`, pass `canWrite={hasPermission("products.catalog.write")}` down to the report.

- [ ] **Step 5: Run to verify they pass**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="pricing|Pricing" && CI=false npm run build
```
Expected: PASS and a clean build.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: single price comparison screen with inline Shoptet editing"
```

---

## Task 8: Documentation

**Files:**
- Modify: `docs/features/` — the product pricing feature spec
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 1: Find and rewrite the feature doc**

```bash
grep -rln "price\|cen" docs/features/ | head
```
Rewrite the pricing feature doc to describe: Shoptet as the source of truth, the live Shoptet-vs-Flexi comparison, the nightly DQT check and tile, the write-through edit with its pre-flight, and the change log. Delete every reference to the master price table, sync states, conflicts and the hourly sync job.

- [ ] **Step 2: Document the single-product price read**

In `docs/integrations/shoptet-api.md`, in the price list section, record that `GET /api/pricelists/{id}?code=X` is used for the pre-write read and confirm the `code=` singular filter behaviour already noted there.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs: describe Shoptet as the retail price source of truth"
```

---

## Final verification

- [ ] **Full backend build and test**

```bash
dotnet build Anela.Heblo.sln -v q --nologo
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes --verbosity minimal
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v q --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false
```
Expected: 0 errors, 0 failures. `dotnet format` produces no output.

- [ ] **Full frontend build and test**

```bash
cd frontend && CI=false npm run build && npx react-scripts test --watchAll=false
```
Expected: clean build, all tests pass.

- [ ] **Manual first write (not automated — see the spec's Open risks)**

The write path touches the production e-shop and the production ERP; there is no sandbox. Change **one** product's price in the UI, then confirm by hand in both Shoptet's admin and Flexi that the new price landed, and that a `ProductPriceChangeLogs` row exists with both flags true. Do this before announcing the feature.

- [ ] **Measure the FlexiPriceTypeUnknown volume**

After the first DQT run, check `TotalMismatches` against the count of rows whose `Details` is `FlexiPriceTypeUnknown`. If that kind dominates, the tile will be permanently orange for a reason unrelated to real divergence — raise it before treating the tile as a live signal, per the spec's Open risks.
