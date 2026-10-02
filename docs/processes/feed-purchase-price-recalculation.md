---
process: feed-purchase-price-recalculation
kind: feed
module: purchase
summary: Nightly job that keeps the Flexi price list purchase price (ceník nakupCena) right - materials and goods take their average stock price, then Flexi rolls the BoM prices up through semi-products to products and sets.
owns:
  - backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/Jobs/PurchasePriceRecalculationJob.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceSyncSource.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchasePriceSyncCandidate.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceRecalculationAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapter.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiPurchasePriceWriter.cs
  - backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPurchasePriceWriter.cs
  - frontend/src/api/hooks/useRecalculatePurchasePrice.ts
  - scripts/flexi-purchase-price-report.py
verified_at: "5e993f9e2"
related:
  - calc-margins
---

# Purchase price recalculation (Flexi nakupCena)

## Purpose
Keeps the **purchase price (nákupní cena, ceník `nakupCena`)** of every item in Flexi
(ABRA FlexiBee) close to reality. Before this job nobody kept the materials' purchase price up to date. For example,
AKL097 was at 3.05 Kč/g while its real average stock price was ≈ 0.31 Kč/g. Flexi computes a product's
purchase price by summing its components' stored purchase prices along the bill of materials
(kusovník, BoM), so the error flowed up: DEZ001100 showed 214 Kč against a real material cost of about 65 Kč
(PR #4291, 2026-09-24).

Each night the job:
1. sets every **material and goods** item (materiál, zboží) to its average stock price,
2. tells Flexi to recalculate the price of every **semi-product** (polotovar) from its BoM,
3. then does the same for every **product and set** (výrobek, sada).

The result is used in Flexi itself, in Heblo's catalog (the purchase price shown on a product, read back through
Flexi user query 41), in the **M0 material cost fallback for margins** (see `calc-margins`: products without
manufacture history use ceník `nakupCena`), and in the Analytics margin reports. Prices are **excluding VAT**,
per the item's primary unit (`mj1`).

## Trigger
- Hangfire recurring job **`purchase-price-recalculation`** ("Purchase Price Recalculation", category Catalog),
  cron `0 2 * * *` in Europe/Prague, i.e. daily at 02:00. Enabled by default (`DefaultIsEnabled = true`).
  It can be disabled or run manually from the Recurring Jobs page
  (`POST /api/recurringjobs/purchase-price-recalculation/trigger`). The job checks its enabled flag itself
  and skips the run when the flag is off.
- On demand: `POST /api/purchase-orders/recalculate-purchase-price` (permission
  `Purchase_PurchaseOrders`, Write) with `{ "productCode": "…" }` recalculates the BoM of one item, or with
  `{ "recalculateAll": true }` runs the full three-phase run. No page calls it: the frontend hook
  `useRecalculatePurchasePrice` exists but no component uses it.

## Data flow
**Phase 1: materials and goods → ceník `nakupCena`** (`CatalogPurchasePriceSyncSourceAdapter` + handler)
1. Catalog items (`ICatalogRepository.GetAllAsync`, the in-memory merged catalog) of type `Material` or `Goods`.
2. A **fresh** read of the whole price list: Flexi user query 41
   (`GET /c/{company}/uzivatelsky-dotaz/41/call.json`, `forceReload: true`, so the 5-minute memory cache
   `FlexiProductPrices` is bypassed). This gives `ErpItemId` (`idcenik`) and the current `nakupCena`.
3. Today's stock (UTC date) from Flexi `stav-skladu-k-datu` for warehouse **5 (MATERIAL)** for materials and
   **4 (ZBOZI)** for goods. The stock price used is `ErpStock.Price` = exact average `tuz / stavMJ`, falling back
   to the 2-decimal `prumCena` when quantity or value is not positive (`FlexiStockMappingProfile`).
4. For each item that has a ceník row with `ErpItemId > 0`: if the stock price is > 0 and differs from `nakupCena` by
   ≥ 0.0001, write it: `PUT /c/{company}/cenik/{idcenik}.json` with
   `{"winstrom":{"cenik":{"nakupCena":"<price>"}}}` (`FlexiPurchasePriceWriter`). After the write the cache entry
   `FlexiProductPrices` is evicted.

**Phase 2: semi-product BoMs**
5. Every catalog item with a BoM (`HasBoM` and `BoMId`), **excluding Material and Goods**, of type `SemiProduct`
   (`PurchaseMaterialCatalogAdapter.GetMaterialsWithBomAsync`). For each one, Flexi `prepocti-nakupni-cenu` on its
   kusovník (`IBoMClient.RecalculatePurchasePrice(bomId)`): Flexi sets the item's `nakupCena` to the sum of its
   components' stored `nakupCena`.

**Phase 3: product and set BoMs**
6. The remaining BoM items (products, sets, anything else that is not a semi-product), ordered so that **sets
   come last**, recalculated the same way.

Target: Flexi `cenik.nakupCena` only. Heblo stores nothing of its own here. The run result is logged and sent
to Application Insights as business event `PurchasePriceRecalculation`, with properties `Status` (`Success`, or
`PartialFailure` when any write or BoM failed), `PriceSyncWritten`, `PriceSyncSkippedNoStockPrice`,
`PriceSyncFailed`, `SuccessCount`, `FailedCount` and `TotalCount`.

