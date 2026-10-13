# Architecture Review: Stop exception swallowing in PackingMaterials daily-consumption handlers

## Skip Design: true

Backend-only error-handling correctness fix. No new or changed UI components, screens, or visual design decisions — nothing for a designer to do here.

## Architectural Fit Assessment

This fits cleanly into the codebase's existing, already-built error-handling architecture — the fix is to stop bypassing that architecture, not to build new machinery.

Confirmed by reading the code (not assumed):

- **A global exception-handling pipeline already exists and already covers this controller.** `ServiceCollectionExtensions.AddCrossCuttingServices()` registers a typed `IExceptionHandler` chain — `UnauthorizedAccessExceptionHandler`, `ValidationExceptionHandler`, `ArgumentExceptionHandler` (in that order, "first match wins" per the code's own comment) — followed by `services.AddProblemDetails()`. `ApplicationBuilderExtensions.ConfigureApplicationPipeline()` wires it in with `app.UseExceptionHandler()`, positioned before `UseRouting()` so it wraps every controller action, `PackingMaterialsController` included. No `UseDeveloperExceptionPage()` and no `CustomizeProblemDetails` callback exist anywhere in the API project — so **any exception that isn't one of the three typed cases falls through to the framework's default `AddProblemDetails()` writer, which returns a generic 500 ProblemDetails body without the exception message or stack trace, in every environment (dev, staging, prod alike)**. This directly resolves the spec's Open Question: **no controller or middleware change is needed for FR-4.** Once the two handlers stop swallowing, the existing pipeline produces the correct response class on its own.
- **This also closes the spec's "does propagating leak sensitive detail" risk that the existing test's own comment worries about** (see Specification Amendments — the current `ProcessDailyConsumptionHandlerTests.Handle_ReturnsGenericError_WhenServiceThrows` test has a "Defense-in-depth: the secret must not leak" assertion on the *handler's own message*; once the handler no longer builds that message at all, there is nothing to leak — the framework's default ProblemDetails writer already guarantees no exception detail reaches the HTTP response).
- **The codebase already has a real, used convention for legitimate ("expected") business-outcome failures**: `BaseResponse.ErrorCode` (a `[HttpStatusCodeAttribute]`-annotated enum) plus `BaseApiController.HandleResponse<T>()`, which maps `ErrorCode` to the right status (400/404/401/403/503/500). Several PackingMaterials handlers/actions already use this (`UpdatePackingMaterialHandler`, `DeletePackingMaterialHandler`, etc., via `response.ErrorCode == ErrorCodes.ResourceNotFound`). **Neither `ProcessDailyConsumptionResponse` nor `GetDailyConsumptionBreakdownResponse` has an `ErrorCode` property, and the code being fixed here never used this convention even for its one genuine business-outcome false case** (`!result.WasRun`). This spec correctly does not introduce `ErrorCode` for these two responses (Out of Scope confirms no DTO shape change) — noted here only so the developer understands why `!result.WasRun` keeps returning a plain `Success = false` rather than being routed through `HandleResponse`.
- **`DailyConsumptionJob`'s re-throw is already correct and untouched.** Its own `catch (Exception ex) { _logger.LogError(...); throw; }` was written for exactly this Hangfire retry contract — it has simply never been reachable for handler-internal failures because the handler ate them first. Fixing the handler is sufficient; no job-side code changes are architecturally required.

## Proposed Architecture

### Component Overview

```
Hangfire scheduler
      │ (daily, 06:00)
      ▼
DailyConsumptionJob.ExecuteAsync()
      │ IMediator.Send(ProcessDailyConsumptionRequest)
      ▼
ProcessDailyConsumptionHandler.Handle()      <-- FR-1: remove catch, let exceptions propagate
      │ IConsumptionCalculationService.ProcessDailyConsumptionAsync()
      ▼
   (infra: DB, cross-module calls)

  ── on exception ──▶ propagates up through MediatR ──▶ DailyConsumptionJob's own
                        catch (unchanged) ──▶ throw ──▶ Hangfire marks run Failed,
                        applies its existing retry policy.                [FR-2, no code change]

HTTP GET /api/packing-materials/consumption
      ▼
PackingMaterialsController.GetDailyConsumptionBreakdown()   <-- no code change needed
      │ IMediator.Send(GetDailyConsumptionBreakdownRequest)
      ▼
GetDailyConsumptionBreakdownHandler.Handle()  <-- FR-3: remove catch, let exceptions propagate
      │ IPackingMaterialRepository.{GetConsumptionsByDateAsync, GetAllWithAllocationsAsync}
      ▼
  ── on exception ──▶ propagates out of the action ──▶ app.UseExceptionHandler()
                        (already registered, before UseRouting) ──▶ falls through the typed
                        handler chain (not Unauthorized/Validation/Argument) ──▶ default
                        AddProblemDetails() writer ──▶ HTTP 500, generic ProblemDetails body. [FR-4, no code change]
```

### Key Design Decisions

#### Decision 1: Remove the catch blocks entirely rather than narrow or rethrow them

**Options considered:**
- (a) Keep `catch (Exception ex)` but change it to log and `throw;` (rethrow) instead of returning a response.
- (b) Catch only specific expected exception types (none exist here) and let everything else propagate — i.e., delete the catch block entirely, since there is no exception type in either handler's try block that represents a legitimate, non-exceptional business outcome.
- (c) Wrap the service/repository call in a `try/catch` that maps to `ErrorCode` + rethrow-if-unmapped, formalizing the `HandleResponse` convention for these two handlers.

**Chosen approach:** (b) — delete the `catch (Exception ex)` blocks in both handlers outright. Do not add a bare `catch { throw; }`, which is redundant and invites a future edit to "helpfully" fill it back in with a swallow.

**Rationale:** Both handlers' only legitimate non-exceptional "false" branch (`!result.WasRun` in `ProcessDailyConsumptionHandler`; none in `GetDailyConsumptionBreakdownHandler`, the empty-list case already returns `Success = true`) is already handled by ordinary control flow before the try block would ever need to distinguish "expected" from "unexpected" failure — there is nothing left for a catch block to legitimately do. Option (c) is out of scope per the spec (no DTO shape changes) and would be over-engineering relative to the two-line fix this issue actually calls for; if the team later wants `ErrorCode`-based mapping for these two responses, that is a separate, explicitly-scoped change, not a byproduct of this bug fix.

#### Decision 2: No new middleware, handler, or `ProducesResponseType` annotation change

**Options considered:**
- (a) Add a `[ProducesResponseType(StatusCodes.Status500InternalServerError)]` annotation to both controller actions to document the now-possible 5xx.
- (b) Leave the `ProducesResponseType` annotations as-is.

**Chosen approach:** (b), with the annotation update flagged as an optional, low-priority follow-up rather than part of this fix.

**Rationale:** `ProducesResponseType` is Swagger/OpenAPI documentation metadata only — it has no effect on runtime behavior, and every other controller action in this codebase that can fail unexpectedly (i.e., essentially all of them, since ASP.NET Core can always throw) does not document a 500 either. Adding it here alone, for only these two actions, would be inconsistent with the rest of the codebase's documentation conventions and is not something this bug-fix-scoped issue asked for. This spec's own "Out of Scope" section already leaves DTO/contract changes out; treat the annotation the same way.

## Implementation Guidance

### Directory / Module Structure

No new files for production code — this is a targeted edit to two existing files:
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs` — remove lines 55-66 (the `catch (Exception ex) { ... }` block); the `try` becomes unnecessary too and should be removed along with it (an unmatched bare `try` with no `catch`/`finally` is not valid C# and not desired here — just remove `try`/`catch` and dedent the body).
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs` — same shape of change, removing its lines 53-64 `catch` block and the now-unnecessary `try`.

New test file needed for FR-2 (none exists today):
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs` — follow the sibling convention already established for other recurring jobs, e.g. `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJobTests.cs` (mock `IMediator`, `ILogger<DailyConsumptionJob>`, `IRecurringJobStatusChecker`; assert `ExecuteAsync` rethrows when `_mediator.Send` throws).

Existing tests to modify in place (not merely add alongside — see Specification Amendments below for why both currently assert the pre-fix behavior):
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs` — `Handle_ReturnsGenericError_WhenServiceThrows`.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs` — `GroupBy_OutOfRangeEnumValue_ReturnsFailureResponse`.

### Interfaces and Contracts

No interface or contract changes. `IConsumptionCalculationService`, `IPackingMaterialRepository`, `ProcessDailyConsumptionResponse`, `GetDailyConsumptionBreakdownResponse`, `IRecurringJob`/`IRecurringJobStatusChecker` are all unchanged. The only behavioral contract change is implicit: both `Handle()` methods may now throw instead of always returning a value — this must be called out in each handler's XML doc / class summary if one exists (neither currently has doc comments on `Handle`, so none need updating), and must be understood by any *other* caller of these two MediatR requests before merging. A repo-wide search for other `IMediator.Send(new ProcessDailyConsumptionRequest...)` / `...GetDailyConsumptionBreakdownRequest...` call sites should be done by the developer to confirm `DailyConsumptionJob` and `PackingMaterialsController` are the only two callers (this review did not find others, but the developer should re-confirm at implementation time since new call sites could have been added since this review).

### Data Flow

See Component Overview above. Two independent flows, no shared code path other than both terminating in an already-existing, already-correct piece of infrastructure (Hangfire's retry engine for the job; `app.UseExceptionHandler()` + `AddProblemDetails()` for the HTTP action) — this fix's entire job is to stop preventing that infrastructure from ever seeing the failure.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Two existing unit tests currently assert the *old* (swallow) behavior and will fail once the catch blocks are removed, if not updated in the same change | Medium | Explicitly listed above and in Specification Amendments; developer must update both tests as part of this same PR, not leave them red or delete them without replacement. |
| `GetDailyConsumptionBreakdownHandler`'s switch-default `ArgumentOutOfRangeException` (`(ConsumptionGroupBy)99` case) will now propagate; it happens to be an `ArgumentException` subclass so the existing `ArgumentExceptionHandler` maps it to 400 automatically — but this is a incidental catch, not a designed-for one | Low | No code change required (behavior is correct: an invalid enum value really is a bad request), but call this out explicitly in the PR description / test so a future reader doesn't mistake the 400 for evidence the try/catch removal was incomplete. Covered by the rewritten `GroupBy_OutOfRangeEnumValue_ReturnsFailureResponse` test asserting the exception propagates from `Handle` (the ASP.NET Core-level 400 mapping is `ArgumentExceptionHandler`'s existing, separately-tested responsibility, not this handler's). |
| Some other, not-yet-found caller of either MediatR request could depend on `Success = false` never throwing | Low | Developer to grep for other call sites before implementing (see Interfaces and Contracts); none found during this review's exploration of `PackingMaterialsController.cs` and `DailyConsumptionJob.cs`. |
| Hangfire retry policy for `daily-consumption-calculation` may not be configured with a sensible retry count/backoff, so "retries now actually happen" could surface a previously-invisible gap | Low | Out of scope per spec (NFR-2); flag as a possible fast-follow issue if the developer notices the job has no `[AutomaticRetry]`-equivalent configuration when implementing, but do not change it as part of this fix. |

## Specification Amendments

1. **Correct an inaccurate factual claim in the brief/spec about `GetDailyConsumptionBreakdownHandler`'s current HTTP behavior.** The brief states failures "return HTTP 200 with `Success = false`". Reading `PackingMaterialsController.GetDailyConsumptionBreakdown` (line 161) shows the action already does `return response.Success ? Ok(response) : BadRequest(new { error = response.Error });` — **today's actual behavior on failure is HTTP 400, not 200.** The underlying architectural problem the issue is about still fully applies and is not weakened by this correction: an *infrastructure* failure (DB unreachable, etc.) is being mapped to a *client* error status (400 — "your request was malformed") instead of a *server* error status (500 — "we failed"), which is arguably a worse signal to API consumers than the brief's 200 claim, not a better one. `ProcessDailyConsumptionHandler`'s consumer (`PackingMaterialsController.ProcessDailyConsumption`, line 143: `return Ok(response);` unconditionally) *does* match the brief exactly — that half of the brief is accurate as written. FR-3/FR-4 in the spec remain correct as written; only the factual justification in the Background/FR-4 text should note the actual current status is 400, not 200, for the query handler.
2. **Two existing unit tests assert the pre-fix (swallow) behavior and must be changed, not just supplemented, as part of this fix — this is a stronger requirement than the spec's FR-1/FR-3 acceptance criteria currently state** (which only say "a unit test... asserts the exception propagates," without flagging that this means *editing* an existing, currently-passing test rather than only adding a new one):
   - `ProcessDailyConsumptionHandlerTests.Handle_ReturnsGenericError_WhenServiceThrows` (`backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`, lines 108-137) currently asserts `response.Success` is `false`, a generic message, and — via its "Defense-in-depth" comment — that the handler's own message never contains the thrown exception's text. Once the catch is removed, this test must be rewritten to assert `await sut.Handle(...)` throws the same exception instance (e.g. `await FluentActions.Invoking(() => sut.Handle(request, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()` per the file's existing FluentAssertions/xUnit/Moq style), and the log-was-called assertion (`VerifyErrorLogged`) should be dropped or repurposed since the handler no longer logs on this path (nothing further needs to log it here — Hangfire/ASP.NET Core's own failure paths are responsible for surfacing it further up, per spec FR-5).
   - `GetDailyConsumptionBreakdownHandlerTests.GroupBy_OutOfRangeEnumValue_ReturnsFailureResponse` (`backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs`, lines 165-185) currently asserts `response.Success` is `false` and `response.Error` is non-null when `GroupBy` is an out-of-range enum cast. This must be rewritten to assert `Handle` throws `ArgumentOutOfRangeException` instead.
3. **FR-4's acceptance criteria can be marked satisfied by inspection/existing infrastructure, not new test infrastructure**, given the confirmed global exception-handler registration above — no new integration test is required to prove a 500 status is produced; a unit-level assertion that the handler's `Handle` throws (FR-3's own criterion) is sufficient, since the HTTP-status mapping itself is `app.UseExceptionHandler()` + `AddProblemDetails()`'s already-tested responsibility, not new code introduced by this fix.

## Prerequisites

None. No migrations, no new configuration, no new infrastructure. The exception-handling pipeline, Hangfire retry engine, and test project conventions this fix relies on all already exist and are already wired up.
