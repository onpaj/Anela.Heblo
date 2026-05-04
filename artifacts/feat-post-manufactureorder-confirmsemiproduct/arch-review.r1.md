Now I have enough context. Producing the architecture review.

# Architecture Review: Differentiate Failure Modes in `POST /api/ManufactureOrder/{id}/confirm-semi-product`

## Architectural Fit Assessment

The feature aligns with the codebase's existing **vertical slice + `BaseResponse` + `ErrorCodes` + `HttpStatusCodeAttribute`** pattern. The fix is, fundamentally, retiring a one-off departure from that pattern: the controller method bypasses `BaseApiController.HandleResponse` (which other endpoints use) and hand-rolls a `BadRequest` for every failure regardless of error type.

**Three integration points exist and are non-negotiable:**

1. **`BaseApiController.HandleResponse<T>`** (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:28`) — reflectively reads `[HttpStatusCode(...)]` off `ErrorCodes` to pick a status. This is the canonical mapping used by 20+ endpoints. **Use it.**
2. **`ErrorCodes` enum + `HttpStatusCodeAttribute`** (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`) — already defines `FlexiApiError = 9002 → ServiceUnavailable (503)` and `BusinessRuleViolation = 0007 → BadRequest (400)`. Conventions encoded here apply project-wide.
3. **`SubmitManufactureHandler`** (`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs:70-77`) — already catches every `Exception` (except `OperationCanceledException`) and returns `SubmitManufactureResponse(ex)`, which sets `ErrorCode = ErrorCodes.Exception`. **The "FlexiBee throws into the workflow's outer catch" path the spec hypothesizes is essentially never taken today.** The actual bug is that step-2 soft failures collapse `ErrorCode = Exception` (→ 500) into `ErrorCode = InvalidOperation` (→ 400) inside the workflow.

**Spec ↔ codebase conflict — must resolve before implementation:**
- Spec requests **502** for FlexiBee failures and **422** for business rule failures.
- Existing convention is **503** (`FlexiApiError`) and **400** (`BusinessRuleViolation`).
- Changing the enum attributes would silently re-map every other endpoint that uses these codes. Out of scope.
- **Recommendation:** Honor existing convention (503/400), or — if the PM explicitly wants 422/502 for *this endpoint only* — introduce **scoped error codes** (e.g. `ConfirmSemiProductBusinessRuleFailure = 1215 → 422`, keep `FlexiApiError → 503`). Do **not** mutate the existing global attributes. See Decision 2 and Spec Amendments.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│  ManufactureOrderController.ConfirmSemiProductManufacture          │
│  (returns ActionResult via HandleResponse; logs FailureCategory)   │
└────────────────────────────┬───────────────────────────────────────┘
                             │ ManufactureOrderApplicationService
                             ▼
┌────────────────────────────────────────────────────────────────────┐
│  ConfirmSemiProductManufactureWorkflow.ExecuteAsync                │
│   – classifies outcome                                             │
│   – produces ConfirmSemiProductManufactureResult with ErrorCode    │
│   – emits structured logs                                          │
└──┬────────────────┬─────────────────────┬──────────────────────────┘
   │ Step 1         │ Step 2              │ Step 3
   ▼                ▼                     ▼
UpdateManufacture   SubmitManufacture     UpdateManufactureOrderStatus
OrderHandler        Handler  (catches     Handler
(BaseResponse)      every ex; wraps as    (BaseResponse;
                    SubmitManufacture     persists
                    Response with         ManualActionRequired flag)
                    ErrorCode.Exception)
                            │
                            ▼
              FlexiManufactureClient (throws FlexiManufactureException
              with OperationKind discriminator on ERP-side failures)
