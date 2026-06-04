# Architecture Review: Fix OrgChartController Error Contract Violation and Information Leakage

## Skip Design: true

Backend-only error-path refactor. No new screens, components, or visual changes.

## Architectural Fit Assessment

The spec's preferred approach (option A) snaps `OrgChartController` onto the **already-canonical pattern** in this codebase. Verified facts on disk:

- **43 of 57 controllers** inherit from `BaseApiController` (`backend/src/Anela.Heblo.API/Controllers/`). `OrgChartController` is one of the few outliers that inherits directly from `ControllerBase`.
- `BaseApiController.HandleResponse<T>` (`BaseApiController.cs:29-60`) does exactly what FR-3 requires: maps `BaseResponse.ErrorCode` to HTTP status via `[HttpStatusCode]` on the `ErrorCodes` enum, with a dedicated `HttpStatusCode.InternalServerError` arm at line 53.
- `ErrorCodes.InternalServerError = 0010` decorated with `[HttpStatusCode(HttpStatusCode.InternalServerError)]` (`ErrorCodes.cs:32-33`) — confirms the spec's correction over the brief (`ServerError` → `InternalServerError`).
- `OrgChartResponse(ErrorCodes, Dictionary<string,string>?)` constructor exists at `OrgChartResponse.cs:17-18`.
- Handlers across the codebase already return typed error responses (e.g. `ScanPackingOrderHandler.cs:104,110,133` — catches exception, logs, returns `new XResponse(ErrorCodes.X)`). Option A is the dominant pattern, not a new one.
- The global exception pipeline (`AddExceptionHandler<UnauthorizedAccessExceptionHandler>` at `ServiceCollectionExtensions.cs:132` + `UseExceptionHandler()` at `ApplicationBuilderExtensions.cs:91`) exists, but is currently used as a *narrow infrastructure-layer* mechanism (401 only). The XML comment in `UnauthorizedAccessExceptionHandler.cs:8-13` makes this distinction explicit: "business-layer 401s flow through BaseApiController.HandleResponse and use the BaseResponse shape." This codifies a project convention — **business failures use `BaseResponse`; infrastructure-layer auth exceptions use `ProblemDetails`**. The OrgChart fix is squarely a business-layer concern and must follow the former.

**Decision: implement option A.** Option B (generic fallback `IExceptionHandler`) would either (a) write `ProblemDetails` and break the `[ProducesResponseType(typeof(OrgChartResponse), 500)]` contract, or (b) require type-aware reflection to construct the right `BaseResponse` subclass per endpoint — over-engineered and inconsistent with the documented convention above. Reject it.

## Proposed Architecture

### Component Overview

```
HTTP GET /api/OrgChart
        │
        ▼
┌─────────────────────────────┐
│ OrgChartController          │  ← inherits BaseApiController (CHANGED)
│   _mediator.Send(request)   │
│   return HandleResponse(r)  │  ← no try/catch
└─────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────┐
│ GetOrganizationStructureHandler             │  ← owns failure conversion (CHANGED)
│   try { return await service.Get…(); }       │
│   catch (Exception ex) {                     │
│     _logger.LogError(ex, "...");             │
│     return new OrgChartResponse(             │
│       ErrorCodes.InternalServerError);       │
│   }                                          │
└─────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────┐
│ OrgChartService             │  ← UNCHANGED
│   throws InvalidOperation…  │     (still wraps URL + inner msg)
└─────────────────────────────┘
```

The wrapped exception messages from `OrgChartService` (which include `_options.DataSourceUrl`) continue to exist but are **never serialized** — the handler catches them, logs them, and returns a sanitized typed envelope.

### Key Design Decisions

#### Decision 1: Failure conversion lives in the handler, not the controller
**Options considered:**
- A. Handler converts exceptions to typed error response; controller is a thin pass-through.
- B. New global `IExceptionHandler` writes the response.
- C. Keep the controller `try/catch` and only swap the anonymous object for `new OrgChartResponse(ErrorCodes.InternalServerError)`.

**Chosen approach:** A.

