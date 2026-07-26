# Specification: Extract Azure Container Name Validation from `DownloadFromUrlHandler`

## Summary
`DownloadFromUrlHandler` currently embeds a 22-line, Azure-Blob-specific container-name validation routine (`IsValidContainerName`) directly in the Application-layer handler. This spec moves that validation into a `DownloadFromUrlRequestValidator` (FluentValidation), registered in the existing `ValidationResultBehavior<TRequest, TResponse>` MediatR pipeline, following the pattern already used by `Analytics`, `Photobank`, `Catalog`, and other modules in this codebase. The change is a pure refactor: the handler's public behavior, response shape, error codes, and HTTP status codes must remain byte-for-byte identical.

## Background
The finding (filed by the daily arch-review routine, 2026-07-17) observed that `DownloadFromUrlHandler.IsValidContainerName` (lines 199–221) hard-codes Azure Blob Storage container naming rules (3–63 chars, lowercase, alphanumeric + single hyphens, must start/end alphanumeric) inside the Application layer, which should stay storage-provider-agnostic. The handler both validates and orchestrates, violating single-responsibility, and bypasses this codebase's established `Validators/` + FluentValidation convention used pervasively elsewhere (e.g. `Features/Photobank/Validators/`, `Features/Catalog/Validators/`, `Features/Analytics/Validators/`).

This codebase has two competing MediatR validation pipeline behaviors:
- `ValidationBehavior<TRequest, TResponse>` (`Common/Behaviors/ValidationBehavior.cs`) — throws `FluentValidation.ValidationException` on failure, caught by `ValidationExceptionHandler` at the API layer and converted to a generic `ProblemDetails` (400, with a generic `errors: [{propertyName, errorMessage}]` array). This is the pattern used by most modules (Photobank, Catalog, Inventory, GiftSettings, CarrierCooling, etc.).
- `ValidationResultBehavior<TRequest, TResponse>` (`Common/Behaviors/ValidationResultBehavior.cs`) — does **not** throw. It runs validators, and on failure constructs `new TResponse { Success = false, ErrorCode = <parsed from first failure's WithErrorCode>, Params = <first failure's WithState> }` and returns it directly from the pipeline, short-circuiting the handler. This requires `TResponse : BaseResponse, new()`. This is the pattern used by `Analytics` (`GetMarginReportRequestValidator`, `GetProductMarginAnalysisRequestValidator`), which uses `.WithErrorCode(((int)ErrorCodes.X).ToString())` and `.WithState(x => (object)new Dictionary<string,string>{...})` on each rule specifically so the pipeline reconstructs the same `Success`/`ErrorCode`/`Params` contract the handler would have built manually.

`DownloadFromUrlResponse` inherits `BaseResponse`, and today the handler manually returns `Success = false, ErrorCode = ErrorCodes.InvalidContainerName, Params = { containerName, cause = "validation" }` for an invalid container name. **`ValidationResultBehavior` is the only one of the two pipeline behaviors that can reproduce this exact contract without throwing** — `ValidationBehavior` would change the wire response from a `DownloadFromUrlResponse` body to a generic `ProblemDetails` body, which is an observable breaking change for any API consumer (frontend, MCP tools, etc.) that reads `response.errorCode` / `response.params.containerName`. This spec therefore mandates the `ValidationResultBehavior` + `WithErrorCode`/`WithState` pattern (matching `Analytics`), not `ValidationBehavior`.

## Functional Requirements

### FR-1: Extract `DownloadFromUrlRequestValidator`
Create `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`, a `FluentValidation.AbstractValidator<DownloadFromUrlRequest>`, containing a single rule on `ContainerName` that reproduces the exact validation logic currently in `IsValidContainerName` (lines 199–221 of `DownloadFromUrlHandler.cs`):
- Non-null/non-empty, length between 3 and 63 characters (inclusive).
- Must equal its own lowercase-invariant form (i.e., no uppercase characters).
- First and last characters must be alphanumeric.
- Every character must be alphanumeric or a single hyphen; no two consecutive hyphens.

