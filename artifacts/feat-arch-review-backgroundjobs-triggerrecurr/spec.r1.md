# Specification: Distinct Error Codes for `TriggerRecurringJob` Failure Modes

## Summary
The `TriggerRecurringJobHandler` currently collapses three semantically distinct failure conditions ("job not registered", "job disabled", "enqueue failed") into a single error code (`RecurringJobNotFound = 1901`) and a single HTTP status (`404 Not Found`). This specification introduces two new error codes and aligns the HTTP response status with the actual failure semantics so API consumers can correctly distinguish missing, disabled, and enqueue-failure conditions.

## Background
`TriggerRecurringJobHandler` (at `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs`) is the MediatR handler used to manually trigger a registered Hangfire recurring job. The handler returns a non-success response in three distinct cases:

| Line | Actual condition | Current error code |
|------|------------------|--------------------|
| 39   | Job is not registered in the DI container (genuinely unknown) | `RecurringJobNotFound` (correct) |
| 57   | Job is registered but disabled and `ForceDisabled = false` | `RecurringJobNotFound` (incorrect) |
| 74   | Job is registered and enabled, but `IHangfireJobEnqueuer.EnqueueJob` returned `null` | `RecurringJobNotFound` (incorrect) |

The matching MVC controller (`RecurringJobsController.cs`, lines 116–119) translates any non-success response into HTTP `404 Not Found`. As a result, "disabled" and "enqueue failure" outcomes are indistinguishable from a true "not found" outcome both in error code and HTTP status. This breaks observability (error monitoring cannot bucket the failures), frontend UX (cannot show appropriate messaging), and HTTP semantics.

The arch review routine flagged the issue on 2026-05-27.

## Functional Requirements

