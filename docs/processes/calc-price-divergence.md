---
process: calc-price-divergence
kind: calculation
module: product-pricing
summary: Live comparison of the retail price with VAT in Shoptet and in ABRA Flexi for every priced catalog product, classified into agreement / difference / missing / unknown — shown on Ceny produktů and fed to the nightly DataQuality price check.
owns:
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/**
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/**
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/**
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/GetPriceDivergenceReport/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/ProductPriceFlexiDto.cs
verified_at: "290275262"
related:
  - flow-product-price-write
---

# Price divergence (Shoptet vs Flexi)

## Purpose
Answers "does Flexi hold the same retail price as the e-shop for every product we sell?".
Shoptet is the source of truth; a product whose Flexi price differs will be invoiced from the
ERP at a different price than the shop charges.

Consumers:
- Page **Ceny produktů** (`/products/pricing`): summary tiles and one row per product.
- The nightly DataQuality price check (job `daily-price-comparison-dqt`, documented by the
  DataQuality module) and its dashboard tile "Kontrola cen".
- The Shoptet → Flexi sync (`flow-product-price-write`) uses the same comparison, scoped to the
  rows on screen, to decide what to write.

It never writes anything anywhere.

## Trigger
On demand, no cache of its own:
- `GET /api/product-pricing/divergence` (`GetPriceDivergenceReportHandler`) — when the page
  loads (React Query refetches as usual; a successful price edit invalidates it).
- `PriceComparisonDqtAdapter.GetDivergencesAsync` — called by DataQuality's
  `PriceComparisonDqtComparer` from Hangfire job `daily-price-comparison-dqt`, cron `0 9 * * *`
  (Europe/Prague), enabled by default.
- `BuildScopedReportAsync` — from the sync endpoint, for a named selection (see below).

## Data flow
1. **Products in scope**: `ICatalogRepository.GetAllAsync` (in-memory catalog) → keep
   `ProductType.Product`, `Goods`, `Set` → de-duplicate by product code (case-insensitive).
2. **Shoptet** (`ShoptetPriceListClient`):
   - full report: `GetPricesWithVatAsync` → `GET /api/pricelists/{Shoptet:DefaultPriceListId}?itemsPerPage=100&page=N`
     for every page;
   - scoped report with ≤ 25 products: `GetPriceWithVatAsync` per product, sequentially →
     `GET /api/pricelists/{id}?code=X`; more than 25 → the full list, then filtered.
   Result: code → **regular** price with VAT. Items without a price, with blank code or with
   an uninterpretable price are skipped (the last are logged as errors).
3. **Flexi** (`FlexiProductPriceErpClient.GetAllAsync`): user query 41, whole ceník in one
   call, cached in `IMemoryCache` key `FlexiProductPrices` for 5 minutes. The full report uses
   the cache; a scoped report forces a reload (and refreshes the shared entry for everyone).
   Rows with blank code dropped; duplicates by code → first row wins.
4. **Row per in-scope product** (`BuildRow`): Shoptet price, Flexi with/without VAT, Flexi
   price type, difference, kind.
5. **Summary**: counts per kind + total in scope.
6. Targets: JSON response to the page; for DataQuality, `PriceComparisonDqtAdapter` maps rows
   to `PriceDivergence {ProductCode, ShoptetPriceWithVat, FlexiPriceWithVat, Kind (as text), IsMismatch}`
   and DataQuality persists the run.

## Logic & formulas
**Shoptet price with VAT** (per item, from `price.price`): if `includingVat` is true, the
number as is; else `price × (1 + vatRate/100)`; rounded to 2 decimals, away from zero. The
`actionPrice` is **ignored** here.

**Flexi price with VAT** (from query 41 `cena` = `cenaZakl`, per `ProductPriceFlexiDto`):
- `typcenydphk = typCeny.sDph` → `cena` is already with VAT; without VAT = `cena / (1 + VAT/100)` rounded to 2.
- otherwise (`typCeny.bezDph` **or missing**) → `cena` is without VAT; with VAT = `cena × (100 + VAT)/100` (not rounded).
- VAT rate from `typszbdphk`: `typSzbDph.dphZakl`/`dphZakl`/`základní` = 21,
  `dphSniz`/`snížená` = 12, `dphSniz2`/`druhá snížená` = 10, `dphOsv`/`osvobozeno` = 0
  (enum and Czech spellings, case-insensitive). Anything else → 21 % is assumed for the read
  and the row is flagged (below). One warning per batch is logged for each unknown.

**Classification** (`PriceComparisonService.ClassifyRow`), first match wins:

| # | Kind (enum value) | Rule | Counts as DQT mismatch |
|---|---|---|---|
| 1 | `MissingInShoptet` (2) | no Shoptet price | no (informational) |
| 2 | `MissingInFlexi` (3) | Shoptet has a price, query 41 has no row | yes |
| 3 | `FlexiPriceTypeUnknown` (4) | Flexi row has no `typcenydphk` → with-VAT figure is a guess | yes, even if numbers match |
| 4 | `FlexiVatRateUnknown` (5) | VAT band not recognised → 21 % was assumed | yes, even if numbers match |
| 5 | `FlexiDiffers` (1) | both known, prices differ | yes |
| 6 | `InAgreement` (0) | both known, `|round2(Shoptet) − round2(Flexi)| ≤ 0.01` | no |

The 0.01 Kč tolerance (`FlexiRoundTripTolerance`) exists because Flexi stores the price
without VAT and recomputes it on read (190.00 → 157.02 → 189.99).

**Difference**: `DifferenceWithVat = round2(Flexi − Shoptet)` (positive = Flexi is higher);
`DifferencePercent = round2(difference / Shoptet × 100)`; both null when either price is
missing, the percent also null when the Shoptet price is 0. Rounding is away from zero.

DataQuality throws (run = Failed, tile red) when **no** product had a Shoptet price — that
means a broken Shoptet read, not a clean result.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Shoptet:DefaultPriceListId` | `1` | Shoptet retail price list ("Hlavní ceník"); missing → every call throws `InvalidOperationException` |
| `Shoptet:BaseUrl` | `https://api.myshoptet.com` (class default) | Shoptet REST host |
| `Shoptet:ApiToken` | Key Vault / secrets | Shoptet private API token (shared with all Shoptet adapters) |
| `FlexiBeeSettings:Server` / `Company` / `Login` / `Password` | placeholders; real values in Key Vault | Flexi access |
| constants | — | tolerance 0.01 Kč; per-product Shoptet read up to 25 products; Flexi cache 5 min |

## Runtime facts
- Query 41 returns the enum VAT vocabulary (`typSzbDph.dphZakl`, …) and has selected
  `typcenydphk` since 2026-09-11 — code comments in `ProductPriceFlexiDto` (verified against
  company anela_cosmetics_test) — 2026-09-11.
- On 2026-10-01, 43 of 346 Shoptet price-list products carried an `actionPrice`, only
  BAL0001M's was running (regular 539, action 490 Kč) — agent memory
  `gotcha_shoptet_price_export_action_price` — 2026-10-01. Such a product shows
  `InAgreement` here when Flexi holds 539.

## Known quirks
- **Regular price, not the selling price.** During a Shoptet action the report compares the
  list price; the catalog/margins use the action price. A product can be "in agreement" here
  while customers pay something else.
- **Product universe = the catalog.** A product missing from Heblo's catalog (e.g. empty Flexi
  product group) is not compared at all; if the catalog is not loaded yet, the report is empty.
- The full report is not cached by the backend: each page load reads the whole Shoptet price
  list live (1 request per 100 products). Flexi may be up to 5 minutes stale unless a write or
  a sync evicted/reloaded the cache entry.
- A missing `typcenydphk` makes **every** row `FlexiPriceTypeUnknown` (company-wide), hiding
  any real differences behind it; those rows are never auto-synced.
- An uninterpretable Shoptet price is logged and skipped, so that product shows as
  `MissingInShoptet` — not a mismatch on the DQT tile.
- Duplicate ceník rows for one code: the first row is silently used.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceComparisonService.cs` — scope, reads, classification, rounding
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/PriceDivergenceKind.cs` — the kinds
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/PriceComparisonDqtAdapter.cs` — what DataQuality counts as a mismatch
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs` — query 41, cache, with-VAT derivation
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/ProductPriceFlexiDto.cs` — VAT band map
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs` — Shoptet read
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/PriceComparisonDqtComparer.cs` — the nightly consumer (DataQuality)
- `frontend/src/components/pricing/PriceDivergenceReport.tsx` — the screen
