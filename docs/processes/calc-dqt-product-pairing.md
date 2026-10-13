---
process: calc-dqt-product-pairing
kind: calculation
module: data-quality
summary: Every morning checks that each Shoptet product resolves to a sellable Flexi product (by pair code or code) and that each sellable Flexi product exists in Shoptet, storing every unpaired product as a drift result.
owns:
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/ProductPairingMismatch.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftComparisonResult.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtDriftResult.cs
verified_at: "5e993f9e2"
related: []
---

# Product pairing Shoptet vs Flexi (Párování produktů)

## Purpose
A product sold on the e-shop must be linked to the same product in Flexi, or its sales,
stock and invoices can't be booked correctly (the invoice import writes Shoptet codes into
Flexi). This check lists (a) Shoptet products that point to no sellable Flexi product and
(b) sellable Flexi products that are not on the e-shop at all. Results appear on the
**Kvalita dat** page (`/automation/data-quality`, test *Párování produktů*); there is no
dashboard tile for it.

## Trigger
- Hangfire recurring job `daily-product-pairing-dqt` (`ProductPairingDqtJob`), cron
  `0 6 * * *` (06:00 Europe/Prague), enabled by default. Run is labelled with today's date.
- Manual: **Spustit DQT** → *Párování produktů* (`TestType = ProductPairing`). The date range
  is ignored — it is always a snapshot of the current state.

## Data flow
1. Create a `DqtRun` (`TestType` 2, Running) in `public."DqtRuns"`.
2. **Shoptet**: download the products CSV export at `StockClient:Url`
   (`ShoptetStockClient.ListAsync`; `;`-separated, windows-1250; column 0 = code, 1 = pair
   code (*párovací kód*), 2 = name). Fresh download, not the catalog cache.
3. **Flexi**: `FlexiStockClient.ListAsync` — Flexi stock-to-date (`stav-skladu-k-datu`) for
   today, warehouses 5 (materials), 20 (semi-products) and 4 (products + goods); items whose
   name contains "archiv" are dropped. Only items of type **Product or Goods** count as
   *sellable* (`DataQualityErpStockSourceAdapter`).
4. Both reads go through the catalog resilience pipeline (3 retries with exponential back-off
   from 1 s, 30 s timeout, circuit breaker). If a read still fails the run is Failed.
5. Compare (`ProductPairingDqtComparer`) and store each finding in `public."DqtDriftResults"`;
   run Completed with `TotalChecked` and `TotalMismatches`.

## Logic & formulas
All code comparisons are case-insensitive.
- **Check A — Shoptet → Flexi**: for each Shoptet row, the resolved code is the pair code if
  set, otherwise the Shoptet code. If it is not among sellable Flexi codes → finding.
- **Check B — Flexi → Shoptet**: for each sellable Flexi code, if it is neither a Shoptet code
  nor a Shoptet pair code → finding.
- `TotalChecked` = number of distinct identifiers across all Shoptet codes, Shoptet pair
  codes and sellable Flexi codes.
- **Mismatch codes** (`ProductPairingMismatch`, bit flags):

  | Code | Label (UI) | Meaning | Row content |
  |---|---|---|---|
  | 1 `MissingInErp` | Chybí v ERP | Shoptet product has no sellable Flexi match | key = Shoptet code, Shoptet = product name |
  | 5 = 1+4 `PairCodeUnresolved` | Chybí v ERP + Nespárovaný párový kód | Same, and the product has a pair code that doesn't exist in Flexi | as above, `Details` names the pair code |
  | 2 `MissingInShoptet` | Chybí v Shoptet | Sellable Flexi product absent from Shoptet | key = Flexi code, Heblo = Flexi name |

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `daily-product-pairing-dqt` | `0 6 * * *`, enabled | Schedule |
| `StockClient:Url` | placeholder (`https://www.anela.cz/export/products.csv-xxxxxxxxxx`) — real URL is a secret | Shoptet products CSV export |
| `StockClient:TimeoutSeconds`, `StockClient:MaxRetryAttempts` | 8, 3 | HttpClient timeout/retries of that download |
| Flexi connection (`FlexiBeeSettings`) | secrets | Flexi API access |

Warehouse ids 4/5/20 are constants in `FlexiStockClient`.

## Runtime facts
None.

## Known quirks
- **The "sellable" set is whatever Flexi's stock-to-date report for warehouse 4 returns** for
  Product/Goods types. A Flexi product that isn't in that report, or whose name contains
  "archiv", is invisible here — its Shoptet product is reported `MissingInErp` even though the
  item exists in Flexi. (Read from code; what the report includes for zero-stock items is
  decided by Flexi.)
- **Only Product and Goods types are sellable.** A Shoptet product paired to a Flexi item of
  any other type (material, semi-product, …) is reported `MissingInErp`.
- **Every Shoptet variant row is checked**, so one unpaired product with several variants
  produces several findings.
- The job description in Recurring Jobs says "for the current day", but nothing in the check
  is date-bound.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` — both checks, codes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` — schedule
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` — run lifecycle (shared by all drift tests)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs` — "sellable" rule
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` — CSV download and column map
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs` — warehouses, "archiv" filter
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs` — retry/timeout/circuit breaker
