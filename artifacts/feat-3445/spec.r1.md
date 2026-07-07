# Specification: CancellationToken Consistency for IBankStatementImportRepository

## Summary
`IBankStatementImportRepository.GetByIdAsync`, `AddAsync`, and `UpdateAsync` are the only three methods on the interface that do not accept a `CancellationToken`, breaking consistency with the other four methods and preventing callers (`ImportBankStatementHandler`, `GetBankStatementByIdHandler`) from propagating MediatR's pipeline cancellation token into these operations. This change adds `CancellationToken cancellationToken = default` to all three methods on the interface and its EF Core implementation, and threads the token through from every call site and its Moq-based test doubles.

## Background
`BankStatementImportRepository` wraps EF Core access to the `BankStatements` table. Four of its seven methods (`GetFilteredAsync`, `GetExistingResultsByTransferIdsAsync`, `GetMaxStatementDateAsync`, `GetByTransferIdAsync`, `GetDailyStatisticsAsync` — five, in fact) already accept and forward a `CancellationToken`. `GetByIdAsync`, `AddAsync`, and `UpdateAsync` do not, even though their EF calls (`FindAsync`, `SaveChangesAsync`) support cancellation natively.

This asymmetry has two concrete consequences today:
- `ImportBankStatementHandler.ProcessStatementAsync` (called in a per-statement loop from `Handle`) has a `cancellationToken` parameter available but cannot pass it into `InsertNewAsync`/`UpsertExistingAsync`, which call `_repository.AddAsync` and `_repository.UpdateAsync` without it. On a long import run against a slow DB or external bank API, a caller-initiated cancellation (e.g. HTTP request abort, shutdown) cannot interrupt an in-flight insert/update.
- `GetBankStatementByIdHandler.Handle` receives a `cancellationToken` from MediatR but cannot forward it into `_repository.GetByIdAsync`.

This is a pure interface-consistency and plumbing fix identified by automated architecture review — no behavioral change to import logic, dedup logic, or error handling is intended.

## Functional Requirements

### FR-1: Add CancellationToken parameter to IBankStatementImportRepository
Add `CancellationToken cancellationToken = default` as the final parameter to:
- `Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default)`
- `Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default)`
- `Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default)`

