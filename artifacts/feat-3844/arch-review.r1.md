# Architecture Review: Atomic and idempotent TransportBox Receive

## Skip Design: true

Backend-only persistence/transaction fix inside an existing MediatR handler and an existing application service. No new endpoints, DTOs, or UI surface. Confirmed by reading `ChangeTransportBoxStateHandler.cs` end-to-end: the request/response contract (`ChangeTransportBoxStateRequest`/`ChangeTransportBoxStateResponse`) is untouched, and no frontend files reference the changed internals.

## Architectural Fit Assessment

The spec's factual claims all check out against the code as it stands today:

- `HandleReceived` (`ChangeTransportBoxStateHandler.cs:273-305`) calls `_stockOperationService.CreateOperationAsync` per aggregated product **before** `Handle` executes `transition.ChangeStateAsync` (`:126`) and `_repository.UpdateAsync` + `_repository.SaveChangesAsync` (`:134-135`).
- `StockUpProcessingService.CreateOperationAsync` (`StockUpProcessingService.cs:22-42`) does `AddAsync` + its **own** `SaveChangesAsync` immediately — an independent commit.
- `ITransportBoxRepository` and `IStockUpOperationRepository` are both thin `BaseRepository<TEntity,TKey>` wrappers constructed directly over `ApplicationDbContext` (`TransportBoxRepository.cs:12`, `StockUpOperationRepository.cs:12`), and both are registered `AddScoped` off `provider.GetRequiredService<ApplicationDbContext>()` (`LogisticsModule.cs:20-24`, `CatalogModule.cs:52`). `ApplicationDbContext` is `AddDbContext`-registered (Scoped) in `PersistenceModule.cs`. Within one MediatR request scope, both repositories share one context instance — confirmed, not just asserted.
- `IStockUpOperationRepository.GetByDocumentNumberAsync` already exists (`IStockUpOperationRepository.cs:5`, implemented in `StockUpOperationRepository.cs` via `Context.Set<StockUpOperation>().FirstOrDefaultAsync(x => x.DocumentNumber == documentNumber, ct)`), so FR-2's dedup primitive requires no new code, only a call site.
- `IX_StockUpOperations_DocumentNumber_Unique` is a real, unique DB index (`StockUpOperationConfiguration.cs:52-55`).

**One fact materially changes the mechanism decision and the spec did not have visibility into it:** this codebase has a custom EF Core execution strategy, `PollyExecutionStrategy` (`backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs`), registered via `npgsql.ExecutionStrategy(...)` in `PersistenceModule.cs`, with `RetriesOnFailure => true`. EF Core forbids user-initiated (`Database.BeginTransactionAsync`) transactions under a retrying execution strategy unless the whole unit of work is wrapped in `Database.CreateExecutionStrategy().ExecuteAsync(...)` — and **this repo enforces that by CI**: `scripts/check-no-managed-tx.sh` greps `backend/src` for `BeginTransaction|UseTransaction` and fails the build if either appears, wired into `.github/workflows/ci-feature-branch.yml`. The script's own comment states the reason explicitly: *"The PollyExecutionStrategy retries an EF Core operation by replaying it; a caller-owned transaction would silently break that contract by reusing a stale NpgsqlTransaction. SaveChangesAsync's implicit transaction is safe."*

This settles FR-1's open mechanism choice, which the spec deliberately left to the architect: **the explicit `BeginTransactionAsync` option is not merely more complex than the deferred-single-`SaveChangesAsync` option — it is CI-blocked and would fail the build.** The only compliant mechanism is FR-1's second bullet: suppress the per-operation `SaveChangesAsync` for this call path and let the existing, already-`SaveChangesAsync`-wrapped box commit flush both sets of pending changes together. `SaveChangesAsync` on a shared `DbContext` already wraps everything the change tracker has staged (multiple `AddAsync` + one `Update`) in a single implicit transaction, and it already goes through `PollyExecutionStrategy` automatically (no additional wrapping needed) — so this option gets both atomicity and retry-safety for free, with no interaction with the resilience layer at all. Outbox is correctly ruled out for the same reason the spec gives (unnecessary infrastructure given the shared-`DbContext` fact) and additionally because it would be pure net-new machinery when the existing implicit-transaction path already solves the problem.

