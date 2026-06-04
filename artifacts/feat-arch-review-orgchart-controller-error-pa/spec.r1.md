```markdown
# Specification: Fix OrgChartController Error Contract Violation and Information Leakage

## Summary
`OrgChartController.GetOrganizationStructure` returns an anonymous `{ error, message }` object on exceptions instead of the declared `OrgChartResponse` contract, and forwards raw exception messages (including internal URLs and stack-trace fragments) to clients. This spec defines the required fix: align the error response with the typed `BaseResponse`/`OrgChartResponse` contract used by every other module, and stop leaking exception internals to the browser.

## Background
- File: `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs:38-53`.
- The action is declared as `Task<ActionResult<OrgChartResponse>>` and decorated with `[ProducesResponseType(typeof(OrgChartResponse), 200)]`, but its `catch (Exception ex)` block returns `new { error = "...", message = ex.Message }` with status 500.
- `ex.Message` originates upstream in `OrgChartService.GetOrganizationStructureAsync` (`backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs:60-69`), which wraps `HttpRequestException` and `JsonException` in `InvalidOperationException` messages that include `_options.DataSourceUrl` and underlying error text.
- `OrgChartResponse` (`backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/OrgChartResponse.cs:8-19`) already provides an error constructor that accepts an `ErrorCodes` value and parameter dictionary, mirroring the project-wide `BaseResponse` envelope (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`).
- Every other controller in `backend/src/Anela.Heblo.API/Controllers/*.cs` (e.g. `BankStatementsController`, `LeafletController`) inherits from `BaseApiController` and uses `HandleResponse<T>` (`BaseApiController.cs:29-60`) to map `BaseResponse.ErrorCode` to the correct HTTP status code via `[HttpStatusCode]` attributes on `ErrorCodes`.
- Pipeline already exposes a generic exception-handler chain: `services.AddExceptionHandler<UnauthorizedAccessExceptionHandler>()` (`ServiceCollectionExtensions.cs:132`) plus `app.UseExceptionHandler()` (`ApplicationBuilderExtensions.cs:91`). Adding a fallback handler here is supported by the existing infrastructure.

**Impact:**
1. **Broken API contract.** Generated OpenAPI / TypeScript clients deserialize the 500 body as `OrgChartResponse`; every typed field becomes `undefined`, masking the real failure mode.
2. **Information leakage.** Raw exception messages — including the configured SharePoint/data-source URL and wrapped `InvalidOperationException` text — are sent to the browser. Violates `~/.claude/rules/csharp-security.md` ("Do not expose stack traces, SQL text, or filesystem paths in API responses").
3. **Inconsistency.** This is the only controller in the codebase returning an anonymous error object; it bypasses `BaseApiController.HandleResponse` and the `ErrorCodes`/`HttpStatusCode` attribute mapping that the rest of the codebase relies on.

## Functional Requirements

### FR-1: 500 responses must conform to the `OrgChartResponse` contract
On any unhandled exception path inside `GET /api/OrgChart`, the response body must be a serialized `OrgChartResponse` with:
- `Success = false`
- `ErrorCode = ErrorCodes.InternalServerError` (value `0010`, decorated with `[HttpStatusCode(HttpStatusCode.InternalServerError)]`)
- `Organization` left at its default empty `OrganizationDto`
- `Params` either `null` or containing only non-sensitive, structured keys (see FR-3)

> Note: the brief references `ErrorCodes.ServerError`. The actual enum member in `Anela.Heblo.Application.Shared.ErrorCodes` is `InternalServerError`. Use `ErrorCodes.InternalServerError`.

**Acceptance criteria:**
- Hitting `GET /api/OrgChart` while `OrgChartService` throws returns HTTP 500 with a body that deserializes cleanly into `OrgChartResponse`.
- The body contains `success: false` and `errorCode: "InternalServerError"` (or numeric `10` depending on serializer settings already in use).
- The OpenAPI document advertises `OrgChartResponse` as the schema for both 200 and 500 (i.e. the controller declares `[ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status500InternalServerError)]`).
- The regenerated TypeScript client treats the 500 body as the same `OrgChartResponse` type used for 200.

