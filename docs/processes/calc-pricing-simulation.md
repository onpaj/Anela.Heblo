---
process: calc-pricing-simulation
kind: calculation
module: pricing
summary: What-if price and cost simulation (Analýza cen) — builds a per-product baseline of price excl. VAT, material and manufacturing cost and 12-month sold pieces from the catalog, applies sparse user overrides, and derives M0/M1 per product plus before/after revenue and margin totals; scenarios can be saved and reopened.
owns:
  - backend/src/Anela.Heblo.Application/Features/Pricing/**
  - backend/src/Anela.Heblo.Domain/Features/Pricing/**
  - backend/src/Anela.Heblo.Persistence/Pricing/**
  - backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs
  - backend/src/Anela.Heblo.API/Controllers/PricingScenariosController.cs
  - backend/src/Anela.Heblo.API/MCP/Tools/PricingSimulatorMcpTools.cs
verified_at: "85855377e"
related:
  - calc-margins
  - calc-bundle-sales-expansion
---

# Pricing simulation (Analýza cen)

## Purpose
Answers "if we change this product's price, or make it cheaper to produce, what happens to
the whole business?". It was built after the pricing meeting of 11 September 2026: input
costs (raw materials, energy, a new production hire) rose and Anela decided to reprice
products one by one instead of a flat increase (design spec
`docs/superpowers/specs/2026-09-21-pricing-simulator-design.md`).

The user sees a grid of products with today's price, material cost, manufacturing cost, M0,
M1 and pieces sold in the last 12 months, edits any of them, and immediately sees the
effect on total revenue (obrat), total M0 and total M1, before / after / delta. A session
can be saved as a named scenario (scénář), reopened later, and exported as a draft price
list (návrh ceníku, XLSX).

**Nothing is written anywhere outside Heblo.** No price goes to Shoptet or Flexi; the
output is a file people discuss. All amounts are CZK **excl. VAT**.

Consumers: page `/finance/price-analysis` ("Analýza cen", sidebar Finance), REST
`/api/pricing-simulator/*` and `/api/pricing-scenarios/*`, MCP tools `GetPricingBaseline`,
`SimulatePricing`, `ListPricingScenarios`, `GetPricingScenario`, `SavePricingScenario`,
`UpdatePricingScenarioProducts`.

## Trigger
On demand only — no Hangfire job, no BackgroundRefresh task, no cache of its own. Every
request rebuilds the baseline from the in-memory catalog and recalculates.

| Action | Endpoint / tool | Permission |
|---|---|---|
| Open page / baseline | `GET /api/pricing-simulator/baseline` · `GetPricingBaseline` | `Finance_PriceAnalysis` read |
| One cell edit (blur / Enter), bulk edit, reset | `POST /api/pricing-simulator/recalculate` · `SimulatePricing` | read |
| List / open scenario | `GET /api/pricing-scenarios`, `GET /api/pricing-scenarios/{id}` · `ListPricingScenarios`, `GetPricingScenario` | read |
| Save new / overwrite whole scenario | `POST /api/pricing-scenarios`, `PUT /api/pricing-scenarios/{id}` · `SavePricingScenario` | write |
| Partial update (edit/remove some products, rename) | MCP `UpdatePricingScenarioProducts` only (no REST endpoint) | write |
| Delete scenario | `DELETE /api/pricing-scenarios/{id}` (no MCP tool) | write |

## Data flow
1. **Baseline** (`PricingBaselineBuilder.BuildAsync`): reads every `CatalogAggregate` from
   `ICatalogRepository.GetAllAsync` (in-memory catalog; nothing from the DB or an external
   system), applies the filter, sorts by product code and makes one `PricingBaselineRow`
   per product.
2. **Simulation** (`PricingSimulationCalculator.Calculate`, pure — no clock, no I/O): takes
   the baseline, the caller's sparse override list and at most one edit; turns the edit
   into an override, then builds every row and the totals.
3. **Stateless round trip**: the response returns the new override list; the browser (or
   the MCP caller) keeps it and sends it back with the next edit. The server stores nothing
   until the user saves.
4. **Save** (`SavePricingScenarioHandler`): writes `public."PricingScenarios"` (name,
   description, author e-mail, timestamps, filter as `FilterJson` jsonb) and one
   `public."PricingScenarioItems"` row per override, including a **snapshot** of that
   product's baseline (price, material, manufacturing, quantity) at save time.
5. **Reopen** (`GetPricingScenarioHandler`): rebuilds the baseline with the scenario's own
   saved filter, replays the stored overrides, and sets `BaselineDrifted` on every row whose
   live baseline price / material / manufacturing cost differs from the snapshot.
6. **Export** (browser only, `exportPricingScenario.ts`): edited rows only, file
   `cenik-navrh-<scenario name or "aktualni-analyza">-<YYYY-MM-DD>.xlsx`.

## Logic & formulas
**Product set.** Filter = product code contains, product name contains (both
case-insensitive), product type. Without a type only `Product` and `Goods` are listed —
the same rule as the margins list (`GetProductMarginsHandler`), so gift packages
(`Set`, `BAL…`) appear only when the type filter asks for them.

**Baseline per product** (`PricingBaselineBuilder.ToBaselineRow`):

| Column (UI) | Field | Source |
|---|---|---|
| Cena | `Price` | `CatalogAggregate.PriceWithoutVat` — e-shop price excl. VAT if > 0 (the **running action price** while a Shoptet action is active), else the Flexi price; 0 if none |
| Materiál | `MaterialCost` | `M0.CostLevel` of the **latest month** in `Margins.MonthlyData` |
| Výroba | `ManufacturingCost` | `M1.CostLevel` of the same latest month |
| Prodáno 12m | `Quantity` | `GetTotalSold(now − 12 months, now)` = Σ `AmountB2B + AmountB2C` of all sales rows in the rolling 12 months (includes the current partial month) |

- "Latest month" is the newest key in the margin history, which is the **last complete
  month** (the margin window excludes the current month — see calc-margins). It is
  deliberately not the 13-month average shown on the margins page: the premise is that
  costs just rose, and an average would understate today's cost.
- M0 cost and M1 cost themselves (receipt prices, purchase-price fallback, VYROBA pool
  allocated by difficulty points) are computed by calc-margins; this process only reads
  them. M2/M3 are deliberately out of scope.
- `HasData` = price > 0 **and** a latest margin month exists. Rows without data are
  returned with `IsExcluded = true`, shown greyed out and read-only, and left out of every
  total.

**Overrides are sparse and hold only the independent variables** — price, material cost,
manufacturing cost, forecast quantity. Margins are never stored; they are always derived.
A field is pinned only when an edit determines it; everything else keeps following the live
catalog (`over?.X ?? baseline.X`).

**Edit → override** (`ApplyEdit`; `P`, `Cm` = the row's current effective price and
material cost):

| Edit field (UI cell) | Effect |
|---|---|
| `Price` | price := value |
| `MaterialCost` | material := value |
| `ManufacturingCost` | manufacturing := value |
| `M0Amount` | material := `P − value` (price unchanged) |
| `M0Percentage` | material := `P − P × value / 100` |
| `M1Amount` | manufacturing := `(P − Cm) − value` (material and M0 unchanged) |
| `M1Percentage` | manufacturing := `(P − Cm) − P × value / 100` |
| `ForecastQuantity` | forecast quantity := value |

A margin edit **never changes the price** — it is read as "we found a cheaper way to make
it". To reach a target margin by raising the price, edit the price.

**Validation of an edit** (the row it implies): price > 0, material ≥ 0, manufacturing ≥ 0,
quantity ≥ 0. Otherwise the edit is rejected with error 3801–3804
(`PricingInvalidPrice`, `PricingNegativeMaterialCost`, `PricingNegativeManufacturingCost`,
`PricingNegativeQuantity`) and the cell keeps its old value. An edit for a product not in
the current baseline (filtered out, gone from the catalog) → 3807
`PricingProductNotInBaseline`. **Negative margins are allowed** — showing that a product
loses money is part of the point. Directly sent overrides are bounded by FluentValidation
(`PricingOverrideDtoValidator`: same bounds, product code required and unique in the list).

**Per row** (`BuildRow`), effective values = override ?? baseline:
```
M0 = Price − Material                M0 % = M0 / Price × 100
M1 = Price − Material − Manufacturing M1 % = M1 / Price × 100
```
The same formula on the untouched baseline gives the `Baseline*` "before" columns.
`IsEdited` = the row has an override. No rounding anywhere in the calculation (the UI rounds
for display); a percentage of a zero base is reported as 0.

**Totals** (`BuildTotals`) over non-excluded rows only:
```
Revenue before = Σ BaselinePrice × BaselineQuantity      Revenue after = Σ Price × ForecastQuantity
M0 before      = Σ (BaselinePrice − BaselineMaterial) × BaselineQuantity
M0 after       = Σ M0 × ForecastQuantity
M1 before      = Σ (BaselinePrice − BaselineMaterial − BaselineManufacturing) × BaselineQuantity
M1 after       = Σ M1 × ForecastQuantity
Delta = after − before; Delta % = Delta / before × 100 (0 if before = 0)
```
`EditedProductCount` counts edited non-excluded rows; `ExcludedProductCount` counts excluded
rows. Forecast quantity defaults to the 12-month sold quantity, so with no quantity edit
the totals assume "same pieces as last 12 months at the new price" — there is no
price-elasticity model; the forecast is a manual guess.

**Screen helpers (browser only, no server rule of their own):**
- Bulk edit (`pricingBulkEdit.ts`): a percentage change of each selected product's
  *catalog* value for one of the four independent variables (e.g. all prices +5 %); 0 % puts
  the catalog value back. Products with no usable value, or where the result would be
  rejected, are skipped and counted in a toast.
- Work group (pinned products) and the summary scope (filtered products / whole catalog /
  work group; default "Všechny produkty"). Whole-catalog totals behind a name/code filter come from a second, unfiltered
  `recalculate` call.
- Export columns: Kód, Název, Cena před, Cena po, M0 před Kč, M0 po Kč, M0 po %, M1 před Kč,
  M1 po Kč, M1 po %, Prodáno 12m, Předpověď, Δ obrat (= Price × Forecast − BaselinePrice ×
  BaselineQuantity), Δ M1 (same basis, M1 × quantity). Raw numbers, not formatted strings.

**Scenarios.**
- Name is required, max 200 characters, unique (case-sensitive DB unique index; clash →
  3806 `PricingScenarioNameConflict`). Description max 2000. Unknown id → 3805
  `PricingScenarioNotFound`.
- Save with an id (`PUT`, or `SavePricingScenario` with id) **overwrites the whole
  scenario**: name, description, filter and the full item list, and re-snapshots the
  baseline of every item. `CreatedBy` / `CreatedAt` stay as first saved.
- `UpdatePricingScenarioProducts` (MCP) removes the listed products first, then applies the
  edits one by one on top of the stored overrides, using the scenario's own filter; the
  first failing edit aborts all with `editNumber` in the error. Touched products get a fresh
  snapshot; untouched ones keep theirs, so their drift flag keeps working. Removal codes
  that matched nothing are returned in `UnmatchedRemovals`.
- `SimulatePricing` (MCP) chains a batch of edits through the same recalculate handler,
  all-or-nothing; by default it returns only edited rows (totals always cover the whole
  filter).
- Scenario list is ordered by last modification, newest first; `EditedProductCount` = number
  of stored items.

## Configuration
No configuration key of its own. Constants in code: trailing sales window 12 months
(`PricingBaselineBuilder.TrailingSalesMonths`). Inputs depend on catalog settings documented
in calc-margins:

| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:SalesHistoryDays` | 400 (Staging/Test 100) | Sales history loaded into the catalog; must cover the 12-month quantity window |
| `DataSourceOptions:ManufactureCostHistoryDays` | 365 (Staging/Test 100) | Margin window; its last complete month is the baseline cost |

## Runtime facts
None.

## Known quirks
- **Bundle pieces inflate the quantity.** `GetTotalSold` also counts the synthetic
  quantity-only sales rows that bundle expansion adds to components (`SourceBundleCode`
  set), so a component's "Prodáno 12m" includes pieces sold inside gift packages and sets,
  and revenue before = catalog price × those pieces overstates real revenue. The margins
  revenue allocation excludes those rows; this baseline does not (also flagged in
  calc-bundle-sales-expansion). Open defect.
- **Reopened scenarios show today's "before", not the saved one.** The `Baseline*` columns
  and the "before" totals of a reopened scenario are rebuilt from the live catalog; the
  stored snapshot is used **only** to set the `BaselineDrifted` flag. The domain comment and
  design spec say a reopened scenario "still shows what was actually decided against" — it
  doesn't. Pinned override values are of course kept.
- **Loading a scenario does not restore its filter on the page.** The grid shows the
  scenario's rows, but the filter inputs keep their current values; the next edit
  recalculates with the page's filter (rows can change), and **Uložit** then overwrites the
  scenario's stored filter with the page's. Overrides for products outside the page filter
  survive in the override list but are not shown.
- **Saving from the page resets every drift flag.** The page always saves through the
  whole-scenario overwrite, which snapshots every item afresh. Only the MCP partial update
  keeps old snapshots. A drift flag is also only set by reopening a scenario; any edit after
  that (`recalculate`) returns rows without it.
- **Products that left the baseline disappear silently.** A stored override whose product is
  no longer in the scenario's filter result (renamed, deleted, type changed) yields no row and
  no error, but still counts in `EditedProductCount` and stays in the returned overrides.
  Saving such an override stores a snapshot of 0 / 0 / 0.
- **Drift compares exact decimals.** Material cost is an amount-weighted receipt average and
  manufacturing cost a pool allocation, recomputed every hour; any new receipt, ledger
  posting or month turn flags the row, even for a 0.01 Kč change. After a month turn the
  baseline cost also jumps to the new "latest month".
- **Latest month can carry no material cost.** If the product had no manufacture receipt in
  or after that month (calc-margins M0 rule 4), M0 cost is 0 but the row still counts as
  having data, so its M0 reads 100 %. Gift packages / sets show the frozen Flexi purchase
  price as material and M1 = 0 (see calc-margins → Bundles and sets).
- **Price = today's effective e-shop price**, including a running Shoptet action price; when
  the action ends the baseline price (and every unpinned row) jumps back.
- **Staging quantities are short.** `SalesHistoryDays` = 100 on Staging, so "Prodáno 12m"
  covers only ~100 days there.
- **Name uniqueness is case-sensitive** (`Name == name` on PostgreSQL), so "Podzim" and
  "podzim" can coexist.
- Partial update is MCP-only; delete is web-only (no MCP delete tool).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingBaselineBuilder.cs` — baseline sources and filter
- `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingSimulationCalculator.cs` — every formula: edit algebra, validation, rows, totals
- `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/` — one folder per endpoint (save, reopen + drift, partial update)
- `backend/src/Anela.Heblo.Application/Features/Pricing/Validators/PricingOverrideDtoValidator.cs` — bounds for directly sent overrides
- `backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioRepository.cs` — wholesale item replace on update
- `backend/src/Anela.Heblo.API/MCP/Tools/PricingSimulatorMcpTools.cs` — MCP batching and all-or-nothing semantics
- `frontend/src/components/pages/PriceAnalysis.tsx`, `frontend/src/components/pricing/` — page, grid, bulk edit, scenario bar, XLSX export
