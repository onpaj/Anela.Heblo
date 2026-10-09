# Architecture Review: Validate `TimeWindow` on GetProductMarginSummaryRequest

## Skip Design: true
Backend-only fix: a FluentValidation validator plus a MediatR pipeline behavior registration. No new or changed UI component, screen, or visual design decision is involved. The response contract (`BaseResponse`/`GetProductMarginSummaryResponse` with `Success=false` + `ErrorCode`) already exists and is already rendered by the frontend for sibling Analytics endpoints (confirmed: `AnalyticsController.GetMarginReport`/`GetMarginAnalysis` already return this same shape on validation failure today).

## Architectural Fit Assessment
This fits the existing Vertical Slice / MediatR pipeline convention exactly. `AnalyticsModule.AddAnalyticsModule` already registers the identical pair of `IValidator<T>` + `IPipelineBehavior<T, TResponse>` for two sibling requests in the same module (`GetMarginReportRequest`, `GetProductMarginAnalysisRequest`, lines 37–44 of `AnalyticsModule.cs`), using the generic, already-implemented `ValidationResultBehavior<TRequest, TResponse>` (`backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs`). `GetProductMarginSummaryRequest`/`GetProductMarginSummaryResponse` is the only request in this module missing that pair — this is a pure gap-fill, not a new pattern. `GetProductMarginSummaryResponse` already extends `BaseResponse` (confirmed via `ValidationResultBehavior`'s `where TResponse : BaseResponse, new()` constraint compiling against it in the suggested wiring), so no response-type change is needed.

Confirmed via source inspection:
- `GetProductMarginSummaryRequest.cs` — `TimeWindow` is `string`, default `"current-year"`, no validator attribute/registration.
- `TimeWindowParser.cs` — `ParseTimeWindow` switches on exactly 5 literals, `_ => throw new ArgumentException(...)` for anything else.
- `AnalyticsModule.cs` — no `IValidator<GetProductMarginSummaryRequest>` or `IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>` registered.
- `AnalyticsController.cs` — `GetProductMarginSummary` action already declares `[ProducesResponseType(StatusCodes.Status400BadRequest)]` and calls the shared `HandleResponse(response)` helper used by the two already-validated sibling endpoints, so the 400 path is already wired at the controller level and only needs the pipeline to actually populate `Success=false` for this request.
- `ErrorCodes.cs` — Analytics range is 17XX, last used value is `InvalidReportPeriod = 1705`; next free value is `1706`.
- `AnalyticsConstants.cs` — `ValidationMessages` nested class holds sibling message strings; `1706`'s message belongs here as a new constant.
- Existing tests `GetMarginReportRequestValidatorTests.cs` (validator-only, `FluentValidation.TestHelper`) and `Pipeline/AnalyticsValidationPipelineTests.cs` (full DI-wired MediatR pipeline integration test) are the two direct test analogues — both are straightforward to clone for this request.

## Proposed Architecture

### Component Overview
```
GetProductMarginSummaryRequest (query params)
        │
        ▼
MediatR pipeline
  ┌─────────────────────────────────────────────┐
  │ ValidationResultBehavior<Req, Resp>          │   <-- NEW registration (behavior class reused, unchanged)
  │   └─ IValidator<GetProductMarginSummaryReq>  │   <-- NEW: GetProductMarginSummaryRequestValidator
  │        RuleFor(x => x.TimeWindow)            │
  │          .Must(TimeWindowParser.IsValid)     │   <-- validator calls parser's own allowed-set, not a private copy
  └─────────────────────────────────────────────┘
        │ (valid)                     │ (invalid)
        ▼                             ▼
GetProductMarginSummaryHandler   TResponse{Success=false, ErrorCode=InvalidTimeWindow(1706), Params={timeWindow: "<value>"}}
        │                             │
        ▼                             ▼
_timeWindowParser.ParseTimeWindow   AnalyticsController.HandleResponse(response)
   (never throws now — input          → maps ErrorCode's [HttpStatusCode] attribute → 400
    already guaranteed valid)
```

### Key Design Decisions

#### Decision 1: Where does the allowed-value list live?
**Options considered:**
1. Hardcode a second copy of the five literals directly in the new validator (the issue's suggested-fix snippet does this — a `private static readonly string[] ValidTimeWindows`).
2. Extract the allowed set into a single shared source (e.g. a `public static readonly string[]` or a static `bool IsValid(string)` method on `TimeWindowParser`/`ITimeWindowParser`) that both the validator and the parser's `switch` consult.

**Chosen approach:** Option 2. Add a static allow-list to `TimeWindowParser` and have both the `switch` and the new validator read from it — do **not** duplicate the five string literals.

**Rationale:** The spec's NFR-2 explicitly flags the duplication risk in the issue's own suggested fix. Two independent lists for the same domain concept in the same module is exactly the kind of drift risk arch-review exists to catch — if a sixth time window (e.g. `last-3-months`) is added to the parser in the future without touching the validator, the validator would incorrectly reject a now-valid value (or vice versa if only the validator is updated). A single source (`TimeWindowParser.SupportedTimeWindows` as a `public static readonly IReadOnlyCollection<string>`, or equivalently a `public static bool IsSupported(string value)`) removes that risk with a ~2-line change to `TimeWindowParser.cs`, is still fully backend-only, and does not touch the request/response contract. This is a deviation from the issue's literal suggested-fix snippet, made deliberately per NFR-2 — flag this explicitly to the developer so it isn't silently "simplified back" to the duplicated version during implementation.

Concretely: keep `TimeWindowParser.ParseTimeWindow`'s `switch` exactly as-is (it's already correct and battle-tested), but add:
```csharp
public static readonly string[] SupportedTimeWindows =
    ["current-year", "current-and-previous-year", "last-6-months", "last-12-months", "last-24-months"];
```
as a `public static` field on `TimeWindowParser`, and have the validator's `.Must(...)` reference `TimeWindowParser.SupportedTimeWindows.Contains(v)`. The `switch`'s literals stay hardcoded (refactoring the switch itself to iterate the array is out of scope — surgical change only, per project convention).

#### Decision 2: New `ErrorCodes` value vs. reusing `ValidationError`
**Options considered:**
1. Let the failure fall through to the generic `ValidationError` code (no new enum member needed).
2. Add a specific `InvalidTimeWindow = 1706` code, `[HttpStatusCode(HttpStatusCode.BadRequest)]`.

**Chosen approach:** Option 2, per spec FR-3.

**Rationale:** Every other Analytics validation failure (`InvalidDateRange`, `InvalidReportPeriod`, etc.) has its own specific code — a generic fallback here would be an inconsistency the next arch-review pass would likely flag again. The cost is one enum line plus one message constant; the benefit is a response body that's actually diagnosable by API consumers (matches the issue's "user-actionable message" framing in "Why it matters").

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs` (same directory as `GetMarginReportRequestValidator.cs`).
- Modified: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — add the `SupportedTimeWindows` static field (Decision 1); no change to `ParseTimeWindow`'s existing switch or its `ArgumentException` fallback.
- Modified: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — add the two `services.AddScoped<...>` lines immediately after line 38/44 (the existing sibling registrations), same style.
- Modified: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — append `InvalidTimeWindow = 1706` under the "Analytics module errors (17XX)" block, after `InvalidReportPeriod = 1705`.
- Modified: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs` — add `INVALID_TIME_WINDOW` to `ValidationMessages`, e.g. `"Invalid time window: '{0}'. Supported values: {1}"`.
- New test file: `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginSummaryRequestValidatorTests.cs` — clone the shape of `GetMarginReportRequestValidatorTests.cs`.
- Modified test file: `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` — extend `BuildMediator` to also register the new validator/behavior pair, add a test case analogous to `GetMarginReport_InvalidDateRange_ReturnsInvalidDateRangeErrorCode`.
- Existing test file `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` should be checked for any test that currently exercises an invalid `TimeWindow` expecting an `ArgumentException` directly against the handler — if one exists, it now needs updating (the handler itself is unreachable for invalid input once the pipeline is registered; that behavior moves to the validator/pipeline test instead).