### FR-2: Stop forwarding raw exception text to clients
The 500 response body must not contain any of the following:
- `ex.Message`
- `ex.ToString()` / stack frames
- `OrgChartOptions.DataSourceUrl` or any other configuration value
- Inner-exception messages produced by `OrgChartService` (`"Failed to fetch organizational structure: ..."`, `"Failed to parse organizational structure: ..."`)

`_logger.LogError(ex, ...)` server-side logging must be preserved so operators retain full diagnostic context.

**Acceptance criteria:**
- Integration test asserting the 500 response body does not contain `_options.DataSourceUrl` (use a sentinel URL like `https://sentinel.test/orgchart-source` via test configuration).
- Integration test asserting the 500 body does not contain the substring `Failed to fetch organizational structure:` or any HTTP error message bubbled from `HttpRequestException`.
- Server-side log entry continues to capture exception type, message, stack trace, and structured `{Url}` property.

### FR-3: Align with the project-wide error envelope
The controller must use the same response pattern as the rest of the codebase. Implement **option A** (preferred) or **option B**:

**Option A (preferred): handler returns typed error; controller uses `HandleResponse`.**
- Move the try/catch from the controller into `GetOrganizationStructureHandler`.
- On caught exceptions, log via `ILogger<GetOrganizationStructureHandler>` and return `new OrgChartResponse(ErrorCodes.InternalServerError)`.
- Change `OrgChartController` to inherit from `BaseApiController` and call `return HandleResponse(result);` after `_mediator.Send`.
- Remove the controller-level `try/catch` entirely.

**Option B: add a generic fallback `IExceptionHandler`.**
- Add `OrgChartExceptionHandler` (or a generic `UnhandledExceptionHandler`) under `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/` that maps unhandled exceptions originating from the OrgChart endpoint (or globally) to a `ProblemDetails` or `BaseResponse`-shaped 500 with no exception text.
- Register via `services.AddExceptionHandler<...>()` after `UnauthorizedAccessExceptionHandler`.
- Remove the controller-level `try/catch`.
- If the fallback handler produces `ProblemDetails`, FR-1 changes accordingly: the contract advertised in `[ProducesResponseType]` must match what the middleware writes. **A is preferred because it preserves the typed OpenAPI contract; B is acceptable only if applied uniformly across all controllers (out of scope here).**

**Acceptance criteria:**
- The controller no longer contains `new { error = ..., message = ... }` or any anonymous return type.
- If A is implemented, the controller inherits from `BaseApiController` and uses `HandleResponse(result)`; the `try/catch` block is gone.
- A new unit test on `GetOrganizationStructureHandler` confirms it returns an `OrgChartResponse` with `Success == false` and `ErrorCode == ErrorCodes.InternalServerError` when `IOrgChartService.GetOrganizationStructureAsync` throws.
- The existing test `Handle_PropagatesException_WhenServiceThrows` is updated or replaced to match the new contract (handler swallows and converts to error response).

### FR-4: Preserve 200 success path behavior
The success path must remain functionally identical: `200 OK` with the populated `OrgChartResponse` (including `Organization.Positions` and nested employees). No DTO shape changes.

**Acceptance criteria:**
- Existing handler test `Handle_ReturnsServiceResponse_WhenServiceSucceeds` continues to pass.
- A new integration test against the controller confirms `200` + populated body when the mocked `IOrgChartService` returns a non-error `OrgChartResponse`.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The fix is a pure error-path refactor; the happy path adds zero work.

