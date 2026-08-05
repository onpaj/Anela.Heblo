## Module
Transport Boxes (Logistics)

## Finding
`ChangeTransportBoxStateHandler.HandleReceived` creates one `StockUpOperation` per product **before** the box's state transition is persisted, and the two writes are committed in **separate transactions** with no enclosing transaction anywhere in the app.

- `HandleReceived` runs at `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs:230-262`, calling `_stockOperationService.CreateOperationAsync(...)` per product group (`:246`).
- That delegates to `StockUpProcessingService.CreateOperationAsync`, which does `AddAsync` + **`SaveChangesAsync`** immediately — committing each operation on its own (`backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs:36-37`).
- Only afterwards does the handler apply the transition (`transition.ChangeStateAsync`, `:126`) and commit the box in a **second** `SaveChangesAsync` (`:135`).
- There is no transaction: `grep BeginTransaction`/`TransactionScope` over `backend/src` returns nothing; the only MediatR pipeline behaviors are validation/logging.
- `DocumentNumber` (`BOX-{box.Id:000000}-{productCode}`) has a **unique index** (`backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs:52-55`, "Layer 1 protection") and `CreateOperationAsync` does no dedup before inserting.

## Why it matters (concrete failure scenario)
1. Operator receives a box (InTransit→Received); `HandleReceived` creates and commits `StockUpOperation` rows for every product.
2. The box's own `SaveChangesAsync` (`:135`) fails transiently (DB blip/concurrency) or the process crashes in the window. Box stays **InTransit**; operations remain **Pending**.
3. The background stock-up pipeline processes the Pending operations → **inventory is increased**, but the box is never Received/Stocked. `TransportBoxCompletionService` only scans `Received` boxes, so it never reconciles this one.
4. Operator retries Receive → `HandleReceived` re-inserts the same `DocumentNumber`s → **unique-constraint violation** → swallowed by the blanket `catch (Exception)` (`:159-168`) → generic error. The box is now **permanently wedged in InTransit** despite its stock already being added; recovery needs manual DB intervention.

The handler emits an externally-visible, self-committing side effect (inventory) before persisting its own aggregate state, with no transaction and no idempotent create — a persistence misuse whose consequence is inventory/aggregate divergence plus an unrecoverable box. The unique-index "Layer 1 protection" converts a transient failure into a permanent one on retry.

## Suggested direction
Make receive atomic (an explicit DB transaction spanning both commits), **or** make stock-op creation idempotent (dedup on the deterministic `DocumentNumber` so retries are no-ops), **or** use an outbox/deferred emission so operations are only created once the box transition has committed. Don't rely on two ordered `SaveChangesAsync` calls.

<!-- harness-issue:tsk_d9a9ff00b49d42a8:3151a426 -->