File: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` (lines 15–16, 25).

The default value of `default` preserves source compatibility for any caller not yet updated to pass a token explicitly.

**Acceptance criteria:**
- The interface signatures for all three methods include `CancellationToken cancellationToken = default` as the last parameter.
- All other existing method signatures on the interface are unchanged.

### FR-2: Propagate CancellationToken through BankStatementImportRepository implementation
Update `BankStatementImportRepository` (`backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`) to match the new interface signatures and pass the token into the underlying EF Core calls:
- `GetByIdAsync`: `await _context.BankStatements.FindAsync(new object[] { id }, cancellationToken)` — note `DbSet<T>.FindAsync` has no single-key + token overload; the array-of-keys overload must be used, per the suggested fix.
- `AddAsync`: `await _context.SaveChangesAsync(cancellationToken)`, keeping the existing `DbUpdateException` → detach-and-rethrow behavior unchanged.
- `UpdateAsync`: same `SaveChangesAsync(cancellationToken)` change, same exception handling preserved.

**Acceptance criteria:**
- `GetByIdAsync` calls `_context.BankStatements.FindAsync(new object[] { id }, cancellationToken)` instead of `FindAsync(id)`.
- `AddAsync` and `UpdateAsync` call `_context.SaveChangesAsync(cancellationToken)` instead of the parameterless overload.
- The existing `try/catch (DbUpdateException)` blocks and entity-detach behavior in `AddAsync`/`UpdateAsync` are otherwise untouched.
- No other method bodies in the file are modified.

### FR-3: Propagate CancellationToken from ImportBankStatementHandler
Update `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` so the `cancellationToken` already flowing through `Handle` → `ProcessStatementAsync` reaches the repository:
- `InsertNewAsync` (currently no `cancellationToken` parameter, lines 193–207): add a `CancellationToken cancellationToken` parameter and pass it to `_repository.AddAsync(import, cancellationToken)`.
- `UpsertExistingAsync` (lines 209–224): pass `cancellationToken` to `_repository.UpdateAsync(existing, cancellationToken)`, and to the nested `InsertNewAsync(...)` fallback call on the "existing == null" branch (line 218).
- `ProcessStatementAsync` (line 186): update the call site `InsertNewAsync(statement, accountSetting, itemCount, resultStatus)` to `InsertNewAsync(statement, accountSetting, itemCount, resultStatus, cancellationToken)`.

**Acceptance criteria:**
- `InsertNewAsync` accepts a `CancellationToken` parameter and forwards it to `AddAsync`.
- `UpsertExistingAsync` forwards its existing `cancellationToken` parameter to `UpdateAsync` and to the `InsertNewAsync` fallback call.
- No change to control flow, retry/isRetry logic, logging, or the "persist exactly once" comment/guarantee at lines 182–186.
- The catch block in `Handle` (lines 145–152) that calls `_stateRepository.UpsertAsync(state, CancellationToken.None)` on failure is unchanged — that intentional use of `CancellationToken.None` (to persist failure state even after the caller's token cancels) is out of scope for this fix.

### FR-4: Propagate CancellationToken from GetBankStatementByIdHandler
Update `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` line 29:
`var entity = await _repository.GetByIdAsync(request.Id);` → `var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);`

**Acceptance criteria:**
- `GetByIdAsync` is called with the handler's `cancellationToken` parameter.
- No other logic in the handler changes (null handling, logging, mapping).

### FR-5: Update test doubles and call sites to match new signatures
The following existing tests call the affected methods and must compile and pass against the new signatures:
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` — Moq `.Setup(r => r.GetByIdAsync(42))`, `.Setup(r => r.GetByIdAsync(99999))`, `.Setup(r => r.GetByIdAsync(It.IsAny<int>()))`, `.Verify(r => r.GetByIdAsync(123), Times.Once)`, `.Setup(r => r.GetByIdAsync(7))...` (lines 42, 63, 78, 85, 100).
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — Moq `.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Never/Once)`, `.Setup(r => r.UpdateAsync(It.IsAny<BankStatementImport>()))`, `.Verify(r => r.UpdateAsync(...), Times.Once)`, `.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>()))` (multiple lines, e.g. 139, 163, 177, 178, 220, 314, 328, 350, 362).
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — this is an integration-style test hitting the real `BankStatementImportRepository` (via EF, likely InMemory/SQLite provider) with direct `await _repository.AddAsync(import)`, `await _repository.GetByIdAsync(savedImport.Id)`, `await _repository.UpdateAsync(existing)` calls throughout (~30+ call sites).

Because the new `cancellationToken` parameter has a `= default` value, **no test changes are strictly required for compilation** — existing calls without an explicit token argument continue to compile and behave identically (token = `CancellationToken.None`). However, Moq `.Verify`/`.Setup` expressions using `It.IsAny<...>()` for other arguments must be checked: Moq matches by parameter count only when every parameter is either a literal or an `It.Is`/`It.IsAny` matcher for that exact overload; adding a parameter with a default value does not change the number of parameters in the compiled method, so **existing single-argument Moq `Setup`/`Verify` calls (e.g. `r.GetByIdAsync(42)`) will no longer match invocations that pass a token positionally** once production code is updated to call `GetByIdAsync(request.Id, cancellationToken)` (FR-4) — Moq setups must specify all parameters of the target overload.

