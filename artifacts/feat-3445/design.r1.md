# Design: CancellationToken Consistency for IBankStatementImportRepository

## Component Design

No new components are introduced. This design completes an existing token-propagation path across four already-existing components; each component's responsibility is unchanged, only its method signatures/call sites gain a threaded `CancellationToken`.

### `IBankStatementImportRepository` (Domain contract)
- **Responsibility:** Defines the persistence contract for `BankStatementImport` entities.
- **Change:** `GetByIdAsync`, `AddAsync`, `UpdateAsync` each gain a trailing `CancellationToken cancellationToken = default` parameter, bringing them in line with the interface's other four methods (`GetFilteredAsync`, `GetExistingResultsByTransferIdsAsync`, `GetMaxStatementDateAsync`, `GetByTransferIdAsync`, `GetDailyStatisticsAsync`).
- **Contract stability:** Default value preserves source compatibility; no method removed, renamed, or reordered.

### `BankStatementImportRepository` (EF Core implementation)
- **Responsibility:** Implements the above contract against `DbContext`/`BankStatements` table.
- **Change:**
  - `GetByIdAsync` switches from `_context.BankStatements.FindAsync(id)` to `_context.BankStatements.FindAsync(new object[] { id }, cancellationToken)` — the array-of-keys overload is required because EF Core has no single-key + token overload.
  - `AddAsync` / `UpdateAsync` switch from parameterless `_context.SaveChangesAsync()` to `_context.SaveChangesAsync(cancellationToken)`.
  - Existing `try/catch (DbUpdateException)` detach-and-rethrow behavior in `AddAsync`/`UpdateAsync` is preserved verbatim — cancellation and constraint-violation are orthogonal failure paths.

### `ImportBankStatementHandler` (Application use case)
- **Responsibility:** Orchestrates per-statement import processing within a MediatR `Handle`.
- **Change:** `cancellationToken` (already available in `Handle`/`ProcessStatementAsync`) is threaded into two previously-token-blind private methods:
  - `InsertNewAsync(..., CancellationToken cancellationToken)` — new required parameter (no default, since it's an internal method always called with an explicit token; a default here would silently mask a missed-wiring bug rather than surface it), forwarded to `_repository.AddAsync(import, cancellationToken)`.
  - `UpsertExistingAsync` — already has a `cancellationToken` parameter but wasn't forwarding it; now forwards to `_repository.UpdateAsync(existing, cancellationToken)` and to the fallback `InsertNewAsync(...)` call when `existing == null`.
  - `ProcessStatementAsync`'s call site updated to pass `cancellationToken` into `InsertNewAsync`.
- **Unchanged:** Control flow, retry/`isRetry` logic, logging, "persist exactly once" guarantee, and the intentional `CancellationToken.None` used in the failure-state-persistence catch block (line ~148) — that path deliberately survives cancellation and is explicitly out of scope.

### `GetBankStatementByIdHandler` (Application use case)
- **Responsibility:** Handles `GetBankStatementByIdRequest`, maps repository result to response DTO.
- **Change:** `_repository.GetByIdAsync(request.Id)` → `_repository.GetByIdAsync(request.Id, cancellationToken)`. No change to null-handling, logging, or mapping logic.

### Test doubles (`GetBankStatementByIdHandlerTests.cs`, `ImportBankStatementHandlerTests.cs`)
- **Responsibility:** Moq-based unit tests verifying handler behavior against the repository interface.
- **Change:** All single-argument `Setup`/`Verify` expressions for `GetByIdAsync`, `AddAsync`, `UpdateAsync` must add a second matcher, `It.IsAny<CancellationToken>()`, because Moq matches overloads by full parameter list — once production code calls the two-argument form, a one-argument setup silently stops matching (producing false-negative `Times.Never`/`Times.Once` assertions rather than a compile error).
- **`BankStatementImportRepositoryTests.cs`:** No changes required — it exercises the real EF-backed repository positionally (`AddAsync(import)`, `GetByIdAsync(id)`, `UpdateAsync(existing)`), which resolves to `cancellationToken = default` and remains behaviorally identical.

### Component interaction (token flow)

```
MediatR pipeline (ASP.NET Core request-abort token)
        │
        ├─► GetBankStatementByIdHandler.Handle(request, ct)
        │         └─► repository.GetByIdAsync(id, ct) ─► _context.BankStatements.FindAsync(new object[]{id}, ct)
        │
        └─► ImportBankStatementHandler.Handle(request, ct)
                  └─► ProcessStatementAsync(..., ct)
                            ├─ first-seen  → InsertNewAsync(..., ct) ─► repository.AddAsync(import, ct)
                            └─ retry       → UpsertExistingAsync(..., ct)
                                                 ├─ existing found → repository.UpdateAsync(existing, ct)
                                                 └─ existing null  → InsertNewAsync(..., ct) [fallback] ─► repository.AddAsync(import, ct)
                                                                                                   │
                                                                                                   ▼
                                                                            _context.SaveChangesAsync(ct)
                                                                            [try/catch DbUpdateException → detach, unchanged]
```

## Data Schemas

No data model, database schema, or wire-format changes.

- **Entity/table:** `BankStatementImport` / `BankStatements` — unchanged.
- **API request/response DTOs:** Unaffected; `GetBankStatementByIdRequest`/response and `ImportBankStatementRequest`/response shapes are unchanged, since `CancellationToken` is a pipeline/runtime concern (supplied by MediatR/ASP.NET Core), never a serialized field.
- **Interface contract shapes (source-level, not wire-level):**

```csharp
// IBankStatementImportRepository.cs — final shape
Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
```

- **Internal method signatures (ImportBankStatementHandler, private):**

```csharp
private async Task InsertNewAsync(
    BankStatementImportDto statement,
    AccountSetting accountSetting,
    int itemCount,
    ImportResultStatus resultStatus,
    CancellationToken cancellationToken);

private async Task UpsertExistingAsync(
    /* existing parameters unchanged */,
    CancellationToken cancellationToken);
```

No new events, messages, or persisted payloads are introduced by this change.
