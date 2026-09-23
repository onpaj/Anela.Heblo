# Purchase Price Sync (materials → semi-products → products)

**Date:** 2026-09-23
**Status:** Approved design, ready for implementation planning
**Scope:** Keep Flexi `cenik.nakupCena` correct for every item by syncing materials and
goods from the stock valuation, then re-running Flexi's BoM price roll-up in dependency
order. Revises assumption A2 of `2026-09-03-central-price-management-design.md`.

---

## 1. Problem

Flexi's purchase price (`cenik.nakupCena`) is wrong wherever a material's value is wrong,
and nothing keeps materials' values correct.

- **Materials and goods:** `nakupCena` is not maintained by anyone — no Flexi auto-update
  from purchases, no manual upkeep. It drifts or was entered wrong.
  Example: `AKL097` (ethanol, unit g) has `nakupCena` = 3.048594 Kč/g, while its stock
  valuation (`prumCena`) is ≈ 0.31 Kč/g.
- **Semi-products and products:** `nakupCena` is computed by Flexi's BoM action
  `prepocti-nakupni-cenu`, which sums the **components' `nakupCena`** — not the stock
  valuation. Heblo triggers it nightly (`purchase-price-recalculation`, 02:00) for every
  item with a BoM. A wrong material price therefore flows upward.
  Example: `DEZ001100` has `nakupCena` = 214.07 Kč (selling price 157.02 excl. VAT),
  while its real manufacture cost (M0 from manufacture receipts) is 65.28 Kč.
- **Ordering bug:** the nightly job recalculates BoMs in catalog order
  (`PurchaseMaterialCatalogAdapter.GetMaterialsWithBomAsync`), so a product can be
  recalculated before its semi-product and pick up yesterday's value.

### Where the wrong `nakupCena` surfaces today

| Consumer | Effect |
|---|---|
| Flexi BoM / ceník | Wrong purchase prices for everyone looking in Flexi |
| Heblo catalog "purchase price" (`CatalogAggregate.CurrentPurchasePrice`) | Wrong display |
| Financial Overview stock value (`FinancialOverviewStockValueAdapter`) | Stock valued at `quantity × nakupCena` |
| M0 fallback (`ManufactureBasedMaterialCostProvider`) — Goods, and Product/Set/SemiProduct without a manufacture receipt in the window | Wrong margin |

**Not affected:** M0 of manufactured products with receipts. Heblo prices manufacture
documents from `prumCena` (`FlexiManufactureDocumentService` → `stav-skladu-k-datu`),
so the receipt price is already the real cost.

## 2. Goal

After the nightly job, every item's `nakupCena` in Flexi reflects the stock valuation:

- materials and goods: `nakupCena` = today's `prumCena`;
- semi-products, then products and sets: `nakupCena` = Flexi's BoM roll-up over the
  corrected component prices.

Success check: after the first run, `DEZ001100.nakupCena` drops from 214 to the order of
its manufacture cost (≈ 65), and `AKL097.nakupCena` ≈ its `prumCena`.

## 3. Decisions

| # | Decision | Chosen |
|---|---|---|
| D1 | Correct prices live in | **Flexi** (Heblo writes `nakupCena`) |
| D2 | Source for materials | **`prumCena` only** — no last-purchase fallback |
| D3 | No usable `prumCena` (no stock row, or ≤ 0) | **Skip**; keep existing `nakupCena`; report the count |
| D4 | Semi-products / products | **Flexi BoM roll-up** (`prepocti-nakupni-cenu`), not their own `prumCena` |
| D5 | Goods (resold) | **Same as materials** |
| D6 | Write safety | **Fully automatic nightly**, every change logged; no threshold guard, no approval step |
| D7 | Other writers of `nakupCena` | **None** — Heblo is the sole writer |
| D8 | Shape | **Extend the existing `purchase-price-recalculation` job** into a 3-phase pipeline |

### Revision of A2 (central price management spec)

A2 ("Heblo never writes purchase price") becomes:

> **A2 — Heblo writes purchase price only for materials and goods, only from the stock
> valuation.** The nightly `purchase-price-recalculation` job sets `cenaNakup` =
> `prumCena` for `Material` and `Goods`, then triggers Flexi's BoM roll-up for
> semi-products and then products. Shoptet's `buyPrice` is never written.

## 4. Design

### 4.1 Pipeline

`PurchasePriceRecalculationJob` (unchanged schedule, 02:00, `DefaultIsEnabled = true`)
→ `RecalculatePurchasePriceHandler`. The **`RecalculateAll`** path runs three phases in
strict order. The **single-product** path (one `ProductCode`) is unchanged: it only
recalculates that item's BoM.

**Phase 1 — Sync materials and goods**

1. Load the ceník: `IProductPriceErpClient.GetAllAsync(forceReload: true)` → current
   `PurchasePrice` (`nakupCena`) and `ErpItemId` (`idcenik`) per product code.
2. Load stock for today: `IErpStockClient.StockToDateAsync(today, warehouse)` for
   warehouse **5** (`MaterialWarehouseId`) and **4** (`ProductsWarehouseId`).
   `ErpStock.Price` is `prumCena` (FlexiBeeSDK `StockToDateItem.AveragePrice`,
   `[JsonProperty("prumCena")]`).
3. Take catalog items of type `Material` (matched against warehouse 5) and `Goods`
   (matched against warehouse 4). Other types are ignored in this phase.
4. Per item:
   - no stock row, or `prumCena ≤ 0` → skip (counted as *no stock price*);
   - `|prumCena − nakupCena|` below `PurchasePriceTolerance` (named constant, 0.0001) →
     skip (counted as *unchanged*);
   - otherwise → `IErpPurchasePriceWriter.SetPurchasePriceAsync(erpItemId, prumCena)`.
