---
process: sync-catalog-eshop-products
kind: sync
module: catalog
summary: Sweeps the Shoptet REST API hourly for every product variant's image, weight, dimensions and active flags (Novinka, Akce, Bestseller ...) and loads them into the catalog cache, where they are merged onto each CatalogAggregate.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Products/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Eshop/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs
verified_at: "0b7b902c9"
related: [calc-catalog-merge, sync-catalog-stock, job-background-refresh-hydration]
---

# Catalog: Shoptet product info and flags

## Purpose
Gives every catalog product its e-shop image, gross weight (grams), dimensions, atypical-shipping
marker and the Shoptet flags currently in effect (`CatalogAggregate.EshopFlags`). Read by catalog
detail/list, packing (weight) and anything that needs "is this a bestseller / novelty".

## Trigger
Background refresh task `ICatalogRepository.RefreshEshopProductData`, every 1 h (interval measured
from the end of one run), `HydrationTier` 3, `InitialDelay` 0. Force-refresh from Background Tasky.

## Data flow
`ShoptetProductClient.ListAsync` (strictly sequential, >= 250 ms between request starts):
1. `GET /api/eshop?include=imageCuts` -> image base URL (`orig` cut `urlPath`).
2. `GET /api/products/flags` -> flag catalogue (code, title).
3. Per flag `GET /api/products?flag={code}&itemsPerPage=1000` -> products where the flag is in
   effect now; flags are attached to every variant of those products.
4. `GET /api/products?itemsPerPage=1000&page=N` -> all products (visible and hidden).
5. Per product `GET /api/products/{guid}?include=images` -> variants: code, name, image, weight (kg),
   dimensions, `atypicalShipping`.

Result (one `EshopProduct` per variant) -> `CatalogCacheStore.SetEshopProductData`
(key `CachedEshopProductData`) -> merge scheduled -> `CatalogMergeService.MergeEshopData`.

## Logic & formulas
- Stock, `Location`, `PrimaryStockSource` come from `CachedEshopStockData` (supplies API,
  `sync-catalog-stock`); this cache only supplies image/weight/dimensions/shipping/flags.
- Merge: `Image = ImageUrl`, `DefaultImage = DefaultImageUrl`, `GrossWeight = WeightGrams`,
  `Height/Width/Depth`, `AtypicalShipping`, `EshopFlags = Flags` when the code is in the product cache.
- Code in stock but not in the product cache (new product between the two refreshes, or until the first
  successful product sweep after boot): stock is kept and every info field is set empty (null / false / no flags),
  including values merged earlier. There is no fallback source.
- Duplicate codes in the sweep: the first wins, no exception.
- Weight: API kilograms x 1000 = grams; `0` -> null (packing then falls back to `NetWeight`).
- Flags mean "in effect now" (API `flag=` filter), not "configured": an expired Novinka is absent.
- All-or-nothing: any failed call (after the client's 429/5xx retries) throws, and the previous
  cache stays; the cache is never partially overwritten.
- Empty-cache retry: if the sweep fails while the product cache has never loaded (e.g. 429 or an
  outage at boot), `RefreshEshopProductData` retries up to 2 more times, 5 minutes apart, inside the
  overlap gate; each failed attempt logs a Warning and, after the last, the exception is rethrown
  (task history shows the failure) with the cache untouched. Once the cache is loaded there is no
  retry; the next hourly run is the retry.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `BackgroundRefresh:ICatalogRepository:RefreshEshopProductData:RefreshInterval` | 01:00:00 | Sweep period |
| `BackgroundRefresh:ICatalogRepository:RefreshEshopProductData:HydrationTier` | 3 | Above the Production/Staging `ReadinessTier` (2/1), so `/health/ready` never waits for the ~70 s sweep |
| `ShoptetProductClient` (`MinRequestIntervalMs`, `MaxRetryAttempts`, `RetryBaseDelaySeconds`, `TimeoutSeconds`) | 250 / 4 / 2 / 15 | Throttle, per-call retry and timeout of the sweep client |
| `Shoptet` (`ShoptetApiSettings`: `BaseUrl`, `ApiToken`) | - | Base URL and token, shared with the other Shoptet API clients |

## Runtime facts
None yet (first production run pending deploy).

## Known quirks
- A sweep is ~220 sequential calls, about 70 s, and shares the Shoptet token (and rate limit) with
  packing/expedition; never parallelise.
- Deliberately not wrapped in `ICatalogResilienceService`: its 30 s per-operation timeout would abort
  every ~70 s sweep, and its retry would restart the whole sweep (multiplying Shoptet load). The
  client retries individual calls (429/5xx) itself and throws, leaving the previous cache.
- Until the first successful product sweep after boot, products have no image, weight, dimensions or
  flags (stock and location are already present from the supplies API).
- The sweep's product names also feed the product-pairing DQT (`calc-dqt-product-pairing`); before
  the first sweep those names are blank.
- Overlap is skipped: `RefreshEshopProductData` holds a process-wide non-blocking gate
  (`SemaphoreSlim(1,1)`); a force refresh or startup double-run during a sweep logs "Shoptet product
  refresh already running; skipping" and returns (history shows it as Completed).
  The background-refresh mechanism itself has no per-task overlap protection.
- Why tier 3: `ReadinessTier` is 2 in Production and 1 in Staging; tiers above it load after
  `/health/ready` is Healthy, so the ~70 s sweep never delays readiness.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Products/ShoptetProductClient.cs` — the sweep
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Eshop/EshopProduct.cs` — result shape
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs` — `RefreshEshopProductData`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — `MergeEshopData`