## Proposed Architecture

### Component Overview

```
ChangeTransportBoxStateHandler.Handle
  │
  ├─ CallBackMap dispatch → HandleReceived(box, request, ct)
  │     │
  │     └─ foreach aggregated (ProductCode, Amount):
  │           _stockOperationService.CreateOperationAsync(
  │               documentNumber, productCode, amount, TransportBox, box.Id,
  │               ct, persistImmediately: false)     ← ILogisticsStockOperationService
  │               │
  │               └─ LogisticsStockOperationAdapter.CreateOperationAsync(...)  (maps enum)
  │                     │
  │                     └─ StockUpProcessingService.CreateOperationAsync(...)  ← IStockUpProcessingService
  │                           │
  │                           ├─ existing = _repository.GetByDocumentNumberAsync(documentNumber, ct)   [FR-2 pre-check]
  │                           ├─ if existing != null → log + return (idempotent skip)
  │                           └─ else → _repository.AddAsync(operation, ct)   [staged only, NOT saved — persistImmediately=false]
  │
  ├─ transition.ChangeStateAsync(box, currentTime, userName)     ← mutates box.State + appends StateLog, in memory
  │
  └─ _repository.UpdateAsync(box, ct); _repository.SaveChangesAsync(ct)
        │
        └─ ApplicationDbContext.SaveChangesAsync()   ← ONE flush, ONE implicit transaction, covers:
              • the box's State/StateLog update (via ITransportBoxRepository's DbSet.Update)
              • every staged StockUpOperation insert from this HandleReceived call (via IStockUpOperationRepository's DbSet.AddAsync)
              (same ApplicationDbContext instance — confirmed above)
```

`GiftPackageManufactureService` keeps calling `CreateOperationAsync` with `persistImmediately` defaulted to `true` (see below) — its call sites are unmodified, its immediate-commit behavior is unmodified.

### Key Design Decisions

#### Decision 1: Atomicity mechanism — deferred single `SaveChangesAsync`, not an explicit transaction

**Options considered:**
- (a) `ApplicationDbContext.Database.BeginTransactionAsync(...)` spanning both writes, committed once at the end of `Handle`.
- (b) Suppress the per-call `SaveChangesAsync` inside the stock-up-operation create path for this call, and let `Handle`'s existing box `SaveChangesAsync` flush both.
- (c) Outbox/deferred emission.

**Chosen approach:** (b).

**Rationale:** (a) is blocked by `scripts/check-no-managed-tx.sh` (CI-enforced) and is fundamentally incompatible with `PollyExecutionStrategy` (`RetriesOnFailure = true`) without additionally wrapping the whole unit of work in `Database.CreateExecutionStrategy().ExecuteAsync(...)` — extra plumbing this codebase deliberately avoids everywhere (zero `BeginTransactionAsync`/`UseTransaction` hits anywhere in `backend/src` today). (c) is unneeded complexity per the spec's own "Out of Scope" reasoning, which this review independently confirms: both repositories already share one `DbContext`, so a distributed/eventual mechanism buys nothing. (b) requires **zero reordering** of `Handle`'s existing statements — `HandleReceived` already runs before `ChangeStateAsync`/`UpdateAsync`/`SaveChangesAsync` today; the only change is making the per-operation commit inside `StockUpProcessingService.CreateOperationAsync` conditional, and passing `persistImmediately: false` from the one call site that needs deferral. This is the minimal-surface-area change consistent with "surgical changes."

#### Decision 2: How to suppress the immediate commit without regressing `GiftPackageManufactureService` (FR-3)

