---
process: calc-manufacture-output
kind: calculation
module: manufacture
summary: Monthly production volume report — every Flexi manufacture receipt of the last N months, summed per product and weighted by the product's manufacture difficulty into one "output" number per month.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConstants.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/GetManufactureOutputRequestValidator.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureHistoryClient.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureHistoryRecord.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs
  - backend/src/Anela.Heblo.API/Controllers/ManufactureOutputController.cs
verified_at: "5e993f9e2"
related:
  - feed-manufacture-to-flexi
  - calc-margins
---

# Manufacture output (Souhrn výroby)

## Purpose
Page `/manufacturing/output` ("Souhrn výroby") shows how much the workshop produced each
month, as a chart of a difficulty-weighted output figure with a per-product breakdown and a
detail list of the underlying receipts. It is a workload indicator: one jar of a hard product
counts more than one jar of an easy one.

The same Flexi reader (`FlexiManufactureHistoryClient`) also feeds the catalog's manufacture
history (BackgroundRefresh task `RefreshManufactureHistoryData`, catalog module), which
the margin calculation and the stock analysis use.

## Trigger
On demand: `GET /api/manufacture-output?monthsBack=N` (permission
`Manufacture_ManufactureOutput`), default 13 months. Reads Flexi live on every call — no cache.

## Data flow
1. Flexi `skladovy-pohyb-polozka` (stock movement lines, direction In), one request per configured
   document type in `DataSourceOptions:ManufactureDocumentTypeIds` (de-duplicated), in parallel,
   for `[now − N months, now]`; up to 2 retries on 502/503/504.
2. Group lines by (calendar day, product code without Flexi prefix): Σ amount, average unit
   price, Σ total price → `ManufactureHistoryRecord`.
3. Catalog cache → products that have a `ManufactureDifficulty` (product name + difficulty).
4. Per month: per product Σ amount × difficulty (default 1.0 when the product has none);
   month `TotalOutput` = Σ of those; zero-amount products dropped; empty months filled with 0.

## Logic & formulas
- `WeightedValue = quantity × difficulty`; `TotalOutput = Σ WeightedValue` per month.
- Document types in repo config `[54, 56, 65, 67]`: per memory note gotcha_semiproducts_inflate_m1a_denominator,
  54 / 65 are semi-product receipts (VYROBA-POLOTOVAR / V-PRIJEM-POLOTOVAR, amounts in **grams**),
  56 / 67 product receipts (pieces; 67 = `V-PRIJEM-VYROBEK`, Heblo's own documents).
- Prices are Flexi stock-movement prices (cost, without VAT); the report shows them only in the detail list.
- Months are calendar months of the Flexi movement date; the window itself is computed from UTC now.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:ManufactureDocumentTypeIds` | `[54, 56, 65, 67]` | Flexi movement types read as "manufactured"; empty → the call throws |
| `monthsBack` query | 13 (1–60 by attribute) | Report length |
| Catalog `ManufactureDifficulty` | per product (catalog difficulty settings) | Weight; 1.0 when missing |

## Runtime facts
- Semi-product types 54/65 have been ingested since #4249 (v3.152.0, 2026-09-21); product type 67
  receipts had been missing from history before that (since 2026-03-24). In the window
  2025-09-01..2026-09-30 semi-products contributed 4,448,141 g of 4,540,056 total amount — memory
  note gotcha_semiproducts_inflate_m1a_denominator — 2026-09.

## Known quirks
- **Semi-product grams dominate the chart.** No semi-product has a difficulty setting, so each
  gram of bulk counts as 1 output point while a finished jar counts its difficulty (~35); since
  2026-09-21 the monthly total is mostly bulk weight, not pieces. The same effect hit the M1
  manufacturing cost rate (`calc-margins`).
- **Products without a difficulty show their code as name** — the name lookup uses only products that have a difficulty.
- **`DocumentNumber` is always empty** in the detail list: records are grouped per day and product, and the grouping does not carry a document number.
- Config arrays merge index-wise, so an environment override with fewer elements duplicates ids;
  the client de-duplicates them — memory note gotcha_config_binder_appends_arrays.
- **`GetManufactureOutputRequestValidator` is dead code** (never registered); the `[Range]` attribute on `MonthsBack` is what limits it.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` — monthly aggregation
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs` — Flexi read, grouping, retries
- `backend/src/Anela.Heblo.API/appsettings.json` — `DataSourceOptions:ManufactureDocumentTypeIds`