The rule must use:
```csharp
RuleFor(x => x.ContainerName)
    .Must(IsValidContainerName)
    .WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())
    .WithState(x => (object)new Dictionary<string, string>
    {
        { "containerName", x.ContainerName },
        { "cause", "validation" },
    })
    .WithMessage("Invalid container name");
```
`IsValidContainerName` is copied verbatim (as a `private static bool` helper on the validator class) from the current handler — no rule logic changes.

**Acceptance criteria:**
- The validator class exists in `Features/FileStorage/Validators/` (new folder), matching the location/naming pattern used by `Features/Photobank/Validators/`, `Features/Catalog/Validators/`, `Features/Analytics/Validators/`.
- For every input string that `IsValidContainerName` previously classified as invalid or valid, `DownloadFromUrlRequestValidator` produces the identical classification (validated by porting the existing `[Theory]` cases from `DownloadFromUrlHandlerTests` — see FR-3).
- The rule's `WithErrorCode` value round-trips through `Enum.TryParse<ErrorCodes>(...)` to exactly `ErrorCodes.InvalidContainerName`.
- The rule's `WithState` dictionary contains exactly the keys `containerName` (the submitted value) and `cause` (`"validation"`), matching what the handler previously placed in `Params`.

### FR-2: Wire the validator into the MediatR pipeline via `ValidationResultBehavior`
In `FileStorageModule.cs` (`AddFileStorageModule`), register:
```csharp
services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
    ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();
```
This is the same two-line registration pattern used in `AnalyticsModule.AddAnalyticsModule` for `GetProductMarginAnalysisRequest`/`GetMarginReportRequest`. Do **not** register `ValidationBehavior<,>` for this request type — it throws and changes the response contract (see Background).

**Acceptance criteria:**
- `IValidator<DownloadFromUrlRequest>` and `IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` are registered in `FileStorageModule.cs`.
- An end-to-end MediatR `Send(DownloadFromUrlRequest)` call with an invalid container name (e.g. `"AB"`, `"Invalid_Name"`, `""`) short-circuits before `DownloadFromUrlHandler.Handle` executes (i.e., `_blobStorageService.DownloadFromUrlAsync` is never invoked) and returns a `DownloadFromUrlResponse` with `Success == false`, `ErrorCode == ErrorCodes.InvalidContainerName`, and `Params == { ["containerName"] = <input>, ["cause"] = "validation" }` — identical to today's handler-level behavior.
- A valid container name passes through unaffected and reaches the handler.

### FR-3: Remove the validation from the handler; delete the old checks
In `DownloadFromUrlHandler.cs`:
- Delete the `IsValidContainerName` private static method (lines 199–221).
- Delete the `if (!IsValidContainerName(request.ContainerName)) { ... }` block (lines 61–74) that logs a warning and returns the `ErrorCodes.InvalidContainerName` response.
- Leave the `FileUrl`/URL-format validation (lines 45–59, `ErrorCodes.InvalidUrlFormat`) untouched — it is out of scope for this refactor (the brief and the finding target only the container-name rule).

**Acceptance criteria:**
- `DownloadFromUrlHandler.cs` no longer references `IsValidContainerName` or `ErrorCodes.InvalidContainerName`.
- `dotnet build` succeeds with no new warnings.
- The handler's URL-format validation and all downstream orchestration (resilience execution, HEAD probe, blob upload, error mapping for timeout/http-status/retry-exhausted) are unchanged.

### FR-4: Update existing tests to match the new location of the validation
`DownloadFromUrlHandlerTests.cs` currently instantiates `DownloadFromUrlHandler` directly and calls `.Handle(...)`, bypassing the MediatR pipeline entirely — after FR-3, the handler itself performs no container-name validation, so these handler-level tests can no longer exercise that rule.
- Move the container-name theory cases (`Handle_InvalidContainerName_ShouldReturnErrorResponse`, `Handle_ValidContainerName_ShouldSucceed`, `Handle_ValidationFailure_InvalidContainerName_SetsCauseValidation`, and the invalid/valid `[InlineData]` sets) out of `DownloadFromUrlHandlerTests.cs` into a new `DownloadFromUrlRequestValidatorTests.cs` under `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/`, testing the validator directly (`validator.TestValidate(request)` or `validator.Validate(...)`/`ValidateAsync(...)`), following the naming convention of e.g. `test/Anela.Heblo.Tests/Features/Catalog/Validators/GetCatalogDetailRequestValidatorTests.cs`.
- Remove those same cases from `DownloadFromUrlHandlerTests.cs` (the handler no longer owns this behavior; leave the URL-format and orchestration tests in place).
- `FileStorageControllerTests.cs` needs no change — it mocks `IMediator.Send` directly and only asserts controller-level HTTP-status mapping from a given `DownloadFromUrlResponse`, which is unaffected by where validation happens.
- Add (or confirm) at least one test exercising the full pipeline (validator + `ValidationResultBehavior` + handler wired via DI/`AddFileStorageModule`, or a focused `ValidationResultBehavior`-level test) to prove FR-2's short-circuit and contract-preservation behavior end-to-end, since no existing test currently exercises `FileStorageModule`'s DI wiring together with MediatR dispatch.

