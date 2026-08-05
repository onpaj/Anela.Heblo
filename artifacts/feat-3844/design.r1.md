# Design: Atomic and idempotent TransportBox Receive

## Component Design

No new components are introduced. Five existing files are modified; their contracts and responsibilities change as follows.

### `IStockUpProcessingService` (Catalog application layer)

Responsibility unchanged (create a `StockUpOperation` for a stock-up source). Signature gains a trailing optional parameter that controls whether the create is flushed to the database immediately or merely staged on the shared `ApplicationDbContext` change tracker.

```csharp
Task CreateOperationAsync(
    string documentNumber,
    string productCode,
    int amount,
    StockUpSourceType sourceType,
    int sourceId,
    CancellationToken ct = default,
    bool persistImmediately = true);
```

`persistImmediately` is placed **after** `CancellationToken`, not before it, so every existing call site that passes `ct` positionally as its last argument (all four call sites in `GiftPackageManufactureService`) continues to compile unchanged and continues to get `true` (today's immediate-commit behavior) by default. This is a deliberate deviation from the "ct-last" convention; a comment on the interface should record why, so it isn't "corrected" later and silently reintroduce a positional-argument break for existing callers.

### `StockUpProcessingService` (implementation)

Owns two responsibilities now instead of one: idempotent create, and conditional persistence.

```csharp
public async Task CreateOperationAsync(
    string documentNumber, string productCode, int amount,
    StockUpSourceType sourceType, int sourceId,
    CancellationToken ct = default, bool persistImmediately = true)
{
    var existing = await _repository.GetByDocumentNumberAsync(documentNumber, ct);
    if (existing != null)
    {
        _logger.LogInformation(
            "StockUpOperation {DocumentNumber} already exists (Id={OperationId}, State={State}); skipping duplicate create",
            documentNumber, existing.Id, existing.State);
        return;
    }

    var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);
    await _repository.AddAsync(operation, ct);

    if (persistImmediately)
    {
        await _repository.SaveChangesAsync(ct);
    }
}
```

- **Idempotency pre-check** (FR-2): queries the database via the existing `IStockUpOperationRepository.GetByDocumentNumberAsync`, not the change tracker's locally-staged-but-unsaved entities. This is sufficient because a single `HandleReceived` invocation calls `CreateOperationAsync` at most once per distinct `DocumentNumber` (guaranteed by the caller's `GroupBy(i => i.ProductCode)`), so there is no same-call duplicate the pre-check could miss. Cross-call duplicates (a prior interrupted attempt, or a genuine retry) are exactly what it's designed to catch.
- **Conditional persistence** (FR-1 mechanism): `AddAsync` always stages the new entity on the shared `ApplicationDbContext`'s change tracker; `SaveChangesAsync` only fires when `persistImmediately` is `true`. When `false`, the staged insert is left pending for a later, caller-owned `SaveChangesAsync` to flush — this is what makes the box update and the operation inserts commit together as one implicit EF Core transaction.
- Applies uniformly to both callers of the shared service (transport-box Receive and gift-package manufacture/disassembly), so both get the dedup guard from a single implementation.

### `ILogisticsStockOperationService` / `LogisticsStockOperationAdapter` (Logistics↔Catalog module boundary)

Pure pass-through parameter addition — no new logic. Preserves the existing cross-module contract pattern (Logistics depends only on this interface; the adapter, living in Catalog infrastructure, is the only thing that knows about `IStockUpProcessingService`).

```csharp
Task CreateOperationAsync(
    string documentNumber,
    string productCode,
    int amount,
    LogisticsStockOperationSource sourceType,
    int sourceId,
    CancellationToken cancellationToken = default,
    bool persistImmediately = true);
```

`LogisticsStockOperationAdapter.CreateOperationAsync` maps `LogisticsStockOperationSource` → `StockUpSourceType` as it does today and forwards `persistImmediately` unchanged into `_stockUpProcessingService.CreateOperationAsync`.

### `ChangeTransportBoxStateHandler.HandleReceived` (Logistics use case)

No control-flow restructuring — `HandleReceived` still runs before `transition.ChangeStateAsync`, `_repository.UpdateAsync(box, ct)`, and `_repository.SaveChangesAsync(ct)` in `Handle`. The only change is the single call site inside `HandleReceived`'s per-product loop, which now passes `persistImmediately: false`:

```csharp
await _stockOperationService.CreateOperationAsync(
    documentNumber,
    group.ProductCode,
    group.Amount,
    LogisticsStockOperationSource.TransportBox,
    box.Id,
    cancellationToken,
    persistImmediately: false);
```

Because `ITransportBoxRepository` and `IStockUpOperationRepository` are both scoped wrappers over the same request-scoped `ApplicationDbContext`, the `StockUpOperation` inserts staged here (via `AddAsync`, not yet sent to the database) and the box's `State`/`StateLog` update staged later in `Handle` are flushed together by `Handle`'s existing single `_repository.SaveChangesAsync(cancellationToken)` call — one implicit transaction, one commit, no explicit `BeginTransactionAsync` (which is CI-blocked by `scripts/check-no-managed-tx.sh` and incompatible with the registered `PollyExecutionStrategy` without additional `ExecuteAsync` wrapping this fix deliberately avoids).