```

### Key Design Decisions

#### Decision 1: Outcome modeling — `ErrorCode` propagation, not a parallel enum

**Options considered:**
- **A.** Introduce `ConfirmSemiProductManufactureOutcome { Succeeded, SucceededWithManualErpAction, BusinessRuleFailure, InfrastructureFailure, InfrastructureExternalFailure }` (spec proposal) and switch on it in the controller.
- **B.** Have the workflow set `ErrorCode` correctly on `ConfirmSemiProductManufactureResponse : BaseResponse` and use the existing `HandleResponse` reflective mapping. Add a single boolean `ManualErpActionRequired` to cover the soft-failure body shape on the success path.

**Chosen approach:** **B.**

**Rationale:** `ErrorCode` already encodes outcome category through its `[HttpStatusCode]` attribute. A parallel enum duplicates that information, requires a new `switch` block in the controller, and creates two-source-of-truth drift the next time a code is added. The codebase has 20+ controllers using `HandleResponse` — this endpoint should look like its peers, not introduce a bespoke pattern. The only outcome that can't be expressed by an `ErrorCode` is *"DB committed but ERP needs manual reconciliation"* (Success = true + flag), which warrants exactly one boolean field.

#### Decision 2: Status code policy — match platform convention, not spec

**Options considered:**
- **A.** Change `FlexiApiError` from 503 → 502 globally and `BusinessRuleViolation` from 400 → 422 globally.
- **B.** Add new module-scoped error codes (`ConfirmSemiProductBusinessRuleFailure → 422`, `ConfirmSemiProductErpFailure → 502`).
- **C.** Use existing codes (`FlexiApiError → 503`, an existing 422 like `TransportBoxStateChangeError` — wrong domain — or a new manufacturing 422 code, plus `BusinessRuleViolation → 400` for non-state-transition rule failures).

**Chosen approach:** **C** (with one new manufacturing-scoped 422 code if needed).

**Rationale:** A is a breaking change for every existing consumer of those codes — the spec explicitly calls infrastructure error 502 vs. 503 a *diagnostic* nuance, not a contract requirement, so the cost of breaking 20 other endpoints to satisfy preference isn't justified. B fragments the taxonomy — every domain would need its own copy. C uses existing platform-wide signals (5xx ⇒ infra; 4xx ⇒ client) and adds ≤ 1 new error code (`ManufactureOrderStateTransitionInvalid → 422`) only if no existing code fits step-3 state-transition failures. **Implementation must verify with PM that 503 (vs 502) and 400 (vs 422) are acceptable** — see Spec Amendments. If not, fall back to B.

#### Decision 3: Step-2 soft failure — surface as `Success=false` with `ManualErpActionRequired=true`, HTTP **503**

**Options considered:**
- **A.** Spec proposal: HTTP 200, `Success = true`, new flag `ManualErpActionRequired = true`. Preserves silent today.
- **B.** HTTP 503, `Success = false`, `ErrorCode = FlexiApiError`, new flag `ManualErpActionRequired = true` so the FE knows the row was persisted.

**Chosen approach:** **B.**

**Rationale:** The whole point of this work (per `brief.md`) is that on-call missed the FlexiBee `Canceled` spike because it was a 4xx, not a 5xx. Returning 200 for the most operationally important failure case fully defeats the purpose. The DB write is real, and the FE needs to know it succeeded — that's what the flag is for. But the *primary* operation (ERP submission) failed, so `Success = false` is correct, and 503 lets the existing infra-failure dashboard pick it up. Frontend treats `ErrorCode + ManualErpActionRequired` as an actionable banner instead of a full error toast. This **diverges from the spec** — see Spec Amendments.

#### Decision 4: Distinguishing infrastructure exceptions in the outer catch

**Options considered:**
- **A.** Introduce an `IsExternalServiceException(Exception)` helper inspecting `FlexiManufactureException`, `HttpRequestException`, `TaskCanceledException`, etc.
- **B.** Catch `FlexiManufactureException` separately, fall through to a generic `catch (Exception)` for everything else.
- **C.** Don't bother — `SubmitManufactureHandler` already converts every external exception to a `Success=false` response with `ErrorCode.Exception`. The outer catch is purely defensive against truly unexpected exceptions (DB connection lost, MediatR pipeline crash). Map outer-catch results to `InternalServerError` (500) flatly.

**Chosen approach:** **C** — with **B** as a one-line guard for the rare bypass case.

**Rationale:** The existing `SubmitManufactureHandler` pattern means the outer catch in `ConfirmSemiProductManufactureWorkflow` is dead-code-adjacent for FlexiBee. Building a taxonomy helper to detect FlexiBee exceptions there is solving a problem we don't have. We *do* care about `FlexiManufactureException` if it ever escapes (e.g. thrown during step-1 or step-3 handlers if they ever invoke FlexiBee). A `catch (FlexiManufactureException ex)` clause above the generic catch costs nothing and gives accurate `ErrorCode = FlexiApiError`. Everything else is `InternalServerError`.

#### Decision 5: App Insights custom dimensions — one helper, reused

**Options considered:**
- **A.** Set `Activity.Current?.AddTag("FailureCategory", value)` directly in the controller.
- **B.** `HttpContext.Features.Get<RequestTelemetry>()?.Properties["FailureCategory"] = value` (App Insights SDK).
- **C.** Wrap in a small helper (`ITelemetryEnricher.SetFailureCategory(string)`) so future endpoints reuse it.

**Chosen approach:** **A** behind a thin static helper.

**Rationale:** `Activity.Current` is the .NET-native, OpenTelemetry-portable mechanism — it propagates to App Insights via the existing telemetry pipeline without an explicit `RequestTelemetry` dependency. A static `RequestTelemetryEnricher.SetFailureCategory(string)` (or simpler: an extension method on `HttpContext`) keeps the call site in the controller terse and gives a single place to change if telemetry plumbing evolves. No DI, no new interface — the spec already says "no new logging infrastructure."

## Implementation Guidance

### Directory / Module Structure

All changes are **inside the existing `Anela.Heblo.Application/Features/Manufacture/`** vertical slice — no new modules.

**Files to modify:**

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmSemiProductManufactureResult.cs` — extend with `ErrorCode`, `Params`, `ManualErpActionRequired`, `FailingStep` (logging only), `CausedBy` (logging only, never serialized).
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmSemiProductManufactureResponse.cs` — add `bool ManualErpActionRequired { get; set; }` (default `false`, additive — non-breaking for OpenAPI).
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` — replace internal classification logic, add structured logging at every decision point, propagate sub-step `ErrorCode` and `Params`.
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs:106-139` — replace bespoke `BadRequest`/`Ok`/`StatusCode(500)` block with a single `HandleResponse(response)` call plus the special-case for `ManualErpActionRequired` rendering and a controller-level entry log + `Activity.Current?.AddTag` call.

**Files to create:**

- `backend/src/Anela.Heblo.API/Telemetry/RequestTelemetryExtensions.cs` (or similar, if no existing helper found in step 0 of implementation) — `SetFailureCategory(this HttpContext context, string category)` static helper.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs` — extend existing test file (or create) covering all four outcome branches.
- `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderController_ConfirmSemiProductTests.cs` — extend existing controller tests (or create) covering 200/200-manual/400-or-422/503-or-502.

