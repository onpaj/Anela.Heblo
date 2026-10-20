---
process: sync-catalog-prices
kind: sync
module: catalog
summary: Loads the e-shop selling price (Shoptet price list, incl. running actions), the Flexi price-list selling and purchase prices with VAT rate and BoM link, and the product page URL (Heureka XML feed) into the catalog cache.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetEshopPriceClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/ProductPriceFlexiDto.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductVatRateProvider.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/EshopUrl/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Price/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/EshopUrl/**
verified_at: "5e993f9e2"
related: [calc-catalog-merge, calc-margins]
---

# Catalog prices and e-shop URL

## Purpose
Answers "what does this product sell for, what does it cost us, and where is it on the web
shop?". The e-shop price is what customers pay today (the action price while an action runs).
The Flexi price list (ceník) gives the ERP selling price, the purchase price (nákupní cena), the
VAT rate and whether the item has a bill of materials (kusovník). The catalog list/detail shows
these; margins use the selling price excl. VAT and the purchase price for bought items
(`calc-margins`); the Pricing and Analytics modules read them through the catalog. The URL is
the link to the product page on anela.cz.

## Trigger
BackgroundRefresh tasks, tier 1, first run at start-up:
- `ICatalogRepository.RefreshEshopPricesData` — every 30 min → `CachedEshopPriceData`
- `ICatalogRepository.RefreshErpPricesData` — every 1 h → `CachedErpPriceData`
- `ICatalogRepository.RefreshEshopUrlData` — every 1 h → `CachedEshopUrlData`

## Data flow
1. **E-shop price** (`ShoptetEshopPriceClient.GetAllAsync`):
   1. Shoptet REST `GET /api/pricelists/{Shoptet:DefaultPriceListId}?itemsPerPage=100&page=N`,
      all pages (`ShoptetPriceListClient`). Per product: regular price with VAT and
      `price.actionPrice {price, fromDate, toDate}`.
   2. VAT rate per product from the Flexi price list (`FlexiProductVatRateProvider` → query 41);
      21 % when unknown.
   3. `ProductPriceEshop.FromPriceList` with today's date in Europe/Prague.
2. **ERP price** (`FlexiProductPriceErpClient.GetAllAsync`): Flexi user query **41**
   (`uzivatelsky-dotaz/41`), memory-cached 5 min under `FlexiProductPrices`. Row: item id,
   code, price (`cenaZakl`), purchase price, VAT level (`typszbdphk`), product type, BoM id,
   price type (`typCenyDphK`).
3. **URL** (`HeurekaProductFeedClient`): `GET HeurekaFeedOptions:ProductFeedUrl`
   (`https://www.anela.cz/heureka/export/products.xml`), each `SHOPITEM` → `ITEM_ID` (code) +
   `URL`.
4. Each list replaces its cache and schedules a merge; the merge sets `EshopPrice`, `ErpPrice`
   and `Url` on the product with the same code.

## Logic & formulas
- **Action price**: running when its price > 0 and today (Prague) is within
  [`fromDate`, `toDate`], both inclusive, missing bound = open-ended. Effective
  `PriceWithVat` = action price if running, else regular price.
  `PriceWithoutVat = round(PriceWithVat / (1 + VAT/100), 2, half away from zero)`.
  `RegularPriceWithVat`, `ActionPriceWithVat`, `ActionFrom`, `ActionUntil`, `IsInAction` are kept.
  The e-shop `PurchasePrice` is always null on purpose — Flexi is the purchase-price source.
- **ERP price**: if `typCenyDphK` is `typCeny.sDph` the price includes VAT:
  without = round(price / (1 + VAT/100), 2); otherwise (bez DPH, or missing) the price is excl.
  VAT: with = price × (100 + VAT) / 100 (not rounded). `PurchasePriceWithVat` = purchase ×
  (100 + VAT)/100. `HasBoM` = BoM id present.
- **Which price the catalog shows** (`CatalogAggregate`): `PriceWithVat`/`PriceWithoutVat` = the
  e-shop value if > 0, else ERP. `CurrentPurchasePrice` = ERP purchase price.
- Shoptet `commonPrice` (the struck-through "was" price) is never used.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Shoptet:DefaultPriceListId` | 1 ("Hlavní ceník") | Price list read for e-shop prices; required |
| `Shoptet:BaseUrl` | `https://api.myshoptet.com` (class default) | Shoptet REST base |
| `Shoptet:ApiToken` | Key Vault secret | `Shoptet-Private-API-Token` |
| `HeurekaFeedOptions:ProductFeedUrl` | `https://www.anela.cz/heureka/export/products.xml` | URL feed |
| `BackgroundRefresh:ICatalogRepository:RefreshEshopPricesData` | every 00:30:00, tier 1 | |
| `BackgroundRefresh:ICatalogRepository:Refresh{ErpPrices,EshopUrl}Data` | every 01:00:00, tier 1 | |

## Runtime facts
- 2026-10-01: 43 of 346 price-list products had an action price set, only BAL0001M's was
  running (490 Kč vs regular 539 Kč); the action price has been applied since PR #4373 — agent
  memory `gotcha_shoptet_price_export_action_price` — 2026-10-01.

## Known quirks
- **The Shoptet CSV export pattern 52 is dead**: since #4063 the price comes from the REST price
  list; changing that CSV pattern has no effect.
- **Expired actions stay in Shoptet's list**; only the date check above stops them being used.
- **A product with VAT missing in Flexi** is priced at 21 % for the without-VAT figure, and
  Flexi price writes for it are refused (logged once per load).
- **No resilience wrapper or stale guard**: a failed call leaves the previous list in place and
  the BackgroundRefresh run fails.
- The Heureka feed only lists products exported to Heureka; others have no URL.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` — the three refresh methods
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Price/ProductPriceEshop.cs` — action-price rule
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetEshopPriceClient.cs` — Prague date, VAT lookup
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs` — paging over `/api/pricelists`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs` — query 41, VAT conversion
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/EshopUrl/HeurekaProductFeedClient.cs` — XML parse
