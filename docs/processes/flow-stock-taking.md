---
process: flow-stock-taking
kind: workflow
module: catalog
summary: E-shop stock taking (Inventura) — a warehouse worker counts a product and Heblo sets that absolute stock in Shoptet and logs the count in StockTakingRecords; also how an ERP stock taking is patched into the catalog cache.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingHistory/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureStockSyncAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockTakingRecord.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockTakingType.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/EshopStockTakingRequest.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/EshopStockSupply.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockTakingRepository.cs
  - backend/src/Anela.Heblo.Persistence/Logistics/StockTaking/**
  - backend/src/Anela.Heblo.API/Controllers/StockTakingController.cs
verified_at: "5e993f9e2"
related: [sync-catalog-stock, sync-catalog-master-data, feed-stock-up, calc-catalog-merge]
---

# Stock taking (Inventura)

## Purpose
Lets the warehouse correct the e-shop stock of a product to what is physically on the shelf, so
Shoptet neither oversells nor hides goods. Every count is recorded (who, when, old → new), and
the inventory list shows how many days ago each product was last counted ("Posl. Inventura").
Page: **Zásoby produktů** `/logistics/inventory` → product row → inventory modal
("Inventarizace"). History:
`GET /api/StockTaking/history`. Permission: Manufacture_MaterialInventory (write to submit).

## Trigger
User action: the worker enters the counted quantity in the inventory modal and confirms.
Frontend sends `POST /api/StockTaking/submit` with `productCode`, `targetAmount` and
`softStockTaking` = true when the entered quantity equals the current e-shop stock shown
(rounded to 2 decimals), else false.

## Data flow
1. `SubmitStockTakingHandler` → `EshopStockDomainService.SubmitStockTakingAsync`.
2. **Hard stock taking** (`softStockTaking = false`):
   1. `GET /api/stocks/{Shoptet:StockId}/supplies?code={code}` → old amount = `amount + claim`.
   2. `PATCH /api/stocks/{Shoptet:StockId}/movements` with
      `{"data":[{"productCode": code, "realStock": target}]}` — an **absolute** stock set.
      Non-2xx or a non-empty `errors[]` → exception.
3. **Soft stock taking** (`softStockTaking = true`): no Shoptet call; the record has old = new =
   target ("count confirmed").
4. Insert a row into `public."StockTakingRecords"`: `Type` = Eshop, `Code`, `AmountOld`,
   `AmountNew`, `Date` (UTC now), `User` (current user name).
5. On success the handler appends the record to the cached product and sets its `Stock.Eshop`
   to the new amount, so the page shows it immediately. The next e-shop stock load
   (≤ 5 min) and stock-taking load (≤ 5 min) confirm it (`sync-catalog-stock`,
   `sync-catalog-master-data`).
6. Any exception → response error `StockTakingFailed` with the message; nothing is saved.

**ERP stock taking (materials, semi-products)** is a separate flow owned by the Manufacture
module (`SubmitManufactureStockTakingHandler` → `FlexiStockTakingDomainService`, writing to
Flexi and a `StockTakingRecords` row of type Erp). Catalog's part is
`CatalogManufactureStockSyncAdapter.SyncErpStockTakingAsync`: it reloads that product's Flexi
lots and calls `CatalogCacheStore.ApplyErpStockTakingAsync`, which patches the new ERP stock and
lots into the ERP-stock and lots **source** caches (under a lock) and into the current and stale
merged lists (clone and swap), so the value survives the next merge.

## Logic & formulas
- Shoptet "real stock" = the absolute quantity on hand; Shoptet derives availability from it.
- Old amount (hard) = Shoptet `amount + claim` (claim = pieces reserved by open orders), so the
  logged difference is against physical stock, not against the available figure.
- Validation: product code required, ≤ 50 chars; target 0 … 999 999.99.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Shoptet:StockId` | 1 | Shoptet warehouse id in both URLs |
| `Shoptet:BaseUrl`, `Shoptet:ApiToken` | `https://api.myshoptet.com`, Key Vault | Shoptet REST |
| `StockClient:TimeoutSeconds`, `MaxRetryAttempts`, `RetryBaseDelaySeconds` | 8, 3, 1 | Shoptet stock HttpClient timeout and retry |

## Runtime facts
None.

## Known quirks
- **Failed stock takings leave no trace in the DB**: the error record is returned to the UI but
  never saved, so the history shows only successful counts.
- **Soft vs hard is decided by the browser** comparing with the e-shop figure it displayed; if
  Shoptet changed meanwhile, a "soft" count records a value Shoptet does not have.
- **No DryRun**: Shoptet REST has none; every hard count changes the live shop.
- **In-place cache patch**: the e-shop handler mutates the cached aggregate (`SyncStockTaking`)
  instead of clone-and-swap, and only the merged list — a merge before the next e-shop stock load
  restores the old e-shop figure for up to 5 minutes. The ERP path does it properly.
- **ERP patch before first load**: if the ERP stock cache is still empty after a restart, the
  ERP stock-taking patch cannot be applied to the source cache and the next merge reverts it
  (logged as a warning) — fixed for the normal case in PR #3953, agent memory
  `gotcha_catalogcachestore_no_rmw`.
- The Shoptet HttpClient retry also wraps the PATCH, but an absolute set is idempotent, so a
  repeated call is harmless (unlike stock-up deltas in `feed-stock-up`).
- `docs/integrations/shoptet-api.md` §8 documents the stock endpoints.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/EshopStockDomainService.cs` — `SubmitStockTakingAsync`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` — `GetSupplyAsync`, `SetRealStockAsync`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingHandler.cs` — response, cache patch
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs` — `ApplyErpStockTakingAsync`
- `backend/src/Anela.Heblo.Persistence/Logistics/StockTaking/StockTakingConfiguration.cs` — table
- `frontend/src/components/inventory/InventoryModal.tsx` — soft/hard decision