**Rationale:** Matches the dominant pattern (`ScanPackingOrderHandler`, `GetJournalEntryHandler`, `CreateJournalTagHandler`, etc. — failure is a *value*, not a *thrown exception*). Lets the controller use `HandleResponse<T>` so the OpenAPI `[ProducesResponseType(typeof(OrgChartResponse), 500)]` contract is honored end-to-end. B would either break the typed contract (ProblemDetails) or require error-shape reflection. C works but keeps OrgChart as an outlier with its own try/catch — the spec's NFR-3 explicitly rejects this.

#### Decision 2: Catch `Exception` (broad), not specific types
**Options considered:**
- Catch only `InvalidOperationException` (the type `OrgChartService` wraps with).
- Catch `Exception`.

**Chosen approach:** Catch `Exception`, but re-throw `OperationCanceledException` (preserves cancellation semantics — the request was aborted, not failed).

**Rationale:** `OrgChartService.GetOrganizationStructureAsync` wraps HTTP and JSON failures in `InvalidOperationException`, but a catch-all in the handler is the right safety net for any future failure mode (DI resolution, deserializer changes, options binding). `OperationCanceledException` is conventionally rethrown across this codebase (see `LeafletController.cs:54-57`) — same convention here.

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to fetch organizational structure");
    return new OrgChartResponse(ErrorCodes.InternalServerError);
}
```

#### Decision 3: Logging owned by the handler — not the controller — after the refactor
**Options considered:**
- Keep `_logger` injection in the controller; log there.
- Move logging to handler; controller no longer needs `ILogger`.

**Chosen approach:** Handler owns failure logging. Controller's existing `_logger.LogInformation("Fetching organizational structure")` and constructor `ILogger` dependency are removed (the handler already has its own `LogInformation` at `GetOrganizationStructureHandler.cs:26`, so no observability is lost).

**Rationale:** Aligns with the broader pattern — handlers log their own failures in this codebase. The controller becomes a pure HTTP adapter. **Note for implementers:** the existing handler test `Handle_PropagatesException_WhenServiceThrows` (`GetOrganizationStructureHandlerTests.cs:48-75`) explicitly asserts the handler does **not** log on error and that exceptions propagate. That assertion encoded the *old* "controller owns failure logging" convention. It must be replaced with a new test asserting the new behavior (see Specification Amendments below).

#### Decision 4: No new error code; reuse `InternalServerError`
**Rationale:** `InternalServerError = 0010` is the generic 500 used across modules. Adding an OrgChart-specific code (e.g. `OrgChartDataSourceUnavailable`) would be premature — the spec is fixing a contract violation, not modeling new failure semantics. If callers later need to distinguish "data source down" from "deserialization failed," that's a follow-up.

## Implementation Guidance

### Directory / Module Structure
All changes are in three existing files. No new files.

```
backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs                                       [MODIFY]
backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/
    GetOrganizationStructureHandler.cs                                                              [MODIFY]
backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs            [MODIFY]
```

Optional: add a controller-level integration test under `backend/test/Anela.Heblo.Tests/` (location follows the test project's existing controller-test layout — search for a `Controllers/` folder or `WebApplicationFactory<T>`-based tests; if none exists for other controllers, the handler-level tests + manual verification are sufficient and the integration test is recommended-but-not-required).

### Interfaces and Contracts

**Controller — final shape:**
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrgChartController : BaseApiController
{
    private readonly IMediator _mediator;

    public OrgChartController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OrgChartResponse>> GetOrganizationStructure(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrganizationStructureRequest(), cancellationToken);
        return HandleResponse(result);
    }
}
```

**Handler — final shape:**
```csharp
public async Task<OrgChartResponse> Handle(
    GetOrganizationStructureRequest request,
    CancellationToken cancellationToken)
{
    _logger.LogInformation("Handling request to fetch organizational structure");
    try
    {
        return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to fetch organizational structure");
        return new OrgChartResponse(ErrorCodes.InternalServerError);
    }
}
```

### Data Flow

**Happy path (200):**
```
GET → Controller → Mediator → Handler → Service → HTTP fetch → deserialize
                                                     │
                          OrgChartResponse{Success=true, Organization=...} ◄┘
                                ▼
                       HandleResponse → Ok(response)
```