**Options considered:**
- (a) Add a `bool persistImmediately` parameter to `CreateOperationAsync` on both `IStockUpProcessingService` and `ILogisticsStockOperationService`, threaded through `LogisticsStockOperationAdapter`.
- (b) Add a second method (e.g. `StageOperationAsync`) alongside the existing `CreateOperationAsync`.
- (c) Bypass `ILogisticsStockOperationService` entirely from `ChangeTransportBoxStateHandler` and call `IStockUpOperationRepository.AddAsync` directly.

**Chosen approach:** (a), with `persistImmediately` defaulted to `true` and placed **after** `CancellationToken ct = default` in the parameter list (deviating from the usual "ct-last" convention).

**Rationale:** (c) is rejected — `ILogisticsStockOperationService` exists specifically as a Logistics→Catalog module boundary (`LogisticsStockOperationAdapter` lives in `Catalog.Infrastructure`, implements a `Logistics.Contracts` interface, "Cross-module contract" pattern already used elsewhere in this codebase per `LogisticsModule.cs` comments). Reaching into `IStockUpOperationRepository` from the Logistics handler would violate that boundary for no benefit. (b) duplicates the FR-2 dedup-check logic across two public methods (or forces a private-helper refactor) for a distinction that is really just "save now vs. save later." (a) is chosen specifically **with `persistImmediately` placed after `ct`** — not because it's the prettier convention, but because it means every existing call site in `GiftPackageManufactureService` (4 call sites across `CreateManufactureAsync` and `DisassembleGiftPackageAsync`) needs **zero code changes**: they already pass `ct` positionally as the last argument and simply never mention the new trailing optional parameter, so they keep getting `persistImmediately: true` (today's behavior) automatically. Only `ChangeTransportBoxStateHandler.HandleReceived`'s single call site is touched, adding one named argument.

#### Decision 3: Idempotency check placement (FR-2)

**Options considered:**
- (a) Check-then-act inside `StockUpProcessingService.CreateOperationAsync` itself (applies to both callers).
- (b) Check-then-act only in `ChangeTransportBoxStateHandler.HandleReceived`, leaving the shared service untouched.

**Chosen approach:** (a).

**Rationale:** Putting the dedup check inside the shared service means both callers of `ILogisticsStockOperationService.CreateOperationAsync` (transport-box Receive and gift-package manufacture/disassembly) get the same defense-in-depth for free, with a single implementation to test. It costs `GiftPackageManufactureService` one extra `SELECT` per call (acceptable — NFR-1 explicitly allows "at most one additional existence-check query per distinct product code"), and does not change its behavior on the non-duplicate path (the overwhelmingly common case, since `GiftPackageManufactureLog.Id` is a fresh auto-increment per call). Note the pre-check queries the database (`Context.Set<StockUpOperation>().FirstOrDefaultAsync(...)`), not the change tracker's locally-staged-but-unsaved entities — this is safe here only because `HandleReceived`'s `GroupBy(i => i.ProductCode)` already guarantees at most one `CreateOperationAsync` call per distinct `DocumentNumber` within a single `HandleReceived` invocation, so there is no same-call duplicate for the pre-check to miss.

## Implementation Guidance

### Directory / Module Structure

No new files, no new directories. Every change is to an existing file:

- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs` — add `persistImmediately` parameter to `CreateOperationAsync`.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs` — implement the FR-2 pre-check and make the `SaveChangesAsync` conditional.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs` — add the same parameter (pass-through).
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs` — thread the parameter through.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — `HandleReceived` passes `persistImmediately: false`.
- Test files that must be updated to compile/pass (see Risks): `ChangeTransportBoxStateHandlerTests.cs`, `StockUpProcessingServiceTests.cs`, `LogisticsStockOperationAdapterTests.cs`, `GiftPackageManufactureServiceTests.cs`.

### Interfaces and Contracts

`IStockUpProcessingService` (Catalog):
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

`ILogisticsStockOperationService` (Logistics contract, implemented by the adapter):
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

