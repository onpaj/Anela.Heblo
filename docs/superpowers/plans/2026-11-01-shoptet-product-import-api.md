# Shoptet Product Import: CSV → REST API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Shoptet product CSV export (pattern 46) with the Shoptet REST API as the source of e-shop stock, location, images, weight and — new — product flags (Bestseller, Novinka, Výprodej …) in the Heblo catalog.

**Architecture:** Split today's single CSV source into two API-backed sources with different cadences: **stock + location** from `GET /api/stocks/{id}/supplies` (1 call, every 5 min, unchanged freshness) and **product info + flags** from a sequential product sweep (`/api/products` list + one detail per product + one `?flag=` list per flag, ~220 calls, hourly). `CatalogMergeService` merges both into `CatalogAggregate`. The CSV client, its index map and `StockClient:Url` are deleted at the end.

**Tech Stack:** .NET 8, `HttpClient` + `Microsoft.Extensions.Http.Resilience`, System.Text.Json, xUnit + FluentAssertions + `FakeDelegatingHandler`, React (catalog detail chips).

**Spec:** the *Validated findings* section below (no separate spec; everything in it was verified on 2026-11-01 against the OpenAPI spec, the live test store 780175 and read-only calls to production).

---

## Validated findings (the spec)

Evidence: OpenAPI spec `https://api.docs.shoptet.com/_bundle/Shoptet%20API/openapi.json`, live GETs, CSV downloads. P = production anela.cz, T = test store 780175.

| # | Finding | Evidence |
|---|---|---|
| F1 | Catalog e-shop data comes from CSV **pattern 46** (`StockClient:Url`), read every 5 min by fixed column index. Columns 11–24 are `variant:*` parameter columns, so adding a new variant parameter in Shoptet shifts `stock` (idx 25) and everything after it. Index 15 is `variant:Obsah`, not a "name suffix". | CSV header P+T |
| F2 | **Weight bug:** CSV writes weight in kg with a decimal comma (`0,035`). CsvHelper 32.0.2 with `InvariantCulture` reads the comma as a thousands separator: `0,035→35`, `0,1→1`, `0,25→25`, `0,99→99`. Today **181 of 446** prod rows with a weight are wrong by ×10 or ×100. Consumers treat `GrossWeight` as grams (`CatalogPackingProductSourceAdapter.WeightGrams`, `GetWarehouseStatisticsHandler` divides by 1000). | CsvHelper repro + prod CSV |
| F3 | `GET /api/stocks/1/supplies?itemsPerPage=1000` returns `code, productGuid, amount, claim, location` for every variant in one call. **CSV `stock` == API `amount` and CSV `stockLocation` == API `location` for 457/457 codes (P) and 444/444 (T).** Code sets identical. | live P+T |
| F4 | `GET /api/products?itemsPerPage=1000` works (our doc says max 50 — wrong for products). P has **206 products** (136 visible, 70 hidden), list includes hidden ones. | live P+T |
| F5 | `GET /api/products/code/{code}` exists (our doc says there is no lookup by code — wrong). | spec + live T |
| F6 | Product detail (`/api/products/{guid}` or `/code/{code}`) carries per variant: `code, name, image, weight (kg, decimal string), width, height, depth, atypicalShipping, ean, perStockAmounts[].location`; `include=images` adds `images[]` with `name` + `seoName`; `include=flags` adds `flags[] {code,title,dateFrom,dateTo}`. Variant `image` is the variant's own image (CSV `image` column is the product main image — API is more precise). Weight `0.000` occurs (e.g. `OCH001001B`). | live T |
| F7 | Flag catalogue: `GET /api/products/flags` → `{code,title,system,…}`. P has **12** flags = the 12 columns in the admin screenshot: `--darek` (+ TAJNÝ DÁREK), `action` (AKCE), `bestseller`, `custom1` (VÝPRODEJ), `custom3` (NOVÉ SLOŽENÍ), `darkove-baleni-zdarma`, `doprava-zdarma`, `chlazena-doprava_`, `new` (NOVINKA), `tip` (DÁREK K NÁKUPU), `v-sade-vyhodneji`, `vyhodna-cena`. | live P+T |
| F8 | `GET /api/products?flag={code}&itemsPerPage=1000` returns the products whose flag is **in effect now**. CSV pattern 90 `…FlagActive=1` means *configured*, including expired: for `new` the CSV lists 4 products, 3 expired on 2019-10-31; the API returns only the 1 valid one. `bestseller` 12=12, `custom1` 1=1. Flags are product-level (repeated on every variant row in CSV). | live P vs CSV 90 |
| F9 | `GET /api/products/snapshot` → **403 `required-webhook-registration`**. Not usable without registering a `job:finished` webhook (our doc is right). | live T |
| F10 | Image URL base: `GET /api/eshop?include=imageCuts` → cut `orig` with `urlPath` = `https://<shop-host>/user/shop/orig/`. `urlPath + seoName` serves `200 image/jpeg` without auth. | live T |
| F11 | `pairCode` does **not exist** in the API. In P all 335 non-empty `pairCode` values are numeric Shoptet group ids (`8`, `21` …). `ProductPairingDqtComparer` treats `PairCode` as an ERP code → these rows most likely always report `MissingInErp|PairCodeUnresolved`. | spec grep + prod CSV |
| F12 | Production dims are all `0`, `atypicalShipping` all `0`, 34 rows have no weight. | prod CSV |
| F13 | Pattern 90 (full export incl. all flag columns) is already downloaded nightly to blob `shoptetexport` by `ProductExportDownloadJob` (10/10 `Success` in 10 days), and nothing reads it. | App Insights + app settings |
| F14 | Rate limit: sequential ~4 req/s is safe; parallel ≥4 connections hits 429; token shared with packing/expedition. A full sweep at ≤4 req/s ≈ 60–70 s. | `shoptet-api.md` §3.14 |

