# Architecture Review: Remove dead `result.Failed` write in MarketingInvoiceImportService catch block

## Skip Design: true
No UI, no API contract, no data model change — a one-line dead-code deletion inside a private control-flow branch of an existing internal application service. Nothing here is user-facing or cross-module.

## Architectural Fit Assessment
`MarketingInvoiceImportService` lives in `Features/MarketingInvoices/Services/` and implements the module-owned `IMarketingInvoiceImportService` contract (per `docs/architecture/development_guidelines.md`, module-internal services are not required to sit behind `contracts/` unless consumed by another module — this one is consumed only within the MarketingInvoices vertical slice, confirmed by `grep` showing its only non-test references are within the same feature folder and its DI registration). The proposed change is entirely internal to the method body: it does not change the method signature, the `IMarketingInvoiceImportService` contract, `MarketingImportResult`, or any caller. It fits cleanly — there is no module boundary, DTO, or persistence concern to evaluate.

## Proposed Architecture

### Component Overview
No structural change. Single existing component:

```
MarketingInvoiceImportService.ImportAsync(source, from, to, ct)
  └─ per-transaction loop (try/catch, result.Failed++/Skipped++/Imported via stagedCount) — UNCHANGED
  └─ post-loop flush: await _repository.SaveChangesAsync(ct)
        try   -> result.Imported = stagedCount                         — UNCHANGED
        catch -> log error; [DELETE: result.Failed += stagedCount; DELETE: comment]; throw;
```

### Key Design Decisions

#### Decision 1: Delete the dead write rather than make it "work"
**Options considered:**
1. Delete `result.Failed += stagedCount;` (and its orphaned comment) as the brief proposes.
2. Make the field actually observable — e.g. catch the exception, populate `result`, and return it instead of rethrowing, so `Failed` reflects the flush failure.
3. Leave it as-is.

**Chosen approach:** Option 1 — delete the dead line and comment only.

**Rationale:** The brief and the arch-review finding scope this as removal of dead code (YAGNI), not a behavior change. Option 2 (swallowing the exception and returning a result instead of rethrowing) would be a real behavior change to error handling/observability for a saved-changes failure — it changes what callers see on persistence failure (return value vs. exception) and is out of scope for a "remove dead code" fix; it would need its own spec, caller-impact analysis, and test changes, and no such requirement is present in the brief. Option 3 leaves the misleading dead code and the (contrived but real) `OverflowException`-shadowing risk in place. Option 1 is the minimal, behavior-preserving fix that matches the finding exactly.

## Implementation Guidance

### Directory / Module Structure
No new files. Single-file edit:
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

### Interfaces and Contracts
None affected. `IMarketingInvoiceImportService.ImportAsync` signature, `MarketingImportResult` shape, and all other contracts are untouched.

### Data Flow
Unchanged. On post-loop `SaveChangesAsync` failure: log the error (same message/args as today) → propagate the exception via `throw;` (unchanged) → caller (`ImportMarketingInvoicesHandler` or equivalent) sees the exception, not a `MarketingImportResult` — exactly as today, minus the dead intermediate mutation.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test asserts on `result.Failed` for this path and breaks | Low | Verified: `ImportAsync_FinalSaveChangesThrows_Rethrows` only asserts the exception type and that `SaveChangesAsync` was called once — it does not read `result.Failed` (it cannot, since `result` is never returned on this path). No test change needed. |
| Deleting the comment loses useful context | Low | The comment only explained why `result.Failed` was being set on a dead path; once that line is gone the comment has nothing left to explain. `_logger.LogError` retains the diagnostic (count, platform) at time of failure. |

## Specification Amendments
None — `spec.r1.md` already scopes this correctly (FR-1/FR-2). No additions needed.

## Prerequisites
None. No migrations, config, or infrastructure required.
