# Architecture Review: Fix SaveChangesAsync Exception Swallowing in MarketingInvoiceImportService

## Skip Design: true

## Architectural Fit Assessment

This change is a pure bug fix that restores an invariant the codebase already documents. The comment at `ImportMarketingInvoicesHandler` lines 50–52 explicitly states that import-time exceptions must not be caught at the handler level so Hangfire can retry. The both job classes (`MetaAdsInvoiceImportJob`, `GoogleAdsInvoiceImportJob`) already have a correct catch-log-rethrow block. The missing link is the single swallowed `throw;` inside `MarketingInvoiceImportService`.

The call chain is:

```
HangfireJob.ExecuteAsync
  └─ IMediator.Send(ImportMarketingInvoicesRequest)
       └─ ImportMarketingInvoicesHandler.Handle        ← no catch, by design
            └─ IMarketingInvoiceImportService.ImportAsync
                 └─ IImportedMarketingTransactionRepository.SaveChangesAsync  ← exception swallowed here
```

Every layer above the service correctly lets exceptions propagate. Only the batch-flush catch block breaks the chain. The fix is fully contained within that one service; no layer above or below it changes.

The existing test suite (`MarketingInvoiceImportServiceTests`) is well-structured: one test per behaviour, Moq-based, no integration infrastructure. The test that currently asserts the swallowing behaviour (`ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow`) must be corrected in place, not replaced with a parallel test, to avoid leaving a contradicting assertion in the file.

## Proposed Architecture

### Component Overview

No new components. The fix touches exactly two files:

```
MarketingInvoiceImportService          (src — one-line change)
MarketingInvoiceImportServiceTests     (test — rename + rewrite one test, add one test)
```

The propagation path after the fix:

```
SaveChangesAsync throws
  → catch block: LogError, result.Failed += stagedCount, throw;   ← add throw;
  → ImportAsync propagates exception
  → ImportMarketingInvoicesHandler propagates exception (already correct)
  → MetaAdsInvoiceImportJob / GoogleAdsInvoiceImportJob catch block: LogError, rethrow
  → Hangfire records failure and schedules retry
```

The success-path logging line (`LogInformation` at line 111) is skipped because the exception propagates past it. This is the correct behaviour — a completion log must not be emitted when persistence failed.

### Key Design Decisions

#### Decision 1: Placement of `throw;` relative to the existing log and counter

**Options considered:**
- Add `throw;` before `result.Failed += stagedCount` (loses the accurate failed count in any observer that catches further up).
- Add `throw;` after `result.Failed += stagedCount` (preserves the count; the result object is accessible to any future catch handler higher in the stack, even though today nothing inspects it after a rethrow).
- Remove the catch block entirely (discards the `LogError` call, which has operational value for diagnosing transient DB failures in logs before the Hangfire failure record appears).

**Chosen approach:** Add `throw;` as the final statement of the catch block, after `result.Failed += stagedCount`, exactly as the spec prescribes.

**Rationale:** The log and the counter both have value and exist today. Adding `throw;` as the last statement preserves them and is the minimal diff that closes the propagation gap. The spec's out-of-scope statement explicitly excludes the jobs and the handler; no other change is needed.

#### Decision 2: Test strategy for the rethrow contract

**Options considered:**
- Keep the renamed/fixed test as the only test; rely on the type assertion to implicitly cover preservation.
- Add a second dedicated test (`ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved`) that makes the type-preservation contract explicit.

**Chosen approach:** Two tests as specified — the renamed test verifies that the exception propagates at all (`ThrowsAsync`), and the new test makes the type identity explicit.

**Rationale:** `ThrowsAsync<InvalidOperationException>` already asserts both propagation and type. A second test with a different name documents the *intent* of type preservation for future readers. The cost is negligible; the documentation value is real. Both tests assert the `SaveChangesAsync` mock was called `Times.Once`, confirming the flush was attempted.

## Implementation Guidance

### Directory / Module Structure

No new files. No directory changes. Modify exactly:

- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

### Interfaces and Contracts

`IMarketingInvoiceImportService.ImportAsync` contract changes in one respect: previously it was guaranteed not to throw when `SaveChangesAsync` failed. After the fix, it throws whatever exception `SaveChangesAsync` threw. The exception is not wrapped — `throw;` preserves the original stack trace and type. Callers already handle this: the handler does not catch, the jobs catch and rethrow.

No interface signature changes. No new types.

### Data Flow

**Before fix (broken):**
```
SaveChangesAsync throws InvalidOperationException
  → catch: log, result.Failed += stagedCount
  → ImportAsync returns result (Imported=0, Failed=N)
  → handler returns ImportMarketingInvoicesResponse(Failed=N)
  → job logs "completed", Hangfire records success  ← silent data loss
```

**After fix (correct):**
```
SaveChangesAsync throws InvalidOperationException
  → catch: log, result.Failed += stagedCount, throw;
  → ImportAsync propagates InvalidOperationException
  → handler propagates (no catch)
  → job catch block: log error, rethrow
  → Hangfire records failure, schedules retry  ← correct
```

The success-path completion log at `ImportAsync` line 111 is bypassed because the exception propagates before reaching it. The job-level error log from the job's catch block fires instead.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| The completion log at line 111 of `ImportAsync` is not emitted on flush failure, which changes existing log-based alerting or dashboards | Low | No evidence of log-based alerting in scope; the Hangfire failure record is the primary observability mechanism. If log analysis depends on the completion line being emitted, add a `finally` block to always log completion — but this is outside the spec and should not be added speculatively. |
| Future callers of `ImportAsync` do not expect it to throw on flush failure | Low | The contract is now documented by the corrected tests. `IMarketingInvoiceImportService` has exactly two callers in the codebase (via the handler), both of which already handle exceptions correctly. |
| The `stagedCount` variable captures the count of staged-but-unpersisted transactions, and incrementing `result.Failed` by it before rethrowing could mislead a future handler that inspects the result | Very Low | No caller today inspects `result` after a rethrow. The spec preserves this behaviour. If the result object ever becomes observable again, the `Failed` count will be semantically accurate (those transactions were staged but not committed). |

## Specification Amendments

None required. The spec is complete and internally consistent. The implementation is a strict subset of what the spec prescribes.

One clarification for implementors: the `result.Imported` stays 0 after the rethrow, and `result.Skipped` stays at whatever it accumulated during the per-transaction loop. The spec's test assertions (removing `result.Failed == 2`, `result.Imported == 0`, `result.Skipped == 0`) are correct because none of those values matter once the exception is propagating — the result object is unreachable to any caller.

## Prerequisites

None. This is a self-contained source change with no migration, infrastructure, or configuration dependency.

Build validation before merging:
- `dotnet build` must pass.
- `dotnet format` must produce no diff.
- All tests in `MarketingInvoiceImportServiceTests` must pass, including the two changed/added tests.
- The remaining eight tests in the file must continue to pass without modification.
