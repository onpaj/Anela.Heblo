# Architecture Review: Remove Duplicate Validation from Analytics Margin Report Handlers

## Skip Design: true

## Architectural Fit Assessment

The intent — single-source-of-truth validation via FluentValidation — is sound and matches the convention used in `CatalogModule`, `PhotobankModule`, `CarrierCoolingModule`, `GiftSettingsModule`, etc. However, **the brief and spec contain a load-bearing factual error** that invalidates several acceptance criteria as written:

> "`GetMarginReportRequestValidator` is registered in `AnalyticsModule` … and is wired into the MediatR validation pipeline."

It is **not** wired into the pipeline. Verified in `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs:29-30`:

```csharp
services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();
```

Only `IValidator<T>` is registered. There is no `IPipelineBehavior<TRequest, TResponse>` registration for either request, and **no open-generic pipeline behavior** registered anywhere in `ApplicationModule.cs` (confirmed by exhaustive grep — only `CatalogModule`, `PhotobankModule`, etc. register `ValidationBehavior` explicitly per-request). Therefore the handler-level checks at `GetMarginReportHandler.cs:36-54` and `GetProductMarginAnalysisHandler.cs:32-42` are **not** dead code today — they are the *only* runtime enforcement of those rules. Removing them without first wiring the pipeline behavior would silently regress production behavior: invalid input would flow straight into business logic.

Additionally, even when `ValidationBehavior` runs elsewhere in the codebase, it **throws `FluentValidation.ValidationException`** (`backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationBehavior.cs:32`) — it does **not** return a `BaseResponse` envelope with `ErrorCode`. No `IExceptionHandler<ValidationException>` is registered (only `UnauthorizedAccessExceptionHandler` in `ServiceCollectionExtensions.cs:130`). So FR-3's acceptance criterion — *"Invalid requests continue to fail with the same `ErrorCodes.InvalidDateRange` / `ErrorCodes.InvalidReportPeriod` error codes when sent through the MediatR pipeline"* — **cannot be satisfied by the current infrastructure**. It would degrade to an unhandled `ValidationException` → generic 500 (or whatever the default `UseExceptionHandler` produces), losing the structured error envelope the React frontend consumes.

This is the central architectural issue. The rest of the spec is fine.

## Proposed Architecture

### Component Overview

```
HTTP request
   │
   ▼
AnalyticsController.GetMarginReport / GetProductMarginAnalysis (MVC action)
   │  IMediator.Send(request)
   ▼
MediatR pipeline
   ├── (NEW) ValidationBehavior<GetMarginReportRequest, GetMarginReportResponse>
   │           └─ runs GetMarginReportRequestValidator
   │              └─ on failure: short-circuit with BaseResponse(ErrorCode=…) — see Decision 2
   └── GetMarginReportHandler.Handle   ← validation block removed
          └─ business logic only
```

The structural change is: **add the pipeline behavior wiring** for the two requests (matching the per-request style already used in `CatalogModule.cs:113-119`), and **decide how validation failures translate to `ErrorCodes`**.

### Key Design Decisions

#### Decision 1: Wire `ValidationBehavior` per-request, matching the project convention

**Options considered:**
- (a) Per-request registration in `AnalyticsModule.cs` (matches Catalog, Photobank, CarrierCooling, GiftSettings, ShipmentLabels, Inventory).
- (b) Register `ValidationBehavior` as an open generic (`services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>))`) in `ApplicationModule.cs` for the entire app.

**Chosen approach:** (a). Add two lines to `AnalyticsModule`:

```csharp
services.AddScoped<IPipelineBehavior<GetMarginReportRequest, GetMarginReportResponse>,
                   ValidationBehavior<GetMarginReportRequest, GetMarginReportResponse>>();
services.AddScoped<IPipelineBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>,
                   ValidationBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>>();
```

**Rationale:** Option (b) would change behavior for every MediatR request in the app — many of which lack validators or rely on handler-internal error-result patterns that don't tolerate thrown `ValidationException`. The project's deliberate per-request opt-in is a safety mechanism. Stay consistent.

#### Decision 2: Translate validation failures into the `BaseResponse` envelope (not throw)

**Options considered:**
- (a) **Typed `ValidationBehavior<TRequest, TResponse> where TResponse : BaseResponse, new()`** that, on failure, returns a `TResponse` populated with `Success=false`, `ErrorCode`, and `Params`. The error code is read from FluentValidation's `WithErrorCode("…")` metadata on each rule (precedent exists at `Photobank/Validators/BulkAddPhotoTagRequestValidator.cs:21`).
- (b) Keep `ValidationBehavior` throwing, add an `IExceptionHandler<ValidationException>` that materializes the `BaseResponse` envelope and writes it to the HTTP response.
- (c) Convert `ValidationException` into the result envelope inside the controller(s).

