# Architecture Review: Targeted per-job lookup for `GetJobStartedAt` in Hangfire background worker

## Skip Design: true

## Architectural Fit Assessment
This is a localized correctness/performance fix inside a single private method of `HangfireBackgroundWorker` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`), the sole implementation of `IBackgroundWorker`. It does not touch the public interface, DTOs, controllers, or any cross-module boundary.

Verified against the actual file (not just the spec's description):
- `GetJobById` (lines 126–152) already does two other targeted, O(1) lookups on the same `IStorageConnection`: `connection.GetJobData(jobId)` (line 131) and, via `GetJobState` (lines 154–158), `connection.GetStateData(jobId)`. `GetJobStartedAt` (lines 160–177) is the outlier — it re-derives `JobStorage.Current.GetMonitoringApi()` and pages `ProcessingJobs(0, int.MaxValue)` just to filter down to one key. The fix brings this method in line with the pattern its siblings already use.
- `GetPendingJobs` and `GetRunningJobs` (lines 52–124) use the monitoring API too, but bounded by `_options.MaxPendingJobsPageSize` — correctly out of scope per the spec, and I confirm no unbounded scans exist elsewhere in the class.
- The class holds no injected `IStorageConnection`/`JobStorage` — it reads the ambient static `JobStorage.Current` on every call. This is an existing convention in this class (not introduced by this fix) and matters for testability (see Prerequisites/Risks).
- `Anela.Heblo.API.csproj` references `Hangfire.PostgreSql` (not SQL Server as the spec's Background section speculates) and, importantly, already references `Hangfire.MemoryStorage` in the production project. This means an in-memory `JobStorage` provider is already on the dependency graph and available to tests without adding a new package reference — see Specification Amendments.
- The existing test file `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs` only covers constructor/options wiring via reflection; it has zero coverage of `GetJobById`/`GetJobStartedAt` today, confirming FR-3's premise.

This is a drop-in, mechanical replacement. No architectural decision is required beyond "use the same primitive the sibling method already uses" — which the spec itself proposes and which I confirm is correct and idiomatic for this codebase.

## Proposed Architecture

### Component Overview
No new components. Single method body replaced within an existing class:

```
IBackgroundWorker (interface, unchanged)
        |
HangfireBackgroundWorker (unchanged public surface)
        |
        +-- GetJobById(jobId)                 [unchanged]
              +-- connection.GetJobData(jobId)         [unchanged, O(1)]
              +-- GetJobState(connection, jobId)        [unchanged, O(1)]
              |     +-- connection.GetStateData(jobId)
              +-- GetJobStartedAt(connection, jobId)   [REWRITTEN, now O(1)]
                    +-- connection.GetStateData(jobId)  <-- same primitive, no new call site
```

### Key Design Decisions

#### Decision 1: Read `StartedAt` from `GetStateData` instead of scanning `ProcessingJobs`
**Options considered:**
1. Keep `ProcessingJobs` but cap the page size (mirrors `GetRunningJobs`'s bounded pattern) — still O(N), just a smaller constant; doesn't fix the fundamental mismatch (per-job lookup implemented as a collection scan).
2. Call `connection.GetJobData(jobId)` for `CreatedAt`-adjacent data — `GetJobData` does not carry `StartedAt`; that value lives in the "Processing" state's `Data` dictionary, not the job data record. Ruled out.
3. **Call `connection.GetStateData(jobId)` and read `Data["StartedAt"]` via `JobHelper.DeserializeNullableDateTime` when `Name == "Processing"`.** This mirrors `GetJobState`, which already calls the identical primitive for the identical `jobId` in the identical call path.

**Chosen approach:** Option 3, exactly as specified in `spec.r1.md` FR-1.

**Rationale:** O(1), zero new dependencies, consistent with the class's existing patterns, and it's the documented, supported way Hangfire itself stores/retrieves `ProcessingState` metadata (`Hangfire.States.ProcessingState` writes `StartedAt` into the state `Data` dictionary; `Hangfire.Common.JobHelper.DeserializeNullableDateTime` is the matching reader Hangfire uses internally).

#### Decision 2: Do not consolidate `GetJobState` + `GetJobStartedAt` into one `GetStateData` call
**Options considered:** Since both methods now call `connection.GetStateData(jobId)` with the same `jobId`, `GetJobById` could fetch `StateData` once and pass it to both, avoiding two storage round-trips per status check.

**Chosen approach:** Do not do this now; leave `GetJobState` and `GetJobStartedAt` as independent methods, each performing its own `GetStateData` call.

**Rationale:** The spec explicitly marks this consolidation "Out of Scope" (spec.r1.md, line 73) as a reasonable follow-on, not part of this fix. Keeping the two calls separate is a two-line-diff change with an obvious, narrow blast radius; conflating it with a bigger refactor increases review surface for zero required benefit. Flag it as a fast-follow ticket, not part of this PR — don't opportunistically do it here per this repo's "surgical changes" convention.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Single edit:
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` — replace the body of `GetJobStartedAt` (lines 160–177) per FR-1. Signature (`private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)`) stays identical — no caller changes needed.