**Files to potentially modify (verify first — see Open Question 5 in spec):**

- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `ManufactureOrderStateTransitionInvalid = 1215 [HttpStatusCode(UnprocessableEntity)]` only if PM confirms 422 is required for state-transition failures and no existing code fits.

### Interfaces and Contracts

**`ConfirmSemiProductManufactureResult`** (extend, do not replace — keep existing `Success`/`Message` constructor for callers):

```csharp
public sealed class ConfirmSemiProductManufactureResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public ErrorCodes? ErrorCode { get; init; }
    public Dictionary<string, string>? Params { get; init; }
    public bool ManualErpActionRequired { get; init; }
    public string? FailingStep { get; init; }    // for logging only
    public Exception? CausedBy { get; init; }    // for logging only — never serialized
    
    public static ConfirmSemiProductManufactureResult Succeeded(string message) => new() { Success = true, Message = message };
    public static ConfirmSemiProductManufactureResult ManualErp(string message, ErrorCodes errorCode, Dictionary<string, string>? @params)
        => new() { Success = false, Message = message, ErrorCode = errorCode, Params = @params, ManualErpActionRequired = true };
    public static ConfirmSemiProductManufactureResult BusinessRuleFailure(ErrorCodes errorCode, Dictionary<string, string>? @params, string message, string failingStep)
        => new() { Success = false, ErrorCode = errorCode, Params = @params, Message = message, FailingStep = failingStep };
    public static ConfirmSemiProductManufactureResult InfrastructureFailure(ErrorCodes errorCode, string message, Exception? cause, string? failingStep = null)
        => new() { Success = false, ErrorCode = errorCode, Message = message, CausedBy = cause, FailingStep = failingStep };
}
```