`StockUpProcessingService.CreateOperationAsync` body (replacing the current unconditional `AddAsync` + `SaveChangesAsync`):
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

`ChangeTransportBoxStateHandler.HandleReceived`'s call site (only line that changes inside the loop):
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

`Handle`'s existing lines 116-135 (transition, restore, `UpdateAsync`, `SaveChangesAsync`) are **unchanged** — this is the crux of why the fix is minimal: the deferred flush point already exists.

`LogisticsStockOperationAdapter.CreateOperationAsync` gains the pass-through parameter (default `true`, forwarded unchanged into `_stockUpProcessingService.CreateOperationAsync`).

### Data Flow

**First-time Receive (happy path, N distinct products):**
1. `HandleReceived` aggregates items by `ProductCode` → N groups.
2. For each group: one `GetByDocumentNumberAsync` (miss) + one `AddAsync` (staged, no DB round trip beyond the `INSERT` prepared by the tracker — not yet sent).
3. `Handle` runs `transition.ChangeStateAsync`, `_repository.UpdateAsync(box)`, then **one** `_repository.SaveChangesAsync(cancellationToken)` → one `ApplicationDbContext.SaveChangesAsync()` → one implicit transaction containing N `StockUpOperation` inserts + 1 `TransportBox` update (+ its `StateLog` insert) → committed atomically, retried as a unit by `PollyExecutionStrategy` if a transient error occurs (safe, because `SaveChangesAsync`'s implicit transaction is exactly what the resilience layer is designed around).

**Retry after a partial prior failure (box still `InTransit`/`Reserve`/`Quarantine`, some or all `StockUpOperation` rows already committed from the interrupted attempt):**
1. `HandleReceived` re-aggregates the same groups, re-derives the same `DocumentNumber`s.
2. For each group whose `DocumentNumber` already exists: pre-check hits → log + skip, no `AddAsync`, no duplicate row, no unique-constraint exception.
3. For any group not yet created (partial-partial case): pre-check misses → staged normally.
4. `Handle` proceeds to `ChangeStateAsync` + the single `SaveChangesAsync` exactly as in the happy path — the box transitions to `Received` this time, closing out the previously-wedged state. No manual DB intervention needed; this is the concrete fix for the issue's failure scenario.

**Failure between staging and the final `SaveChangesAsync`** (e.g. process crash): nothing was ever sent to the database for this call (`AddAsync` only mutates the in-memory change tracker) — on restart/retry, the pre-check correctly finds nothing and creates fresh. **Failure during the final `SaveChangesAsync` itself** (e.g. the DB rejects the commit): EF Core's implicit transaction rolls back everything in that `SaveChanges` call — neither the `StockUpOperation` rows nor the box update persist — box stays in its pre-transition state, exactly the FR-1 acceptance criterion.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing Moq call-expression setups/verifies for `CreateOperationAsync` (`ChangeTransportBoxStateHandlerTests.cs`, `StockUpProcessingServiceTests.cs` — actually tests `ProcessPendingOperationsAsync` so unaffected there — and `LogisticsStockOperationAdapterTests.cs`) were written against the 6-parameter overload. Because `persistImmediately` gets a compiler-filled default of `true` in any expression that omits it, a `Verify(x => x.CreateOperationAsync(a,b,c,d,e,f))` will now only match calls where `persistImmediately == true` — but production code for the transport-box path calls with `persistImmediately: false`. These verifies will silently stop matching (test failures, not compile errors). | High | Developer must update every `Setup`/`Verify` for `CreateOperationAsync` across `ChangeTransportBoxStateHandlerTests.cs` and `LogisticsStockOperationAdapterTests.cs` to include an explicit `It.IsAny<bool>()` (or the exact literal expected) as the 7th argument. Treat this as a required, not incidental, part of the change — run the full test suite, don't just add new tests. |
| `GiftPackageManufactureServiceTests.cs` may also assert against `CreateOperationAsync`'s old signature. | Medium | Same as above — audit and update this file's mocks; behavior (persistImmediately defaults to `true`) should not need production-code changes in `GiftPackageManufactureService.cs` itself. |
| Placing `persistImmediately` after `ct` breaks the .NET convention of `CancellationToken` being the last parameter. | Low | Deliberate trade-off (see Decision 2) to keep `GiftPackageManufactureService`'s 4 call sites untouched. Document this rationale in a short code comment above the interface method so a future reader doesn't "fix" the ordering and reintroduce a positional-argument break. |
| A genuine concurrent double-submit of the *same* Receive request (e.g. a double-click racing two requests for the same box) could still hit the unique index between the FR-2 pre-check and the deferred `SaveChangesAsync`, since the two requests run in separate `DbContext`/transaction scopes. | Low | Spec explicitly treats this as acceptable residual risk (FR-2 acceptance criteria call the pre-check "a safety net," not a full concurrency guard, and permit — but do not require — also catching the unique-constraint violation on `SaveChangesAsync` as a last-resort no-op). Given this is a manual, human-paced warehouse action (not a hot path), do not add unique-constraint-exception translation for this fix; it would require touching `ITransportBoxRepository.SaveChangesAsync`'s exception surface for a rare race outside this issue's reported failure mode. Revisit only if double-submits are observed in practice. |
| Someone "fixes" this by reaching for `Database.BeginTransactionAsync` in a future refactor without knowing about `PollyExecutionStrategy`. | Low (already mitigated) | Already covered by the existing CI guard `scripts/check-no-managed-tx.sh` — no new mitigation needed, just don't remove or weaken that guard. |

## Specification Amendments

- **FR-1 mechanism is now decided, not left open.** The spec's "Open Questions" section says the choice between explicit transaction and single deferred `SaveChangesAsync` "is left as an implementation detail for the architect, since both satisfy the acceptance criteria equally." They do **not** satisfy it equally: the explicit-transaction option is incompatible with this repo's `PollyExecutionStrategy` and is blocked by `scripts/check-no-managed-tx.sh` in CI. Implementation must use the deferred-single-`SaveChangesAsync` approach (FR-1's second bullet) exclusively.
- **FR-1/API-Interface-Design amendment:** the "overload/parameter to control whether `SaveChangesAsync` is called immediately" option named in the spec's API/Interface Design section is the one to implement, specifically as a `bool persistImmediately = true` parameter appended after `CancellationToken` on both `IStockUpProcessingService.CreateOperationAsync` and `ILogisticsStockOperationService.CreateOperationAsync`, so that `GiftPackageManufactureService`'s call sites require no code changes.
- **FR-2 amendment:** implement the `GetByDocumentNumberAsync` pre-check **inside `StockUpProcessingService.CreateOperationAsync` itself** (not only in `ChangeTransportBoxStateHandler`), so both consumers of the shared service benefit uniformly and there is one tested implementation of the dedup logic.
- **New, spec-adjacent requirement surfaced by this review (test hygiene, not a new functional requirement):** the PR must update the Moq setups/verifies enumerated in the Risks table; otherwise the build's own test suite will fail even though production code is correct. This isn't optional cleanup — without it, "all tests touched by the change must pass" (per project validation rules) cannot be satisfied.

## Prerequisites

- No migration, no schema change, no new package — confirmed: `IX_StockUpOperations_DocumentNumber_Unique` already exists and is untouched; Npgsql/EF Core's implicit `SaveChangesAsync` transaction requires no new configuration.
- No changes needed to `PersistenceModule.cs`, `PollyExecutionStrategy.cs`, or `scripts/check-no-managed-tx.sh` — this fix is designed specifically to stay inside their existing constraints.
- Before merging: run the full backend test suite (`dotnet build` + `dotnet format` + tests) — the Moq signature-mismatch risk above will only surface as test failures, not compile errors, and only in the two/three test files enumerated in Risks.
