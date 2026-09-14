# Specification: Stop exception swallowing in PackingMaterials daily-consumption handlers

## Summary

`ProcessDailyConsumptionHandler` and `GetDailyConsumptionBreakdownHandler` in the PackingMaterials module each wrap their entire body in a broad `catch (Exception ex)` that logs the error and returns a `Success = false` DTO instead of letting the exception propagate. For the Hangfire-triggered `ProcessDailyConsumptionHandler`, this means `DailyConsumptionJob` sees a normal (non-throwing) MediatR response, logs a warning, and returns — so Hangfire records the job run as **Succeeded** even though processing failed, and no retry is ever scheduled. For the query-side `GetDailyConsumptionBreakdownHandler`, the same pattern causes the API to return HTTP 200 with `Success = false` instead of a 500, hiding infrastructure failures from callers and monitoring. This spec defines the fix: let unexpected exceptions propagate out of both handlers, and narrow each handler's own explicit `Success = false` branches to true business-outcome cases only.

## Background

`DailyConsumptionJob` (`Application/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJob.cs`) is a Hangfire recurring job that sends `ProcessDailyConsumptionRequest` via MediatR and relies on the **Hangfire retry contract**: an unhandled exception thrown out of `ExecuteAsync` is caught by Hangfire, the run is marked Failed, and Hangfire's automatic retry policy kicks in. The job's own `try/catch` (lines 45-69) already re-throws for exactly this reason — but it only ever sees exceptions that escape the MediatR `Send` call. Because `ProcessDailyConsumptionHandler` catches everything internally and returns a normal (non-exceptional) response with `Success = false`, the job's `if (!result.Success)` branch only logs a warning; the outer `try/catch` never fires, and Hangfire is told the job completed successfully. Any transient failure (DB unreachable, cross-module dependency call failing, etc.) inside `IConsumptionCalculationService.ProcessDailyConsumptionAsync` is therefore silently absorbed: no retry, and the day's consumption data is never recalculated unless someone happens to notice the warning log.

`GetDailyConsumptionBreakdownHandler` is invoked synchronously from `PackingMaterialsController.GetDailyConsumptionBreakdown` (an HTTP GET). Its identical catch-and-return-false pattern means a repository/infrastructure failure surfaces to the API consumer as `200 OK` with `Success: false, Error: "..."` rather than a `500` — indistinguishable, on the wire, from a legitimate empty/edge-case result unless the caller specifically checks `Success`.

Both handlers already have one or more genuine, non-exceptional "false" business outcomes:
- `ProcessDailyConsumptionHandler` returns `Success = false` when `!result.WasRun` (already processed for that date) — this is an idempotency guard, not a failure, and must keep returning normally (not throw).
- `GetDailyConsumptionBreakdownHandler` has no equivalent "expected false" business case in its current code — the empty-list case at line 32-33 already returns `Success = true` with empty groups.

## Functional Requirements

### FR-1: `ProcessDailyConsumptionHandler` propagates unexpected exceptions

Remove the outer `catch (Exception ex)` block (lines 55-66) from `ProcessDailyConsumptionHandler.Handle`. Any exception thrown by `_consumptionService.ProcessDailyConsumptionAsync` (or elsewhere in the try block) must propagate out of the handler unhandled by this method.

The existing `!result.WasRun` branch (lines 30-39), which returns `Success = false` for the legitimate "already processed today" case, is **not** an exception path and must be left returning normally exactly as today.

**Acceptance criteria:**
- A unit test that makes `IConsumptionCalculationService.ProcessDailyConsumptionAsync` throw asserts the exception propagates out of `Handle` (is not caught and converted to a `Success = false` response).
- A unit test for the `!result.WasRun` case still asserts a normal `Success = false` response is returned (no exception, no behavior change).
- The success-path unit test (materials processed / no invoices found) is unchanged.

### FR-2: `DailyConsumptionJob` retry contract now actually engages on handler failure