### Interfaces and Contracts
- `GetProductMarginSummaryRequestValidator : AbstractValidator<GetProductMarginSummaryRequest>` — single `RuleFor(x => x.TimeWindow)` rule, `.Must(v => TimeWindowParser.SupportedTimeWindows.Contains(v))`, `.WithErrorCode(((int)ErrorCodes.InvalidTimeWindow).ToString())`, `.WithState(x => (object)new Dictionary<string,string>{{"timeWindow", x.TimeWindow}})`, `.WithMessage(...)` — mirrors `GetMarginReportRequestValidator`'s exact idiom (error code as string, `WithState` for `Params`, `WithMessage` last).
- No change to `GetProductMarginSummaryRequest`, `GetProductMarginSummaryResponse`, `GetProductMarginSummaryHandler`, or `ITimeWindowParser`'s interface signature (`SupportedTimeWindows` is added to the concrete `TimeWindowParser` class, not the interface — the validator takes a compile-time dependency on the concrete class's static field, exactly as `AnalyticsConstants` is referenced statically elsewhere in this module; this avoids adding an unused instance-method surface to `ITimeWindowParser` for a value that has no per-instance state).

### Data Flow
Query string → model binding → `GetProductMarginSummaryRequest` → MediatR `Send` → `ValidationResultBehavior` runs the validator → invalid: short-circuits with `Success=false` response, handler and `TimeWindowParser.ParseTimeWindow` are never invoked → `AnalyticsController.HandleResponse` maps `ErrorCode.InvalidTimeWindow`'s `[HttpStatusCode(BadRequest)]` attribute to a 400 response. Valid: behavior calls `next()`, handler runs unchanged, `ParseTimeWindow` is now guaranteed to hit one of its five `switch` arms (its `ArgumentException` fallback becomes unreachable in this call path but remains as a defensive guard for any other caller of the parser).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Allowed-values list drifts between validator and parser | Medium | Decision 1: single shared static list on `TimeWindowParser`, not two copies |
| An existing handler-level test asserts `ArgumentException` is thrown for bad `TimeWindow` and breaks once the pipeline short-circuits first | Low | Explicitly check `GetProductMarginSummaryHandlerTests.cs` during implementation and update/relocate that assertion to the new validator/pipeline test |
| New `ErrorCodes` value collides with a value added concurrently by unrelated work | Low | `1706` is the immediate next free slot after `1705` in the 17XX Analytics block as of this review; developer should re-check for collisions at implementation time before committing |
| Case sensitivity / whitespace variants (e.g. `Current-Year`, trailing spaces) still slip through if `.Must` uses a case-sensitive `Contains` inconsistently with future parser changes | Low | Keep the validator's comparison exactly as case-sensitive/exact as the parser's `switch` (no `.Trim()`/`.ToLower()` normalization introduced on either side) — do not silently accept variants the parser itself would reject |

## Specification Amendments
- Spec FR-3 said "e.g. `InvalidTimeWindow`" for the new error code name and left the message wording open — this review pins the code to `InvalidTimeWindow = 1706` and gives a concrete message template (see Implementation Guidance) so the developer/planner don't need to re-derive it.
- Spec's Open Questions section framed the shared-vs-duplicated allowed-list choice as a non-blocking implementation detail either way was acceptable. This review resolves it as a firm decision (Decision 1: shared static list) rather than leaving it to developer discretion, because the duplication risk is concrete and the fix is trivial — no spec conflict, just a narrowing.
- No other amendments; FR-1, FR-2, all NFRs, Data Model, API/Interface Design, Dependencies, and Out of Scope sections are architecturally sound as written and require no changes.

## Prerequisites
None. No migrations, no config, no infrastructure changes, no new external dependencies. All referenced types (`ValidationResultBehavior<,>`, `ErrorCodes`, `AnalyticsConstants`, `BaseApiController.HandleResponse`) already exist and are already exercised by the two sibling requests in this module.