Test addition (new test methods, existing file — do not relocate):
- `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs` — add the FR-3 coverage here. The spec's "Open Questions" note about possibly relocating is unnecessary churn; this file already houses `HangfireBackgroundWorkerTests`, and moving it is out of scope for a targeted perf fix.

### Interfaces and Contracts
No interface or contract changes. `IBackgroundWorker`, `BackgroundJobInfo`, and all public signatures are untouched. The only "contract" that matters here is Hangfire's own storage contract:
- `IStorageConnection.GetStateData(string jobId) : StateData?` — `StateData.Name` (string, e.g. `"Processing"`), `StateData.Data` (`IReadOnlyDictionary<string, string>` / `Dictionary<string,string>` depending on Hangfire version — verify against the installed `Hangfire.Core` version's signature before writing the code; do not assume it matches a different Hangfire major version's API).
- `Hangfire.Common.JobHelper.DeserializeNullableDateTime(string)` — Hangfire's own helper for parsing `StartedAt`/`EnqueuedAt`-style stored timestamps. Use it rather than `DateTime.Parse` to stay consistent with how Hangfire serialized the value in the first place (culture/format safety).

### Data Flow
1. Caller invokes `IBackgroundWorker.GetJobById(jobId)`.
2. `GetJobById` opens a connection, fetches `JobData` (unchanged), computes `state` via `GetJobState` (unchanged), then computes `StartedAt` via the rewritten `GetJobStartedAt`.
3. `GetJobStartedAt` calls `connection.GetStateData(jobId)` once (a single keyed storage read, not a page scan).
4. If `stateData?.Name == "Processing"` and `stateData.Data["StartedAt"]` deserializes successfully, return the `DateTime`. In every other case (null stateData, non-Processing state, missing/invalid key, or any exception) return `null`, preserving today's fail-safe `try/catch { return null; }` behavior.
5. `GetJobById` assembles and returns `BackgroundJobInfo` exactly as before — no shape change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hangfire's `StateData.Data` key name/casing for `"StartedAt"` could differ across `Hangfire.Core` versions or storage providers (Postgres vs. the in-memory test provider) | Medium | Before finalizing, grep the installed `Hangfire.Core` source/NuGet package (or `Hangfire.States.ProcessingState`) for the literal key it writes, and assert the exact key/format in the new unit test rather than assuming `"StartedAt"` a priori. The FR-3 tests are the safety net for this. |
| Testing requires swapping the ambient static `JobStorage.Current`, since `HangfireBackgroundWorker` is not constructed with an injected storage/connection | Medium | Use the already-referenced `Hangfire.MemoryStorage` package: in test setup, set `JobStorage.Current = new Hangfire.MemoryStorage.MemoryStorage()` (or the project's existing pattern if one exists elsewhere in the test suite — grep first), seed a job into the desired state via `BackgroundJob.Enqueue`/`BackgroundJob.Schedule` plus a manual state transition, then call `GetJobById`. Restore/reset `JobStorage.Current` after each test (or use a fixture) to avoid cross-test pollution, since it's a process-wide static. |
| Silent behavior drift if `Data["StartedAt"]` is present under a non-`"Processing"` state name in some Hangfire version (e.g., custom states) | Low | Spec FR-1 already pins the check to `stateData?.Name == "Processing"` exactly — implement it as an exact string match, not a "key exists" check, to avoid returning stale/incorrect timestamps for jobs in other states. |
| None of the above are new risks introduced by this change — the removed code path (`ProcessingJobs(0, int.MaxValue)`) was strictly worse on every axis (perf, memory, and no better correctness) | N/A | No mitigation needed; this is a strict improvement with no behavior-preserving downside once FR-1's acceptance criteria are met. |

## Specification Amendments
1. **Storage provider correction:** spec.r1.md's Background section states "SQL Server, per the Hangfire.SqlServer provider convention used by Hangfire." The actual project (`Anela.Heblo.API.csproj`) references `Hangfire.PostgreSql`, not `Hangfire.SqlServer`. This doesn't change the fix (the `IStorageConnection`/`GetStateData` API is provider-agnostic), but implementers should not go looking for SQL Server-specific behavior or assume T-SQL semantics anywhere near this code.
2. **Test infrastructure clarification:** spec.r1.md's Dependencies section is unsure which fake/in-memory `IStorageConnection` provider to use. Resolved: `Hangfire.MemoryStorage` (v1.8.1.2) is already a package reference in `Anela.Heblo.API.csproj` (the production project, likely used for local dev/demo mode) and is therefore available transitively to the test project. Use `Hangfire.MemoryStorage.MemoryStorage` as the `JobStorage.Current` test double rather than introducing a new mocking dependency or hand-rolled fake of `IStorageConnection` (which is a large interface — not worth hand-mocking when a real in-memory provider already ships with the solution).
3. **Test location confirmed, no relocation needed:** spec.r1.md hedges on whether `HangfireBackgroundWorkerTests.cs` (under `backend/test/Anela.Heblo.Tests/Features/Invoices/`) is the right home. Confirmed: this is the existing, sole test file for this class today. Add the new tests here; do not create a new test file or namespace for this fix.

## Prerequisites
None. No migrations, no config, no new package references (Hangfire.MemoryStorage is already present), no infrastructure changes. This can be implemented and merged standalone.