No code change is required in `DailyConsumptionJob` itself — its existing `catch (Exception ex) { ...; throw; }` (lines 45, 65-69) already does the right thing once FR-1 lets exceptions reach it. This requirement exists to make the end-to-end behavior explicit and testable.

**Acceptance criteria:**
- An integration/unit test for `DailyConsumptionJob.ExecuteAsync`, with the mediator mocked to throw when handling `ProcessDailyConsumptionRequest`, asserts the exception propagates out of `ExecuteAsync` (so Hangfire will mark the run Failed and apply its retry policy).
- The existing "job disabled" skip path and the normal success/warning logging paths (lines 35-39, 54-63) are unchanged.

### FR-3: `GetDailyConsumptionBreakdownHandler` propagates unexpected exceptions

Remove the outer `catch (Exception ex)` block (lines 53-64) from `GetDailyConsumptionBreakdownHandler.Handle`. Any exception thrown while loading or grouping consumption data must propagate out of the handler.

**Acceptance criteria:**
- A unit test that makes `IPackingMaterialRepository.GetConsumptionsByDateAsync` (or `GetAllWithAllocationsAsync`) throw asserts the exception propagates out of `Handle`.
- The `GetDailyConsumptionBreakdownResponse.Error` property, if it has no other remaining caller after this change, is flagged in Open Questions rather than silently removed (see below) — this spec does not mandate removing it, only that it is no longer populated via the deleted catch block.
- The empty-consumptions early return (`consumptions.Count == 0` → `Success = true`) and the three `BuildGroupBy*` success paths are unchanged.

### FR-4: API layer surfaces unhandled exceptions as 5xx, not silently

**Acceptance criteria:**
- After FR-3, an exception thrown inside `GetDailyConsumptionBreakdownHandler` results in the ASP.NET Core pipeline's existing global exception handling producing a non-2xx response (verified by whatever integration test infrastructure the codebase already uses for controller-level error behavior — do not introduce a new exception-handling middleware as part of this change; only confirm the existing one is on the request path for this controller/action).
- If the architecture review determines no global exception handler is registered for this controller, that gap is raised as an Open Question / architect concern below rather than assumed away.

### FR-5: Logging is preserved at the call sites that still observe failure

The informational logging that surrounded the removed catch blocks (`_logger.LogInformation` before the call, and any logging still reachable on legitimate branches) is unaffected. `DailyConsumptionJob`'s own `_logger.LogError(ex, ...)` in its `catch` block (line 67) remains the log record for a failed run, now correctly paired with an actual Hangfire-visible failure.

**Acceptance criteria:**
- No `_logger.LogError` call is removed from `DailyConsumptionJob`.
- No new logging is required in the two handlers for the exception path — the exception itself, once propagated, is expected to be logged by whatever cross-cutting logging exists further up the pipeline (Hangfire's own failure logging for the job; ASP.NET Core's for the HTTP request). If no such cross-cutting logging exists, note it in Open Questions rather than adding ad hoc logging to the handlers as part of this change.

## Non-Functional Requirements

### NFR-1: No behavior change on any success or legitimate-false path

This is a pure error-handling correctness fix. Every currently-passing test for the success paths of both handlers, and for the `!result.WasRun` idempotency path, must continue to pass unmodified (aside from the new tests added by FR-1/FR-3).

### NFR-2: Hangfire retry semantics

`DailyConsumptionJob` must, after this change, actually rely on Hangfire's configured retry policy for this job (attempt count / backoff) rather than any change being made to that policy here — this spec only restores the *path* by which Hangfire learns the run failed. Confirming or changing Hangfire's retry attempt configuration for `daily-consumption-calculation` is out of scope (see Out of Scope).

## Data Model

No changes to `ProcessDailyConsumptionResponse`, `GetDailyConsumptionBreakdownResponse`, or any domain/entity types. `ProcessDailyConsumptionResponse.Success`/`.Message` and `GetDailyConsumptionBreakdownResponse.Success`/`.Error` remain on the DTOs; only the code paths that used to populate them from a caught exception are removed. See Open Questions on whether `.Error`/exception-derived `.Message` values become dead fields.