## Logic & formulas
- **Order matters.** Flexi's roll-up reads each component's *stored* `nakupCena` and does **not** recurse
  (verified 2026-09-24 in `docs/integrations/flexi-api.md`). So the order is materials → semi-products →
  products → sets.
- Phase-1 decision per candidate:
  | Condition | Outcome (counter) |
  |---|---|
  | No ceník row or `ErpItemId <= 0` | not a candidate; excluded and logged (first 10 codes) |
  | No stock row, or stock price ≤ 0 | skipped (`SkippedNoStockPrice`); the old `nakupCena` stays |
  | \|stock price − nakupCena\| < 0.0001 | `Unchanged` |
  | otherwise | written (`Written`) or, on error, `Failed` |
- The written price is rounded to **6 decimals**, half away from zero. Flexi stores 6 decimals, so the tolerance does not
  cause nightly re-writes. A price that rounds to ≤ 0 is refused.
- **A load failure stops the whole run before any write.** If the ceník read fails, or a stock read for warehouse 5 or 4
  returns **zero rows**, the run throws. Zero rows is treated as a failure because the SDK hides HTTP errors as an
  empty list. The job then logs, tracks the exception and rethrows, so Hangfire marks the run failed. No BoM is
  recalculated on partial data.
- A failed single write or single BoM recalculation is logged and counted, and the loop continues. A cancelled run
  stops at once.
- A Flexi roll-up that returns non-success counts as a failure ("Flexi rejected the roll-up").
- Materials and goods that have a BoM are **never** rolled up: their price comes from stock (phase 1).
- Single-product mode (`productCode`) runs only the BoM roll-up for that item. It does not refresh its
  components first and does not run phase 1. An item without a BoM returns `InvalidValue`; an unknown code returns
  `CatalogItemNotFound`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Hangfire job `purchase-price-recalculation` | `0 2 * * *`, Europe/Prague, enabled | Schedule; cron and enabled flag can be changed at runtime on the Recurring Jobs page |
| `FlexiBeeSettings:Server/Company/Login/Password` | secrets (Key Vault) | Flexi connection used for all reads and writes |
| `PurchasePriceTolerance` (code constant) | 0.0001 | Minimum difference worth a write |
| Warehouse ids (code constants) | 5 = MATERIAL, 4 = ZBOZI | Stock price source per item type |

## Runtime facts
- Materials and goods stock: 530 items measured, 504 have `tuz / stavMJ` within 1 % of `prumCena`, 23 within
  1–5 %, 3 more than 5 % off (cheap per-gram materials) — `docs/integrations/flexi-api.md` — 2026-09-24.
- Flexi has no semi-products nested in semi-products and no products nested in products (3,215 kusovník
  rows); `SET*`/`BAL*` sets have no kusovník, so the "sets last" order is only a safeguard —
  `docs/integrations/flexi-api.md` — 2026-09-24.

## Known quirks
- **A cold catalog makes the run do nothing.** If the in-memory catalog has no Material/Goods items (e.g. a run
  right after a restart, before the catalog merge), phase 1 logs a warning and returns no candidates. The BoM
  list comes from the same empty catalog, so `TotalCount` = 0, and the run is still reported as
  `Status = Success` in telemetry.
- **Items missing from the catalog are never updated.** The candidates come from Heblo's catalog, so a Flexi item
  that the catalog drops (e.g. a ZBOZI card with an empty product group `skupZboz`, see the 2026-10-01 SA016005
  case) keeps its old `nakupCena`.
- **Skipped items keep stale prices.** Items with no stock or a non-positive stock value are skipped every night,
  so their `nakupCena` is whatever was typed in by hand. Their BoM parents still sum that value.
- **Single-product recalculation is incomplete.** It rolls up only that item's own BoM, using whatever its
  semi-products currently hold. It does not refresh them first.
- **The API "ForceReload" flag does nothing.** `RecalculatePurchasePriceRequest.ForceReload` is accepted but never
  read.
- The Flexi write targets **the live company**: there is no sandbox. `scripts/flexi-purchase-price-report.py`
  is a read-only preview of what phase 1 would write (`nakupCena` vs `prumCena`). It classifies items by
  ceník `typZasobyK`, not by catalog type, so a few rows can differ from the job.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/Jobs/PurchasePriceRecalculationJob.cs` — job metadata, cron, telemetry
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs` — the three phases, tolerance, single-product mode
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPurchasePriceSyncSourceAdapter.cs` — phase-1 candidates, warehouse ids, empty-stock guard
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs` — `GetMaterialsWithBomAsync` (which BoMs, semi-product/set flags)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiPurchasePriceWriter.cs` — the ceník PUT, rounding, cache eviction
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs` — user query 41 read and `RecalculatePurchasePrice` (roll-up)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockMappingProfile.cs` — exact average stock price
- `docs/integrations/flexi-api.md` — verified Flexi behaviour of `nakupCena`, `prepocti-nakupni-cenu`, `stav-skladu-k-datu`