(Ignore the spec's `enum ConfirmSemiProductManufactureOutcome` — see Decision 1.)

**`ConfirmSemiProductManufactureResponse`** (additive change):

```csharp
public class ConfirmSemiProductManufactureResponse : BaseResponse
{
    public string? Message { get; set; }
    public bool ManualErpActionRequired { get; set; }   // NEW — additive, default false
    public ConfirmSemiProductManufactureResponse() : base() { }
    public ConfirmSemiProductManufactureResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

**Controller body** (target shape):

```csharp
[HttpPost("{id}/confirm-semi-product")]
public async Task<ActionResult<ConfirmSemiProductManufactureResponse>> ConfirmSemiProductManufacture(
    int id, [FromBody] ConfirmSemiProductManufactureRequest request)
{
    if (id != request.Id) return BadRequest("ID in URL does not match ID in request body.");

    Logger.LogInformation("ConfirmSemiProductManufacture entered for order {OrderId} qty {ActualQuantity}", id, request.ActualQuantity);

    var result = await _manufacturingApplicationService.ConfirmSemiProductManufactureAsync(
        request.Id, request.ActualQuantity, request.ChangeReason);

    var response = MapResultToResponse(result);
    HttpContext.SetFailureCategory(DeriveCategory(result));
    return HandleResponse(response);  // reflective HTTP-status mapping via ErrorCode attribute
}
```

The workflow's outer `try`/`catch` retains `catch (FlexiManufactureException)` (→ `FlexiApiError`) above the generic `catch (Exception)` (→ `InternalServerError`). No new helper class.

### Data Flow

**Happy path (`Succeeded`):** Controller → workflow → step1 OK → step2 OK → step3 OK → `Result(Success=true)` → `Response(Success=true, ManualErpActionRequired=false)` → `HandleResponse` → **HTTP 200**.

**ERP soft fail (most operationally important — was 400, now 503 + flag):** Controller → workflow → step1 OK → step2 returns `Success=false, ErrorCode=Exception` (FlexiBee swallowed by `SubmitManufactureHandler`) → workflow detects step-2 failure → step3 still runs (status persisted with `ManualActionRequired=true`) → workflow returns `Result.ManualErp(message, FlexiApiError, params)` → `Response(Success=false, ErrorCode=FlexiApiError, ManualErpActionRequired=true)` → `HandleResponse` reads `[HttpStatusCode(ServiceUnavailable)]` → **HTTP 503** with body `{Success: false, ErrorCode: FlexiApiError, ManualErpActionRequired: true, Message, Params}`. `Activity` tag `FailureCategory = ManualErpActionRequired`.

**Business-rule fail in step 1 or step 3:** Workflow returns `Result.BusinessRuleFailure(<sub-response.ErrorCode>, <sub-response.Params>, message, "UpdateQuantity"|"UpdateStatus")` → `Response(Success=false, ErrorCode=<original>, …)` → status determined by the original code's `[HttpStatusCode]` attribute (e.g. `OrderNotFound` → 404, `BusinessRuleViolation` → 400, future `…StateTransitionInvalid` → 422 if added).

**Outer-catch infrastructure exception (rare):** `catch (FlexiManufactureException ex)` → `Result.InfrastructureFailure(FlexiApiError, message, ex)` → 503. `catch (Exception ex)` → `Result.InfrastructureFailure(InternalServerError, message, ex)` → 500.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend assumes step-2 ERP failure is HTTP 200 (today's contract) and breaks when it sees 503 | **High** | Audit `frontend/src/api/hooks/useConfirmSemiProductManufacture.ts` and any consumer reading `response.status` before merge. Update the FE hook to treat `503 + ManualErpActionRequired=true` as a non-fatal warning banner, not a hard error. PR sequencing: FE update lands first or in same PR. |
| OpenAPI client regeneration produces a non-additive diff | Medium | Verify after build that the only diff is the new optional `ManualErpActionRequired` boolean. Add to PR test plan. |
| Other endpoints branch on `BusinessRuleViolation → 400` and would surprise if we ever switch to 422 globally | Medium | Decision 2 explicitly forbids editing the global enum attribute. Any 422 we need is a *new* code, scoped to manufacturing. |
| `Activity.Current` is null in tests using `WebApplicationFactory` without DiagnosticListener | Low | Use `?.AddTag(...)` (null-safe). Tests assert behavior via response body and HTTP code, not telemetry tags. |
| Workflow's `catch (FlexiManufactureException)` overlaps with `SubmitManufactureHandler`'s catch — duplicate logging | Low | Log at Warning in handler (existing), at Error in workflow's outer catch. Logs co-correlate via `OrderId`. |
| `UpdateManufactureOrderStatusHandler` swallows DB exceptions and returns `InternalServerError` (line 132). Workflow sees this as a sub-step `Success=false` and can't tell business-rule from infrastructure | Medium | The handler returns `ErrorCode = InternalServerError` for DB failures and `ErrorCode = ResourceNotFound` / `InvalidOperation` for business rules — propagate this `ErrorCode` to the result and let `HandleResponse` map it. The workflow does **not** need to second-guess; the sub-step's `ErrorCode` is authoritative. |
| Spec's HTTP 502 vs. 503 confusion will surface in PR review | Medium | Document the choice (503 = platform convention) in the PR description. Link to `ErrorCodes.cs:236-243`. |

## Specification Amendments

1. **Reject `ConfirmSemiProductManufactureOutcome` enum** (FR-1, Data Model). Use `ErrorCode` propagation through the existing `BaseResponse` + `HandleResponse` pattern instead. The outcome categories described in FR-1 are still the *test acceptance categories* — they just don't materialize as a parallel C# enum. (Decision 1.)

2. **Use HTTP 503 for FlexiBee infrastructure failures, not 502** (FR-3). The existing `[HttpStatusCode(ServiceUnavailable)]` on `ErrorCodes.FlexiApiError` is platform-wide. Changing it would break unrelated endpoints. 5xx-level alerts fire identically on 502 and 503. If PM insists on 502, add a *new* manufacturing-scoped error code mapped to 502 instead of mutating the global one. (Decision 2.)

3. **Use HTTP 400 for business-rule failures unless a state-transition-specific 422 is explicitly requested** (FR-1 outcome 3). `BusinessRuleViolation → 400` is the existing convention. The transport-box module has a precedent for 422 (`TransportBoxStateChangeError`); if the manufacture state-transition case warrants the same, add a new code `ManufactureOrderStateTransitionInvalid → 422`. Otherwise stay on 400 to match the rest of the manufacturing module. (Decision 2.)

4. **Reject FR-1 outcome 2 (HTTP 200 for ERP soft-failure)**. Returning 200 for the most operationally important failure path defeats the brief's stated goal (5xx alerting). Instead: HTTP 503 with `Success = false, ErrorCode = FlexiApiError, ManualErpActionRequired = true, Message, Params`. Frontend reads `ManualErpActionRequired` to render an actionable "ERP needs reconciliation" banner instead of a generic error toast. The DB row was written and the user can navigate forward; the flag is the contract for that. (Decision 3.) **This requires PM confirmation before implementation starts** — the spec's open question 1 is exactly this.

5. **Drop `IsExternalServiceException(Exception)` helper** (FR-3 acceptance criterion). `SubmitManufactureHandler` already converts every external exception to a soft failure. The workflow's outer catch needs only a `catch (FlexiManufactureException)` clause above the generic `catch (Exception)`. No new helper. (Decision 4.)

6. **Resolve open question 6 before starting:** check `backend/src/Anela.Heblo.API/` for an existing `Activity.Current?.AddTag` / `RequestTelemetry` enrichment helper. If found, use it; if not, create a single `HttpContextExtensions.SetFailureCategory(string)` next to the controller. (Decision 5.)

## Prerequisites

1. **PM sign-off on Spec Amendments 2, 3, and 4** — these are contract decisions that affect the FE contract surface and observability targets. Implementation cannot proceed until 4 (return code for ERP soft fail) is decided.
2. **Frontend hook audit** — confirm `frontend/src/api/hooks/useConfirmSemiProductManufactureMutation*` (or equivalent generated client wrapper) doesn't branch on `response.status === 400`. If it does, schedule the FE update in the same PR.
3. **OpenAPI regeneration sanity check** — verify the build pipeline regenerates the TypeScript client and produces an additive-only diff (just `manualErpActionRequired?: boolean`).
4. **No DB migration, no config change, no infra change.** This is a purely application-layer fix.
5. **Test data:** existing manufacturing E2E fixtures cover the happy path. Adding a unit/integration test that simulates `FlexiManufactureException` from `SubmitManufactureHandler` requires no new infrastructure — the handler is mockable via `IManufactureClient`.