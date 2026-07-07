# Architecture Review: CancellationToken Consistency for IBankStatementImportRepository

## Skip Design: true

## Architectural Fit Assessment

This is a pure plumbing/consistency fix with no architectural novelty — it brings `IBankStatementImportRepository` in line with the dominant convention already used across the codebase. A sample of other repository interfaces (`IPackageRepository`, `IPackingMaterialRepository`, `IMeetingTranscriptRepository`, `IFeatureFlagOverrideRepository`, `IJournalRepository`, `IMarketingActionRepository`, `IRecurringJobConfigurationRepository`, `IStockUpOperationRepository`, `IEshopStockClient`, `ICostCache`) shows every async method taking `CancellationToken cancellationToken = default` as its final parameter. `IBankStatementImportRepository` itself already does this on 4 of its 7 methods (`GetFilteredAsync`, `GetExistingResultsByTransferIdsAsync`, `GetMaxStatementDateAsync`, `GetByTransferIdAsync`, `GetDailyStatisticsAsync`). `GetByIdAsync`, `AddAsync`, `UpdateAsync` are the outliers. There is no ambiguity here: the fix is not introducing a new pattern, it's completing an existing one.

Verified against the actual files:
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — confirmed `GetByIdAsync` (line 15), `AddAsync` (line 16), `UpdateAsync` (line 25) lack the token; all other methods have it.
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — `GetByIdAsync` calls `_context.BankStatements.FindAsync(id)` (single-key overload, no CT support); `AddAsync`/`UpdateAsync` call parameterless `_context.SaveChangesAsync()` inside `try/catch (DbUpdateException)` blocks that detach the entry on failure — this exception handling must be preserved untouched.
- `ImportBankStatementHandler.cs` — `ProcessStatementAsync` (has `cancellationToken`) calls `InsertNewAsync` (no CT param) and `UpsertExistingAsync` (has CT param but doesn't forward it to `UpdateAsync`/nested `InsertNewAsync`). Confirmed line-level correspondence with spec FR-3.
- `GetBankStatementByIdHandler.cs` line 29 — `_repository.GetByIdAsync(request.Id)` drops the handler's `cancellationToken`. Confirmed matches FR-4 exactly.
- Test files confirmed: `GetBankStatementByIdHandlerTests.cs` has 5 single-arg Moq `Setup`/`Verify` calls on `GetByIdAsync` (lines 42, 63, 78, 85, 100); `ImportBankStatementHandlerTests.cs` has 9 single-arg Moq calls on `AddAsync`/`UpdateAsync`; `BankStatementImportRepositoryTests.cs` calls the real repository ~30+ times with no token argument (integration-style, EF-backed).

No new components, no new dependencies, no schema change, no UI. This is an internal signature/plumbing change confined to one module.

## Proposed Architecture

### Component Overview

```
MediatR pipeline (CancellationToken flows in from ASP.NET Core request abort / IRequestHandler)
        │
        ▼
GetBankStatementByIdHandler.Handle(request, ct) ──────► repository.GetByIdAsync(id, ct)
        │
ImportBankStatementHandler.Handle(request, ct)
        │
        ▼
   ProcessStatementAsync(..., ct)
        │
        ├─ isRetry=false → InsertNewAsync(..., ct) ──────► repository.AddAsync(import, ct)
        │
        └─ isRetry=true  → UpsertExistingAsync(..., ct)
                                │
                                ├─ existing found    → repository.UpdateAsync(existing, ct)
                                └─ existing == null  → InsertNewAsync(..., ct)  [fallback]
                                                              │
                                                              ▼
                                                    repository.AddAsync(import, ct)
        │
        ▼
BankStatementImportRepository (EF Core)
   GetByIdAsync  → _context.BankStatements.FindAsync(new object[]{id}, ct)
   AddAsync      → _context.SaveChangesAsync(ct)   [try/catch DbUpdateException → detach, unchanged]
   UpdateAsync   → _context.SaveChangesAsync(ct)   [try/catch DbUpdateException → detach, unchanged]
```

No new boxes are added to this diagram — it's the existing call graph with a token wire added end-to-end.

### Key Design Decisions

#### Decision 1: `FindAsync` overload selection
**Options considered:**
- Keep `FindAsync(id)` and check `cancellationToken.ThrowIfCancellationRequested()` manually before/after the call.
- Switch to `FindAsync(new object[] { id }, cancellationToken)` — the array-of-keys + token overload that EF Core actually exposes (there is no `FindAsync(TKey, CancellationToken)` single-key overload).

**Chosen approach:** Use `FindAsync(new object[] { id }, cancellationToken)`, exactly as specified in FR-2.

**Rationale:** This is the only EF Core API that both preserves `FindAsync`'s local-tracked-entity short-circuit behavior (avoids a DB round trip if the entity is already tracked) and honors cancellation. Manual `ThrowIfCancellationRequested()` calls would be redundant scope creep beyond what the spec/finding asks for (see NFR/Out-of-Scope: "no new explicit checks in application code").

#### Decision 2: Default parameter value vs. required parameter
**Options considered:**
- Make `cancellationToken` a required parameter (no default), forcing every call site to be touched, including `BankStatementImportRepositoryTests.cs`.
- Add `cancellationToken = default` as specified.

**Chosen approach:** `CancellationToken cancellationToken = default`, matching the pattern already used on the interface's other four methods.

**Rationale:** Consistency with existing sibling methods on the same interface (all use `= default`). It also avoids a mechanical, low-value edit of ~30+ call sites in `BankStatementImportRepositoryTests.cs` that don't test cancellation behavior — those calls will keep compiling unchanged and resolve to `CancellationToken.None`, which is semantically identical to today.

#### Decision 3: Scope of the fix — interface only, not a broader repository audit
**Options considered:**
- Fix `IBankStatementImportRepository` only (as scoped in the brief/spec).
- Take this as a trigger to audit all `IXxxRepository` interfaces across Catalog, Manufacture, Purchase, etc. for the same asymmetry.

**Chosen approach:** Scope strictly to `IBankStatementImportRepository`, per spec's explicit "Out of Scope" section.

**Rationale:** Broader audit is legitimate future work but is an unrelated, larger-blast-radius change that shouldn't ride on this ticket. Flagging it as a follow-up backlog item is more appropriate than silently expanding scope here (see Specification Amendments below).

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Only these existing files change:
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` (interface)
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` (EF implementation)
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` (`InsertNewAsync`, `UpsertExistingAsync`, `ProcessStatementAsync` call site)
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` (line 29 call site)
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` (Moq matcher updates)
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` (Moq matcher updates)
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — verify it still compiles; no edits required per spec, but the reviewer implementing this should confirm no positional-argument ambiguity is introduced (there shouldn't be, since `AddAsync(import)`/`GetByIdAsync(id)`/`UpdateAsync(existing)` remain single-candidate overloads).

### Interfaces and Contracts

```csharp
// IBankStatementImportRepository.cs — final shape
Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
```

`InsertNewAsync` and `UpsertExistingAsync` (private methods on `ImportBankStatementHandler`) both need a `CancellationToken cancellationToken` parameter added (no default — these are internal, always called with an explicit token from `ProcessStatementAsync`/`Handle`, so a default value here would mask a missed wiring bug rather than prevent one).

### Data Flow

For `GetBankStatementByIdRequest`: MediatR's `Handle(request, cancellationToken)` → `_repository.GetByIdAsync(request.Id, cancellationToken)` → `_context.BankStatements.FindAsync(new object[]{id}, cancellationToken)`. If the HTTP client aborts the request mid-flight, EF Core's `FindAsync` will observe the token and throw `OperationCanceledException`, which MediatR/ASP.NET Core's pipeline already handles (this is existing, unmodified behavior for the other four methods — no new exception-handling path is being introduced).

For `ImportBankStatementRequest`: `Handle`'s per-statement loop passes `cancellationToken` into `ProcessStatementAsync`, which now threads it through both branches (`InsertNewAsync` on first-seen statements, `UpsertExistingAsync` → `UpdateAsync` or fallback `InsertNewAsync` on retries) down to `SaveChangesAsync(cancellationToken)`. The existing `DbUpdateException` → detach-and-rethrow logic is unaffected — cancellation and DB-constraint failures are orthogonal failure modes and both paths still terminate the same way (exception propagates to `Handle`'s catch, which persists failure state using `CancellationToken.None` — intentionally unchanged, per spec).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Moq `Setup`/`Verify` calls with a single argument silently stop matching once production code calls the two-arg overload, causing tests to report `Times.Never` for calls that actually happened (false negative on the very telemetry these tests exist to verify) | Medium | FR-5 requires updating all such Moq expressions to include `It.IsAny<CancellationToken>()` as a second matcher; this must be done as part of the same change, verified by running the full Bank test suite, not left as a follow-up |
| `FindAsync(new object[] { id }, cancellationToken)` behaves subtly differently from `FindAsync(id)` in edge cases (e.g., composite keys, nullability of `id`) | Low | `BankStatementImport`'s PK is a single `int Id`; the array-of-keys overload with one element is functionally identical to the single-key overload for this entity. No composite key exists on this entity today |
| Scope creep — someone "while in the file" fixes other repositories too | Low | Spec explicitly marks broader audit out of scope; call this out in PR description so reviewers don't expand the diff |
| `BankStatementImportRepositoryTests.cs` (~30+ call sites) silently continues to not exercise cancellation, giving false confidence that CT propagation is tested end-to-end | Low | Accepted per spec's Out-of-Scope section — these are integration tests against real EF behavior, not unit tests of cancellation; flagging as accepted risk, not something to fix in this ticket |

## Specification Amendments

None required — the spec is implementation-ready and matches the codebase exactly at every cited line number and pattern. One non-blocking observation to record for the backlog (not this ticket): the same `GetByIdAsync`/`AddAsync`/`UpdateAsync`-without-token asymmetry likely exists on other `IXxxRepository` interfaces outside Bank (spec explicitly calls this out as out of scope). Recommend filing a separate "repository CancellationToken audit" backlog item rather than folding it into this fix.

## Prerequisites

None. No migrations, no config, no infrastructure changes. This can be implemented and merged independently of any other in-flight work in the Bank module. The only sequencing consideration is trivial: interface change (FR-1) must land before or atomically with the implementation (FR-2) and call-site changes (FR-3/FR-4), since C# won't compile a mismatched interface/implementation — in practice this is a single commit/PR, not a multi-step rollout.