### NFR-2: Security
- Eliminate information leakage of configuration values and exception internals (see FR-2).
- Server-side logs must continue to capture full exception detail via structured logging (`{Url}`, exception object). Do not weaken logging.
- Aligns with `~/.claude/rules/csharp-security.md` → "Return safe client-facing messages" and "Do not expose stack traces, SQL text, or filesystem paths in API responses".

### NFR-3: Consistency
The controller must follow the same pattern as the other 40+ controllers in `backend/src/Anela.Heblo.API/Controllers/`: inherit from `BaseApiController`, return `BaseResponse`-derived bodies, rely on `HandleResponse<T>` for status-code mapping driven by `[HttpStatusCode]` on `ErrorCodes`.

### NFR-4: OpenAPI / TypeScript client compatibility
After the fix, regenerating the TypeScript client must continue to compile and must expose 500 responses with the same `OrgChartResponse` type used for 200 responses.

### NFR-5: Test coverage
Per `~/.claude/rules/csharp-testing.md`, maintain 80%+ coverage. Add the explicit failure-path test described in FR-3.

## Data Model
No data-model changes. Reuses existing types:
- `BaseResponse` (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`)
- `ErrorCodes.InternalServerError = 0010` with `[HttpStatusCode(HttpStatusCode.InternalServerError)]` (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:32-33`)
- `OrgChartResponse` and its `(ErrorCodes, Dictionary<string,string>?)` constructor (`OrgChartResponse.cs:17-18`)

## API / Interface Design

### `GET /api/OrgChart`
Unchanged route, auth (`[Authorize]`), and success contract.

**Response — 200 OK**
```json
{
  "success": true,
  "errorCode": null,
  "params": null,
  "organization": { "name": "...", "positions": [ /* ... */ ] }
}
```

**Response — 500 Internal Server Error (NEW shape)**
```json
{
  "success": false,
  "errorCode": "InternalServerError",
  "params": null,
  "organization": { "name": null, "positions": [] }
}
```
(`errorCode` serialization format — string vs. integer — follows whatever JsonSerializerOptions the API already uses for other `BaseResponse` errors; do not introduce a new convention.)

### Controller signature after fix (option A)
```csharp
public class OrgChartController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OrgChartResponse>> GetOrganizationStructure(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrganizationStructureRequest(), cancellationToken);
        return HandleResponse(result);
    }
}
```

### Handler signature after fix (option A)
```csharp
public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken ct)
{
    try
    {
        return await _orgChartService.GetOrganizationStructureAsync(ct);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error fetching organizational structure");
        return new OrgChartResponse(ErrorCodes.InternalServerError);
    }
}
```

## Dependencies
- `Anela.Heblo.Application.Shared.BaseResponse` and `ErrorCodes` (existing).
- `Anela.Heblo.API.Controllers.BaseApiController.HandleResponse<T>` (existing).
- `Microsoft.Extensions.Logging` (existing).
- OpenAPI / TypeScript client regeneration tooling — see `docs/development/api-client-generation.md` (the OpenAPI TypeScript client is auto-generated on build).

No new packages.

## Out of Scope
- Refactoring `OrgChartService` exception strategy (it will keep throwing wrapped `InvalidOperationException`; the handler will catch it).
- Removing the `_options.DataSourceUrl` interpolation inside `OrgChartService`'s wrapped exception messages — those strings continue to be logged server-side and are no longer forwarded to clients per FR-2.
- Introducing a global unhandled-exception middleware that replaces all controller-level `try/catch` blocks across the codebase. Other controllers also contain `catch (Exception)` blocks (`SmartsuppWebhookController`, `LeafletController`, `BankStatementsController`, etc.); auditing them is a separate effort.
- Localization of the new error response. `OrgChartResponse(ErrorCodes.InternalServerError)` will pass `Params = null`; any localization layer already wired to `ErrorCode` continues to work unchanged.
- Changing the 200-response shape, route, auth, or any DTO fields.

## Open Questions
None.

## Status: COMPLETE
```