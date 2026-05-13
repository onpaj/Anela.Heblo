# Specification: Optimize GetRunningInvoiceImportJobs Endpoint Performance

## Summary
The `GET Invoices/GetRunningInvoiceImportJobs` endpoint averages 3,734 ms per call, exceeding the 3,000 ms slow-request threshold. The handler issues N+1 database connections per processing job and fetches unbounded result sets from Hangfire storage. This spec defines targeted backend fixes — connection reuse, paging caps, and short-lived caching — to bring the endpoint comfortably under the threshold and prevent regression as the queue grows.

## Background
The endpoint is polled by the frontend while invoice imports are running so the UI can show progress. It is implemented in `HangfireBackgroundWorker` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`) and reads job state from Hangfire's PostgreSQL storage.

Slow-request telemetry recorded 2 hits in the last 24 hours averaging 3,734 ms (threshold 3,000 ms), making this a new finding. Two root causes are confirmed:

1. **N+1 storage connections** — `GetRunningJobs()` opens a fresh `IStorageConnection` inside the per-job loop, and `GetJobDisplayName` opens yet another connection per job. With N processing jobs this produces 2N PostgreSQL round-trips, each paying connection-acquisition overhead.
2. **Unbounded paging** — `GetPendingJobs()` calls `monitoring.EnqueuedJobs("default", 0, int.MaxValue)` and `monitoring.ScheduledJobs(0, int.MaxValue)`, fetching the entire queue regardless of size. Latency degrades linearly as the Hangfire backlog grows.

Because the frontend polls the endpoint repeatedly during an active import, even a moderate cost per call compounds quickly.

## Functional Requirements

### FR-1: Reuse a single storage connection in `GetRunningJobs()`
Refactor `GetRunningJobs()` so a single `IStorageConnection` is acquired before iterating over processing jobs and reused for all `GetJobData` and `GetJobParameter` calls (including those currently inside `GetJobDisplayName`). The helper method must accept the existing connection rather than opening its own.

**Acceptance criteria:**
- Exactly one `JobStorage.Current.GetConnection()` call per invocation of `GetRunningJobs()`, regardless of how many processing jobs exist.
- `GetJobDisplayName` (or its replacement) takes the connection as a parameter and does not open a new one.
- The connection is disposed exactly once when the loop completes (via `using` or equivalent).
- Returned data for each job (id, display name, state, parameters) is functionally identical to the current implementation.

### FR-2: Cap page size in `GetPendingJobs()`
Replace `int.MaxValue` with a bounded cap (default: 200) for both `EnqueuedJobs("default", …)` and `ScheduledJobs(…)` calls. The cap should be exposed as a named constant or configuration value so it can be tuned without code edits.

**Acceptance criteria:**
- `EnqueuedJobs` and `ScheduledJobs` are called with a page size of 200 (or the configured value), never `int.MaxValue`.
- A constant such as `MaxPendingJobsPageSize = 200` exists in a clear location (top of the file or a config section).
- When the queue exceeds the cap, the endpoint still returns a useful response (the first N jobs) without erroring.
- The response shape is unchanged from the current implementation.

### FR-3: Short-lived in-memory caching of the endpoint result
Cache the full payload returned by the endpoint for a short window (default: 2 seconds) using `IMemoryCache` to absorb repeated polls from the frontend during active imports.

**Acceptance criteria:**
- A single cache key (e.g. `"invoices:running-import-jobs"`) is used.
- Cache TTL is configurable (default 2 seconds); a TTL of 0 or unset disables caching.
- Concurrent requests during the TTL window return the cached payload without re-querying Hangfire storage.
- The cache is in-process (`IMemoryCache`) — no distributed cache dependency.
- Cache entries do not persist beyond the TTL even if the endpoint is idle.

### FR-4: Observability of the fix
The endpoint must remain visible in slow-request telemetry so regressions are detected, and the fix must be measurable.

**Acceptance criteria:**
- No existing telemetry/logging for this endpoint is removed or silenced.
- Average duration under representative load drops below the 3,000 ms slow-request threshold (target: well under 1,000 ms in normal conditions).

## Non-Functional Requirements

### NFR-1: Performance
- **Target average latency:** < 1,000 ms under normal load (1–20 processing jobs, queue depth < 200).
- **Hard ceiling:** < 3,000 ms slow-request threshold even with queue depth at the page-size cap.
- **Database round-trips per call:** O(1) connections (single connection reused), with per-job `GetJobData`/`GetJobParameter` calls executed over that connection.

### NFR-2: Correctness & backward compatibility
- The endpoint's HTTP contract, response schema, and field semantics must not change.
- Existing callers (frontend polling loop) must work unchanged.
- DTOs remain classes (not C# records), per project rules.

### NFR-3: Security
- No change to authentication or authorization — the endpoint's existing access controls remain in force.
- No new sensitive data is exposed; cached payload contains the same information as the live response.

### NFR-4: Resource usage
- Memory footprint of the cache is bounded (single small entry, short TTL).
- No new long-lived background tasks or timers are introduced.

### NFR-5: Maintainability
- Follow project coding conventions: small focused methods, named constants, no magic numbers.
- New constants and configuration values are documented inline where they're defined.

## Data Model
No schema changes. The endpoint continues to read from Hangfire's existing storage (PostgreSQL) via `IStorageConnection` and `IMonitoringApi`.

Relevant runtime types (unchanged):
- `IStorageConnection` — Hangfire storage connection (now reused per request).
- `IMonitoringApi` — Hangfire queue inspection API.
- Existing DTO returned by `GetRunningInvoiceImportJobs` (shape preserved).

## API / Interface Design

### HTTP endpoint (unchanged contract)
- **Route:** `GET /Invoices/GetRunningInvoiceImportJobs`
- **Auth:** Existing requirements unchanged.
- **Response body:** Existing DTO; field set, types, and ordering preserved.

### Internal refactor in `HangfireBackgroundWorker`
- `GetRunningJobs()` — acquires one connection, passes it to helpers.
- `GetJobDisplayName(IStorageConnection connection, string jobId)` — new signature; no longer opens its own connection.
- `GetPendingJobs()` — uses `MaxPendingJobsPageSize` constant in place of `int.MaxValue`.
- Caching layer wraps the controller action (or the worker method invoked by it) using `IMemoryCache` with a configured TTL.

### Configuration
Two new optional settings (defaults applied when absent):
- `Hangfire:MaxPendingJobsPageSize` — int, default `200`.
- `Hangfire:RunningJobsCacheSeconds` — int, default `2`.

If these are not added to `appsettings.json`, the defaults take effect — no required configuration changes for deployment.

## Dependencies
- **Hangfire** — already in use; this spec relies on `IStorageConnection`, `IMonitoringApi`, `GetJobData`, `GetJobParameter`, `EnqueuedJobs`, `ScheduledJobs`.
- **`IMemoryCache`** — standard `Microsoft.Extensions.Caching.Memory`. Already available in .NET 8 host; register in DI if not already registered.
- **No new NuGet packages** required.
- **No frontend changes** required (HTTP contract is unchanged).

## Out of Scope
- Replacing Hangfire or changing the underlying job storage.
- Modifying the frontend polling behavior or polling interval.
- Persistent / distributed caching (Redis, etc.).
- Pagination of the endpoint response (the cap silently truncates; clients still see "the first 200").
- Adding new fields, metrics, or UI affordances.
- General performance audits of other Hangfire-related endpoints.
- Database schema changes or migrations.

## Open Questions
None.

## Status: COMPLETE