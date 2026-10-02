---
process: feed-product-weight
kind: feed
module: catalog
summary: Recomputes each finished product's net weight from its Flexi bill of materials and writes it back to the Flexi price list (ceník), nightly for all products or on demand for one.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductWeightRecalculationJob.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductWeightRecalculationService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Services/IProductWeightRecalculationService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductWeightRecalculationResult.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RecalculateProductWeight/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Products/FlexiProductClient.cs
verified_at: "5e993f9e2"
related: [calc-catalog-merge, sync-catalog-stock]
---

# Product weight to Flexi

## Purpose
Keeps the net weight (hmotnost) of every manufactured product in Flexi equal to the weight of
its recipe, so shipping, packaging and any export that reads the Flexi weight stay right when a
recipe changes. Heblo computes nothing itself: Flexi's BoM (kusovník) weight is read and copied
into the price-list item. The new weight comes back into Heblo with the next ERP stock load
(`CatalogAggregate.NetWeight`).

## Trigger
- Hangfire recurring job `product-weight-recalculation`, cron `0 2 * * *` (02:00 Europe/Prague),
  enabled by default, category Catalog; can be run from Recurring Jobs; skipped when disabled
  there. All products.
- Manually: `POST /api/Catalog/recalculate-product-weight` (all) or
  `POST /api/Catalog/recalculate-product-weight/{productCode}` (one), permission
  Products_Catalog write.

## Data flow
1. Products: all catalog aggregates with `Type == Product` (the single-product call refuses any
   other type, including `Set`).
2. For each product, one at a time: Flexi `kusovnik` BoM weight for the code
   (`IBoMClient.GetBomWeight` → `NetWeight`).
3. If a weight came back: save the Flexi price-list item (`cenik`) with that code and
   `Weight` = the BoM weight (`IPriceListClient.SaveAsync`). A Flexi error result throws
   "Failed to update product Weight: …".
4. The in-memory aggregate's `NetWeight` is set to the new value.
5. Result: processed / success / error counts and messages; the job logs them and sends
   telemetry `ProductWeightRecalculation` (`Success` or `PartialSuccess`).

## Logic & formulas
- Weight is taken as Flexi computes it for the BoM, in Flexi's weight unit; Heblo does not
  convert it.
- No comparison with the current value — every product is written every night.
- A product whose BoM returns no weight is counted as success and not written (job path); the
  single-product path reports it as an error ("no weight returned").

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Hangfire job `product-weight-recalculation` | `0 2 * * *`, enabled | Schedule |
| Flexi connection (`FlexiBeeSettings`) | secrets | ERP API |

## Runtime facts
None.

## Known quirks
- **Sets and semi-products are skipped**: only `ProductType.Product`; BAL/SET gift packages are
  typed `Set` by the catalog and never recalculated.
- **Writes every product every night** to the live ERP, even when nothing changed.
- **The job ignores cancellation** (`RecalculateAllProductWeights()` is called without the token),
  so a stopping app still walks the whole list.
- **Errors per product do not fail the Hangfire run**; they appear only in logs/telemetry as
  `PartialSuccess`.
- **The cached aggregate is patched in place** (`product.NetWeight = …`), against the catalog's
  clone-and-swap rule; harmless because the next merge reloads the weight from ERP stock.
- Runs at the same minute as `product-export-download`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductWeightRecalculationJob.cs` — schedule, telemetry
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/ProductWeightRecalculationService.cs` — product selection, loop
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Products/FlexiProductClient.cs` — BoM weight read and ceník write
- `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs` — manual endpoints