### Decisions taken in this plan

- **D1 — `PairCode`:** dropped. The API has no such field and F11 shows the DQT check that reads it is very likely broken already. Task 1 verifies this against stored DQT results before anything is removed; if the check turns out to be meaningful, stop and ask before Task 5.
- **D2 — Flags = "in effect now"** (API `flag=` filter, F8), not "configured". Validity dates are not stored.
- **D3 — Weight stays grams** in `CatalogAggregate.GrossWeight` (all consumers assume grams). API weight (kg) × 1000; `0` → `null` so the packing fallback to `NetWeight` still triggers.
- **D4 — Snapshot endpoint not used** (needs webhook infra, F9). `/api/products/changes` incremental sync is a possible later optimisation, not in scope.
- **D5 — `ProductExportDownloadJob` untouched** (separate decision whether to keep archiving pattern 90).

## Global Constraints

- Every Shoptet call is a **read-only GET** except existing stock writes; no new writes.
- Product sweep is **strictly sequential**, minimum **250 ms** between request starts, retry on **429** with exponential backoff (max 4 attempts); never parallelise.
- Findings must be in `docs/integrations/shoptet-api.md` **before** code relies on them (Task 1).
- DTOs are classes, never records. API hooks use absolute URLs `${apiClient.baseUrl}${relativeUrl}`.
- Process docs updated in the same PR as the code: `docs/processes/sync-catalog-stock.md`, `docs/processes/calc-catalog-merge.md`, new `docs/processes/sync-catalog-eshop-products.md`; run `python3 scripts/process-docs/check.py index`.
- Validation before done: `dotnet build` + `dotnet format`; FE `CI=false npm run build` + `npm run lint`; touched tests pass.
- Failed refresh must keep the previous cache (no partial overwrite).

## Review Focus

1. **Expired flag configured in Shoptet** (Novinka until 2019-10-31) → must not appear on the product. Pinned in Task 3 test `ListAsync_FlagsComeFromFlagFilter_NotFromDetailConfiguration`.
2. **Variant in supplies but missing from the product sweep** (product created between the two refreshes) → stock still shown, image/weight/flags empty, no exception. Pinned in Task 4 test `Merge_StockWithoutProductInfo_KeepsStockAndLeavesInfoEmpty`.
3. **429 or 5xx in the middle of the 200-call sweep** → whole refresh fails, previous product cache stays. Pinned in Task 3 test `ListAsync_DetailCallFailsAfterRetries_Throws` + Task 4 test `RefreshEshopProductData_ClientThrows_KeepsPreviousCache`.
4. **Variant without own image / weight `0.000`** → falls back to product main image; weight `null` (packing then uses `NetWeight`). Pinned in Task 3 tests `ListAsync_VariantWithoutImage_UsesMainImage`, `ListAsync_ZeroWeight_MapsToNull`.
5. **More than 1000 variants / products** (pagination) → all pages read. Pinned in Task 3 `ListAsync_ProductListHasTwoPages_ReadsBoth` and Task 5 `ListAsync_SuppliesHaveTwoPages_ReadsBoth`.