**Failure path (500):**
```
GET → Controller → Mediator → Handler → Service → throws InvalidOperationException
                                          │  (contains URL + inner message)
                                          ▼
                          catch → LogError(ex, ...)   ← server log keeps full detail
                                          │
                          new OrgChartResponse(ErrorCodes.InternalServerError)
                                          ▼
                       HandleResponse → StatusCode(500, response)
                                          │
                          { success:false, errorCode:InternalServerError,
                            params:null, organization:{name:null,positions:[]} }
```

Critical invariant: the exception object **never** crosses the handler boundary on the failure path, so `ex.Message` / `ex.ToString()` cannot leak into the HTTP response.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| TypeScript client regeneration changes shape of 500 response, breaking existing consumers | LOW | Per `docs/development/api-client-generation.md`, regenerate locally and inspect the diff before commit. The 500 schema becomes `OrgChartResponse` (was untyped); any consumer that used the old anonymous `{error, message}` shape was already broken under TS types. |
| Removing controller-level `_logger.LogInformation("Fetching…")` reduces observability | LOW | Handler already emits the same `LogInformation` at `GetOrganizationStructureHandler.cs:26` — no log loss. |
| Other tests/integration tests assert the old 500 shape | LOW | Grep for `"Failed to fetch organizational structure"` and `error = "Failed to fetch"` across `backend/test/`. If any tests assert the anonymous shape, update them. The known test (`Handle_PropagatesException_WhenServiceThrows`) is called out in Specification Amendments. |
| `BaseApiController.HandleResponse` returns `StatusCode((int)statusCode, response)` for `InternalServerError` (`BaseApiController.cs:53`) — verify it serializes `OrgChartResponse` (not anonymous) | LOW | Confirmed in code: `response` of type `T : BaseResponse` is passed to `StatusCode`; ASP.NET serializes via the configured JSON pipeline. Same path as `Ok(response)`. |
| Hidden caller that depended on `params` containing `ex.Message` | LOW | The new contract sets `Params = null` (constructor default). Callers should localize from `ErrorCode`. The spec explicitly accepts `Params = null` (FR-1). |

## Specification Amendments

The spec is sound. Two clarifications to remove ambiguity for implementers:

1. **Replace, don't update, the existing failure-path handler test.** `Handle_PropagatesException_WhenServiceThrows` (`GetOrganizationStructureHandlerTests.cs:48-75`) asserts two things that become *false* under option A:
   - `await act.Should().ThrowAsync<InvalidOperationException>()` — the new handler **does not throw**, it returns.
   - `_loggerMock.Verify(... LogError ..., Times.Never)` — the new handler **does** log on error.

   Replace the entire test with the spec's FR-3 acceptance test: assert the handler returns `OrgChartResponse { Success: false, ErrorCode: ErrorCodes.InternalServerError }` when the service throws, and assert `LogError` is called **once** with the exception object (so server-side diagnostics are preserved per NFR-2).

2. **Drop `_logger` from the controller.** The spec's controller example (under "Controller signature after fix (option A)") still keeps the controller constructor; clarify that the `ILogger<OrgChartController>` field and parameter should be removed entirely. The handler already logs the "Fetching" info-level message, and `BaseApiController` exposes a lazy `Logger` property if a future need arises.

3. **Optional integration test placement.** FR-2 calls for an integration test asserting the 500 body does not contain `_options.DataSourceUrl` or `"Failed to fetch organizational structure:"`. Verify the test project has a `WebApplicationFactory<Program>` set up (search `backend/test/` for `WebApplicationFactory`). If not, the handler-level test plus a manual verification (start app, kill data source, hit endpoint, inspect body) is acceptable for solo-developer cadence. Do not block this fix on building integration test infrastructure that doesn't exist yet.

## Prerequisites

None.

- No migrations.
- No new packages.
- No config changes (the OpenAPI TypeScript client is auto-generated on build per `CLAUDE.md` project facts — regeneration is automatic; just inspect the diff).
- No infrastructure changes.

Ready for implementation.