**Chosen approach:** (a) with a **new, scoped** behavior class — call it `ValidationResultBehavior<TRequest, TResponse>` — registered only for the two Analytics requests. Leave the existing `ValidationBehavior` (throwing variant) untouched so other modules are not disturbed. The validators add `WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString())` etc. to each rule so the behavior maps the first failure to the correct enum.

**Rationale:**
- Preserves the exact response shape (`GetMarginReportResponse { Success=false, ErrorCode, Params }`) that the frontend and existing tests already depend on — meeting FR-3 and FR-6 literally.
- Avoids the cross-cutting risk of (b)/(c), which would change behavior for every other module using `ValidationBehavior`.
- Reuses an already-established project idiom (`WithErrorCode`).
- Keeps `ErrorCodes` enum as the single source of truth for error semantics.

**Trade-off:** Requires adding `WithErrorCode(...)` metadata to the two existing validators and a new ~30-line `ValidationResultBehavior<TRequest, TResponse>` class. This is a real expansion of the spec's scope but is **necessary** to satisfy FR-3 and FR-6.

#### Decision 3: Test layering

- **Validator unit tests** (new files): use FluentValidation `TestHelper` (`TestValidate` / `ShouldHaveValidationErrorFor`), following the exact pattern in `GetManufacturingStockAnalysisRequestValidatorTests.cs`. Cover happy path + each failure rule + boundary values.
- **Pipeline integration tests** (new, minimal): use `ServiceCollection`-built `IMediator` (or `WebApplicationFactory` style) to send one invalid request per handler and assert the returned `BaseResponse` carries the correct `ErrorCode`. This guards the wiring decision in Decision 1 and the translation decision in Decision 2.
- **Handler unit tests**: rewrite or delete each test in `GetMarginReportHandlerTests.cs` and `GetProductMarginAnalysisHandlerTests.cs` that currently feeds invalid input directly to `Handle()` (lines 173-232, 262-288 of `GetMarginReportHandlerTests.cs`; lines 109-152 of `GetProductMarginAnalysisHandlerTests.cs`). These are: `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_PeriodTooLong_ReturnsErrorResponse`, `Handle_ZeroDaysPeriod_ReturnsErrorResponse`, `Handle_EmptyProductId_ReturnsErrorResponse`.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/
  Common/Behaviors/
    ValidationBehavior.cs                    ← UNCHANGED (throws — used by other modules)
    ValidationResultBehavior.cs              ← NEW: returns BaseResponse envelope
  Features/Analytics/
    AnalyticsModule.cs                       ← ADD 2 IPipelineBehavior registrations
    Validators/
      GetMarginReportRequestValidator.cs     ← ADD WithErrorCode() to each rule
      GetProductMarginAnalysisRequestValidator.cs ← same
    UseCases/GetMarginReport/
      GetMarginReportHandler.cs              ← REMOVE lines 35-54
    UseCases/GetProductMarginAnalysis/
      GetProductMarginAnalysisHandler.cs     ← REMOVE lines 31-42

backend/test/Anela.Heblo.Tests/Features/Analytics/
  Validators/
    GetMarginReportRequestValidatorTests.cs              ← NEW
    GetProductMarginAnalysisRequestValidatorTests.cs     ← NEW
  Pipeline/
    AnalyticsValidationPipelineTests.cs                  ← NEW (1 IMediator test per handler)
  GetMarginReportHandlerTests.cs                         ← REMOVE invalid-input tests
  GetProductMarginAnalysisHandlerTests.cs                ← REMOVE invalid-input tests
```

### Interfaces and Contracts

`ValidationResultBehavior<TRequest, TResponse>` shape:

```csharp
public class ValidationResultBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : BaseResponse, new()
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationResultBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any()) return await next();

        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), ct))))
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .ToList();

        if (failures.Count == 0) return await next();

        var first = failures.First();
        var errorCode = Enum.TryParse<ErrorCodes>(first.ErrorCode, out var parsed)
            ? parsed
            : ErrorCodes.ValidationError;

        return new TResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Params = BuildParams(failures),
        };
    }
}
```

Validator updates (illustrative, both validators):

```csharp
RuleFor(x => x.StartDate)
    .LessThanOrEqualTo(x => x.EndDate)
    .WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString())
    .WithMessage(AnalyticsConstants.ValidationMessages.INVALID_DATE_RANGE);

RuleFor(x => x)
    .Must(x => (x.EndDate - x.StartDate).TotalDays <= AnalyticsConstants.MAX_REPORT_PERIOD_DAYS)
    .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
    .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_LONG, AnalyticsConstants.MAX_REPORT_PERIOD_DAYS));
