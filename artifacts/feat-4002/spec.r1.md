# Specification: Classify Plaud upstream 5xx failures as transient and surface sustained outages

## Summary
The `plaud-polling` Hangfire job fails with a generic `InvalidOperationException` whenever the Plaud CLI reports an upstream HTTP 5xx (observed: `API error: 500 Internal Server Error` on `plaud recent`). These are transient third-party outages, not application defects, yet they are logged at Error level and fail the Hangfire run, producing telemetry noise that looks like a code bug. This feature adds a typed exception for upstream-unavailable failures, handles it gracefully in the polling job, and raises a single visible error only when the outage is sustained.

## Background
- Issue #4002: 6 occurrences between 2026-08-29 13:35Z and 2026-08-30 20:15Z of `InvalidOperationException` from `PlaudCliClient.RunCliCoreAsync`, message `Plaud CLI exited with code 1: ... [FETCH_FAILED] Failed to fetch recent files. Error: API error: 500 Internal Server Error`.
- Prior Plaud telemetry issues (#3118, #2995, #3520) were auth expiry and were solved with `PlaudAuthExpiredException` plus token refresh. Upstream 5xx is a different failure mode.
- Today `RunCliCoreAsync` logs every non-zero exit with `LogError`, then throws `InvalidOperationException` (unless `AUTH_FAILED`). `PlaudPollingJob` does not catch a failure of the initial `ListRecentAsync`, so the Hangfire run goes Failed (`[AutomaticRetry(Attempts = 0)]`; the next 5-minute tick is the retry).
- The job already polls only every 5 minutes with no retry, so there is no retry storm and no circuit breaker is needed; the 6 events are simply 6 polling ticks hitting an unhealthy upstream.
- The issue's correlation hypothesis (null-`Message` ExceptionTelemetry tripping `McpProductNotFoundTelemetryFilter`) is tracked in a separate issue and is out of scope here.

## Functional Requirements

### FR-1: Typed exception for Plaud upstream 5xx
`PlaudCliClient.RunCliCoreAsync` must detect CLI failures whose stderr contains an upstream HTTP 5xx marker (`API error: 5xx`, e.g. `API error: 500 Internal Server Error`, 502, 503, 504) and throw a new `PlaudUpstreamUnavailableException` instead of the plain `InvalidOperationException`.
**Acceptance criteria:**
- `PlaudUpstreamUnavailableException` lives in `Anela.Heblo.Adapters.Plaud`, derives from `InvalidOperationException` (so existing catch sites keep working), carries the trimmed stderr in its message and exposes the parsed HTTP status code.
- Stderr `API error: 500 Internal Server Error` yields the new exception with status 500; 502/503/504 likewise.
- `AUTH_FAILED` still yields `PlaudAuthExpiredException` (checked first, unchanged behaviour); 4xx API errors and any other non-zero exit still yield plain `InvalidOperationException`.
- The auth-retry logic in `RunCliAsync` is unaffected (no token refresh on 5xx).

### FR-2: Downgrade log level for upstream 5xx
**Acceptance criteria:**
- For upstream 5xx the client logs at Warning (not Error) with exit code and stderr; all other non-zero exits keep `LogError`.

### FR-3: Polling job tolerates a transient upstream outage
`PlaudPollingJob.ExecuteAsync` must catch `PlaudUpstreamUnavailableException` from the initial `ListRecentAsync` call.
**Acceptance criteria:**
- The run completes successfully (Hangfire Succeeded), logs a Warning "Plaud upstream unavailable, will retry on next tick" and returns without ingesting.
- If `PlaudUpstreamUnavailableException` escapes from a per-recording ingest (via the mediator), the loop stops early (remaining recordings would hit the same outage), logs a Warning, and the run completes without throwing. Other exceptions keep the existing per-recording log-and-continue behaviour.
- `PlaudAuthExpiredException` and all other exceptions from `ListRecentAsync` still propagate and fail the run as today.

### FR-4: Sustained-outage visibility
So that a silent stop of recording imports is noticed, the job tracks consecutive upstream-unavailable runs.
**Acceptance criteria:**
- A singleton in-memory tracker counts consecutive polling runs ending in upstream-unavailable; a fully successful list call resets it to 0.
- When the count reaches `MeetingTasks:UpstreamFailureAlertThreshold` (default 12, about one hour at 5-minute cadence) the job logs exactly one Error ("Plaud upstream has been unavailable for N consecutive runs") and then logs again only every further multiple of the threshold, to avoid noise.
- Threshold of 0 or less disables the Error escalation (Warnings only).
- The counter is process-local and resets on app restart; this is acceptable.

### FR-5: Process documentation
**Acceptance criteria:**
- `docs/processes/sync-plaud-recordings.md` is updated (Known quirks / Configuration: new key, new behaviour on upstream 5xx, corrected "Failure counters" quirk) and `verified_at` bumped; `python3 scripts/process-docs/check.py index` passes.

## Non-Functional Requirements

### NFR-1: Performance
No additional CLI calls, no added latency. Tracker is O(1) in memory.

### NFR-2: Security
No new secrets or endpoints. Stderr is already logged today; no new sensitive data is exposed.

### NFR-3: Compatibility
No API, DB schema or frontend change. DTO rule not affected.

## Data Model
No persistent entities. New in-memory singleton `PlaudPollingHealthTracker` (consecutive failure counter, thread-safe via `Interlocked`).

## API / Interface Design
- `PlaudUpstreamUnavailableException : InvalidOperationException` (adapter project), with `int StatusCode`.
- `MeetingTasksOptions.UpstreamFailureAlertThreshold` (int, default 12).
- Because the Application layer's job cannot reference the Adapter assembly (Adapter depends on Application), the exception type must be reachable from the Application layer: see architecture review for placement (define it in `Application/Features/MeetingTasks/Services`, next to `IPlaudClient`).

## Dependencies
Plaud CLI (`@plaud-ai/cli`) stderr format; existing `IPlaudClient`, `PlaudPollingJob`, `MeetingTasksOptions`.

## Out of Scope
- Fixing the NullReferenceException in `McpProductNotFoundTelemetryFilter` (separate issue).
- Retry/backoff inside the client, a persisted circuit breaker, a dashboard widget, e-mail/Teams alerting.
- Changing `PlaudAuthExpiredException` handling.
- Checking Plaud's status page (operational step, not code).

## Open Questions
None.

## Status: COMPLETE