### FR-1: Introduce `RecurringJobDisabled` error code
Add a new error code `RecurringJobDisabled = 1903` to `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. Use it to indicate that the requested recurring job exists in the registry but is currently disabled (the caller did not request `ForceDisabled = true`).

**Acceptance criteria:**
- `ErrorCodes.RecurringJobDisabled` is defined with the integer value `1903`.
- Value `1903` does not collide with any other entry in `ErrorCodes`.
- The handler's "found but disabled" branch (current line ~57) returns `Success = false` with `ErrorCode = ErrorCodes.RecurringJobDisabled`.
- The error response includes the job identifier so the consumer can correlate the result.

### FR-2: Introduce `RecurringJobEnqueueFailed` error code
Add a new error code `RecurringJobEnqueueFailed = 1904` to `ErrorCodes.cs`. Use it to indicate that the job is registered and enabled, but the underlying Hangfire enqueue call returned `null` (Hangfire-side failure).

**Acceptance criteria:**
- `ErrorCodes.RecurringJobEnqueueFailed` is defined with the integer value `1904`.
- Value `1904` does not collide with any other entry in `ErrorCodes`.
- The handler's "enqueue returned null" branch (current line ~74) returns `Success = false` with `ErrorCode = ErrorCodes.RecurringJobEnqueueFailed`.
- The error response includes the job identifier.

### FR-3: Preserve `RecurringJobNotFound` semantics
Restrict `RecurringJobNotFound` (1901) to its literal meaning: the requested job identifier is not registered in the DI container / job registry.

**Acceptance criteria:**
- The handler's "job not registered" branch (current line ~39) is the only path returning `ErrorCode = ErrorCodes.RecurringJobNotFound`.
- A grep for `ErrorCodes.RecurringJobNotFound` in the BackgroundJobs use case folder yields exactly one usage (the not-registered branch).

### FR-4: HTTP status mapping in `RecurringJobsController`
Update `RecurringJobsController.TriggerRecurringJob` (currently mapping any failure to `NotFound`) to switch on `response.ErrorCode` and return the appropriate HTTP status code:

| Error code | HTTP status |
|------------|-------------|
| `RecurringJobNotFound` (1901) | `404 Not Found` |
| `RecurringJobDisabled` (1903) | `409 Conflict` |
| `RecurringJobEnqueueFailed` (1904) | `500 Internal Server Error` |
| Any other / fallback | `500 Internal Server Error` |

**Acceptance criteria:**
- Triggering an unknown job id returns HTTP 404 with `errorCode = 1901`.
- Triggering a disabled job with `forceDisabled = false` returns HTTP 409 with `errorCode = 1903`.
- Triggering a job for which `IHangfireJobEnqueuer.EnqueueJob` returns `null` returns HTTP 500 with `errorCode = 1904`.
- The success path is unchanged (HTTP 200 with the existing response body).
- The response body for failures continues to be the existing `TriggerRecurringJobResponse` shape (no breaking field changes).

### FR-5: Logging continuity
Each failure branch must continue to log at its current severity, but include the new error code in structured logging properties so log queries can group on it.

**Acceptance criteria:**
- "Not found" branch logs at `Warning` and includes `ErrorCode = 1901`.
- "Disabled" branch logs at `Warning` and includes `ErrorCode = 1903`.
- "Enqueue failed" branch logs at `Error` and includes `ErrorCode = 1904`.
- Existing log message templates are preserved; only structured properties are augmented.

### FR-6: Unit test coverage
Add or update unit tests for `TriggerRecurringJobHandler` to cover the three failure branches and assert the correct error code is returned. Add or update controller-level tests (or integration tests, whichever pattern is already in use for `RecurringJobsController`) to assert the HTTP status mapping defined in FR-4.

**Acceptance criteria:**
- Test "returns RecurringJobNotFound when job id is not registered" passes.
- Test "returns RecurringJobDisabled when job is disabled and forceDisabled is false" passes.
- Test "returns RecurringJobEnqueueFailed when EnqueueJob returns null" passes.
- Test "controller maps RecurringJobDisabled to 409" passes.
- Test "controller maps RecurringJobEnqueueFailed to 500" passes.
- Test "controller maps RecurringJobNotFound to 404" passes (regression).
- Overall changed-file test coverage remains ≥ 80%.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The change is limited to constant lookups, a switch statement in the controller, and structured-log property additions. Response time for the trigger endpoint must remain within existing baseline (target: < 200 ms p95 for the in-process portion, excluding Hangfire latency).

### NFR-2: Security
No security surface change. The endpoint's existing authentication and authorization requirements are preserved verbatim. Error responses must not leak internal exception details, stack traces, or job-handler internals — they should expose only the job identifier and the error code/message already produced by the handler.

### NFR-3: Backward compatibility
- The existing client of error code `1901` may observe a behavioral change: HTTP `404` will no longer be returned for disabled jobs or enqueue failures. This is an intentional correction, but must be called out in the PR description and release notes.
- The response DTO shape (`TriggerRecurringJobResponse`) is unchanged. No regeneration of OpenAPI client breaking-changes is expected; the TypeScript client will pick up the new error code constants once `ErrorCodes` is exposed (if it is exposed at all — see Open Questions).

### NFR-4: Observability
Error-monitoring dashboards filtering on `ErrorCode` will now see three distinct buckets (1901 / 1903 / 1904) instead of one. No dashboard migration is required; existing 1901 queries continue to work and will simply receive fewer hits.

## Data Model
No database schema changes. The only "data" being added is two integer constants in the `ErrorCodes` static class:

```
ErrorCodes.RecurringJobNotFound        = 1901   // (existing)
ErrorCodes.RecurringJobDisabled        = 1903   // (new)
ErrorCodes.RecurringJobEnqueueFailed   = 1904   // (new)
```

Note: `1902` is intentionally skipped to leave room for an in-flight error code, if any exists in the `1900` range. The architect should verify the gap is intentional and not collide with any pending change.

## API / Interface Design

### Endpoint
`POST /api/recurring-jobs/{jobId}/trigger` (path and verb unchanged — confirm exact route in `RecurringJobsController` during implementation).

### Request
Unchanged. Existing `TriggerRecurringJobRequest` (or equivalent query/body parameters, including `forceDisabled`) is preserved.

### Response — success
Unchanged. HTTP `200 OK` with the existing `TriggerRecurringJobResponse` body, `Success = true`, and the Hangfire enqueue identifier.

### Response — failures

**Job not registered**
```
HTTP/1.1 404 Not Found
{
  "success": false,
  "errorCode": 1901,
  "message": "Recurring job '{jobId}' is not registered.",
  ...
}
```

**Job disabled (and `forceDisabled = false`)**
```
HTTP/1.1 409 Conflict
{
  "success": false,
  "errorCode": 1903,
  "message": "Recurring job '{jobId}' is disabled. Pass forceDisabled=true to override.",
  ...
}
```

**Enqueue failed**
```
HTTP/1.1 500 Internal Server Error
{
  "success": false,
  "errorCode": 1904,
  "message": "Failed to enqueue recurring job '{jobId}'.",
  ...
}
```

### Error mapping (controller)
The controller's existing `if (!response.Success) return NotFound(response);` is replaced with a switch on `response.ErrorCode`:

- `1901` → `NotFound(response)`
- `1903` → `Conflict(response)`
- `1904` → `StatusCode(StatusCodes.Status500InternalServerError, response)`
- default → `StatusCode(StatusCodes.Status500InternalServerError, response)`

## Dependencies
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — new constants.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs` — branch-specific error codes.
- `RecurringJobsController.cs` (BackgroundJobs feature folder under the API project) — HTTP status mapping.
- Existing test project for BackgroundJobs use cases (xUnit / NUnit, whichever the repo already uses) — new tests.
- No new NuGet packages.
- No frontend changes required for this specification, but the frontend should be informed so it can optionally surface differentiated UX in a follow-up.

## Out of Scope
- Reworking the response DTO (`TriggerRecurringJobResponse`) shape or field names.
- Adding retry logic for the enqueue-failure case.
- Auditing other handlers in BackgroundJobs (or elsewhere) for similar error-code conflation — that is a candidate follow-up task but is not part of this change.
- Changes to the recurring-job registration, scheduling, or disabling flow.
- Localization of error messages.
- Frontend UX changes that distinguish 409 vs 500 vs 404 in the UI.
- Updating any external documentation portal beyond what is already auto-generated from OpenAPI.

## Open Questions
None.

## Status: COMPLETE