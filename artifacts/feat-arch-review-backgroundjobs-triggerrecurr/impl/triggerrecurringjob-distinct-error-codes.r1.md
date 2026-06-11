# Implementation: Distinct Error Codes for `TriggerRecurringJob` Failure Modes

## What was implemented

Replaced the single `RecurringJobNotFound` error code that `TriggerRecurringJobHandler` returned for all three failure conditions with three distinct codes, and fixed the controller's `TriggerJob` action to route failures through the existing attribute-based HTTP-status mapper (`BaseApiController.HandleResponse`) instead of short-circuiting to `NotFound`.

Two new `ErrorCodes` enum members were introduced:
- `RecurringJobDisabled = 1904` — `[HttpStatusCode(HttpStatusCode.Conflict)]` → HTTP 409
- `RecurringJobEnqueueFailed = 1905` — `[HttpStatusCode(HttpStatusCode.InternalServerError)]` → HTTP 500

`RecurringJobNotFound = 1901` is now restricted to its literal meaning (job not registered).

An incidental requirement surfaced during verification: the existing `LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes` test enforces Czech translations for every `ErrorCodes` value. Czech translations for the two new codes were added to `frontend/src/i18n.ts` to satisfy that test.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `RecurringJobDisabled = 1904` and `RecurringJobEnqueueFailed = 1905` with `[HttpStatusCode]` attributes in the BackgroundJobs section
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs` — changed the "disabled" and "enqueue null" branches to return the correct new codes; augmented all three failure-branch logs with `ErrorCode` as a structured property
- `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` — replaced `return NotFound(response)` with `return HandleResponse(response)` in `TriggerJob`; added `[ProducesResponseType(409)]` and `[ProducesResponseType(500)]` decorations
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs` — **new**: pure unit tests covering all three handler failure branches
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs` — renamed the inaccurate `TriggerJob_WhenTriggerFails_ShouldReturnBadRequest` test; added `TriggerJob_WhenJobIsDisabled_ShouldReturn409Conflict` and `TriggerJob_WhenEnqueueFails_ShouldReturn500InternalServerError`
- `frontend/src/i18n.ts` — added Czech translations for `RecurringJobDisabled` and `RecurringJobEnqueueFailed` (required by `LocalizationCoverageTests`)

## Tests

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs` — 3 unit tests:
  - `Handle_WhenJobIsNotRegistered_ReturnsRecurringJobNotFound`
  - `Handle_WhenJobIsDisabledAndForceDisabledIsFalse_ReturnsRecurringJobDisabled`
  - `Handle_WhenEnqueuerReturnsNull_ReturnsRecurringJobEnqueueFailed`

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs` — 6 tests (3 existing + 3 new/updated):
  - `TriggerJob_WithValidJobName_ShouldReturnAcceptedWithSuccessResponse` (unchanged)
  - `TriggerJob_WithNonExistentJobName_ShouldReturnNotFound` (unchanged — 404 regression)
  - `TriggerJob_WhenUpdateFailedErrorReturned_ShouldReturn500` (renamed from inaccurate test)
  - `TriggerJob_WhenJobIsDisabled_ShouldReturn409Conflict` (new)
  - `TriggerJob_WhenEnqueueFails_ShouldReturn500InternalServerError` (new)
  - `TriggerJob_ShouldPassCancellationTokenToMediator` (unchanged)

Full suite result: **4068 passed, 0 failed, 3 skipped** (skips are pre-existing integration tests requiring live external services).

## How to verify

```bash
# From the worktree root
dotnet build Anela.Heblo.sln

# BackgroundJobs tests only (fast)
dotnet test Anela.Heblo.sln --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs" 

# Full suite
dotnet test Anela.Heblo.sln --no-build

# Audit: RecurringJobNotFound should only appear in TriggerRecurringJobHandler's not-registered branch
grep -n "ErrorCodes.RecurringJobNotFound" \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs
# Expected: lines 41 (log param) and 43 (return) — both in the job == null block
```

## Notes

**Spec amendments applied (per arch-review.r1.md):**
- Error codes are `1904` and `1905`, not `1903` and `1904` as the original spec proposed — `1903` was already `InvalidCronExpression`.
- Controller uses `HandleResponse(response)` (attribute-based mapping) instead of a `switch (response.ErrorCode)` block — consistent with `GetRecurringJobs`, `UpdateJobStatus`, and `UpdateJobCron` in the same controller.
- Success path preserved as `Accepted(response)` → HTTP 202, not `200 OK` (spec had an error in the success description).

**Follow-up items (out of scope per spec, flagged in PR):**
1. `RecurringJobsController.TriggerJob` does not bind `ForceDisabled` from the HTTP request. The new `RecurringJobDisabled` (1904) code is only reachable via in-process callers (unit tests, direct `IMediator.Send`) until a `?forceDisabled=true` query parameter is added to the action.
2. `TriggerRecurringJobRequest.ForceDisabled` is semantically inverted — the name implies "make it disabled" but it actually means "force trigger even when disabled". Consider renaming to `ForceRun` or `IgnoreDisabledState` in a follow-up.

**Frontend i18n:** The `LocalizationCoverageTests` test enforces Czech translations for every `ErrorCodes` value. Adding new enum members without translations causes a test failure. This means frontend i18n updates for new error codes are implicitly required by the test suite for any future error code additions.

## PR Summary

Replaces the single `RecurringJobNotFound` (1901) error code that `TriggerRecurringJobHandler` returned for all three failure conditions with three semantically correct codes:

- **Not registered** → `RecurringJobNotFound` (1901) → HTTP 404 (unchanged)
- **Registered but disabled** → `RecurringJobDisabled` (1904) → HTTP 409 Conflict (new)
- **Enqueue returned null** → `RecurringJobEnqueueFailed` (1905) → HTTP 500 (new)

The controller's `TriggerJob` action was the only endpoint in `RecurringJobsController` bypassing `BaseApiController.HandleResponse`. It now uses `HandleResponse` for the failure path, consistent with the three sibling actions.

**Breaking behavior change:** Clients relying on `404` for any `TriggerJob` failure will now receive `409` for disabled jobs and `500` for enqueue failures. The `404` is preserved only for genuinely unknown job IDs.

**Follow-up ticket required:** `TriggerJob` does not bind `ForceDisabled` from the HTTP request, so `RecurringJobDisabled` (1904) is not reachable via HTTP until `[FromQuery] bool forceDisabled = false` is added to the action signature.

### Changes
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — new `RecurringJobDisabled = 1904` and `RecurringJobEnqueueFailed = 1905` with `[HttpStatusCode]` attributes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs` — corrected two failure branches; all three branches now log `ErrorCode` as structured property
- `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` — `return NotFound(response)` → `return HandleResponse(response)`; added `[ProducesResponseType(409)]` and `[ProducesResponseType(500)]`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs` — new: 3 handler unit tests (all three failure branches)
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs` — renamed inaccurate test; added 2 new controller-level tests (409 and 500 mapping)
- `frontend/src/i18n.ts` — Czech translations for the two new error codes (required by `LocalizationCoverageTests`)

## Status
DONE
