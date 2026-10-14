---
process: calc-dqt-price-comparison
kind: calculation
module: data-quality
summary: Every morning compares each sellable product's retail price with VAT in Shoptet (source of truth) against Flexi's ceník price, storing differences, products missing in Flexi and unknown Flexi price types/VAT bands, and feeding the Kontrola cen dashboard tile.
owns:
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/PriceComparisonDqtJob.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/PriceComparisonDqtComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IPriceComparisonSource.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/PriceComparisonStatusTile.cs
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/PriceComparisonDqtAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/PriceComparisonMismatch.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftComparisonResult.cs
verified_at: "5e993f9e2"
related: []
---

# Retail price check Shoptet vs Flexi (Kontrola cen)

## Purpose
The retail price a customer pays is set in Shoptet; Flexi's price list (ceník) must carry
the same price, because Flexi prices feed documents and margin reporting. This check lists
every sellable product whose Flexi price disagrees with Shoptet, is missing in Flexi, or
can't be trusted because Flexi's price type or VAT band is unknown. Result: the dashboard
tile **Kontrola cen** (drills down to the pricing screen `/products/pricing`, where prices can
be synced) and the **Kvalita dat** page (`/automation/data-quality`, test *Kontrola cen*,
columns "Shoptet" and "Flexi").

The comparison itself belongs to the ProductPricing module (`PriceComparisonService`, the
same code as the live pricing screen — see `docs/features/product-pricing.md`); this doc
covers the nightly run and how its result is stored and shown.

## Trigger
- Hangfire recurring job `daily-price-comparison-dqt` (`PriceComparisonDqtJob`), cron
  `0 9 * * *` (09:00 Europe/Prague — after the other four checks; 06:00 collided with
  product pairing), enabled by default. Run labelled with today's date.
- Manual: **Spustit DQT** → *Kontrola cen* (`TestType = PriceComparison`). The date range is
  ignored — snapshot of current prices.

## Data flow
1. Create a `DqtRun` (`TestType` 5, Running) in `public."DqtRuns"`.
2. `PriceComparisonService.BuildReportAsync` (via `PriceComparisonDqtAdapter`):
   - In scope: catalog-cache products of type **Product, Goods or Set**, de-duplicated by code.
   - **Shoptet**: the whole retail price list `GET /api/pricelists/{Shoptet:DefaultPriceListId}`
     (100 per page, all pages); the **regular** price with VAT per code (not the time-limited
     action price). Items without a price are skipped; unreadable prices are logged and skipped.
   - **Flexi**: Flexi user query **41** (ceník: price, price type `bezDph`/`sDph`, VAT band),
     cached in memory for 5 minutes under `FlexiProductPrices` (the nightly run uses the cache
     if warm). With-VAT price is derived from the price type and VAT band.
   - Each in-scope product gets one `Kind`.
3. Guard: if **no** in-scope product had a Shoptet price, the comparer throws → run Failed,
   tile red (see quirks).
4. Mismatch kinds and `MissingInShoptet` rows are stored in `public."DqtDriftResults"`; run
   Completed with `TotalChecked` = in-scope products, `TotalMismatches` = mismatch rows only.

## Logic & formulas
- Prices are **with VAT**, CZK. Agreement = both rounded to 2 dp (half away from zero) differ
  by **≤ 0.01** (absorbs Flexi's without-VAT → with-VAT round-trip).
- Classification precedence (first match wins) and how DQT stores it:

  | Kind | Stored `MismatchCode` | Counts as mismatch | UI label |
  |---|---|---|---|
  | `MissingInShoptet` — no Shoptet price | 4 | No (informational) | Chybí v Shoptetu (bez ceny) |
  | `MissingInFlexi` — Shoptet price, no Flexi row | 2 | Yes | Chybí ve Flexi |
  | `FlexiPriceTypeUnknown` — Flexi price type not known | 3 | Yes | Neznámý typ ceny ve Flexi |
  | `FlexiVatRateUnknown` — VAT band not recognised (21 % assumed) | 5 | Yes | shown as raw "5" (see quirks) |
  | `FlexiDiffers` — prices differ > 0.01 | 1 | Yes | Rozdílná cena |
  | `InAgreement` | — | not stored | — |

- Row content: key = product code, `ShoptetValue` = Shoptet price with VAT, `HebloValue` =
  **Flexi** price with VAT (column labelled "Flexi"), `Details` = the kind name.
- **Tile** `pricecomparisonstatus` (*Kontrola cen*): newest price run; Failed → red,
  Running or mismatches > 0 → amber, otherwise green; shows checked / mismatches and, for a
  completed run, the count of `MissingInShoptet` rows ("N bez ceny v Shoptetu").

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `daily-price-comparison-dqt` | `0 9 * * *`, enabled | Schedule |
| `Shoptet:DefaultPriceListId` | 1 (`appsettings.json`; "Hlavní ceník", the retail list) | Shoptet retail price list; if unset the read throws and the run is Failed. Never the wholesale lists 38/39 |
| `Shoptet:BaseUrl`, `Shoptet:ApiToken` | secrets | Shoptet REST API access |
| Flexi connection (`FlexiBeeSettings`) | secrets | Flexi API / user query 41 |

## Runtime facts
- Live-ERP finding: writing a with-VAT price into Flexi without `typCenyDphK = typCeny.sDph`
  stored a wrong price (287.00 read back as 347.27 incl. VAT) — the reason the price-type
  field matters to this check — `docs/features/product-pricing.md` — 2026-09-11.

## Known quirks
- **"Green over nothing" is prevented by failing the run**: since `MissingInShoptet` isn't a
  mismatch, an empty or truncated Shoptet read (wrong/missing `Shoptet:DefaultPriceListId`,
  paginator stopping early, every price unreadable) would otherwise complete with zero
  mismatches. The comparer throws instead, so the run is Failed and the tile red.
- **The UI has no label for code 5** (`FlexiVatRateUnknown`): `DqtRunDetail.tsx` maps codes
  0–4 only, so such rows show the bare number "5".
- **The Shoptet side is the regular list price**, not the running action price that the catalog
  shows as the e-shop selling price since #4373; a product on sale is still compared on its
  regular price (which is what Flexi should hold).
- **Flexi may be up to 5 minutes stale** (memory cache) — irrelevant at 09:00 unless someone
  just used the pricing screen; a price written through Heblo evicts the cache.
- **Sets are in scope here but not in `calc-dqt-product-pairing`**, which only treats Product
  and Goods as sellable.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/PriceComparisonDqtComparer.cs` — guard, code mapping, informational rows
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/PriceComparisonDqtAdapter.cs` — which kinds count as mismatches
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceComparisonService.cs` — scope, tolerance, classification
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs` — Shoptet price list read
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs` — user query 41, cache
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/PriceComparisonStatusTile.cs` — tile
- `docs/features/product-pricing.md` — full pricing feature, price-type and VAT-band rules
