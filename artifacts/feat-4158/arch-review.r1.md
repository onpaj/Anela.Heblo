# Architecture Review: Bank staleness warning logged twice per scheduled job run

## Skip Design: true

This is a backend-only logging/duplication fix inside MediatR handler and background-job code. No UI, no new endpoints, no request/response contract changes. The designer phase adds no value here.

## Architectural Fit Assessment

The codebase's Bank module follows a clear layering: `BankImportJobBase` (Infrastructure/Jobs, an `IRecurringJob`) is the *orchestration* layer for scheduled runs — it owns watermark-gap policy decisions (stale-warning threshold, max-backfill clamping) and computes the date range before delegating execution via MediatR. `ImportBankStatementHandler` (UseCases/ImportBankStatement) is the *use-case* layer — it executes an import for a given, already-decided date range and is also reachable directly from `BankStatementsController` for manual/ad hoc imports.

The duplication exists because the handler independently re-derives the same staleness judgment the job already made, using the same `BankImportWatermarkOptions.StaleWarningDays` threshold against the same `BankImportState.LastValidImportDate`. This is a layering violation: the handler is reaching into orchestration-level policy (is this run "on schedule" or "overdue") that isn't its concern — its contract is "import statements for `[DateFrom, DateTo]`," which it does correctly regardless of whether the caller is a job or a human. Removing the check from the handler is the correct direction and aligns cleanly with the existing separation already established by `BankImportJobBase`.

I confirmed by reading the code that:
- `_watermarkOptions` (`BankImportWatermarkOptions`) in `ImportBankStatementHandler` is used **only** inside the block being removed (`Grep` of the field found exactly 3 references: field declaration, constructor assignment, and the one usage in the doomed `if` block). No other logic in the handler depends on it.
- `BankStatementsController.ImportStatements` (`POST /api/bank-statements/import`) is a real, separate manual-trigger call path that goes straight to `_mediator.Send(new ImportBankStatementRequest(...))`, bypassing `BankImportJobBase` entirely. Removing the handler's check silently removes staleness-warning logging for this path.
- `GetBankStatementListRequestValidator` (the only `IValidator<T>` wired into a `ValidationBehavior` pipeline anywhere in `BankModule.cs`) validates `GetBankStatementListRequest` — an unrelated *read/query* request. There is no `IValidator<ImportBankStatementRequest>` and no `ValidationBehavior` pipeline registered for `ImportBankStatementRequest` in `BankModule.AddBankModule`. The issue's own suggested fallback location (`GetBankStatementListRequestValidator`) does not fit: wrong request type, wrong concern (pagination/filter validation, not staleness).

## Proposed Architecture

### Component Overview

```
Before:
  BankImportJobBase.ExecuteAsync
    └─ ResolveDateFromAsync ──► [LogWarning if stale] ──► dateFrom
    └─ mediator.Send(ImportBankStatementRequest) ──► ImportBankStatementHandler.Handle
                                                          └─ [LogWarning if stale] (DUPLICATE)
                                                          └─ execute import

  BankStatementsController.ImportStatements
    └─ mediator.Send(ImportBankStatementRequest) ──► ImportBankStatementHandler.Handle
                                                          └─ [LogWarning if stale] (only source today)
                                                          └─ execute import

After:
  BankImportJobBase.ExecuteAsync
    └─ ResolveDateFromAsync ──► [LogWarning if stale] ──► dateFrom   (sole source, job path)
    └─ mediator.Send(ImportBankStatementRequest) ──► ImportBankStatementHandler.Handle
                                                          └─ execute import (no staleness check)

  BankStatementsController.ImportStatements
    └─ mediator.Send(ImportBankStatementRequest) ──► ImportBankStatementHandler.Handle
                                                          └─ execute import (no staleness check — gap accepted, see Decision 2)
```

### Key Design Decisions

#### Decision 1: Remove the duplicate check from the handler, not from the job
**Options considered:**
- (A) Remove from `ImportBankStatementHandler` (handler stops evaluating staleness).
- (B) Remove from `BankImportJobBase.ResolveDateFromAsync` (job stops evaluating staleness).

**Chosen approach:** (A) — remove the handler's check, keep the job's.

**Rationale:** `BankImportJobBase` is the only place that computes and owns the "is this watermark stale for a scheduled run" decision — it's an orchestration/scheduling concern. The handler has no legitimate reason to re-derive it; its job is to execute a given date range. This also matches the issue's own "Why it matters" framing ("the handler has no business knowing whether the watermark is stale — that decision belongs to the orchestration layer"). Removing from the job side would eliminate the *only* meaningful staleness signal for the primary (scheduled) path, which is exactly the signal the issue wants preserved, just once.

#### Decision 2: Do not add a replacement staleness check for the manual-trigger (`BankStatementsController`) path — accept the gap (spec Open Question resolved as option (b))
**Options considered:**
- (a) Add a new staleness check somewhere on the manual-trigger path (controller, a new validator, or a new pipeline behavior for `ImportBankStatementRequest`).
- (b) Accept that the manual-trigger path no longer logs a staleness warning.

