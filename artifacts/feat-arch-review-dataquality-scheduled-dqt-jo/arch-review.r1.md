# Architecture Review: Persist scheduled DQT run records before execution

## Skip Design: true

This is a backend-only reliability fix in three `IRecurringJob` implementations. No UI, no API, no schema, no visual surface area.

## Architectural Fit Assessment

The proposal aligns cleanly with the existing architecture:

- **Vertical-slice layout** — all three target files live in `Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/` and only the bodies of `ExecuteAsync` are touched. No module boundary is crossed.
- **Repository pattern** — `IDqtRunRepository : IRepository<DqtRun, Guid>` (`backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs:5`) already exposes `AddAsync` and `SaveChangesAsync` via `BaseRepository` (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:57,97`). No new method is required.
- **Trigger parity** — the manual path (`RunDqtHandler.cs:40-41`) already performs `AddAsync` → `SaveChangesAsync` before handing off to the runner. The scheduled jobs presently diverge from this by omitting the save, and the fix brings them back into line.
- **Runner contract preserved** — `InvoiceDqtJobRunner.RunAsync` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs:22`) already opens with `GetByIdAsync(dqtRunId)` and ends with `SaveChangesAsync` in a `finally`. Pre-saving the run row in the caller does not change what the runner can or must do; the runner remains the sole owner of result rows and status transitions.

The fix is the smallest, most local change that achieves trigger-path parity, and does so without disturbing the runner's `finally`-block contract.

## Proposed Architecture

### Component Overview

```
Hangfire scheduler
       │
       ▼
┌──────────────────────┐    1. IsJobEnabledAsync
│ <Xxx>DqtJob          │───────────────────► IRecurringJobStatusChecker
│ : IRecurringJob      │
│                      │    2. DqtRun.Start(...)
│                      │
│                      │    3. AddAsync(run)
│                      │───────────────────► IDqtRunRepository
│                      │                     (EF Core, scoped DbContext)
│                      │    4. SaveChangesAsync    ◄── NEW
│                      │───────────────────►
│                      │
│                      │    5. RunAsync(run.Id)
│                      │───────────────────► I<Invoice|Drift>DqtJobRunner
└──────────────────────┘                            │
                                                    │ GetByIdAsync(run.Id)
                                                    │ try { Compare → AddResults → Complete }
                                                    │ catch { Fail(ex.Message) }
                                                    │ finally { SaveChangesAsync }
                                                    ▼
                                             IDqtRunRepository
```

The only change is the insertion of step 4. Steps 1–3 and 5 are unchanged.

### Key Design Decisions

#### Decision 1: Save in the caller, not the runner

**Options considered:**
- (A) Add `SaveChangesAsync` after `AddAsync` in each of the three `<Xxx>DqtJob.ExecuteAsync` methods.
- (B) Refactor `RunAsync` on each job runner to perform `AddAsync` + initial `SaveChangesAsync` internally so callers only need to provide a `DqtRun` (or build it inside the runner).
- (C) Introduce an outbox / transactional log so the run row is captured even if the DB save itself crashes.

**Chosen approach:** (A).

**Rationale:**
- (B) would centralise persistence in the runner — a strictly better long-term shape — but the spec (`Out of Scope` section) explicitly excludes it as "a larger change requiring its own design." The runner currently expects the row to already exist (`GetByIdAsync` at `InvoiceDqtJobRunner.cs:24`), and reworking it would touch both manual and scheduled paths, the test suite, and the contract of `RunAsync`. Out of scope.
- (C) is an outbox pattern; explicitly out of scope per the spec and unjustified for three nightly jobs.
- (A) matches the existing manual path verbatim (`RunDqtHandler.cs:40-41`), is one line per file, and reuses the existing repository surface.

#### Decision 2: Reuse the in-scope `cancellationToken`

**Options considered:**
- (A) Pass the existing `cancellationToken` parameter to `SaveChangesAsync`.
- (B) Use `CancellationToken.None` so a host-shutdown signal does not leave a half-persisted row.

**Chosen approach:** (A).

**Rationale:**
- Matches the surrounding `AddAsync(run, cancellationToken)` and `_jobRunner.RunAsync(run.Id, cancellationToken)` calls (consistency, and explicit FR-5 requirement).
- A `SaveChangesAsync` cancelled at the save boundary either commits or doesn't — EF Core does not produce partial writes for a single-statement save of one new row.
- If the host token cancels mid-save, the job is being shut down anyway; the absence of a `Running` row is the correct outcome.

#### Decision 3: Test at the job level, not via the runner

**Options considered:**
- (A) Add unit tests on each `<Xxx>DqtJob` that mock `IDqtRunRepository` and assert the call order `AddAsync` → `SaveChangesAsync` → `RunAsync`.
- (B) Add an integration test against `ApplicationDbContext` (in-memory or real) that asserts a `Running` row exists when the runner throws.

**Chosen approach:** (A) primarily; (B) optional.

**Rationale:**
- There are currently **no test files for `InvoiceDqtJob`, `ProductPairingDqtJob`, or `StockWriteBackDqtJob`** (only the runners are covered — see `backend/test/Anela.Heblo.Tests/Features/DataQuality/`). This fix should ship with the missing unit tests; otherwise the regression cannot be guarded.
- The behaviour under test is a sequencing requirement — perfectly expressed via `MockSequence` or `Verify` with `Callback` recording invocation order on a mock `IDqtRunRepository`. Heavier machinery is unwarranted.
- An optional integration test (`InMemoryDatabase`, like `SmartsuppWebhookAuditCleanupJobTests` at `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditCleanupJobTests.cs:14`) can give FR-4 the strongest possible signal, but only if cheap to add.