**Acceptance criteria:**
- All `Setup`/`Verify` expressions in `GetBankStatementByIdHandlerTests.cs` for `GetByIdAsync` are updated to include a second argument matcher (`It.IsAny<CancellationToken>()`) since production code (FR-4) now calls the two-argument overload.
- All `Setup`/`Verify` expressions in `ImportBankStatementHandlerTests.cs` for `AddAsync` and `UpdateAsync` are updated to include `It.IsAny<CancellationToken>()` as the second argument, since production code (FR-3) now calls the two-argument overload.
- `BankStatementImportRepositoryTests.cs` continues to compile unchanged (it calls the real implementation positionally without a token, which resolves to `cancellationToken = default`); no behavior change expected since these are direct calls, not Moq setups.
- `dotnet build` and the full `Anela.Heblo.Tests` + `Anela.Heblo.Adapters.*.Tests` suites pass after the change.

## Non-Functional Requirements

### NFR-1: Performance
No performance targets change. This is plumbing only — no new DB round trips, no new allocations beyond a `CancellationToken` struct parameter (negligible). `SaveChangesAsync(cancellationToken)` and `FindAsync(keys, cancellationToken)` have identical performance characteristics to their non-token overloads when the token is not cancelled.

### NFR-2: Security
No security surface change. No new inputs, no new trust boundaries. Cancellation token propagation only affects how quickly an in-flight operation can be abandoned; it does not affect authorization, data exposure, or persistence correctness (partial writes are still governed by the existing `DbUpdateException` handling, which is preserved unchanged).

## Data Model
No data model changes. `BankStatementImport` entity and `BankStatements` table are unaffected.

## API / Interface Design

Before:
```csharp
Task<BankStatementImport?> GetByIdAsync(int id);
Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);
Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement);
```

After:
```csharp
Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
```

This is a binary/source-compatible-by-default change for external callers not yet updated (default parameter), but this spec requires updating all in-repo callers (FR-3, FR-4) to actually pass their token through, otherwise the fix delivers no functional benefit — only interface uniformity.

Call-site changes:
- `GetBankStatementByIdHandler.Handle`: `_repository.GetByIdAsync(request.Id, cancellationToken)`
- `ImportBankStatementHandler.InsertNewAsync(..., CancellationToken cancellationToken)`: `_repository.AddAsync(import, cancellationToken)`
- `ImportBankStatementHandler.UpsertExistingAsync`: `_repository.UpdateAsync(existing, cancellationToken)` and `InsertNewAsync(statement, accountSetting, itemCount, resultStatus, cancellationToken)` on the fallback path

## Dependencies
- Entity Framework Core (`Microsoft.EntityFrameworkCore`) — `DbSet<T>.FindAsync(object[], CancellationToken)` and `DbContext.SaveChangesAsync(CancellationToken)` overloads already exist in EF Core; no package upgrade needed.
- Moq — test updates rely on Moq's standard argument-matcher behavior; no version change needed.
- No changes to `IBankImportStateRepository`, `IBankStatementImportService`, `IBankClientFactory`, or any other collaborator interface.

## Out of Scope
- Changing the parameterless `AddAsync`/`UpdateAsync`/`GetByIdAsync` calls in `BankStatementImportRepositoryTests.cs` to pass an explicit token — they will continue to use the default value, which is acceptable since that test exercises the real EF implementation directly and isn't validating cancellation propagation.
- Introducing actual mid-loop cancellation checks (e.g. `cancellationToken.ThrowIfCancellationRequested()`) beyond what `FindAsync`/`SaveChangesAsync` already do internally — this fix relies on EF Core's own cancellation checks inside those calls, not on new explicit checks in application code.
- Changing `CancellationToken.None` usage in `Handle`'s catch block (line 148) that persists failure state on the state repository — that is a deliberate "always persist failure info even if cancelled" behavior, unrelated to this finding.
- Any change to `IBankImportStateRepository` or its implementation — it already takes tokens on all methods and is not part of this finding.
- Broader repository-interface audit across other modules (e.g., other `IXxxRepository` interfaces in Catalog, Manufacture, Purchase, etc.) — this fix is scoped to `IBankStatementImportRepository` only, per the finding.

## Open Questions
None.

## Status: COMPLETE
