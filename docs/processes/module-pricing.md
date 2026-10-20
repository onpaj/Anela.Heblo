---
process: module-pricing
kind: module
module: pricing
summary: Price analysis simulator (Analýza cen) — lets finance try new prices, costs and sales forecasts per product and see the effect on revenue and M0/M1 margin before deciding a new price list; saves named scenarios and exports a draft ceník, never writes prices anywhere.
owns: []
verified_at: "85855377e"
related:
  - calc-pricing-simulation
  - calc-margins
  - calc-bundle-sales-expansion
---

# Pricing (Analýza cen — price simulator)

## Purpose
When input costs rise, Anela reprices products one by one rather than by a flat percentage.
This module is the sandbox for that decision: it starts from today's selling price excl. VAT,
the latest monthly material cost (M0 cost) and manufacturing labour cost (M1 cost) and the
pieces sold over the last 12 months, lets the user change any of them, and shows the effect
on each product's margin and on total revenue, total M0 and total M1. The result is a saved
scenario and an XLSX draft price list (návrh ceníku) that people discuss and approve.

It is a **simulator only**: it never changes a price in Shoptet or Flexi and has no
background processing. Actually changing e-shop prices is a different module (Product
pricing, `/products/pricing`).

## Users & screens
Permission `Feature.Finance_PriceAnalysis` ("Analýza cen"): read =
`finance.price_analysis.read`, write = `finance.price_analysis.write`.

| Route | Who | What |
|---|---|---|
| `/finance/price-analysis` (sidebar Finance → "Analýza cen") | read | Filter (code, name, type), editable grid (Cena, Materiál, Výroba, M0 Kč/%, M1 Kč/%, Prodáno 12m, Prognóza ks), per-column bulk edit by %, work group of pinned products, summary band (revenue / M0 / M1 before-after-delta for "Všechny produkty" (default), "Filtrované produkty" or "Pracovní skupina"), product detail modal, scenario bar (load / save / delete), "Export ceníku (XLSX)" |

Saving and deleting scenarios needs write. Recalculating needs only read (nothing is stored).

MCP tools (`PricingSimulatorMcpTools`): `GetPricingBaseline`, `SimulatePricing`,
`ListPricingScenarios`, `GetPricingScenario` (read); `SavePricingScenario`,
`UpdatePricingScenarioProducts` (write). There is no MCP delete. No dashboard tile.

## Processes
- `calc-pricing-simulation` — baseline from the catalog, edit → override algebra, M0/M1 per
  row, before/after totals, scenario save / reopen with drift detection; trigger: on demand
  (page and MCP).

Plain CRUD without a process doc (rules are in `calc-pricing-simulation` → *Scenarios*):
- List / open / save / overwrite / delete scenarios (`/api/pricing-scenarios`).
- Partial scenario update — MCP `UpdatePricingScenarioProducts` only.
- XLSX export — generated in the browser from the rows on screen; no server endpoint.

No recurring jobs, no BackgroundRefresh tasks.

## Data owned
Main Heblo database, `public` schema (migration `AddPricingScenarios`, 2026-09-21):
- `PricingScenarios` — one row per saved scenario: `Name` (unique, ≤ 200), `Description`
  (≤ 2000), `CreatedBy` (author e-mail), `CreatedAt`, `ModifiedAt` (UTC), `FilterJson` (jsonb:
  the code / name / type filter it was built under).
- `PricingScenarioItems` — one row per edited product in a scenario: `ProductCode`, the
  pinned overrides `Price`, `MaterialCost`, `ManufacturingCost`, `ForecastQuantity` (each
  nullable = "follow the catalog"), and the snapshot `BaselinePrice`,
  `BaselineMaterialCost`, `BaselineManufacturingCost`, `BaselineQuantity` taken when the item
  was saved. Unique on (ScenarioId, ProductCode); cascade-deleted with the scenario.

Margins are never stored. No cache, no blob storage, no retention — scenarios live until
someone deletes them.

## External systems
None directly. All inputs come from the in-memory catalog, which itself is loaded from
Flexi and Shoptet by the Catalog module.

## Dependencies
- Reads **Catalog** (`ICatalogRepository.GetAllAsync`): `PriceWithoutVat` (effective e-shop
  price incl. running action price, else Flexi price), the latest month of the precomputed
  margin history (calc-margins: M0 cost, M1 cost) and the sales history (12-month pieces,
  which include bundle-expansion rows — calc-bundle-sales-expansion).
- Uses **Users** (`ICurrentUserService`) for the author e-mail and the MCP permission check.
- Nothing else in Heblo reads the scenario tables.

## Known quirks
- The 12-month quantity counts component pieces sold inside bundles, so revenue "before"
  is overstated for bundle components (open defect; details in `calc-pricing-simulation`).
- A reopened scenario shows today's catalog values in the "before" columns, not the saved
  snapshot; the snapshot only drives the drift flag (`BaselineDrifted`).
- Loading a scenario on the page does not restore its filter; saving afterwards stores the
  page's filter and re-snapshots every item, which clears all drift flags.
- Only M0 and M1 are simulated; M2/M3 were left out on purpose while their method was being
  redefined (design spec `docs/superpowers/specs/2026-09-21-pricing-simulator-design.md`).
- No price-elasticity model: the forecast quantity is the 12-month quantity unless edited.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Pricing/PricingModule.cs` — DI, validators
- `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingSimulationCalculator.cs` — all pricing rules
- `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingBaselineBuilder.cs` — catalog → baseline
- `backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs`, `PricingScenariosController.cs` — REST + permissions
- `backend/src/Anela.Heblo.API/MCP/Tools/PricingSimulatorMcpTools.cs` — MCP tools
- `backend/src/Anela.Heblo.Persistence/Pricing/` — EF configuration and repository
- `frontend/src/components/pages/PriceAnalysis.tsx`, `frontend/src/components/pricing/` — UI (except `PriceDivergenceReport.tsx`, which belongs to Product pricing)