## Implementation Guidance

### Directory / Module Structure

No new files in `src/`. Modifications limited to:

```
backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/
├── InvoiceDqtJob.cs            (modify — add line 50)
├── ProductPairingDqtJob.cs     (modify — add line 50)
└── StockWriteBackDqtJob.cs     (modify — add line 50)
```

New test files (one per job, mirroring existing `<Service>Tests.cs` naming):

```
backend/test/Anela.Heblo.Tests/Features/DataQuality/
├── InvoiceDqtJobTests.cs            (new)
├── ProductPairingDqtJobTests.cs     (new)
└── StockWriteBackDqtJobTests.cs     (new)
```

### Interfaces and Contracts

No interface or contract changes. The fix uses methods already exposed on `IDqtRunRepository`:

- `Task<DqtRun> AddAsync(DqtRun, CancellationToken)` (inherited from `IRepository<DqtRun, Guid>`)
- `Task<int> SaveChangesAsync(CancellationToken)` (inherited)

Sequence each `ExecuteAsync` must follow after the fix:

```csharp
var run = DqtRun.Start(<testType>, <from>, <to>, DqtTriggerType.Scheduled);
await _repository.AddAsync(run, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken); // <-- inserted
await _jobRunner.RunAsync(run.Id, cancellationToken);
```

### Data Flow

**Happy path:** unchanged outcome — `DqtRun` ends in `Succeeded`/`Failed` with results and counters. Only difference is one extra committed transaction at the start (the `Running` row) and one less new row at the end (the runner's `SaveChangesAsync` now updates instead of inserts the run).

**Crash path (new):** if the process is killed between the new `SaveChangesAsync` and the runner's `finally`, the `Running` row is durable. Operators see it via the existing dashboard tiles (`DqtYesterdayStatusTile`, `DataQualityStatusTile`) and the existing `GetDqtRuns` query.

**Cancellation path:** if the host signals shutdown during the new `SaveChangesAsync`, EF Core throws `OperationCanceledException`. The current `ExecuteAsync` has no `try/catch`, so the exception propagates to Hangfire, which logs it as a job failure and retries per its policy. This matches today's behaviour for any other failure between `AddAsync` and `RunAsync`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| EF change-tracker double-tracks `run`: after the new `SaveChangesAsync`, the runner's `GetByIdAsync` returns the already-tracked entity, and `Complete()`/`Fail()` mutations are saved as updates instead of inserts. | Low | This is the intended outcome and matches the manual path. `FindAsync`/`GetByIdAsync` returns the tracked instance from `BaseRepository.GetByIdAsync` (`BaseRepository.cs:27`) — same object reference, no duplication. Verified by the existing manual path working in production. |
| The runner currently relies on `GetByIdAsync` finding the row. Pre-saving makes this guaranteed; if the save throws, `RunAsync` never executes — correct. | None | Behavioural improvement, not a risk. |
| Test coverage gap: no existing tests for the three job classes, so a future regression (removing the new line) goes undetected. | Medium | Add `<JobName>Tests.cs` files asserting the `AddAsync` → `SaveChangesAsync` → `RunAsync` sequence using `Mock<IDqtRunRepository>` (sequence verification with `MockSequence` or callback-based order capture). |
| Status-check short-circuit (`IsJobEnabledAsync`) means `SaveChangesAsync` should *not* be called when the job is disabled. The fix as proposed is below the early `return`, so this is already correct — but reviewers must keep it that way. | Low | Place the new line strictly between `AddAsync` and `_jobRunner.RunAsync`, never before the `IsJobEnabledAsync` check (which would persist a `Running` row for a disabled job). |
| Hangfire job retries on transient failure may now produce multiple `Running` rows per scheduled day. | Low | Pre-existing behaviour: the manual path can already produce multiple rows per day via retried clicks. Operators already disambiguate via `StartedAt`. No new mitigation required. |
| Documentation in `docs/features/data-quality-dqt.md` describes the persistence flow at a high level; a description of pre-save semantics is absent. | Low | Optional doc note added in the same PR — one sentence: "Scheduled and manual triggers both persist the `Running` row before execution starts, so crashed runs leave an audit trail." |

## Specification Amendments

The spec is sound. Two small additions are recommended:

1. **Test scope clarification (FR-4 / NFR-4).** The spec calls for "at least current unit-test coverage" of the three job classes — but currently **no tests exist** for `InvoiceDqtJob`, `ProductPairingDqtJob`, or `StockWriteBackDqtJob` (only `*JobRunner*` tests). The amendment: this PR introduces those test files. Each should verify the `AddAsync` → `SaveChangesAsync` → `RunAsync` ordering and the disabled-job short-circuit path.

2. **`DqtTestType` enum value for the product-pairing job (FR-2).** The spec uses "`ProductPairing` (or the corresponding `DqtTestType` value used by this job)". Verified against the code: `ProductPairingDqtJob.cs:48` uses `DqtTestType.ProductPairing` and `StockWriteBackDqtJob.cs:48` uses `DqtTestType.StockWriteBackReconciliation`. The spec can be made precise on this point.

No structural amendments needed.

## Prerequisites

None. The change is purely additive code:

- No database migration (no schema changes).
- No new DI registrations — `IDqtRunRepository` is already registered (`PersistenceModule.cs`).
- No new packages.
- No new configuration keys.
- No infrastructure or environment changes.

Implementation can begin immediately.