## API / Interface Design

- `POST` action backing `ProcessDailyConsumptionHandler` (via `PackingMaterialsController.ProcessDailyConsumption`): on an unhandled exception, will now return whatever the app's existing global exception-handling middleware/filter produces (expected: 5xx), instead of `200 OK` with `Success: false`. No controller code change is anticipated; confirm during architecture review whether a change is in fact needed for this action to surface a 5xx.
- `GET` action backing `GetDailyConsumptionBreakdownHandler` (`PackingMaterialsController.GetDailyConsumptionBreakdown`, `[ProducesResponseType(typeof(GetDailyConsumptionBreakdownResponse), StatusCodes.Status200OK)]`): on an unhandled exception, will now return a 5xx via the same existing exception-handling path rather than `200 OK` with `Success: false`. The `ProducesResponseType` 200-only annotation on this action becomes inaccurate once exceptions can surface as non-200 — architect/designer to confirm whether adding a `ProducesResponseType(StatusCodes.Status500InternalServerError)` annotation is expected as part of this change or a separate concern.

## Dependencies

- Existing ASP.NET Core exception handling configured for the API host (must be confirmed to exist and apply to `PackingMaterialsController`; this spec assumes it does but does not verify it — flagged for architecture review).
- Hangfire's configured retry policy for recurring jobs (existing infrastructure; not modified by this change).
- `IConsumptionCalculationService`, `IPackingMaterialRepository` — unchanged interfaces, only how their thrown exceptions are handled by the two callers changes.

## Out of Scope

- Changing Hangfire's retry count/backoff configuration for `daily-consumption-calculation` or any other job.
- Adding new global exception-handling middleware — if one doesn't already cover this controller, that's a separate finding, not fixed by this spec.
- Any other handler in the codebase with a similar catch-and-swallow pattern outside the PackingMaterials module (the issue and this spec are scoped to PackingMaterials only, per the brief's Module field).
- Changing `ProcessDailyConsumptionResponse`/`GetDailyConsumptionBreakdownResponse` DTO shape (e.g., removing `Message`/`Error` fields) unless the architecture review determines it's required to avoid dead/misleading fields.
- Adding alerting/monitoring on top of Hangfire's failure state (e.g., a notification when a retry is exhausted) — out of scope for this fix, which only restores the existing retry contract.

## Open Questions

- **Global exception handling coverage**: Does the API host already have exception-handling middleware/filters that turn an unhandled exception from any MediatR handler into a proper 5xx response for both the recurring-job's internal `IMediator.Send` call path (irrelevant — Hangfire catches it, not ASP.NET Core) and the HTTP controller action path (relevant for FR-3/FR-4)? Architect to confirm by inspecting `Program.cs`/`Startup` configuration. Assumption for this spec: yes, standard ASP.NET Core exception handling is registered; if not, FR-4 needs an added middleware/filter as a companion change.
- **`GetDailyConsumptionBreakdownResponse.Error` field**: Once the catch block that was its only writer is removed, is `Error` a dead field that should be removed as part of this change, or kept for forward compatibility (e.g., other legitimate-but-false business outcomes that might be added later)? Assumption: keep the field (no removal) — this spec does not mandate a DTO shape change; architect/designer may recommend otherwise.
- **Any other handlers with the same pattern in this module**: The issue names exactly these two handlers. Should a broader sweep of `Application/Features/PackingMaterials/**` for the same `catch (Exception ex) { ...; return new *Response { Success = false } }` pattern be done as part of this fix, or strictly limited to the two named handlers? Assumption: strictly limited to the two named handlers (brief cites specific line ranges for both) — a broader sweep is a separate arch-review-worthy task if warranted.

## Status: HAS_QUESTIONS
