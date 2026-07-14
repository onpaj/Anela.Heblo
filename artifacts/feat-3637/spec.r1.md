# Specification: Targeted per-job lookup for `GetJobStartedAt` in Hangfire background worker

## Summary
`HangfireBackgroundWorker.GetJobStartedAt` currently retrieves the entire Hangfire "processing" job set (`monitoring.ProcessingJobs(0, int.MaxValue)`) to find the start time of a single, known job ID. This is replaced with a targeted, O(1) lookup via `IStorageConnection.GetStateData(jobId)`, reading `StartedAt` directly out of the "Processing" state's data dictionary. This is a pure internal performance fix with no change to the public contract of `IBackgroundWorker` or `BackgroundJobInfo`.

## Background
`HangfireBackgroundWorker` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`) implements `IBackgroundWorker`, wrapping Hangfire's static `BackgroundJob` / `JobStorage` APIs so the rest of the codebase can enqueue, schedule, and query background jobs through an interface.

`GetJobById(string jobId)` (line 126) is the single-job status endpoint used by callers that need the state of one specific job (e.g. a status-polling UI or API endpoint). It already does a targeted lookup for the job's core data (`connection.GetJobData(jobId)`, O(1)) and its current state (`GetJobState`, which calls `connection.GetStateData(jobId)`, also O(1)). However, for the job's `StartedAt` timestamp, it delegates to `GetJobStartedAt` (line 160), which pages through **every** currently-processing job (`monitoring.ProcessingJobs(0, int.MaxValue)`) and does an in-memory linear scan (`FirstOrDefault(j => j.Key == jobId)`) to find the one matching entry.

This means a single-job status check — which should be O(1) — degrades to O(N) in both Hangfire storage query cost and application memory allocation, where N is the number of jobs currently in the "Processing" state. Under production load with many concurrent/queued jobs, this is unnecessary load on the job storage backend (SQL Server, per the Hangfire.SqlServer provider convention used by Hangfire) and repeated full materialization of processing-job data for every status poll.

Hangfire already stores `StartedAt` as part of the "Processing" state's `Data` dictionary (set by `Hangfire.States.ProcessingState`), and this value is retrievable directly via `connection.GetStateData(jobId)` — the same primitive already used by the sibling method `GetJobState`. No monitoring-API table scan is needed.

This was flagged by the automated daily architecture-review routine (2026-07-14) as a performance finding in the BackgroundJobs module. The fix is small, self-contained, and low-risk: it changes only the internal implementation of one private method, preserving its signature and public-facing behavior.

## Functional Requirements

### FR-1: Replace the full-scan lookup with a targeted per-job state read
`GetJobStartedAt(IStorageConnection connection, string jobId)` must no longer call `JobStorage.Current.GetMonitoringApi().ProcessingJobs(0, int.MaxValue)`. Instead, it must call `connection.GetStateData(jobId)` and, when the job's current state is `"Processing"`, read the `StartedAt` value out of that state's `Data` dictionary using `Hangfire.Common.JobHelper.DeserializeNullableDateTime`.

**Acceptance criteria:**
- The method contains no call to `ProcessingJobs` (or any other `int.MaxValue`-paged monitoring API call).
- For a job whose current Hangfire state is `"Processing"` and whose state `Data` dictionary contains a parseable `"StartedAt"` entry, the method returns that value as a `DateTime`.
- For a job that is not in the `"Processing"` state (e.g. `"Succeeded"`, `"Enqueued"`, `"Failed"`, `"Scheduled"`), the method returns `null`.
- For a job ID that does not exist in storage (`GetStateData` returns `null`), the method returns `null`.
- If the `"StartedAt"` key is missing from the state data, or its value cannot be deserialized by `JobHelper.DeserializeNullableDateTime`, the method returns `null` rather than throwing.
- Any unexpected exception raised while reading state data is caught and results in a `null` return (preserving today's `try/catch { return null; }` fail-safe behavior), consistent with `GetJobById`'s existing overall exception handling.

### FR-2: Preserve existing method signature and call sites
`GetJobStartedAt` keeps its current signature — `private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)` — and its only caller, `GetJobById` (line 144), requires no changes. `IBackgroundWorker`, `BackgroundJobInfo`, and all other public types are unaffected.

**Acceptance criteria:**
- `GetJobById`'s behavior (the shape and field values of the returned `BackgroundJobInfo`, and the `null` return for unknown job IDs) is unchanged for all existing test cases and manual verification scenarios, except that the `StartedAt` field is now computed via a direct state lookup instead of a full scan.
- No changes to `IBackgroundWorker`, `BackgroundJobInfo`, `GetPendingJobs`, `GetRunningJobs`, `GetJobState`, or `GetJobDisplayName`.

### FR-3: Regression test coverage for the fixed method
Add unit test coverage exercising `GetJobStartedAt`'s (or `GetJobById`'s) behavior across the state variations described in FR-1, since no such coverage currently exists (the existing `HangfireBackgroundWorkerTests.cs` only covers constructor/options wiring).

**Acceptance criteria:**
- A test confirms that a job whose state data reports `"Processing"` with a valid `StartedAt` payload yields the expected non-null `DateTime` (via `GetJobById`, using an in-memory/fake `IStorageConnection` or Hangfire's `InMemoryStorage` test provider — whichever fits the existing test setup conventions for this class/project).
- A test confirms that a job in a non-`"Processing"` state yields `StartedAt == null`.
- A test confirms that a nonexistent job ID yields `StartedAt == null` (or `GetJobById` returns `null`, per existing behavior for unknown jobs).
- Tests do not depend on real SQL Server-backed Hangfire storage; they use whatever in-memory/test-double approach is already established in the test project (see Dependencies).

## Non-Functional Requirements

### NFR-1: Performance
- The fixed lookup must be O(1) with respect to the number of currently-processing jobs — a single indexed/keyed storage read per call, not a paged scan.
- No new N+1 or repeated-query patterns may be introduced elsewhere in the class as a side effect of this change.
- No functional regression in response time for `GetJobById` overall; the change should only improve or maintain latency, never worsen it.

### NFR-2: Security
No change. This method reads job metadata already accessible to authorized callers of `IBackgroundWorker`; no new data exposure, authentication, or authorization concerns are introduced.

## Data Model
No schema or entity changes. This fix operates entirely on Hangfire's existing internal storage model, specifically:
- `IStorageConnection.GetStateData(jobId)` → `StateData` with `Name` (state name, e.g. `"Processing"`) and `Data` (a `Dictionary<string, string>` of state-specific fields, including `"StartedAt"` for the Processing state, serialized via Hangfire's `JobHelper`).

No new fields are added to `BackgroundJobInfo`.

## API / Interface Design
No public API, controller, or route changes. This is an internal implementation change within `HangfireBackgroundWorker`, a private helper method used only by `GetJobById`. The `IBackgroundWorker` interface contract (`GetJobById(string jobId) : BackgroundJobInfo?`) is unchanged.

## Dependencies
- `Hangfire.Storage.IStorageConnection.GetStateData(string jobId)` — already in use elsewhere in this class (`GetJobState`).
- `Hangfire.Common.JobHelper.DeserializeNullableDateTime(string)` — standard Hangfire helper for deserializing stored date/time values; used by Hangfire internally for the same `"StartedAt"` field.
- Existing test project conventions in `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs` (or a more suitable test location/namespace if the reviewer/architect prefers relocating Hangfire-worker tests — see Open Questions/assumption below) and whatever fake/in-memory `IStorageConnection` or Hangfire test-storage provider is already available in the solution.

## Out of Scope
- Changing `GetPendingJobs` or `GetRunningJobs`, which also call monitoring APIs with a bounded page size (`_options.MaxPendingJobsPageSize`) — these are not full-scan (`int.MaxValue`) queries and are not part of this finding.
- Any change to Hangfire storage provider configuration, retention policy, or dashboard behavior.
- Broader refactors of `HangfireBackgroundWorker` (e.g. consolidating `GetJobState` and `GetJobStartedAt` into a single `GetStateData` call to avoid two separate lookups within `GetJobById`) — this would be a reasonable follow-on optimization but is not required to resolve the specific finding, which targets only the `int.MaxValue` scan.
- Adding new public API endpoints or UI surfaces for job status.

## Open Questions
None.

## Status: COMPLETE
