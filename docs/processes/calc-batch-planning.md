---
process: calc-batch-planning
kind: calculation
module: manufacture
summary: Splits one bulk semi-product batch across all the finished products (pack sizes) filled from it, so that every size reaches the same number of days of sales coverage, using catalog stock, planned manufacture and sales history.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/BatchPlanningService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IBatchPlanningService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/BatchDistributionCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IBatchDistributionCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ConsumptionRateCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IConsumptionRateCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductBatch.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductVariant.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchPlanRequestValidator.cs
verified_at: "5e993f9e2"
related:
  - calc-batch-calculation
  - flow-manufacture-order
  - calc-manufacture-stock-analysis
---

# Batch planning

## Purpose
One bulk batch (e.g. 10 kg of a cream) is filled into several pack sizes (30 ml, 50 ml,
refill …). Batch planning (page `/manufacturing/batch-planning`, "Plánování dávek") proposes how
many pieces of each size to fill so that no size runs out before the others, given what is on
stock, what is already planned, and how fast each size sells. The result can be turned into a
manufacture order (`flow-manufacture-order`). MCP tool `CalculateBatchPlan` exposes it to Claude.

## Trigger
On demand: `POST /api/manufacture-batch/calculate-batch-plan` (permission
`Manufacture_BatchPlanning` write). No persistence.

## Data flow
1. If the requested code is a finished **Product**: read its Flexi BoM; its phase type decides
   single- vs multi-phase, and for multi-phase the code is replaced by the BoM's semi-product.
2. Sales window: `TimePeriod` resolved by `TimePeriodResolver` (same presets as purchase
   analysis), else `[FromDate, ToDate]`, else the last 30 days.
3. **Multi-phase**: products using the semi-product = Flexi BoM lines where it is an ingredient
   (`FindByIngredientAsync`, kusovník "where used"); for each one found in the catalog read
   `Stock.Total`, `Stock.Planned`, `NetWeight` (grams per piece) and `SalesHistory`.
4. **Single-phase**: the product itself.
5. Optimise → per-size recommended pieces, future stock and coverage, summary.

## Logic & formulas
- **Daily sales rate** = Σ (B2B + B2C sold pieces in each range) ÷ Σ days of the ranges ×
  `SalesMultiplier`, rounded to 2 dp, from the catalog sales history (`CatalogAggregate.SalesHistory`, owned by the catalog module).
- **Stock counted** = `Stock.Total` + `Stock.Planned` (open manufacture orders, added by #4223; the two never overlap — memory note gotcha_stock_planned_disjoint_from_total).
  `CurrentDaysCoverage` = that ÷ daily rate.
- **Multi-phase volume**: available bulk = `TotalWeightToUse` if given, else
  `MmqMultiplier` (default 1) × semi-product MMQ; minus `DirectSemiproductAmount` (bulk sold as-is).
  Fixed sizes (`ProductConstraints.IsFixed`) take `FixedQuantity × NetWeight` first; if they need
  more than available → error `FixedProductsExceedAvailableVolume` with the deficit.
- **Optimiser** (`BatchDistributionCalculator`, over sizes with daily sales > 0 and weight > 0):
  binary-search the largest common coverage D (0–1000 days, step 0.1) such that
  Σ ceil(max(D × dailySales − effectiveStock, 0)) × weight ≤ remaining bulk; then each size gets
  floor(max(D × dailySales − effectiveStock, 0)) pieces. Leftover bulk is then filled with extra
  pieces, **largest pack first**, until less than the smallest pack weight remains.
- **Single-phase**: pieces = `MmqMultiplier × MMQ` (MMQ in pieces), or `TotalWeightToUse ÷ NetWeight`,
  or `TargetDaysCoverage (default 30) × daily rate`; rounded to whole pieces.
- `FutureStock` = current + planned + recommended; `FutureDaysCoverage` = ÷ daily rate (∞ shown as max when no sales).
- Units: bulk in grams (or ml as stored in `NetWeight`), sizes in pieces.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Fallback sales window | 30 days (constant in handler) | When no period and no dates are sent |
| Flexi product attribute MMQ | per product | Default bulk amount (multi-phase: grams of semi-product; single-phase: pieces) |
| Catalog `NetWeight` | per product | Grams of bulk per piece |

## Runtime facts
None.

## Known quirks
- **`TargetDaysCoverage` mode is ignored for multi-phase.** The multi-phase path always uses
  `TotalWeightToUse ?? MmqMultiplier × MMQ`; a coverage target only affects the reported
  `TargetDaysCoverage`. `CalculateTargetProductionAndVolume`, which handles all three modes, is never called (dead code).
- **Sizes with no sales or no `NetWeight` get 0** from the optimiser except via the leftover
  fill; a size missing `NetWeight` contributes 0 g, so its pieces are effectively free.
- **Leftover goes to the largest pack**, although the code comment says the opposite.
- **`CalculateBatchPlanRequestValidator` is dead code** (never registered), so mode-specific
  "required" rules and `FromDate ≤ ToDate` are not enforced server-side.
- Products found in BoMs but missing from the catalog are skipped with a log warning.
- `TimePeriod = CustomPeriod` without both dates yields no ranges and a daily rate of 0.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` — product → semi-product, sales window
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/BatchPlanningService.cs` — single/multi-phase plan, fixed sizes, summary
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/BatchDistributionCalculator.cs` — equal-coverage optimiser
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ConsumptionRateCalculator.cs` — daily sales rate
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs` — period presets
