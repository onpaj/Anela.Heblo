# Analýza cen (Pricing Simulator) — Design

**Date:** 2026-09-21
**Status:** Approved for planning

**Naming:** the user-facing name is **"Analýza cen"**, in the Finance menu section. Internally
the backend slice is `Features/Pricing` and routes are under `/api/pricing-simulator` and
`/api/pricing-scenarios`.

## Purpose

Let the user experiment freely with product pricing and costs, and see the effect on
total revenue and total margin immediately.

The origin is the pricing meeting of 11 September 2026 ("Cenotvorba, analýza dat
a produktové portfolio"). Input costs rose — suroviny, energie, a new production hire —
and the decision was to reprice products individually rather than apply a flat percentage
increase. That requires a tool that answers, per product and in aggregate: if I change this
price, or find a cheaper way to manufacture this, what happens to the whole business?

The output of a session is a scenario that can be saved, reopened, and exported as an XLSX
draft ceník for discussion and approval.

## Scope

### In scope

- A grid of products (filterable) showing current sale price ex-VAT, M0, M1, and
  trailing-12-month sales quantity.
- Editable per row: sale price, M0, M1, forecast quantity.
- Live totals: revenue and total margin, before / after / delta.
- Named scenarios persisted in Postgres: save, list, reopen, delete.
- XLSX export of a scenario.

### Out of scope (non-goals)

- **No write-back** to the e-shop or ERP. The simulator changes nothing real; its output is
  a file people discuss.
- **No M2 / M3.** The 11 Sep meeting moved M2 from a fixed 177,6 Kč to a proportional
  ~57 % of price, and M3 remains an acknowledged house number. Andy still owes written
  M0–M3 definitions. Simulating on a number that is mid-redefinition would produce output
  that goes stale on the switchover.
- **No elasticity model.** Forecast quantity is entered by hand (see "Sales forecast").
- **No VAT handling.** Every price in this feature is ex-VAT, end to end.
- **No multi-currency.** CZK throughout.

## Decisions and their reasons

| Decision | Reason |
|---|---|
| Server owns the calculation | One implementation; the recalculate endpoint is the same code path that save and export consume, so the screen and the exported ceník can never disagree. |
| Recalculate fires on cell blur / Enter | One request per completed edit. The round-trip lands in the natural pause between cells rather than mid-keystroke. |
| Baseline = latest month's costs | The premise is that costs just rose. Pricing next season against a 13-month average would understate current cost. Note this differs from `ProductMarginsList`, which shows 13-month averages. |
| M0 + M1 only | What was asked for, and it sidesteps the unsettled M2/M3 methodology. |
| M1 means `M1_A` | `M1_B` (direct manufacturing) is a stub provider returning a constant — see `DirectManufactureCostProvider`. `M1_A` is the real ManufactureDifficulty-weighted flat manufacturing cost, and is what `ProductMarginDto.M1` already exposes. |
| Margin edits change cost, never price | "I found a way to manufacture this cheaper" leaves the shelf price alone and raises margin. One rule, no modes. |
| Forecast quantity is manual | No data exists to calibrate an elasticity coefficient. A hand-entered number is honest about being a guess. |
| Scenarios snapshot their baseline | Costs move monthly. Reopening October's scenario in December must show what was actually decided against, and flag rows whose baseline has since drifted. |

## Calculation model

Per row: price `P`, material cost `Cm`, manufacturing cost `Cf`, forecast quantity `Q`.

```
M0 = P − Cm                    M0% = (P − Cm) / P × 100
M1 = P − Cm − Cf               M1% = (P − Cm − Cf) / P × 100

therefore:  Cm = P − M0        Cf = M0 − M1
```

Because M0 and M1 decompose cleanly onto the two cost components, all four editable cells
have exactly one unambiguous meaning:

| Edit | Interpretation | Effect |
|---|---|---|
| price `P` | Shelf price changes; costs unchanged | M0 and M1 both shift by the price delta |
| `M0` | Material cost changed | `Cm` implied; M1 shifts by the same delta (manufacturing unchanged) |
| `M1` | Manufacturing cost changed | `Cf` implied; M0 unchanged |
| forecast `Q` | Volume assumption changed | No per-row margin change; totals only |

Margin cells accept either a Kč amount or a percentage; the other representation is derived.

**Invariant:** `M1 ≤ M0 ≤ P`.

Edits are independent and cumulative. The user may change M0 on one product, the price on
another, and the forecast on a third; each edit recomputes that row from its current state,
and the totals reflect the whole accumulated set.

### Margin edits are stored as costs, not as margins

A margin value is a *function* of price and cost, so it cannot be the durable representation
of an edit. Consider: edit M0 (implying a material cost), then edit the price. Costs are
pinned on a price edit, so M0 necessarily moves again — and a stored M0 from the first edit
is now stale. Replaying it on reload would reproduce a different row.

Therefore a margin edit is **normalised to the cost it implies** before it is stored:

```
user types M0  →  store materialCost       = P − M0
user types M1  →  store manufacturingCost  = M0 − M1
```

The row state carried in `overrides` and persisted in a scenario is always
`{ price, materialCost, manufacturingCost, forecastQuantity }` — the independent variables.
Margins are always derived, never stored. This makes reload order-independent and gives the
scenario exactly one representation.

The UI still presents margins as the editable cells; the translation happens in
`PricingSimulationCalculator`, which is the only place that knows the rule.

### Validation

Because price is pinned, a margin edit is valid exactly when the cost it implies is
non-negative. Rejected, with the prior cell value retained:

- `P ≤ 0` — price must be positive
- implied material cost `< 0`, i.e. `M0 > P` or `M0% > 100`
- implied manufacturing cost `< 0`, i.e. `M1 > M0`
- `Q < 0`

`M0% = 100` is **allowed** — it means zero material cost, which is computable and simply
unusual. The rule is negative cost, not implausible cost.

Negative margins are **permitted**. Showing that a product loses money at a given price is
one of the things the tool is for.

### Totals

```
revenue_before = Σ (baselinePrice × baselineQuantity)
revenue_after  = Σ (price × forecastQuantity)
M0_total_after = Σ (M0 × forecastQuantity)      # likewise M1
```

Deltas are reported in absolute Kč and as a percentage of the "before" figure.

**A cost edit cannot move revenue.** Revenue is price × quantity; only price and forecast
edits affect it. Cost edits move total margin alone. Both totals are on screen because they
respond to different edits.

Rows with no sale price or no margin history are displayed and flagged, but excluded from
the totals, with the excluded count shown — so a total is never silently built on partial
data.

## Backend

New vertical slice `backend/src/Anela.Heblo.Application/Features/Pricing/` with its own
`PricingModule.cs`. Margin logic today is split across the Catalog and Analytics modules;
the simulator is a third concern and should not be bolted onto either. Validators and
`ValidationBehavior` are registered manually in the module — this codebase has no
`AddValidatorsFromAssembly`.

### Components

| Component | Responsibility |
|---|---|
| `PricingSimulationCalculator` | Pure. Baseline rows + overrides + one edit → computed rows + totals. Single source of truth; every entry point goes through it. |
| `GetPricingBaselineHandler` | Builds baseline rows from `ICatalogRepository`: latest-month `MarginData` (`M0`, `M1_A` and their `CostLevel`s), `PriceWithoutVat`, and `GetTotalSold(now−12m, now)`. |
| `RecalculatePricingHandler` | Stateless. Re-derives the baseline server-side, applies the override set and the incoming edit, returns all rows plus totals. |
| Scenario CRUD handlers | `GetPricingScenarios`, `GetPricingScenario`, `SavePricingScenario`, `DeletePricingScenario` over `IPricingScenarioRepository`. |

XLSX export is **not** a backend concern — the frontend already has
`frontend/src/utils/exportToXlsx.ts` (exceljs), which this feature reuses.

### API

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/pricing-simulator/baseline` | Filtered product set with baseline values and initial totals |
| POST | `/api/pricing-simulator/recalculate` | Override set + one edit → recomputed rows and totals |
| GET | `/api/pricing-scenarios` | List saved scenarios |
| GET | `/api/pricing-scenarios/{id}` | Load one scenario, recomputed against its snapshot |
| POST | `/api/pricing-scenarios` | Create |
| PUT | `/api/pricing-scenarios/{id}` | Update |
| DELETE | `/api/pricing-scenarios/{id}` | Delete |

Request body for recalculate. The client sends only the **sparse override set** — never
baselines, which the server re-derives itself. This keeps the payload small at any catalog
size and prevents client-supplied cost figures from reaching the calculation:

```jsonc
{
  "filter": { "productCode": null, "productName": null, "productType": null },
  "overrides": [
    { "productCode": "DEO002050", "price": 500, "materialCost": 175,
      "manufacturingCost": null, "forecastQuantity": 1100 }
  ],
  "edit": { "productCode": "DEO002050", "field": "M1Percentage", "value": 51.0 }
}
```

`overrides` carries normalised independent variables; `edit` carries the gesture the user
actually made, which the calculator normalises. `edit.field` is one of:

```
Price | M0Amount | M0Percentage | M1Amount | M1Percentage | ForecastQuantity
```

`edit` is nullable; null means "recompute everything as given", used on scenario load.

That same `overrides` array is what a scenario persists — there is no second representation
of a simulation anywhere in the system.

All DTOs are **classes, not records** (the OpenAPI generators mishandle record parameter
order). Every `*Response` inherits `BaseResponse`, which a reflection contract test enforces
in CI.

### Persistence

Two tables. The migration is written and applied **manually** — this project does not
automate migrations in deployment.

**`PricingScenario`**

| Column | Notes |
|---|---|
| `Id` | PK |
| `Name` | Required |
| `Description` | Nullable |
| `CreatedBy`, `CreatedAt`, `ModifiedAt` | Audit |
| `FilterJson` | The filter the scenario was built under |

**`PricingScenarioItem`**

| Column | Notes |
|---|---|
| `Id`, `ScenarioId` | PK / FK |
| `ProductCode` | |
| `Price`, `MaterialCost`, `ManufacturingCost`, `ForecastQuantity` | Nullable — only edited values are stored, normalised to independent variables |
| `BaselinePrice`, `BaselineMaterialCost`, `BaselineManufacturingCost`, `BaselineQuantity` | Snapshot at save time |

On load, the current baseline is re-derived and compared against the snapshot; rows whose
baseline has drifted are flagged in the UI.

Note when adding child items to a tracked scenario: do **not** set the PK explicitly when
adding to the parent's navigation collection — EF emits an UPDATE affecting zero rows
instead of an INSERT, and mocked repository tests do not catch it.

### Authorization

New feature `Finance_PriceAnalysis`, label **"Analýza cen"**, with `HasWrite: true` — read to
use the simulator, write to save scenarios. Added to `access-matrix.json` and regenerated via
`backend/tools/Anela.Heblo.AccessMatrixGen`, together with a `MenuPath` entry:

```jsonc
{ "path": "/finance/price-analysis",
  "requires": [ { "feature": "Finance_PriceAnalysis", "level": "Read" } ] }
```

A separate feature rather than reusing `Finance_MarginAnalysis`, because this adds a write
surface over commercially sensitive data and the two audiences may differ.

## Frontend

New page `frontend/src/components/pages/PriceAnalysis.tsx` at route
`/finance/price-analysis`, following `docs/design/layout_definition.md` and reusing the
filter controls from `ProductMarginsList`.

It appears in the sidebar under the **Finance** section as **"Analýza cen"**, directly after
"Analýza marže" — the two are read together: the margin report says which products are
underperforming, the simulator says what to do about it.

```
┌─ Filtr: [kód] [název] [typ]          Scénář: [Podzim 2026 ▾] 💾 ⬇ ⟲ ─┐
├─ CELKEM      před          po          Δ                              │
│  Obrat    2 840 120 Kč  3 102 400 Kč  +262 280 Kč  (+9,2 %)          │
│  M0       1 654 300 Kč  1 916 580 Kč  +262 280 Kč  (+15,9 %)         │
│  M1       1 180 900 Kč  1 489 600 Kč  +308 700 Kč  (+26,1 %)         │
│                                          17 produktů upraveno         │
├───────────────────────────────────────────────────────────────────────┤
│  Kód  Název      Cena   M0 Kč/%   M1 Kč/%   mat  výr  12m   Předp.  Δ │
│  …    Malá čar. [500]  [325/65%] [255/51%]  175   70  1240  [1240]  ●│
└───────────────────────────────────────────────────────────────────────┘
```

- The totals band is **sticky**. It is the thing being watched; it must not scroll away while
  editing row 140.
- Implied material and manufacturing costs are shown read-only, so the user can sanity-check
  what a margin edit actually implied about cost.
- **No paging.** Totals must span the whole filtered set, so the grid loads all of it. A few
  hundred products is inexpensive. Beyond ~1000 rows, virtualise the body rather than page.
- Edited cells are highlighted; each row has a reset control, and the page has a global reset.
- During a recalculate round-trip the previous totals remain visible with a subtle spinner,
  rather than blanking — the numbers must never flash.

API hooks construct absolute URLs as `${apiClient.baseUrl}${relativeUrl}`; relative URLs hit
port 3001 instead of 5001.

## Error handling

New error codes, each requiring a matching `ErrorHandlingTests` module-range bucket entry and
a Czech translation in `i18n.ts` — CI fails on either omission.

| Condition | Behaviour |
|---|---|
| Invalid cell input | Inline error on the cell, prior value retained, totals untouched |
| Recalculate fails (network / 500) | Toast, cell reverts, totals badged as stale |
| Scenario save fails | Toast; edits stay in the grid so nothing is lost |
| Product missing price or margin history | Row displayed and flagged, excluded from totals, excluded count shown |

The generated TypeScript client **throws on non-200** rather than returning
`success: false`, so `if (!response.success)` branches are dead code. Error codes are read
from the caught `SwaggerException`, where `errorCode` is a string.

## Testing

TDD throughout: test first, watch it fail, implement, watch it pass.

**Unit (backend)** — `PricingSimulationCalculator`:
each of the four edit kinds; the `M1 ≤ M0 ≤ P` invariant; negative margins permitted;
each validation rejection; zero-price and missing-margin rows; totals aggregation; excluded-row
accounting; percentage/amount equivalence on margin input.

Two cases deserve naming explicitly, because they are where the model would break:

- *edit order independence* — editing M0 then price yields the same row as editing price
  then M0, because both are normalised to costs.
- *reload fidelity* — a scenario saved after a mixed sequence of edits reproduces the
  identical row set when loaded.

**Integration (backend)** — baseline endpoint over a seeded catalog; recalculate round-trip
with a mixed override set; scenario save / load / delete; baseline-drift flagging on reload.

**Frontend unit** — edit-to-request mapping, inline error display, totals rendering,
per-row and global reset, stale-totals badge. Shell-component tests in this repo mock their
context dependencies; any new context on a shell component must be mocked there too.

**E2E** — `frontend/test/e2e/analytics/`, fixtures from
`frontend/test/e2e/fixtures/test-data.ts`, throwing (not skipping) when expected data is
absent. Flow: load the page, edit a price, assert totals move, save the scenario, reload,
assert it is restored. Authentication via `navigateToApp()`.

## Open questions

None. Deferred by decision: M2/M3 support, pending Andy's written M0–M3 definitions.