---

## File Structure

| File | Responsibility |
|---|---|
| `docs/integrations/shoptet-api.md` (modify §4) | F3–F10 recorded |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` (modify) | Task 2 weight fix; Task 5 `ListAsync` → supplies |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Eshop/EshopProduct.cs` (create) | `EshopProduct`, `EshopProductFlag` |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Eshop/IEshopProductClient.cs` (create) | port |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Products/ShoptetProductClient.cs` (create) | sweep orchestration |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Products/Model/*.cs` (create) | JSON response classes |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Products/ShoptetProductClientOptions.cs` (create) | throttle/retry settings |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs` (modify) | product cache get/set |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs` (modify) | `RefreshEshopProductData` |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` (modify) | merge two maps |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs` (modify) | `EshopFlags` |
| `frontend/src/components/catalog/...detail` (modify, exact file found in Task 6 step 1) | flag chips |

---

### Task 1: Record findings + verify D1

**Files:**
- Modify: `docs/integrations/shoptet-api.md` §4 (products) and §4.3/§4.4 (CSV)

- [ ] **Step 1: Verify D1.** Query the latest `ProductPairingDqtJob` results (table behind `DqtDriftResultDto`, prod DB `Heblo_V3`, read-only) and count mismatches whose `Details` contain `PairCode`. Expected: ≈ one per variant with a numeric pairCode (~335). If the count is ~0 or the pairs resolve to real ERP codes → **stop and ask the user** whether to keep pattern 46 for `PairCode`.
- [ ] **Step 2: Edit `shoptet-api.md`.** Replace the wrong claims and add the facts: `itemsPerPage` max 1000 on `/api/products` (F4); `/api/products/code/{code}` exists (F5); detail fields + includes (F6); flags endpoints and "in effect now" semantics of `?flag=` vs CSV `FlagActive` (F7, F8); bulk supplies parity with CSV (F3); snapshot 403 text (F9); image cuts + public `urlPath` (F10); pattern 46 column layout + variant-column shift risk + decimal-comma weight (F1, F2); `pairCode` not in API (F11). Date each "verified 2026-11-01".
- [ ] **Step 3: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs: Shoptet product API findings for CSV replacement"
```

### Task 2: Fix CSV weight parsing (ships independently, live until Task 5)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` (`StockDataMap`)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs`
- Modify: `docs/processes/sync-catalog-stock.md` (Known quirks: weight now parsed as cs-CZ kg → grams)

**Interfaces:** Produces: `EshopStock.Weight` in **grams** (unchanged meaning, now correct).

- [ ] **Step 1: Write failing test** `ListAsync_WeightWithDecimalComma_ParsesKilogramsToGrams` — CSV body (windows-1250, `;`, 32 columns, header row) with weights `0,035`, `0,1`, `0,25`, `` → assert `Weight` = `35`, `100`, `250`, `null`.
- [ ] **Step 2: Run** `dotnet test backend/test/Anela.Heblo.Tests --filter ShoptetStockClientTests` → FAIL (`1` ≠ `100`).
- [ ] **Step 3: Implement** a private `KilogramsToGramsConverter : DefaultTypeConverter` in `ShoptetStockClient.cs` parsing with `CultureInfo.GetCultureInfo("cs-CZ")`, `NumberStyles.Float`, returning `kg * 1000` or `null` for blank; apply to `Weight` only (`Map(m => m.Weight).Index(27).TypeConverter<KilogramsToGramsConverter>()`).
- [ ] **Step 4: Run** the filter again → PASS.
- [ ] **Step 5: Commit** `fix: parse Shoptet CSV weight as Czech decimal kilograms`

### Task 3: `ShoptetProductClient` — product sweep with flags

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Eshop/EshopProduct.cs`, `IEshopProductClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Products/ShoptetProductClient.cs`, `ShoptetProductClientOptions.cs`, `Model/ProductListResponse.cs`, `Model/ProductDetailResponse.cs`, `Model/FlagListResponse.cs`, `Model/EshopImageCutsResponse.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` (register typed client)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetProductClientTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public class EshopProductFlag { public string Code { get; set; } = ""; public string Title { get; set; } = ""; }
  public class EshopProduct {
      public string Code { get; set; } = "";          // variant code
      public string ProductGuid { get; set; } = "";
      public string Name { get; set; } = "";          // product name
      public string? VariantName { get; set; }
      public bool IsVisible { get; set; }             // product visibility == "visible" && variant.visible
      public string? ImageUrl { get; set; }           // variant image, fallback product main image
      public string? DefaultImageUrl { get; set; }    // product main image
      public double? WeightGrams { get; set; }        // kg*1000, 0 → null
      public double? Height { get; set; } public double? Width { get; set; } public double? Depth { get; set; }
      public bool AtypicalShipping { get; set; }
      public IReadOnlyList<EshopProductFlag> Flags { get; set; } = Array.Empty<EshopProductFlag>();
  }
  public interface IEshopProductClient { Task<IReadOnlyList<EshopProduct>> ListAsync(CancellationToken ct); }
  ```
  `ShoptetProductClientOptions` (section `ShoptetProductClient`): `MinRequestIntervalMs = 250`, `MaxRetryAttempts = 4`, `RetryBaseDelaySeconds = 2`, `TimeoutSeconds = 15`.

Sweep order inside `ListAsync` (one request at a time, `MinRequestIntervalMs` between starts):
1. `GET /api/eshop?include=imageCuts` → `orig.urlPath`.
2. `GET /api/products/flags` → code→title.
3. per flag: `GET /api/products?flag={code}&itemsPerPage=1000&page=n` → set of product guids.
4. `GET /api/products?itemsPerPage=1000&page=n` → guid, name, visibility.
5. per guid: `GET /api/products/{guid}?include=images` → one `EshopProduct` per variant; image URL = `urlPath + seoName` of the `images[]` entry whose `name` == `variant.image` (fallback: `isMainImage` entry).

- [ ] **Step 1: Write failing tests** (fake handler routes by path+query, returns canned JSON shaped like the live T responses):
  - `ListAsync_MapsVariantFieldsFromDetail` — 1 product, 2 variants → codes, names, `WeightGrams` `35`/`71` from `"0.035"`/`"0.071"`, `ImageUrl` = `https://shop.test/user/shop/orig/51-1_x.jpg`.
  - `ListAsync_FlagsComeFromFlagFilter_NotFromDetailConfiguration` — detail JSON contains `flags:[{code:"new",dateTo:"2019-10-31"}]` but `?flag=new` list is empty, `?flag=bestseller` lists the guid → `Flags` = `[{bestseller,"BESTSELLER"}]` only.
  - `ListAsync_VariantWithoutImage_UsesMainImage` — `variant.image = null` → main image URL.
  - `ListAsync_ZeroWeight_MapsToNull` — `"0.000"` → `null`.
  - `ListAsync_ProductListHasTwoPages_ReadsBoth` — paginator `pageCount=2` → both pages requested, both products returned.
  - `ListAsync_DetailCallFailsAfterRetries_Throws` — one detail returns 500 every time → `HttpRequestException`.
  - `ListAsync_RequestsAreSequential` — handler records concurrent in-flight count; assert max == 1.
- [ ] **Step 2: Run** `dotnet test backend/test/Anela.Heblo.Tests --filter ShoptetProductClientTests` → FAIL (types missing).
- [ ] **Step 3: Implement** models (classes, `[JsonPropertyName]`, decimals as strings parsed with `InvariantCulture` — API uses dot decimals) and `ShoptetProductClient`. Register with `AddHttpClient<IEshopProductClient, ShoptetProductClient>` using the same BaseAddress/token as `ShoptetStockClient`, plus a resilience handler: retry on transient + **429** (exponential, jitter, `MaxRetryAttempts`), per-attempt timeout `TimeoutSeconds`. Throttle with `Task.Delay` on the elapsed time since the previous request start.
- [ ] **Step 4: Run** the filter → PASS.
- [ ] **Step 5: Manual smoke against T** (read-only): run the integration-test pattern used by `ShoptetStockClientIntegrationTests` (category `Integration`, skipped in CI) with a new test `ListAsync_TestStore_ReturnsAllSupplyCodes` asserting the product codes ⊇ the supply codes and `bestseller` flag count == 12. Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests --filter ListAsync_TestStore_ReturnsAllSupplyCodes` → PASS, elapsed < 120 s.
- [ ] **Step 6: Commit** `feat: Shoptet product sweep client with product flags`

### Task 4: Product cache, refresh task and merge

**Files:**
- Modify: `CatalogCacheStore.cs` — `GetEshopProductData()` / `SetEshopProductData(IList<EshopProduct>)`, key `CachedEshopProductData`, same invalidate/load-date pattern as `SetEshopStockData`.
- Modify: `CatalogStockRefreshService.cs` — `RefreshEshopProductData(CancellationToken)` via `_resilienceService.ExecuteWithResilienceAsync(..., "RefreshEshopProductData", ct)`.
- Modify: `ICatalogRepository.cs`, `CatalogRepository.cs`, `ManufactureOrderTestFactory.cs` (stub) — `Task RefreshEshopProductData(CancellationToken ct)`.
- Modify: `CatalogModule.cs` — `RegisterRefreshTask<ICatalogRepository>(nameof(ICatalogRepository.RefreshEshopProductData), …)`.
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` — `BackgroundRefresh:ICatalogRepository:RefreshEshopProductData` `{ InitialDelay "00:00:00", RefreshInterval "01:00:00", Enabled true, HydrationTier 2 }` (tier 2 so readiness does not wait for a ~70 s sweep — confirm tier meaning in `docs/processes/job-background-refresh-hydration.md`; if tier 2 still blocks readiness, use the latest non-blocking tier).
- Modify: `CatalogAggregate.cs` — `public IReadOnlyList<EshopProductFlag> EshopFlags { get; set; } = Array.Empty<EshopProductFlag>();`
- Modify: `CatalogMergeService.cs` — build `eshopProductMap` from `GetEshopProductData()`; `MergeEshopData(product, eshopStockMap, eshopProductMap)`: stock + location from stock map (as today); `Image`, `DefaultImage`, `GrossWeight`, dims, `AtypicalShipping`, `EshopFlags` from product map **when present**, otherwise from the stock map's legacy fields (removed in Task 5).
- Test: `CatalogMergeServiceTests.cs`, `CatalogRepositoryCacheOptimizationTests.cs`

**Interfaces:** Consumes `IEshopProductClient`, `EshopProduct` (Task 3). Produces `CatalogAggregate.EshopFlags`.

- [ ] **Step 1: Write failing tests:** `Merge_ProductInfoPresent_TakesImageWeightAndFlagsFromProductInfo`; `Merge_StockWithoutProductInfo_KeepsStockAndLeavesInfoEmpty` (after Task 5 there is no legacy fallback — write the assertion for empty `EshopFlags` and keep stock); `RefreshEshopProductData_ClientThrows_KeepsPreviousCache`.
- [ ] **Step 2: Run** `dotnet test backend/test/Anela.Heblo.Tests --filter "CatalogMergeServiceTests|CatalogRepositoryCacheOptimizationTests"` → FAIL.
- [ ] **Step 3: Implement** the changes listed above.
- [ ] **Step 4: Run** the filter → PASS; then full `dotnet test backend/test/Anela.Heblo.Tests --no-build` after a build (see memory `gotcha_dotnet_test_worktree_contention`).
- [ ] **Step 5: Docs:** create `docs/processes/sync-catalog-eshop-products.md` from `_TEMPLATE.md` (owns `Products/**`, `Eshop/**`; trigger hourly; data flow = the 5 sweep steps; quirks = flags are "in effect now", weight kg→g, 0→null, ~220 calls/≈70 s, shares token with packing); add the new cache row to `calc-catalog-merge.md`; run `python3 scripts/process-docs/check.py index`.
- [ ] **Step 6: Commit** `feat: load Shoptet product info and flags into catalog`

### Task 5: Stock + location from supplies; delete the CSV

**Files:**
- Modify: `ShoptetStockClient.cs` — `ListAsync` pages `GET /api/stocks/{StockId}/supplies?itemsPerPage=1000&page=n`; returns `EshopStock { Code, Stock = amount, Location }`; delete `StockDataMap`, `KilogramsToGramsConverter`, CsvHelper usage, `RedactToken` if unused.
- Modify: `Stock/Model/GetSuppliesResponse.cs` — add `location`, `productGuid`, `paginator`.
- Modify: `EshopStock.cs` — remove `PairCode, Name, NameSuffix, DefaultImage, Image, Weight, Height, Depth, Width, AtypicalShipping` (D1).
- Modify: `CatalogMergeService.cs` — remove the legacy fallback branch from Task 4.
- Modify: `DataQualityEshopStockSourceAdapter.cs` + `ProductPairingDqtComparer.cs` — read `Code`/`Name` from `IEshopProductClient` data (cache via `CatalogCacheStore.GetEshopProductData()`), drop `PairCode` logic and the `PairCodeUnresolved` flag usage (D1).
- Modify: `ShoptetStockClientOptions.cs` — remove `Url`; `ShoptetApiAdapterServiceCollectionExtensions.cs` — rename resilience pipeline `shoptet-stock-csv` → `shoptet-stock`.
- Modify: `appsettings.json` — remove `StockClient:Url`.
- Test: `ShoptetStockClientTests.cs`, `ShoptetStockClientResilienceTests.cs`, `ProductPairingDqtComparer` tests, DoubleShot tests that stub `ListAsync`.

- [ ] **Step 1: Write failing tests:** `ListAsync_ReadsSupplies_MapsAmountAndLocation` (`"120.000"` → `120m`, `location "A30-1"`); `ListAsync_SuppliesHaveTwoPages_ReadsBoth`; `ListAsync_NullAmount_MapsToZero`.
- [ ] **Step 2: Run** `dotnet test backend/test/Anela.Heblo.Tests --filter ShoptetStockClientTests` → FAIL.
- [ ] **Step 3: Implement** the list above; fix compile errors in DQT/DoubleShot tests by updating their stubs (behaviour unchanged: they use `Code` + `Stock` only).
- [ ] **Step 4: Run** `dotnet build` + full test run → PASS; `dotnet format`.
- [ ] **Step 5: Parity check against T before merging:** temporary local run (secrets point to T) — compare `GetCatalogData()` e-shop stock + location for all codes before/after the switch; expect 444/444 equal (F3). Record the result in the PR description.
- [ ] **Step 6: Docs:** `sync-catalog-stock.md` — source is now supplies API, remove CSV column table + index-shift quirk, config table without `StockClient:Url`; run `check.py index`.
- [ ] **Step 7: Commit** `refactor: read Shoptet e-shop stock from supplies API instead of CSV export`
- [ ] **Step 8: After production deploy (manual, ask the user first):** delete KV secret `StockClient--Url` in `kv-heblo-prod` (and `kv-heblo-stg`) once the 5-min refresh shows success for a day.

### Task 6: Show flags in the catalog

**Files:**
- Modify: catalog detail DTO (`backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/` — the DTO that already carries `Properties`) — add `List<EshopFlagDto> EshopFlags` (class `EshopFlagDto { string Code; string Title; }`) and map it.
- Modify: catalog detail component in `frontend/src/components/` (find with `grep -rn "isTopProduct" frontend/src/components`) — render each flag title as a small chip per `docs/design/ui_design_document.md`.
- Test: mapping test in `backend/test/Anela.Heblo.Tests/Features/Catalog/`; FE component test next to the detail component.

- [ ] **Step 1: Write failing tests:** BE `GetCatalogDetail_ProductWithFlags_ReturnsFlagTitles`; FE `renders e-shop flag chips` (two flags → two chips with the titles; none → no chip container).
- [ ] **Step 2: Run** BE filter + `cd frontend && npx react-scripts test --watchAll=false <file>` → FAIL.
- [ ] **Step 3: Implement**; regenerate the TS client (`dotnet msbuild -t:GenerateFrontendClientManual`).
- [ ] **Step 4: Run** tests → PASS; `CI=false npm run build` + `npm run lint`.
- [ ] **Step 5: Commit** `feat: show Shoptet product flags in catalog detail`

---

## Rollout notes

- Tasks 1–2 can ship as their own PR immediately (doc + weight bug fix).
- Tasks 3–4 are additive (CSV still feeds stock); Task 5 is the switch; Task 6 is UI. Suggested PRs: {1,2}, {3,4}, {5}, {6}.
- Out of scope / follow-ups: `/api/products/changes` incremental sweep; filtering/sorting the catalog list by flag; MCP exposure of flags; deciding the fate of `ProductExportDownloadJob` (F13).