**Acceptance criteria:**
- All previously-passing container-name test cases still pass, relocated to validator-level tests with identical input/output expectations.
- `DownloadFromUrlHandlerTests.cs` still compiles and its remaining (non-container-name) tests pass unchanged.
- The full backend test suite passes (`dotnet build` + `dotnet test`, or the project's standard test command).

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The validation work is identical in cost (same string checks), only relocated from being invoked inline in the handler to being invoked by the MediatR pipeline before the handler runs. No additional I/O, allocations of consequence, or async overhead beyond FluentValidation's existing `ValidateAsync` machinery already used elsewhere in this codebase.

### NFR-2: Security
No change in security posture. This is an internal input-validation rule with no bearing on authentication/authorization (the `DownloadFromUrl` endpoint remains behind `[FeatureAuthorize(Feature.Admin_Administration)]`, unaffected by this refactor). No new secrets, external calls, or trust boundaries are introduced.

## Data Model
No data model changes. No new entities, DTOs, or persisted state. `DownloadFromUrlRequest` and `DownloadFromUrlResponse` are unchanged (both remain classes, per this codebase's DTO convention — no records).

## API / Interface Design
No change to the public HTTP API surface. `POST /api/FileStorage/download` (`FileStorageController.DownloadFromUrl`) keeps its existing request/response shape:
- Invalid container name → HTTP 400 (`ErrorCodes.InvalidContainerName` carries `[HttpStatusCode(HttpStatusCode.BadRequest)]`), body is `DownloadFromUrlResponse` with `Success=false`, `ErrorCode=InvalidContainerName`, `Params={containerName, cause}` — identical to current behavior, just produced by the pipeline instead of the handler.
- All other paths (valid input, URL-format errors, download failures) are untouched.

New internal-only surface introduced by this refactor:
- `Anela.Heblo.Application.Features.FileStorage.Validators.DownloadFromUrlRequestValidator : AbstractValidator<DownloadFromUrlRequest>` — not exposed outside the Application layer.

## Dependencies
- `FluentValidation` (already a project dependency; used identically to `Analytics`/`Photobank`/`Catalog` validators).
- `Anela.Heblo.Application.Common.Behaviors.ValidationResultBehavior<TRequest, TResponse>` (existing, reused as-is — no changes to this class).
- `Anela.Heblo.Application.Shared.ErrorCodes.InvalidContainerName` (existing enum value, reused as-is).
- MediatR (existing; validator/behavior registration relies on the existing `AddMediatR` handler scan plus explicit per-request `IPipelineBehavior` registration, matching the codebase's established per-module pattern — there is no global/assembly-wide FluentValidation auto-registration in this codebase).

## Out of Scope
- The `FileUrl`/URL-format validation (`Uri.TryCreate` + scheme check, `ErrorCodes.InvalidUrlFormat`) is not touched or moved.
- No change to `IBlobStorageService`, `IDownloadResilienceService`, retry/resilience behavior, HEAD-probe logic, or blob-name derivation.
- No introduction of a second storage provider or provider-abstraction layer for naming rules beyond what's needed to relocate this one rule out of the handler.
- No change to `ErrorCodes` enum values or HTTP status mappings.
- No change to the `FileStorageController` or its tests.
- No broader migration of other handlers in the codebase that may have similar inline-validation smells (this spec is scoped to `DownloadFromUrlHandler` only, per the filed finding).

## Open Questions
None.

## Status: COMPLETE