**Chosen approach:** (b).

**Rationale:**
1. There is no existing extension point that fits. `GetBankStatementListRequestValidator` — the location the issue floats as a fallback — validates an unrelated request type and is wired to a different pipeline; using it (or adding a new one) would mean introducing a *new* `IValidator<ImportBankStatementRequest>` + `ValidationBehavior<,>` registration in `BankModule.cs` purely to carry a log statement, which is disproportionate infrastructure for a one-line warning and reintroduces the exact anti-pattern this fix removes (business/orchestration logic — "is this data stale" — leaking into request validation, which should validate *shape*, not *domain staleness*).
2. The manual-trigger path is operator-initiated: a human calls `POST /api/bank-statements/import` directly (there is no scheduled/unattended caller of this endpoint in the codebase). The operator already knows the date range they asked for and can see the result (`BankStatementImportResultDto`) synchronously; they are not depending on a background log line to notice staleness the way an unattended job's monitoring would.
3. This keeps the fix minimal and strictly scoped to the reported duplication, per the issue's actual "Suggested fix" (which describes (a) as optional — "If the manual-API trigger path also needs the warning" — not required).

If a future need arises for staleness visibility on manual triggers, it should be a deliberate, separately-scoped feature (e.g., surfacing `LastValidImportDate` / days-behind directly in `BankStatementImportResultDto` for the caller to see, not a duplicate log line) — not a copy of the job's check.

## Implementation Guidance

### Directory / Module Structure
No new files, directories, or modules. All changes are in-place edits:
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

### Interfaces and Contracts
`ImportBankStatementHandler`'s constructor signature changes: the `IOptions<BankImportWatermarkOptions> watermarkOptions` parameter and the `_watermarkOptions` field are removed (per spec FR-2 — confirmed safe, no other use in the class). This is an internal, DI-resolved constructor, not a public API contract, so:
- `BankModule.AddBankModule` requires **no change** — `ImportBankStatementHandler` is resolved by MediatR's handler registration (reflection-based), not an explicit `services.AddScoped<ImportBankStatementHandler>(...)` call; removing a constructor parameter does not require touching DI registration here (verify no explicit registration exists — none was found in `BankModule.cs`).
- `BankImportWatermarkOptions` itself, and its registration (`services.Configure<BankImportWatermarkOptions>(...)`) in `BankModule.cs`, are **unchanged** — `BankImportJobBase` still consumes it.
- Test constructor call sites (`ImportBankStatementHandlerTests.cs`, two `new ImportBankStatementHandler(...)` call sites at lines ~65 and ~79) must drop the `Options.Create(new BankImportWatermarkOptions())` argument to match the new signature.

`ImportBankStatementRequest`, `BankStatementImportResultDto`, and all other public/DTO shapes are unchanged — no OpenAPI client regeneration needed.

### Data Flow
Unchanged except for the removal of one log statement and one now-dead field read. `state` (the `BankImportState`) is still loaded in `Handle` — it remains needed later for `state.RecordSuccess(...)` / `state.RecordFailure(...)` / `_stateRepository.UpsertAsync(state, ...)`. Only the `if (state.LastValidImportDate.HasValue) { ...LogWarning... }` block is deleted; the rest of `state`'s lifecycle in `Handle` is untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Manual-trigger path silently loses staleness-warning visibility | Low | Accepted per Decision 2 — operator sees the import result synchronously; not a monitored/unattended path. Document the behavior change in the PR description so it's a visible, intentional trade-off, not a silent regression. |
| Existing test `Handle_LogsStaleWarning_WhenWatermarkIsStale` (and its sibling not-stale case) asserts behavior being removed | Certain (not a risk — a required change) | Planner/developer must update `ImportBankStatementHandlerTests.cs`: remove (or repurpose to assert *absence* of the log) the stale-warning assertions, and update both `new ImportBankStatementHandler(...)` call sites to drop the removed constructor argument. |
| Missed a second/other caller of `ImportBankStatementHandler` that relied on the log for monitoring | Low | `Grep` for `ImportBankStatementRequest(` across `backend/` found exactly two production call sites (`BankImportJobBase`, `BankStatementsController`) plus test files — both are accounted for above. |

## Specification Amendments

None required — the spec's FR-1 (remove duplicate check) and FR-2 (remove unused dependency, conditional on no other use — confirmed true) stand as written. The spec's Open Question is resolved here: **option (b)** (accept the gap on the manual-trigger path) is the architecturally sound choice — see Decision 2. FR-3 as scoped in the spec (the "preferred (a) vs accept (b)" branch) should be treated by the planner as **closed, resolved to (b)** — do not implement a new validator/check for the manual-trigger path as part of this fix.

## Prerequisites

None. No migrations, no config changes, no new infrastructure. This is a same-PR, no-prerequisite code change.
