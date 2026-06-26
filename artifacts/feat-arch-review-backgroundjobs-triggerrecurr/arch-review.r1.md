I have enough context. Two critical findings drive the review: (1) the spec's proposed `1903` collides with the existing `InvalidCronExpression = 1903`, and (2) `BaseApiController.HandleResponse()` already performs error-code → HTTP-status mapping via the `[HttpStatusCode]` attribute on the enum — the bug is that `TriggerJob` is the one endpoint in this controller that doesn't use it.

# Architecture Review: Distinct Error Codes for `TriggerRecurringJob` Failure Modes

## Skip Design: true

## Architectural Fit Assessment

The fix sits inside a well-established convention. Two pillars of that convention are already in place and must be reused, not reinvented:

1. **`ErrorCodes` enum is the single source of truth for both error identity and HTTP status.** Each value is decorated with `[HttpStatusCode(HttpStatusCode.X)]` (see `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`). The pairing of "error code + HTTP status" is declared at the enum, not in the controller.
2. **`BaseApiController.HandleResponse<T>()` is the single dispatch point** (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:28-59`). It reflects the `[HttpStatusCode]` attribute and returns the matching `ActionResult`. Every other endpoint in `RecurringJobsController` already uses it (`GetRecurringJobs`, `UpdateJobStatus`, `UpdateJobCron`).

The root cause of the reported defect is therefore narrower than the spec describes:
- `TriggerRecurringJobHandler` collapses three failures onto one code (handler bug — spec is right).
- `RecurringJobsController.TriggerJob` is the **only** action in the controller that bypasses `HandleResponse()` and instead does `if (!response.Success) return NotFound(response)` (controller bug — spec proposes the wrong fix).

The spec proposes a `switch (response.ErrorCode)` block inside the controller. That diverges from the existing convention and creates a parallel mapping mechanism for one endpoint only. We must instead align `TriggerJob` with the rest of the codebase.

## Proposed Architecture

### Component Overview

```
HTTP POST /api/RecurringJobs/{jobName}/trigger
        │
        ▼
RecurringJobsController.TriggerJob
        │  (delegates failure mapping to BaseApiController.HandleResponse)
        ▼
MediatR ── TriggerRecurringJobRequest ──► TriggerRecurringJobHandler
                                                │
                ┌───────────────────────────────┼──────────────────────────────────┐
                ▼                               ▼                                  ▼
        job not registered             job found, disabled                  enqueue returned null
        ErrorCodes.RecurringJobNotFound  ErrorCodes.RecurringJobDisabled    ErrorCodes.RecurringJobEnqueueFailed
        (1901, NotFound)                 (1904, Conflict)                   (1905, InternalServerError)
                │                               │                                  │
                └───────────────┐               │               ┌──────────────────┘
                                ▼               ▼               ▼
                          BaseApiController.HandleResponse (existing)
                                          │
                                          ▼
                          HttpStatusCodeAttribute reflection ──► 404 / 409 / 500
```

### Key Design Decisions

#### Decision 1: Reuse `HandleResponse()` instead of adding a per-endpoint `switch`
**Options considered:**
- (a) Switch-on-`ErrorCode` inside `TriggerJob` (what the spec proposes).
- (b) Decorate the new enum values with `[HttpStatusCode]` and delegate failure mapping to the existing `HandleResponse()`.

**Chosen approach:** (b). Keep failure mapping in the enum + base controller. Only the success branch stays bespoke.

**Rationale:** The codebase already has one mapping mechanism. A second one in a single endpoint would (i) drift from the convention used by the three sibling actions in the same controller, (ii) duplicate logic that the reflection-based mapper already provides for free, and (iii) make adding a fourth error code a code change in the controller rather than a one-line enum addition.

#### Decision 2: Preserve `202 Accepted` on success — do not collapse to `200 OK`
**Options considered:**
- (a) Replace the body of `TriggerJob` with `return HandleResponse(response);` (would change success to `200 OK`).
- (b) Keep `Accepted(response)` for success; delegate **only** the failure branch to `HandleResponse`.

**Chosen approach:** (b).

```csharp
if (!response.Success)
{
    return HandleResponse(response);  // failure mapping via [HttpStatusCode] attribute
}
return Accepted(response);            // success unchanged
```

**Rationale:** `202 Accepted` accurately describes a "queued for asynchronous execution" outcome and is already documented (`[ProducesResponseType(typeof(...), StatusCodes.Status202Accepted)]`) and asserted (`RecurringJobsControllerTriggerTests.cs:78`). The spec text says "Success path is unchanged (HTTP 200…)" — that is factually wrong; the actual success status today is `202`. Calling this out as a spec amendment.

`HandleResponse` returns `Ok` on success, but in this code path we only ever reach it when `!response.Success`, so its success branch is never taken — no behavioral regression.

#### Decision 3: Error-code numbering — `1904` and `1905`
**Options considered:**
- (a) `1903` and `1904` (spec proposal).
- (b) `1904` and `1905`.

**Chosen approach:** (b).

**Rationale:** The spec is **factually incorrect** about the `19XX` range. `1903` is already `InvalidCronExpression` (a `BadRequest`-mapped code used by `UpdateRecurringJobCronHandler`). Adopting `1903` for `RecurringJobDisabled` would collide. Current `19XX` allocation:

| Code | Existing | 
|------|---------|
| 1901 | `RecurringJobNotFound` (NotFound) |
| 1902 | `RecurringJobUpdateFailed` (InternalServerError) |
| 1903 | `InvalidCronExpression` (BadRequest) — **already taken** |

Use the next free slots:

| Code | New | HTTP |
|------|-----|------|
| 1904 | `RecurringJobDisabled` | `Conflict` |
| 1905 | `RecurringJobEnqueueFailed` | `InternalServerError` |

#### Decision 4: HTTP status for "disabled" is `409 Conflict`
**Rationale:** Matches the spec's intent and the existing convention in this codebase, where "state precondition violated" maps to `Conflict` (e.g., `ManufactureDifficultyConflict = 1303`, `TransportBoxDuplicateActiveBoxFound = 1405`, `OrderNotInPackingState = 3001`). `422 Unprocessable Entity` is reserved for entity-level state-machine errors (`TransportBoxStateChangeError`, `ShipmentLabelsNotGenerated`) — wrong scale here.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits in three existing locations only:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Add `RecurringJobDisabled = 1904` (`Conflict`) and `RecurringJobEnqueueFailed = 1905` (`InternalServerError`) in the BackgroundJobs (19XX) section. |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs` | Change line 57 → `RecurringJobDisabled`. Change line 74 → `RecurringJobEnqueueFailed`. Line 40 stays as `RecurringJobNotFound`. |
| `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` | Replace lines 115-118 (`if (!response.Success) return NotFound(response);`) with `if (!response.Success) return HandleResponse(response);`. |

Also update the `[ProducesResponseType]` decorations on `TriggerJob` to reflect the new failure statuses:
```csharp
[ProducesResponseType(typeof(TriggerRecurringJobResponse), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status404NotFound)]            // RecurringJobNotFound
[ProducesResponseType(StatusCodes.Status409Conflict)]            // RecurringJobDisabled  (new)
[ProducesResponseType(StatusCodes.Status500InternalServerError)] // RecurringJobEnqueueFailed (new)
```

Tests (existing files in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/`):
- `RecurringJobsControllerTriggerTests.cs` — update the test currently named `TriggerJob_WhenTriggerFails_ShouldReturnBadRequest` (which actually asserts `NotFoundObjectResult`; the test name is already inconsistent). After the fix, `RecurringJobUpdateFailed` will map to `500`, not `404`. Add new tests for `RecurringJobDisabled → 409` and `RecurringJobEnqueueFailed → 500`.
- `TriggerRecurringJobHandlerIntegrationTests.cs` — add handler-level tests asserting each error branch returns the right `ErrorCode` (the file currently has only happy-path coverage).

### Interfaces and Contracts

No contract changes. `TriggerRecurringJobResponse` shape is unchanged. The only externally visible diff is:
- `ErrorCode` field can now also carry `1904` or `1905`.
- HTTP status for the "disabled" and "enqueue-failed" paths changes from `404` to `409` / `500`.

The OpenAPI-generated TypeScript client will pick up `1904` / `1905` automatically if `ErrorCodes` is exported via the spec; if not, frontend gets the integer in the body and the existing decoder still works.

### Data Flow

```
1. POST /api/RecurringJobs/{jobName}/trigger
2. Controller builds TriggerRecurringJobRequest { JobName = jobName }
3. MediatR dispatches to TriggerRecurringJobHandler
4. Handler picks the failure branch (or success):
     - not registered      → response.ErrorCode = 1901
     - registered+disabled → response.ErrorCode = 1904
     - enqueue returned null → response.ErrorCode = 1905
     - success             → response.Success = true, JobId set
5. Controller:
     - if !Success → BaseApiController.HandleResponse(response)
                       → reflects [HttpStatusCode] on the enum value
                       → returns NotFound / Conflict / StatusCode(500)
     - else → Accepted(response)  // HTTP 202
6. Each handler failure branch logs at its current severity (Warning/Warning/Error)
   with the new ErrorCode included in structured-log scope.
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test `TriggerJob_WhenTriggerFails_ShouldReturnBadRequest` asserts `NotFoundObjectResult` for `RecurringJobUpdateFailed`; after the fix it will be `500`. | Medium | Update the test as part of the change, rename for accuracy. Test was previously masking the bug. |
| Consumer of the endpoint that relied on `404` for any failure will see new `409` / `500` for disabled / enqueue-failed cases. | Medium (intentional). | Call out in PR description and release notes. Frontend currently doesn't differentiate — no UX regression. |
| Controller never binds `ForceDisabled` from the request — the "disabled" failure branch is unreachable through the API today. | High (latent). | Out of scope per the spec, but **must be flagged** in the PR as a follow-up. Without binding `ForceDisabled`, the new `RecurringJobDisabled` code is only reachable via direct `IMediator.Send` from server code or tests. The new error code is still correct to introduce, but a follow-up should add `[FromQuery] bool forceDisabled = false` to the controller signature. |
| Adding a `switch` in the controller (spec's proposal) would silently bypass the convention used by sibling endpoints, increasing maintenance risk. | High (design). | Rejected — see Decision 1. Use `HandleResponse()`. |
| `1903` collision with `InvalidCronExpression`. | Critical (build break / runtime mis-mapping). | Use `1904` and `1905` — see Decision 3. |

## Specification Amendments

The spec must be updated before implementation:

1. **FR-1 (`RecurringJobDisabled`): change value from `1903` to `1904`.** The proposed `1903` collides with the existing `InvalidCronExpression = 1903`. The spec's Data Model note ("`1902` is intentionally skipped to leave room…") is factually wrong: `1902` is `RecurringJobUpdateFailed` and `1903` is `InvalidCronExpression`. There is no gap to fill in the 1900 range; the next free codes are 1904 and 1905.

2. **FR-2 (`RecurringJobEnqueueFailed`): change value from `1904` to `1905`.**

3. **FR-4 (HTTP status mapping):** Drop the per-endpoint `switch (response.ErrorCode)` design. Instead:
   - Annotate the two new enum values with `[HttpStatusCode(HttpStatusCode.Conflict)]` and `[HttpStatusCode(HttpStatusCode.InternalServerError)]` respectively.
   - In `TriggerJob`, replace the failure mapping with `return HandleResponse(response);`. The existing reflection-based mapper handles all three statuses (`404` / `409` / `500`) and the default fallback. This matches `GetRecurringJobs`, `UpdateJobStatus`, and `UpdateJobCron` in the same controller.

4. **Response — success:** correct the spec from "HTTP `200 OK`" to **`202 Accepted`**, matching the current behavior and the controller's `[ProducesResponseType(..., Status202Accepted)]` decoration. Update the success-path text in the "API / Interface Design" section accordingly.

5. **Out of Scope / Follow-up note:** Add a follow-up reminder that `RecurringJobsController.TriggerJob` does not currently bind `ForceDisabled` from the HTTP request. The new `RecurringJobDisabled` code will only be reachable via in-process callers until that binding is added. (Spec already lists "Changes to the recurring-job registration, scheduling, or disabling flow" as out of scope, but the missing binding should be explicitly flagged for a separate ticket.)

6. **FR-5 (Logging continuity):** clarify "include the new error code in structured logging properties" — the existing handler already logs warnings/errors; the practical addition is including `ErrorCode = <int>` in the structured payload (e.g., via `using (_logger.BeginScope(...))` or a templated property). No new log-message text required.

## Prerequisites

None. No migrations, no infrastructure, no config, no new NuGet references. All changes are local to three source files and two test files in the BackgroundJobs vertical slice.