5. Invalidate the `FlexiProductPrices` cache after the phase.

**Phase 2 — Semi-products.** `prepocti-nakupni-cenu` for every BoM whose owner is
`ProductType.SemiProduct`.

**Phase 3 — Products and sets.** `prepocti-nakupni-cenu` for every remaining BoM
(`Product`, `Set`, anything else with a BoM).

Invalidate the `FlexiProductPrices` cache at the end of the job.

### 4.2 Components

| Component | Location | Change |
|---|---|---|
| `RecalculatePurchasePriceHandler` | `Application/Features/Purchase/UseCases/RecalculatePurchasePrice/` | Three phases on the `RecalculateAll` path; phase order enforced |
| `RecalculatePurchasePriceResponse` | same | Per-phase counts (see 4.4); existing fields kept |
| `IMaterialCatalogService` / `PurchaseMaterialCatalogAdapter` | `Purchase/Contracts`, `Catalog/Infrastructure` | BoM references carry the owner's `ProductType` so the handler can split phases 2/3; new query for Material/Goods items with `ErpItemId` and current `nakupCena` |
| New `IPurchaseStockPriceSource` | `Purchase/Contracts` + adapter in `Catalog/Infrastructure` (next to `CatalogPurchasePriceRecalculationAdapter`) | Returns `prumCena` per product code for a warehouse; wraps `IErpStockClient.StockToDateAsync` |
| New `IErpPurchasePriceWriter` | Domain port | `SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken)` |
| New `FlexiPurchasePriceWriter` | `Adapters/Anela.Heblo.Adapters.Flexi/Price/` | `PUT cenik { "winstrom": { "cenik": [{ "id": ..., "nakupCena": ... }] } }`, invariant culture; mirrors `FlexiProductPriceWriter`'s HTTP/error handling; evicts `FlexiProductPriceErpClient.CacheKey` |
| `central-price-management-design.md` | `docs/superpowers/specs/` | A2 revised as in §3 |

Purchase reaches Catalog/Flexi only through its own contracts (same pattern as
`CatalogPurchasePriceRecalculationAdapter`); no direct cross-module references.

The selling-price writer (`FlexiProductPriceWriter`) and its VAT logic are not touched.

### 4.3 Error handling

| Failure | Behaviour |
|---|---|
| Phase 1 input load fails (ceník or stock) | Job fails (exception → Hangfire failure, logged). **No** BoM recalculation runs on partial data. |
| Single `nakupCena` write fails | Log with product code, count as failed, continue. Item self-heals next night. |
| Single `prepocti-nakupni-cenu` fails | Log, count as failed, continue (current behaviour). Phase 3 runs even if a semi-product failed; that product uses the semi-product's previous value. |

The pipeline is idempotent (writes only on difference), so retries and manual re-runs
are safe.

### 4.4 Logging and response

- Each write: `Information` — `code, type, old nakupCena → new, prumCena`. This is the
  audit trail of what the job changed.
- Skipped for no stock price: one summary line per run with the count and the first
  ~10 codes.
- Per-phase summary line: written / unchanged / skipped / failed (phase 1),
  succeeded / failed (phases 2, 3).
- `RecalculatePurchasePriceResponse` exposes the same per-phase counts so the manual
  trigger (API/UI) returns them.

## 5. Testing

**Unit (`RecalculatePurchasePriceHandlerTests`, xUnit + Moq):**
- Phase 1 writes `prumCena` for `Material` and `Goods` when it differs; skips within
  tolerance; skips when no stock row or `prumCena ≤ 0`; ignores other types.
- Material is matched against warehouse 5, Goods against warehouse 4.
- Ordering: every write happens before any recalculation; every SemiProduct
  recalculation before any Product/Set recalculation (recorded call order).
- Phase 1 input failure → no recalculation calls at all.
- Single write / recalculation failure does not stop the rest.
- Per-phase counts in the response.
- Single-product path unchanged.

**Adapter (`FlexiPurchasePriceWriter`):** parse the request body as JSON and assert
`id` and `nakupCena` (invariant decimal format) — not substring matching.

## 6. Live verification (with owner's approval, before merge)

No sandbox exists; each call hits the live company.

1. `PUT cenik { id, nakupCena }` on one material — accepted, stored in `mj1` units.
   Record the finding in `docs/integrations/` alongside the other Flexi notes.
2. `prepocti-nakupni-cenu` on `DEZ001100` alone — determine whether it recurses into
   the `DEZ001001M` BoM or reads its stored `nakupCena`.
   - If it reads the stored value (expected): the phase 2 → 3 order is required, as
     designed.
   - If any semi-product contains another semi-product, phase 2 must be ordered by BoM
     dependency (topological), not just by type. Decide during implementation from the
     live data; the type split remains the minimum.

## 7. Rollout

1. **Before deploy:** a one-off read-only report (outside the job) comparing
   `nakupCena` vs `prumCena` for all materials and goods, so the owner knows what the
   first night will change (e.g. `AKL097` ×0.1).
2. Deploy. The first nightly run performs the large cleanup.
3. Next day: check the job's summary log; spot-check `AKL097` and `DEZ001100` in Flexi
   and in Heblo.

## 8. Out of scope

- Financial Overview stock value switching to `prumCena` directly (separate change).
- M0 fallback VAT bug — handled separately (worktree `fix-m0-fallback-vat`).
- Costing Goods' M0 from `prumCena` instead of `nakupCena` (becomes moot once
  `nakupCena` = `prumCena`, apart from timing).
- Any threshold guard, dry-run mode or approval UI for the writes (D6).
- Shoptet `buyPrice`.
