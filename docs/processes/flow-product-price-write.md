---
process: flow-product-price-write
kind: workflow
module: product-pricing
summary: Operator-driven writes of the retail price with VAT — a price edit written to Shoptet then Flexi, and a Shoptet-to-Flexi sync of the rows on screen — each attempt recorded in ProductPriceChangeLogs.
owns:
  - backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/ProductPricingModule.cs
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SetProductPrice/**
  - backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SyncProductPrices/**
  - backend/src/Anela.Heblo.Domain/Features/ProductPricing/IEshopPriceListClient.cs
  - backend/src/Anela.Heblo.Domain/Features/ProductPricing/EshopPriceListEntry.cs
  - backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPriceWriter.cs
  - backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductPriceChangeLogRepository.cs
  - backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPriceChangeLog.cs
  - backend/src/Anela.Heblo.Persistence/ProductPricing/**
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceWriter.cs
  - frontend/src/pages/ProductPricingPage.tsx
  - frontend/src/components/pricing/**
  - frontend/src/api/hooks/useProductPricing.ts
  - frontend/src/api/hooks/priceDivergenceMerge.ts
verified_at: "290275262"
related:
  - calc-price-divergence
---

# Product price edit and Shoptet → Flexi sync

## Purpose
Two buttons on **Ceny produktů** (`/products/pricing`) that change live prices:

1. **Edit price** (pencil on a row) — the operator sets a new retail price **with VAT**; Heblo
   writes it to the e-shop (Shoptet) and then to the ERP (Flexi), so both agree without
   anyone touching either admin.
2. **Synchronizovat (N)** — for the N rows currently visible after filtering, copies the
   Shoptet price into Flexi wherever they differ. Shoptet is never written by this action.
   This is the fix for a price someone changed directly in Shoptet.

Both are live writes with no test environment behind them. Each attempted write is recorded
in `ProductPriceChangeLogs` as history.

## Trigger
User action, permission `products.catalog.write` (buttons are hidden without it):
- Edit: `PUT /api/product-pricing/prices/{productCode}` body `{ priceWithVat }`
  (`SetProductPriceHandler`). Synchronous.
- Sync: `POST /api/product-pricing/sync` body `{ productCodes: [...] }`
  (`SyncProductPricesHandler`). Synchronous, up to ~2 minutes of writes.

No Hangfire job, no retry queue.

## Data flow
### Price edit (states: nothing written → Shoptet written → both written)
1. Validate: code 1–50 chars; `0.01 ≤ priceWithVat ≤ 1,000,000`.
2. Read the current Shoptet price — `GET /api/pricelists/{id}?code=X`. Read error →
   `ProductPriceShoptetWriteFailed` (3605); no price → `ProductPriceNotFoundInShoptet` (3602).
   Nothing written.
3. Pre-flight Flexi — find the ceník row for the code in query 41 (5-min cache
   `FlexiProductPrices`). Read error → `ProductPriceErpReadFailed` (3609); no row or
   `idcenik ≤ 0` → `ProductPriceFlexiItemIdUnknown` (3603). Nothing written.
4. Write Shoptet — `PATCH /api/pricelists/{id}` body
   `{"data":[{"code":X,"priceWithVat":{"price":"287.00"}}]}`. Fails →
   `ProductPriceShoptetWriteFailed` (3605); nothing written.
5. Write Flexi — `PUT {Server}/c/{Company}/cenik/{idcenik}.json` body
   `{"winstrom":{"cenik":{"cenaZakl":"287.00","typCenyDphK":"typCeny.sDph"}}}` (5-min HTTP
   timeout). On success the `FlexiProductPrices` cache entry is evicted so the next comparison
   reads the new price. Fails → `ProductPriceFlexiWriteFailed` (3606): **Shoptet already has
   the new price, Flexi does not** — no rollback, no retry.
6. Append a `ProductPriceChangeLogs` row for every outcome of steps 2–5 (validation failures
   are not logged): old = Shoptet's previous price.
7. Frontend refetches the divergence report on success and on 3606.

### Sync Shoptet → Flexi
1. Validate: 1–10,000 codes, each 1–50 chars. Codes that are not priced catalog products are
   ignored.
2. Fresh scoped comparison (`calc-price-divergence`, `BuildScopedReportAsync`): Flexi reloaded
   bypassing the cache; Shoptet read per product (≤ 25 codes) or as the whole list.
   A read failure here propagates as an error; nothing written.
3. Targets = rows with kind `FlexiDiffers` **and** Shoptet price > 0. If none → return the rows.
4. For each target, sequentially: look up `idcenik` (first ceník row per code; a warning is
   logged if any code has several), then the same Flexi `PUT` as above with the Shoptet price.
   - no ceník id → counted failed, no log row;
   - write error → counted failed, log row with the error;
   - write cut off by the budget → counted failed, log row
     "Write budget spent while the write was in flight; outcome unknown.";
   - success → counted written, log row (`ShoptetSucceeded = true` although nothing was sent
     to Shoptet; old = Flexi's previous price with VAT).
5. Stop starting new writes after **2 minutes** (`WriteBudget`); the rest are `remainingCount`.
6. If anything was written, re-run the scoped comparison to return the rows as Flexi now holds
   them (a failure here falls back to the pre-write rows).
7. Response: `rows`, `writtenCount`, `failedCount`, `remainingCount`. The frontend merges the
   rows into the cached report (`mergeSyncedRows`), re-counts the summary tiles, and shows
   "Synchronizováno v HH:MM — zapsány N ceny do Flexi, M řádek se změnil" plus
   "Zbývá N … — spusťte synchronizaci znovu" or an amber alert for failures.

## Logic & formulas
- The operator always types the price **with VAT**; Shoptet and Flexi receive exactly that
  number, formatted `F2`. No VAT rate is used anywhere on the write path.
- Flexi write declares `typCenyDphK = typCeny.sDph`, so Flexi stores the number as a with-VAT
  price. **Side effect:** an item that was `bezDph` becomes `sDph` in Flexi master data.
- Only `priceWithVat.price` (the regular price) is sent to Shoptet; an action price
  (`actionPrice`) on the product is left as it is.
- Sync never writes rows that are `InAgreement`, `MissingInShoptet`, `MissingInFlexi`,
  `FlexiPriceTypeUnknown` or `FlexiVatRateUnknown` — the "unknown" rows would come back
  unknown after a write (the unknown-ness is in what query 41 returns) and be rewritten on
  every click.
- Frontend guards: confirm dialog when the new price differs from the Shoptet price by more
  than 50 %, always for a product with no Shoptet price yet; a draft below 0.01 is rejected;
  sync asks `window.confirm` naming the number of products and the live ERP.
- Both adapters refuse a price ≤ 0 (Shoptet treats 0 as a real free price since 2026-09-14;
  writing by code instead of `idcenik` would create a new Flexi ceník item).
- Change log rows are written with `CancellationToken.None`; a failed insert is logged and
  swallowed (never changes the response).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Shoptet:DefaultPriceListId` | `1` | Price list written and read ("Hlavní ceník") |
| `Shoptet:ApiToken` | Key Vault / secrets | Shoptet write access |
| `FlexiBeeSettings:Server` / `Company` / `Login` / `Password` | placeholders; real values in Key Vault | Flexi ceník PUT (basic auth) |
| constants | — | price 0.01–1,000,000; sync ≤ 10,000 codes; write budget 2 min (gateway gives up at 230 s); Flexi PUT timeout 5 min |

## Runtime facts
- Writing `cenaZakl` alone to a `bezDph` item put 287 in as the base price and Flexi showed
  347.27 with VAT; converting to excl-VAT first also wrote the wrong figure — hence price +
  `typCeny.sDph` together — code comments in `FlexiProductPriceWriter` /
  `SetProductPriceHandler`, live ERP — 2026-09-11.
- Shoptet treats a literal 0 as a genuine free price, not "clear the price" — code comment in
  `ShoptetPriceListClient` — 2026-09-14.
- Shoptet `PATCH` with a flat `priceWithVat` returns 422 `invalid-request-data`; it must be an
  object — `docs/integrations/shoptet-api.md` / client comment (no date).

## Known quirks
- **Partial failure is permanent until someone acts.** Shoptet written + Flexi failed (3606)
  leaves the two divergent; the row shows `FlexiDiffers` and the next sync or nightly
  DataQuality check is the safety net.
- **Editing during a Shoptet action** changes only the regular price; customers keep paying the
  action price until it ends, and the comparison then shows the new regular price.
- **Stale code comments**: `SetProductPriceHandler` (class summary), `FlexiProductVatRateProvider`
  and the query-41 warning say the edit is refused for an unknown Flexi price type or VAT
  rate. It is not — only a missing `idcenik` blocks it. `docs/features/product-pricing.md` also
  still describes the old excl-VAT conversion, "no server-side ceiling" and "only bezDph items
  are written".
- Read failure on the Shoptet pre-read of an edit is reported as 3605 "Shoptet write failed".
- `ProductPriceChangeLogs` has no operation column: a sync whose Flexi write failed looks
  exactly like an edit whose Flexi leg failed, and `OldPriceWithVat` means Flexi's old price in
  one case and Shoptet's in the other. Nothing reads the table.
- A sync row with no ceník id is counted failed but leaves no log row (only an app log warning).
- Several ceník rows for one code: the first is written; the sync logs a warning, the edit
  does not.
- A read-only user has no way to force-refresh the comparison (the sync button is write-only);
  they must reload the page and accept up to 5-minute-old Flexi prices.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SetProductPrice/SetProductPriceHandler.cs` — edit, order of steps, error codes
- `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SyncProductPrices/SyncProductPricesHandler.cs` — sync, target rule, budget, counts
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceWriter.cs` — Flexi PUT payload, cache eviction
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs` — Shoptet PATCH payload
- `backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceChangeLogRepository.cs` — log append, truncation, detach on failure
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — 3602–3609
- `frontend/src/components/pricing/PriceDivergenceReport.tsx`, `frontend/src/api/hooks/useProductPricing.ts`, `frontend/src/api/hooks/priceDivergenceMerge.ts` — confirmations, merge, status line