### `GiftPackageManufactureService` (unaffected caller — FR-3)

No production-code change. Its four call sites (`CreateManufactureAsync`, `DisassembleGiftPackageAsync`) already pass `ct` as the last positional argument and never reference `persistImmediately`, so they keep resolving to the default `true` — identical immediate-commit behavior to today, plus the new dedup pre-check as an incidental defense-in-depth improvement (one extra `SELECT` per call, within NFR-1's stated budget).

## Data Schemas

No schema changes. No new entities, fields, migrations, or indexes.

### `StockUpOperation` (existing entity, unchanged shape)

| Field | Type | Notes |
|---|---|---|
| `Id` | int (PK) | unchanged |
| `DocumentNumber` | string | unique; `BOX-{boxId:000000}-{productCode}` for transport-box sources; **unchanged derivation** — this fix does not touch how the value is computed, only whether/when a row with that value is inserted |
| `ProductCode` | string | unchanged |
| `Amount` | int | unchanged |
| `SourceType` | `StockUpSourceType` (TransportBox, GiftPackageManufacture) | unchanged |
| `SourceId` | int | unchanged |
| `State` | `StockUpOperationState` (Pending, Submitted, Completed, Failed) | unchanged |
| `CreatedAt` / `SubmittedAt` / `CompletedAt` / `ErrorMessage` | — | unchanged |

Constraint: `IX_StockUpOperations_DocumentNumber_Unique` (unique index on `DocumentNumber`) remains in place, untouched, as the defense-in-depth guard behind the new application-level pre-check.

### `TransportBox` (existing entity, unchanged shape)

`State` (`TransportBoxState`) and `StateLog` (audit trail) are written exactly as today by `transition.ChangeStateAsync` + `_repository.UpdateAsync`; only the **timing of the surrounding commit** changes (it now shares one `SaveChangesAsync` call with the pending `StockUpOperation` inserts instead of running after an already-committed set of operations).

### Method contract shapes (request/response — no wire/API changes)

No public/external API surface changes. `ChangeTransportBoxStateRequest` / `ChangeTransportBoxStateResponse` and the HTTP endpoint wrapping `ChangeTransportBoxState` are unaffected; the shapes below are internal C# method signatures, not transport payloads.

**`IStockUpProcessingService.CreateOperationAsync`** — internal call shape:
```
Input:  documentNumber: string, productCode: string, amount: int,
        sourceType: StockUpSourceType, sourceId: int,
        ct: CancellationToken = default,
        persistImmediately: bool = true
Output: Task (void; no return value change)
Side effect (persistImmediately=true, default): one existence check (SELECT by DocumentNumber)
        + [if not existing] one staged insert + one immediate SaveChangesAsync (unchanged
        end-to-end behavior for GiftPackageManufactureService)
Side effect (persistImmediately=false): one existence check + [if not existing] one staged
        insert only — no SaveChangesAsync; caller is responsible for a later flush
```

**`ILogisticsStockOperationService.CreateOperationAsync`** — identical shape, `sourceType` typed as `LogisticsStockOperationSource` instead of `StockUpSourceType`; adapter performs the enum mapping and forwards `persistImmediately` unchanged.

### Data flow / commit boundary (the actual fix)

```
First-time Receive (N distinct products), happy path:
  HandleReceived:  N × [GetByDocumentNumberAsync (miss) → AddAsync (staged only)]
  Handle:          transition.ChangeStateAsync → UpdateAsync(box) → SaveChangesAsync
                        └─ ONE ApplicationDbContext.SaveChangesAsync() call
                           └─ ONE implicit transaction containing:
                                • N StockUpOperation INSERTs (staged above)
                                • 1 TransportBox UPDATE + StateLog INSERT
                           → committed atomically; retried as a unit by PollyExecutionStrategy
                             on transient failure (safe — this is exactly the pattern the
                             resilience layer is designed around)

Retry after a partial prior failure (some/all DocumentNumbers already exist):
  HandleReceived:  re-derives same DocumentNumbers
                   → existing ones: pre-check hits → log + skip, no AddAsync, no duplicate
                   → missing ones: pre-check misses → staged normally
  Handle:          proceeds to ChangeStateAsync + the single SaveChangesAsync exactly as in
                   the happy path → box reaches Received; no unique-constraint violation,
                   no manual DB intervention

Crash/failure before the final SaveChangesAsync:
  Nothing was sent to the database (AddAsync only mutates the in-memory change tracker) →
  on retry, pre-check finds nothing → creates fresh, as in the happy path.

Failure during the final SaveChangesAsync itself:
  EF Core's implicit transaction rolls back everything staged in that call → neither the
  StockUpOperation rows nor the box update persist → box remains in its pre-transition
  state → satisfies FR-1's "never operations-without-box-transition" invariant.
```
