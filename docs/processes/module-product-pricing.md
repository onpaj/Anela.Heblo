---
process: module-product-pricing
kind: module
module: product-pricing
summary: Keeps the retail selling price (with VAT) the same in Shoptet and ABRA Flexi — a live comparison of both price lists (Ceny produktů), an operator price edit written to both systems, and a one-click push of Shoptet prices into Flexi.
owns: []
verified_at: "290275262"
related:
  - calc-price-divergence
  - flow-product-price-write
---

# Product pricing (Ceny produktů)

## Purpose
Anela's retail price for every product, goods item and set lives in two places: the e-shop
price list in **Shoptet** (what customers pay; the "Hlavní ceník", id 1) and the ceník in
**ABRA Flexi** (what the ERP puts on invoices and what accounting sees). **Shoptet is the
source of truth.** The two drift apart whenever someone changes a price directly in Shoptet's
or Flexi's own admin, or when a Heblo write reaches only one of them.

This module answers "do Shoptet and Flexi agree on the retail price of every product, and if
not, which ones and by how much?", and lets the operator fix it from one screen:
- change a product's price once in Heblo and have it written to **both** systems;
- push the Shoptet price into Flexi for every row that differs (Shoptet is never written by
  that action).

There is **no local master price table** and no scheduled price sync: every view reads both
systems live. (An earlier design with a master price table and an hourly sync job was removed
in September 2026; its migrations `20260903130253_AddProductPricing` and
`20260910155013_DropProductPriceMasterTables` are still in the history.)

All prices in this module are **including VAT** and are the **regular** (list) price — a
running Shoptet action price ("akční cena") is ignored here, unlike in the catalog and margins.

## Users & screens
Permission `Feature.Products_Catalog` ("Katalog"): read = `products.catalog.read`, write =
`products.catalog.write`.

| Route | Who | What |
|---|---|---|
| `/products/pricing` (sidebar Produkty → "Ceny", page title "Ceny produktů") | read | Summary tiles (counts per comparison result), table of every priced product with Shoptet price, Flexi price, Flexi price type, difference in Kč and %, filter by name/code and "Zobrazit pouze rozdílné". Read-only users get a banner "Pouze čtení — … nic se nezapisuje". |
| same page | write | Pencil per row to edit the price (writes Shoptet + Flexi); button "Synchronizovat (N)" pushes Shoptet → Flexi for the N rows currently visible after filtering. A warning banner says saving writes to the live shop and the live ERP. |

Dashboard tile "Kontrola cen" (`pricecomparisonstatus`) belongs to the DataQuality module; it
shows the latest nightly price check and links to `/products/pricing`.

API: `ProductPricingController` (`/api/product-pricing`). No MCP tool.

## Processes
- `calc-price-divergence` — live Shoptet-vs-Flexi comparison and classification of every
  priced product; trigger: opening `/products/pricing` (`GET /api/product-pricing/divergence`),
  and nightly through DataQuality's price check.
- `flow-product-price-write` — the two write actions: an operator's price edit
  (`PUT /api/product-pricing/prices/{productCode}`, Shoptet then Flexi) and the
  Shoptet → Flexi sync of the visible rows (`POST /api/product-pricing/sync`); both log to
  `ProductPriceChangeLogs`.

The nightly automated check — Hangfire job `daily-price-comparison-dqt` (`0 9 * * *`),
DQT test type `PriceComparison` — is owned and documented by the **DataQuality** module; this
module only supplies its data through `PriceComparisonDqtAdapter` (see `calc-price-divergence`).

No recurring jobs of its own, no plain CRUD.

## Data owned
- `public."ProductPriceChangeLogs"` — one row per price write Heblo **attempted** (edit or
  sync), whatever the outcome: product code, old and new price with VAT, `ChangedAt` (UTC),
  `ChangedBy` (user e-mail, else name, else id, else "unknown"), `ShoptetSucceeded`,
  `FlexiSucceeded`, `ErrorMessage` (≤ 2000 chars). Index (ProductCode, ChangedAt). Created by
  migration `20260910185627_AddProductPriceChangeLog`. **Append-only and read by nothing** —
  no endpoint, no screen, no repository read method.
- In-memory cache entry `FlexiProductPrices` (5 min) is written by the shared Flexi ceník
  reader and evicted by this module's Flexi price writer; it is not owned here but this module
  depends on its freshness.

## External systems
| System | Direction | What |
|---|---|---|
| Shoptet REST API | read | `GET /api/pricelists/{DefaultPriceListId}?itemsPerPage=100&page=N` (whole list), `GET /api/pricelists/{id}?code=X` (one product) |
| Shoptet REST API | **write** | `PATCH /api/pricelists/{id}` with `priceWithVat.price` — price edit only |
| ABRA Flexi | read | user query 41 (`uzivatelsky-dotaz/41`, whole ceník: `idcenik`, `kod`, `cena`, `typszbdphk`, `typcenydphk`, …) |
| ABRA Flexi | **write** | `PUT /c/{company}/cenik/{idcenik}.json` with `cenaZakl` + `typCenyDphK = typCeny.sDph` — price edit and sync |

Neither system has a sandbox: every write hits the live e-shop / live ERP.

## Dependencies
- Reads the in-memory **Catalog** (`ICatalogRepository.GetAllAsync`) for the list of products
  in scope (types Product, Goods, Set) and their names.
- Shares adapters with the Catalog: `ShoptetPriceListClient` also feeds the catalog's e-shop
  price (`ShoptetEshopPriceClient`, which applies the action price), and Flexi user query 41
  (`FlexiProductPriceErpClient`) is also the catalog's ERP price and purchase-price source.
  `IProductVatRateProvider` / `VatRateCalculator` live in this module's domain folder but are
  used only by the catalog's e-shop price load; `IErpPurchasePriceWriter` likewise lives here
  but is used only by the Catalog/Purchase nightly purchase-price sync.
- **DataQuality** consumes the comparison through its own contract `IPriceComparisonSource`
  (implemented here by `PriceComparisonDqtAdapter`).

## Known quirks
- The comparison and the sync use Shoptet's **regular** price. While a Shoptet action runs,
  customers pay the action price but this module (and Flexi, after a sync) holds the regular
  one; the catalog and margins use the action price. Different "e-shop price" by design.
- `docs/features/product-pricing.md` (the original spec) is partly out of date: it still says
  the Flexi write converts to an excl-VAT price, that there is no server-side price ceiling
  (the validator caps at 1,000,000 Kč), that only `bezDph` items can be written, and lists 5
  classification kinds (there are 6). The code is right; the detail is in the two process docs.
- Code comments in `SetProductPriceHandler`, `FlexiProductVatRateProvider` and the
  query-41 warning say a price write is refused for an unknown Flexi price type or VAT band.
  It is not — the write no longer uses a VAT rate, and only a missing ceník id blocks it.
- `ProductPriceChangeLogs` cannot tell an edit from a sync (no operation column), and nobody
  reads it.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs` — endpoints and permissions
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/ProductPricingModule.cs` — DI, validators, DataQuality binding
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceComparisonService.cs` — the comparison
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/` — report, set price, sync
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs` — Shoptet price list read/write
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/` — query 41 read, ceník price writer
- `frontend/src/pages/ProductPricingPage.tsx`, `frontend/src/components/pricing/PriceDivergenceReport.tsx`, `frontend/src/api/hooks/useProductPricing.ts` — UI
- `docs/features/product-pricing.md` — original design spec (partly stale, see quirks)
- `docs/integrations/shoptet-api.md` → price lists section — Shoptet wire details
