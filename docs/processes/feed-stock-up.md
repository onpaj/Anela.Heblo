---
process: feed-stock-up
kind: feed
summary: Pushes warehouse stock changes from received transport boxes and gift-package manufacture/disassembly into Shoptet as relative stock movements, tracked per document number in StockUpOperations.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Services/EshopStockDomainService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/*StockUpOperation*/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs
  - backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperation*.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
verified_at: "a008e2306"
related: []
---

# Stock-up to Shoptet

## Purpose
Keeps the Shoptet e-shop stock in step with physical warehouse movements that happen in Heblo,
so the shop does not oversell. Heblo computes no quantity here: each operation carries the
quantity of the event that caused it. Staff monitor and fix operations on page
`/stock-up-operations` (API `StockUpOperationsController`). A received transport box only
reaches **Stocked** once all of its operations are Completed.

Not covered here: stock taking (`EshopStockDomainService.SubmitStockTakingAsync` sets an
absolute stock via the same endpoint, but synchronously and without `StockUpOperations`).

## Trigger
No Hangfire job. Two in-process BackgroundRefresh tasks (tier 2):
- `IStockUpProcessingService.ProcessPendingOperationsAsync` — every 1 min, first run 1 min
  after start: submits every Pending operation.
- `ITransportBoxCompletionService.CompleteReceivedBoxesAsync` — every 1 min, first run 1:30
  after start: moves Received boxes to Stocked or Error.

Operations are created on demand by the source events below; staff can Retry/Accept via API.

## Data flow
1. **Create** a row in `public."StockUpOperations"` in state Pending (unique `DocumentNumber`):
   - Transport box → Received (from InTransit, Reserve or Quarantine), `ReceivedSideEffect`:
     one operation per product code, `BOX-{boxId:000000}-{productCode}`, amount = sum of the
     box's item amounts rounded half away from zero to int. Staged and saved in the same
     `SaveChanges` as the box state change; skipped if that document number already exists.
   - Gift package manufacture (`GiftPackageManufactureService`), in one DB transaction:
     `GPM-{logId:000000}-{ingredient}` with −(int)(RequiredQuantity × quantity) per ingredient,
     and `GPM-{logId:000000}-{package}` with +quantity.
   - Gift package disassembly: `GPD-{logId:000000}-{package}` with −quantity, and
     `GPD-{logId:000000}-{component}` with +(int)(RequiredQuantity × quantity) per component.
2. **Submit** (`StockUpProcessingService`): load all Pending rows (newest first) and for each,
   one at a time: mark Submitted and save → `PATCH /api/stocks/{Shoptet:StockId}/movements`
   with `{"data":[{"productCode":…, "amountChange": amount}]}` → mark Completed and save.
   Any exception → Failed with `ErrorMessage = "Processing failed: …"`; the loop continues.
3. **Box completion** (`TransportBoxCompletionService`): for each box in state Received
   (oldest first) read its `TransportBox` operations: all Completed → box Stocked; any Failed →
   box Error with the failed document numbers; none at all → box Error; otherwise wait.

## Logic & formulas
- `amountChange` is a **relative delta** in pieces (int): positive = stock-up, negative =
  consumption. Shoptet stores it as a movement by the API user; no document number is sent
  (the endpoint rejects extra fields).
- Shoptet success = HTTP 2xx **and** empty `errors[]`; a 200 with errors, or any non-2xx,
  throws `HttpRequestException` → operation Failed.
- State machine (`StockUpOperation`): Pending → Submitted → Completed; any state → Failed.
  `Reset` (Failed → Pending), `ForceReset` (any non-Completed → Pending),
  `AcceptFailure` (Failed → Completed, appends "Manually accepted at … UTC" to the error).
- Retry (`POST /api/StockUpOperations/{id}/retry`): Failed → `Reset`, Pending/Submitted →
  `ForceReset`; Completed is refused. The next processing tick re-submits it.
- Accept (`POST …/{id}/accept`): only from Failed; hides it from active lists without touching Shoptet.
- A box in Error is never re-checked by the completion task (it reads only Received boxes).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Shoptet:StockId` | 1 (class default) | Shoptet warehouse id in the movements URL |
| `Shoptet:BaseUrl`, `Shoptet:ApiToken` | secrets | Shoptet REST base address and `Shoptet-Private-API-Token` |
| `StockClient:TimeoutSeconds` | 8 | Per-attempt timeout of the Shoptet stock HttpClient |
| `StockClient:MaxRetryAttempts` | 3 | Transient-error retries of that HttpClient (see quirks) |
| `StockClient:RetryBaseDelaySeconds` | 1 | Exponential back-off base |
| `BackgroundRefresh:IStockUpProcessingService:ProcessPendingOperationsAsync` | every 00:01:00, delay 00:01:00, tier 2 | Submission loop |
| `BackgroundRefresh:ITransportBoxCompletionService:CompleteReceivedBoxesAsync` | every 00:01:00, delay 00:01:30, tier 2 | Box completion loop |

## Runtime facts
None.

## Known quirks
- **There is no duplicate protection on the Shoptet side.** Movements are deltas, Shoptet has
  no idempotency key and no document-number search, and `VerifyStockUpExistsAsync` always
  returns false (it is not called). Retrying an operation whose PATCH actually landed — e.g.
  one stuck in Submitted after a crash, or Failed because the post-PATCH save failed — applies
  the stock change **twice**. Check Shoptet stock history before retrying a Submitted row.
- **The Shoptet stock HttpClient retries transient failures** (`shoptet-stock-csv` resilience
  handler, 3 attempts, 8 s per attempt) and that handler also wraps the PATCH. A PATCH that
  timed out after Shoptet applied it can be re-sent by the retry. (Read from code, not observed.)
- **`docs/features/stock-up-process.md` is outdated**: it describes Playwright browser
  automation, pre-submit and post-verify Shoptet checks and a 2-minute completion interval.
  The current code uses the REST API, has no Shoptet-side checks and runs every minute.
- **Amounts are integers**: box items are rounded half away from zero per product; gift
  ingredient/component quantities are truncated by `(int)` cast.
- **Gift-package ingredient check uses warehouse stock only** (`WarehouseStock`), because
  consumption is booked against the warehouse; checking `Stock.Available` (which adds transport
  and "sklad výroby") let stock go negative on 2026-09-15. The disassembly check and the
  package's displayed stock still use `Stock.Available` (`LogisticsCatalogSourceAdapter`).
  The ingredient check is best-effort: two concurrent runs can both pass.
- **Shoptet refuses stock changes on product sets** (`stock-change-not-allowed`), per
  `docs/integrations/shoptet-api.md` §8.5 — such an operation ends Failed.
- **A Failed operation puts its whole box into Error**; after Retry succeeds the box stays in
  Error until someone moves it on manually.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs` — create/stage and the submit loop
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs` — state machine
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/EshopStockDomainService.cs` — bridge to the Shoptet client
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` — `UpdateStockAsync` PATCH and error handling
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — HttpClient and retry handler
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs` — BOX- operations
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — GPM-/GPD- operations, ingredient check
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs` — Logistics → Catalog contract
- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — box Stocked/Error
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RetryStockUpOperation/RetryStockUpOperationHandler.cs` — retry rules
- `backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs` — table, unique index
- `docs/integrations/shoptet-api.md` — §8 stock endpoints
