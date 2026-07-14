# Design: Targeted per-job lookup for `GetJobStartedAt` in Hangfire background worker

## Component Design

### `HangfireBackgroundWorker` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`)
No new components are introduced. The change is scoped to the internals of one existing method.

- **`GetJobById(string jobId) : BackgroundJobInfo?`** (public, unchanged signature and behavior)
  - Opens a storage connection.
  - Fetches job data via `connection.GetJobData(jobId)` (unchanged, O(1)).
  - Resolves current state via `GetJobState(connection, jobId)` (unchanged, O(1) — calls `connection.GetStateData(jobId)`).
  - Resolves `StartedAt` via `GetJobStartedAt(connection, jobId)` (**rewritten** below).
  - Assembles and returns `BackgroundJobInfo` exactly as today; no shape change.

- **`GetJobStartedAt(IStorageConnection connection, string jobId) : DateTime?`** (private static, signature unchanged)
  - Responsibility narrows to: given a job ID, return its `StartedAt` timestamp *if and only if* the job's current Hangfire state is `"Processing"`, else `null`.
  - New implementation:
    1. Call `connection.GetStateData(jobId)` — a single keyed storage read (same primitive `GetJobState` already uses for the same `jobId`).
    2. If the result is `null`, return `null`.
    3. If `stateData.Name != "Processing"` (exact string match, not a key-existence check), return `null`.
    4. Otherwise, look up `stateData.Data["StartedAt"]`; if the key is missing, return `null`.
    5. Deserialize the value via `Hangfire.Common.JobHelper.DeserializeNullableDateTime(string)`; return the resulting `DateTime?` (which itself may be `null` if deserialization fails per that helper's contract).
  - Wrap the entire body in the existing `try/catch { return null; }` fail-safe (preserving current behavior for any unexpected exception, e.g. storage errors).
  - Removed: the call to `JobStorage.Current.GetMonitoringApi().ProcessingJobs(0, int.MaxValue)` and the subsequent `FirstOrDefault(j => j.Key == jobId)` linear scan.

- **No changes** to `IBackgroundWorker`, `BackgroundJobInfo`, `GetPendingJobs`, `GetRunningJobs`, `GetJobState`, `GetJobDisplayName`, or any other public member of the class.

### Interfaces and Contracts consumed (external, unchanged)
- `Hangfire.Storage.IStorageConnection.GetStateData(string jobId) : StateData?` — already used by `GetJobState`; now also used directly by `GetJobStartedAt` instead of the monitoring API.
- `Hangfire.Common.JobHelper.DeserializeNullableDateTime(string) : DateTime?` — Hangfire's own helper for parsing stored `StartedAt`-style timestamps; used instead of `DateTime.Parse` to match Hangfire's own serialization format/culture handling.
- Verify the installed `Hangfire.Core` version's exact `StateData.Data` type (`Dictionary<string,string>` vs `IReadOnlyDictionary<string,string>`) before writing the key lookup, per the architecture review's note — do not assume a specific major-version shape.

### Test component (`backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs`)
No new test file/namespace. Add test methods to the existing file covering:
- Job in `"Processing"` state with a valid, parseable `StartedAt` entry in state `Data` → `GetJobById(...).StartedAt` (or equivalent) is non-null and matches the seeded value.
- Job in a non-`"Processing"` state (e.g. `"Succeeded"`, `"Enqueued"`, `"Failed"`, `"Scheduled"`) → `StartedAt == null`.
- Nonexistent job ID (`GetStateData` returns `null`) → `StartedAt == null` (or `GetJobById` returns `null`, matching existing unknown-job behavior).
- Test setup uses `Hangfire.MemoryStorage.MemoryStorage` assigned to `JobStorage.Current` (already a package reference in the production project) as the `IStorageConnection` test double, seeding jobs into the desired state; reset/restore `JobStorage.Current` between tests since it is a process-wide static.

## Data Schemas

No new or changed schemas, DTOs, database entities, API request/response shapes, or event payloads. This fix reads exclusively from Hangfire's existing internal storage model:

- `StateData` (Hangfire-defined, read-only from this codebase's perspective):
  - `Name: string` — current state name (e.g. `"Processing"`, `"Succeeded"`, `"Enqueued"`, `"Failed"`, `"Scheduled"`).
  - `Data: IDictionary<string, string>` (exact type per installed Hangfire version) — state-specific fields; for `"Processing"` this includes a `"StartedAt"` entry written by `Hangfire.States.ProcessingState` and serialized in Hangfire's own datetime string format.

- `BackgroundJobInfo` — unchanged; no new fields. Its `StartedAt` field's *value* is now sourced from `GetStateData` instead of `ProcessingJobs`, but its type and the field's meaning are identical.

No migrations, no new tables/columns, no new API contracts.