// … same for MIN, and for ProductId on the analysis validator (ErrorCodes.RequiredFieldMissing)
```

Note that the precedent in `Photobank/Validators/BulkAddPhotoTagRequestValidator.cs:21` parses the error code via `((int)ErrorCodes.X).ToString()`. Match that exactly so a future generic translator can be unified later.

### Data Flow

**Invalid request (e.g., `StartDate > EndDate`):**
1. `AnalyticsController` invokes `IMediator.Send(request)`.
2. `ValidationResultBehavior<GetMarginReportRequest, GetMarginReportResponse>` runs `GetMarginReportRequestValidator`.
3. Validator returns failure with `ErrorCode = "<InvalidDateRange int value>"`.
4. Behavior short-circuits without calling `next()`, returns `new GetMarginReportResponse { Success=false, ErrorCode=ErrorCodes.InvalidDateRange, Params={ startDate, endDate } }`.
5. Handler is **never invoked**. Controller serializes the envelope to JSON. Frontend receives identical shape to today.

**Valid request:**
1. Behavior validates → no failures → calls `next()`.
2. Handler executes business logic (with the removed `if`-blocks gone).
3. Returns `GetMarginReportResponse { Success=true, … }`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec assumes validators are pipeline-wired; they aren't. Removing handler checks without wiring would silently regress production. | **CRITICAL** | Decision 1 + 2 add wiring as a **prerequisite step** of the same PR. Pipeline integration tests (Decision 3) lock it in. |
| `ValidationException`-throwing variant of behavior would produce 500s instead of `ErrorCodes.InvalidDateRange` envelopes. | **CRITICAL** | Decision 2: introduce a separate `ValidationResultBehavior` that returns the envelope. Do not reuse the existing throwing behavior. |
| `Params` shape today includes `startDate`/`endDate` keys; behavior must emit equivalent params or frontend localization breaks. | HIGH | Map FluentValidation `PropertyName`/`AttemptedValue` into the same key names. Add explicit pipeline integration test asserting params dictionary keys match today's contract. |
| Migrating `Handle_EmptyProductId_ReturnsErrorResponse` is in scope (block is the full lines 30-39 of analysis handler) but spec FR-1/FR-2 wording emphasizes only "date-range and report-period". | MEDIUM | Spec amendment below — make ProductId guard removal explicit. Add validator test for `ProductId` empty → `ErrorCodes.RequiredFieldMissing`. |
| Other modules without pipeline-behavior wiring may have the same hidden duplication. | LOW | Out of scope per spec ("Modifying any other Analytics module handlers... is out of scope"). Note in PR description for follow-up. |
| `dotnet format` and analyzer warnings on the new behavior class. | LOW | Run `dotnet format` per CLAUDE.md validation gate. |

## Specification Amendments

The spec must be updated **before implementation begins**:

1. **Add FR-0 (Prerequisite — pipeline wiring):** Before any handler code is deleted, register `IPipelineBehavior` for both requests in `AnalyticsModule`. Acceptance: a pipeline integration test (sending invalid input via `IMediator.Send`) fails before the wiring change and passes after.

2. **Add FR-0b (Validation result translation):** Introduce `ValidationResultBehavior<TRequest, TResponse>` in `Application/Common/Behaviors/` that returns a `BaseResponse`-shaped failure (does **not** throw). Annotate the two validators' rules with `WithErrorCode(((int)ErrorCodes.XXX).ToString())` matching the codes currently returned by the handlers.

3. **Clarify FR-2 acceptance:** Also remove the `string.IsNullOrWhiteSpace(request.ProductId)` guard at `GetProductMarginAnalysisHandler.cs:32-35`. The validator already enforces `ProductId.NotEmpty()`. Acceptance: handler returns `ErrorCodes.RequiredFieldMissing` (via pipeline) when `ProductId` is empty, verified by validator + pipeline tests.

4. **Strengthen FR-3 acceptance:** Replace *"validation behavior is verified by an existing integration/pipeline test"* with: "A new test under `Tests/Features/Analytics/Pipeline/` constructs a real `IMediator` with `ValidationResultBehavior` registered and asserts the exact `ErrorCode` and `Params` shape returned for each failure mode."

5. **Update FR-6 verification:** Add a step to confirm the `Params` dictionary keys/values returned by the pipeline behavior match what the handlers return today (frontend depends on these for error-message interpolation).

6. **Update Out of Scope:** It is **not** acceptable to defer the pipeline-wiring fix to a separate PR — the spec's own correctness depends on it. Move "wire `IPipelineBehavior` for the two requests" *into* scope.

## Prerequisites

- No infrastructure changes required (no migrations, no config, no Key Vault changes).
- `WithErrorCode` is already a known FluentValidation idiom in this codebase — no new package versions.
- Existing test infrastructure (xUnit, Moq, FluentAssertions, `FluentValidation.TestHelper`) is sufficient — `FluentValidation.TestHelper` is already used in `GetManufacturingStockAnalysisRequestValidatorTests.cs`.
- Verify build + format gates pass per project CLAUDE.md: `dotnet build`, `dotnet format`, and all touched test projects must pass.