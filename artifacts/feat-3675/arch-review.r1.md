# Architecture Review: Extract Azure Container Name Validation from `DownloadFromUrlHandler`

## Skip Design: true

Verified against the spec and the handler source: `DownloadFromUrlHandler.cs` has no rendering, no controller contract change (`FileStorageController` and its tests are explicitly out of scope in the spec), and `DownloadFromUrlRequest`/`DownloadFromUrlResponse` shapes are unchanged. This is a pure Application-layer refactor — relocating a validation routine from a handler method into a FluentValidation validator invoked by an existing MediatR pipeline behavior. No frontend files, API contracts, or OpenAPI-generated clients are touched. Skip Design is correct.

## Architectural Fit Assessment

This fits the codebase's established conventions exactly, and the spec has already done the grounding work correctly. Verified directly against source:

- `ValidationResultBehavior<TRequest, TResponse>` (`backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs`) requires `TResponse : BaseResponse, new()`, runs all `IValidator<TRequest>` instances, and on failure builds `new TResponse { Success = false, ErrorCode = <parsed>, Params = <state> }` without throwing — confirmed by reading the class. `DownloadFromUrlResponse : BaseResponse` (confirmed), so this behavior is a structural fit, not just a plausible one.
- `ValidationBehavior<TRequest, TResponse>` (confirmed by reading the class) throws `FluentValidation.ValidationException` unconditionally on failure — using it here would replace the `DownloadFromUrlResponse` body with a generic `ProblemDetails` shape on the wire, an observable breaking change. The spec's rejection of `ValidationBehavior` is architecturally correct.
- `AnalyticsModule.AddAnalyticsModule` (confirmed by reading the file) registers exactly this pattern — `IValidator<TRequest>` + `IPipelineBehavior<TRequest, TResponse>` bound to `ValidationResultBehavior<TRequest, TResponse>` per request type — for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest`. `GetProductMarginAnalysisRequestValidator` (confirmed by reading the file) uses the identical `RuleFor(...).Must(...).WithErrorCode(((int)ErrorCodes.X).ToString()).WithState(x => (object)new Dictionary<string,string>{...}).WithMessage(...)` idiom the spec proposes reusing.
- `docs/architecture/filesystem.md` documents `Features/{Feature}/Validators/` as the canonical location for FluentValidation request validators in complex features; `Features/Analytics/Validators/`, and equivalent `Validators/` folders in Catalog/Photobank, are the concrete precedent. `FileStorage` currently has no `Validators/` folder — this refactor introduces the first one, following the documented pattern rather than deviating from it.
- `docs/architecture/development_guidelines.md`'s DTO rule ("DTOs are never records") is already satisfied: `DownloadFromUrlRequest` and `DownloadFromUrlResponse` are both classes (confirmed).
- `ErrorCodes.InvalidContainerName = 1802` (confirmed in `Shared/ErrorCodes.cs`) is reused as-is — no enum changes.

No module-boundary, DI-layering, or persistence concerns arise: this is entirely internal to `Anela.Heblo.Application.Features.FileStorage`.

## Proposed Architecture

### Component Overview

```
MediatR pipeline for DownloadFromUrlRequest (registered per-request-type in FileStorageModule,
matching AnalyticsModule's pattern — there is no global auto-registration in this codebase):

  Send(DownloadFromUrlRequest)
        │
        ▼
  ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>
        │   runs IValidator<DownloadFromUrlRequest> (= DownloadFromUrlRequestValidator)
        │
        ├── invalid ContainerName ──► short-circuit: returns DownloadFromUrlResponse
        │                              { Success=false, ErrorCode=InvalidContainerName,
        │                                Params={containerName, cause=validation} }
        │                              (DownloadFromUrlHandler.Handle is NEVER invoked)
        │
        └── valid ──────────────────► next() → DownloadFromUrlHandler.Handle(...)
                                        (URL-format check + orchestration, unchanged)
```

Before: `DownloadFromUrlHandler.Handle` performs URL validation, then container-name validation (`IsValidContainerName`, lines 199–221), then orchestration — three responsibilities in one method.
After: container-name validation moves one layer up into the MediatR pipeline; the handler retains only URL validation + orchestration. URL validation is explicitly out of scope for this refactor (per spec) and stays inline — this review does not recommend expanding scope to also extract it, see Specification Amendments.

### Key Design Decisions

#### Decision 1: `ValidationResultBehavior` over `ValidationBehavior`
**Options considered:** (a) `ValidationBehavior` (throws `ValidationException`, caught by `ValidationExceptionHandler`, returns generic `ProblemDetails`); (b) `ValidationResultBehavior` (no throw, reconstructs the response's own `Success`/`ErrorCode`/`Params` contract).
**Chosen approach:** (b), matching the spec.
**Rationale:** Confirmed by reading both behavior classes — only `ValidationResultBehavior` can produce a `DownloadFromUrlResponse` body with `Success=false, ErrorCode=InvalidContainerName, Params={...}`. `ValidationBehavior` would change the wire contract from a typed response to `ProblemDetails`, breaking any consumer (frontend, MCP tools) that reads `response.errorCode`/`response.params`. This is not a style choice — it is required to preserve the "byte-for-byte identical" behavior the spec mandates.

#### Decision 2: Per-request-type pipeline registration, not global
**Options considered:** (a) register `ValidationResultBehavior<,>` and `ValidationBehavior<,>` open generics globally via `AddMediatR` assembly scan; (b) explicit per-request-type registration in each module, as done today.
**Chosen approach:** (b) — two explicit `services.AddScoped<...>` lines in `FileStorageModule.AddFileStorageModule`, exactly mirroring `AnalyticsModule`.
**Rationale:** Confirmed there is no global/open-generic pipeline registration anywhere in the codebase for these two behaviors — every module wires its own request types individually. Introducing global registration here would be a silent, out-of-scope architectural change affecting every other MediatR request in the system (many of which have no validator and would be unaffected only by coincidence, or worse, would pick up `ValidationBehavior` and start throwing where they didn't before). Follow the existing per-module convention; do not "fix" this project-wide as a side effect of a FileStorage refactor.

#### Decision 3: Validator owns the `IsValidContainerName` predicate as a private static helper
**Options considered:** (a) copy the character-validation loop verbatim into a `private static bool IsValidContainerName` on the validator class (as the spec specifies); (b) extract it to a shared `Xcc` or `Domain` helper for reuse.
**Chosen approach:** (a).
**Rationale:** This rule is Azure Blob Storage-specific and today has exactly one call site. Promoting it to a shared/cross-cutting helper is speculative generalization with no second consumer — it would also fight the module-independence principle in `development_guidelines.md` ("Don't create shared services... unless a real consumer exists" is the same reasoning ADR-005 uses for not adding a `UserIdResolver` prematurely). If a second blob-consuming feature appears later needing the same rule, extract then.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/FileStorage/
├── UseCases/DownloadFromUrl/
│   ├── DownloadFromUrlHandler.cs        # MODIFIED: remove IsValidContainerName + its call site
│   ├── DownloadFromUrlRequest.cs        # unchanged
│   └── DownloadFromUrlResponse.cs       # unchanged
├── Validators/                          # NEW folder (first in FileStorage)
│   └── DownloadFromUrlRequestValidator.cs   # NEW
└── FileStorageModule.cs                 # MODIFIED: add validator + pipeline behavior registration

backend/test/Anela.Heblo.Tests/Features/FileStorage/
├── DownloadFromUrlHandlerTests.cs        # MODIFIED: remove container-name theory cases
├── FileStorageModuleTests.cs             # possibly EXTENDED: DI-wiring proof (see below)
└── Validators/                           # NEW folder
    └── DownloadFromUrlRequestValidatorTests.cs   # NEW: relocated theory cases
```

This matches the `Features/Analytics/Validators/` + `test/.../Analytics/Validators/`-equivalent (`Catalog/Validators/` confirmed present with matching `*RequestValidatorTests.cs` naming, e.g. `GetCatalogDetailRequestValidatorTests.cs`) pairing already used elsewhere.

### Interfaces and Contracts

No new public interfaces. One new internal class:

```csharp
namespace Anela.Heblo.Application.Features.FileStorage.Validators;

public class DownloadFromUrlRequestValidator : AbstractValidator<DownloadFromUrlRequest>
{
    public DownloadFromUrlRequestValidator()
    {
        RuleFor(x => x.ContainerName)
            .Must(IsValidContainerName)
            .WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "containerName", x.ContainerName },
                { "cause", "validation" },
            })
            .WithMessage("Invalid container name");
    }

    private static bool IsValidContainerName(string containerName) { /* verbatim from handler */ }
}
```

DI registration in `FileStorageModule.AddFileStorageModule` (add alongside existing lines, same file):

```csharp
services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
    ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();
```

Requires `using FluentValidation;`, `using Anela.Heblo.Application.Common.Behaviors;`, and `using Anela.Heblo.Application.Features.FileStorage.Validators;` added to `FileStorageModule.cs`.

### Data Flow

1. Controller sends `DownloadFromUrlRequest` via `IMediator.Send`.
2. MediatR resolves the pipeline: `ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` runs before `DownloadFromUrlHandler.Handle`.
3. Behavior resolves `IEnumerable<IValidator<DownloadFromUrlRequest>>` → finds `DownloadFromUrlRequestValidator`, calls `ValidateAsync`.
4. Invalid container name → validator produces a `ValidationFailure` carrying `ErrorCode="1802"` and `CustomState` = the dictionary → behavior parses `Enum.TryParse<ErrorCodes>("1802", ...)` → `ErrorCodes.InvalidContainerName` → constructs and returns `DownloadFromUrlResponse { Success=false, ErrorCode=InvalidContainerName, Params={containerName, cause} }` directly, **without calling `next()`** — `DownloadFromUrlHandler.Handle` never executes, `_blobStorageService` is never touched.
5. Valid container name → validator returns no failures → behavior calls `next()` → `DownloadFromUrlHandler.Handle` runs exactly as today, minus the now-removed inline check (URL-format validation still runs first inside the handler, unaffected).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `WithErrorCode` string must round-trip through `Enum.TryParse<ErrorCodes>` to exactly `InvalidContainerName` (1802) — a typo or wrong cast silently falls back to generic `ValidationError` | Medium | Use `((int)ErrorCodes.InvalidContainerName).ToString()` exactly as the `Analytics` validators do (never a raw string literal); cover with the round-trip assertion already specified in spec FR-1's acceptance criteria |
| Validator not registered (forgotten `AddScoped` line) silently disables the check — `ValidationResultBehavior` returns early via `if (!_validators.Any()) return await next();`, so a missing registration means **no error**, not a loud failure | Medium | The DI-wiring test called for in spec FR-4 (end-to-end `Send` through `AddFileStorageModule`) is not optional — it is the only test that would catch a missing registration; a validator-only unit test cannot |
| Handler tests (`DownloadFromUrlHandlerTests`) currently instantiate `DownloadFromUrlHandler` directly, bypassing MediatR/DI entirely — after this change they structurally cannot exercise the container-name rule at all | Low | Spec FR-4 already accounts for this by relocating those cases to `DownloadFromUrlRequestValidatorTests`; confirm no test silently continues to assert old handler-level behavior that no longer applies |
| `Params` dictionary ordering/equality in tests — `WithState` produces a `Dictionary<string,string>` cast to `object`; behavior does `firstFailure.CustomState as Dictionary<string, string>` | Low | Straightforward reference-typed cast, same pattern already proven by `Analytics` validators in production; no new risk introduced |

## Specification Amendments

None required. The spec's architectural reasoning (Background section) is accurate and already reflects direct inspection of both pipeline behaviors and the `Analytics` precedent — this review independently re-verified every claim (`ValidationResultBehavior`/`ValidationBehavior` source, `AnalyticsModule` registration, `GetProductMarginAnalysisRequestValidator` rule syntax, `DownloadFromUrlResponse : BaseResponse`, `ErrorCodes.InvalidContainerName = 1802`, DTOs-as-classes) and found no discrepancy. One clarification worth calling out explicitly for the implementer: `FileStorageModule.cs` currently has no `using FluentValidation;`, `using Anela.Heblo.Application.Common.Behaviors;`, or `Validators` namespace import — these three `using` additions are implied by FR-2 but not spelled out; add them.

## Prerequisites

None beyond what already exists in the repo. `FluentValidation` is already a referenced package (used by `Analytics`/`Photobank`/`Catalog` validators in the same project), `ValidationResultBehavior` and `ErrorCodes.InvalidContainerName` already exist and require no changes. No migrations, config, or infrastructure changes are needed before implementation starts.
