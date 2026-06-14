# Specification: Fix OrgChartController Error Response Contract

## Summary
The `OrgChartController.GetOrganizationStructure` endpoint currently returns an anonymous object on errors that violates its declared `ActionResult<OrgChartResponse>` contract and leaks raw exception messages to clients. This spec replaces the error path with a typed `OrgChartResponse` error and sanitizes the response, aligning the endpoint with the rest of the codebase and the generated OpenAPI/TypeScript client.

## Background
The codebase uses a consistent typed-error pattern: every controller returns `BaseResponse`-derived DTOs whose `Success`, `ErrorCode`, and `Params` fields express both success and failure cases. The TypeScript client is generated from the OpenAPI spec, so any response shape that is not declared on the endpoint becomes invisible to the client — fields show up as `undefined` and callers cannot react to errors in a typed way.

`OrgChartController.GetOrganizationStructure` is the only module in the project that deviates from this pattern. Its catch block constructs an anonymous `{ error, message }` object, which:

1. **Breaks the contract** — `OrgChartResponse` is the declared return type; the anonymous object is not assignable to it. The generated TypeScript client receives `OrgChartResponse | undefined` and silently treats the error response as a malformed success.
2. **Leaks internal details** — `ex.Message` may contain the configured `DataSourceUrl` (the SharePoint endpoint), connection-string fragments, or wrapped `InvalidOperationException` messages produced inside `OrgChartService`. These reach the browser unfiltered.
3. **Diverges from convention** — `OrgChartResponse` already has a constructor (`OrgChartResponse.cs:17-18`) that takes an `ErrorCodes` value, designed exactly for this purpose.

This was filed by the daily arch-review routine on 2026-06-04.

## Functional Requirements

### FR-1: Typed error response on `GetOrganizationStructure` failure
When `GetOrganizationStructure` catches an unhandled exception, the response body MUST be a serialized `OrgChartResponse` constructed via the existing `OrgChartResponse(ErrorCodes errorCode)` constructor.

**Acceptance criteria:**
- The 500 response body deserializes successfully into `OrgChartResponse` on the client.
- `Success` is `false`, `ErrorCode` is `ErrorCodes.ServerError` (or a more specific code if one fits — see Open Questions), and the data-bearing properties (e.g. `Departments`, root org node) are `null`/empty rather than partially populated.
- The HTTP status code remains `500 Internal Server Error`.
- The `Content-Type` is `application/json` and matches the success response.

### FR-2: No raw exception details in the response
The response body MUST NOT contain `ex.Message`, `ex.StackTrace`, the underlying inner exception text, or any string built from those values.

**Acceptance criteria:**
- Inspecting the JSON response for any failure mode (transport error, parse error, upstream 404, etc.) shows no SharePoint URL, no stack frame, and no internal class names.
- Confidential details (configured URLs, secrets, host names) are confirmed absent via a unit test that injects a service throwing an exception whose `Message` contains a marker string; the response body must not contain the marker.

### FR-3: Full exception detail is still logged server-side
The existing `_logger.LogError(ex, ...)` call MUST be preserved so operators retain the diagnostic information that is now removed from the response.

**Acceptance criteria:**
- Log output for a forced failure contains the full exception (message + stack trace) at `Error` level with the existing message template.
- No log statements are removed or downgraded in severity.

### FR-4: Generated TypeScript client recognizes the error shape
After regenerating the OpenAPI client, the TypeScript `OrgChartResponse` type MUST include the fields needed to detect and react to the error case (`success`, `errorCode`, `params`).

**Acceptance criteria:**
- Running `npm run build` in `frontend/` regenerates `OrgChartResponse` such that `success`, `errorCode`, and `params` are typed properties (matching `BaseResponse`).
- A frontend caller can branch on `response.success === false` without TypeScript errors.

### FR-5: Existing success path is unchanged
The success behaviour, status code, and response body of `GetOrganizationStructure` MUST remain bit-for-bit identical to today's output (modulo any field set by `BaseResponse` that is already present).

**Acceptance criteria:**
- Existing unit/integration tests covering the happy path continue to pass without modification.
- A snapshot of a successful response before and after the change is byte-identical for the data-bearing fields.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The fix is a single allocation swap inside a catch block on the error path; it must not add latency to the success path.

### NFR-2: Security
- Eliminates information leakage of internal infrastructure URLs and exception text to unauthenticated and authenticated clients alike.
- Logging policy is unchanged — operators still see full detail server-side.
- No new secrets, new endpoints, or new authentication surface.

### NFR-3: Compatibility
- HTTP status code (`500`) is unchanged.
- The generated TypeScript client must continue to compile. Any frontend code currently relying on the `{ error, message }` shape (if it exists) must be updated as part of this change.

### NFR-4: Consistency
The endpoint must conform to the project-wide pattern: typed `BaseResponse`-derived DTOs with `Success`/`ErrorCode`/`Params`. No new error-envelope shapes are introduced.

## Data Model

No schema changes. Reuses existing types:

- **`OrgChartResponse`** (`backend/src/Anela.Heblo.API/.../OrgChartResponse.cs`) — already inherits the `BaseResponse` family via its existing `(ErrorCodes)` constructor.
- **`ErrorCodes`** — existing enum; the relevant value is `ServerError` unless a more specific upstream-failure code already exists (see Open Questions).
- **`BaseResponse`** — existing base class exposing `Success`, `ErrorCode`, `Params`.

## API / Interface Design

### Endpoint (unchanged signature)
`GET /api/orgchart/structure` — `ActionResult<OrgChartResponse>`

### Success response (unchanged)
```
HTTP/1.1 200 OK
Content-Type: application/json

{ "success": true, "errorCode": null, "params": null, ...<existing fields>... }
```

### Error response (changed)
Before:
```
HTTP/1.1 500 Internal Server Error
{ "error": "Failed to fetch organizational structure",
  "message": "Connection refused to http://internal-sharepoint/..." }
```

After:
```
HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{ "success": false, "errorCode": "ServerError", "params": null }
```

### Controller change (illustrative)
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error fetching organizational structure");
    return StatusCode(StatusCodes.Status500InternalServerError,
        new OrgChartResponse(ErrorCodes.ServerError));
}
```

### Alternative considered: global exception middleware
If the project already has global exception-handling middleware that produces a `BaseResponse`-shaped 500 for unhandled exceptions, the preferred change is to **remove the try/catch entirely** and let the middleware handle it. The architect should verify which path the rest of the codebase uses and apply the same.

## Dependencies

- `OrgChartResponse` class and its `(ErrorCodes)` constructor — already in place.
- `ErrorCodes` enum — already in place.
- OpenAPI client generation pipeline (`npm run build` in `frontend/`) — required to refresh the TypeScript types.
- (Conditional) global exception-handling middleware, if it exists in the project.

## Out of Scope

- Refactoring `OrgChartService` exception types or messages.
- Introducing new `ErrorCodes` enum values beyond what already exists (unless a fitting one is already present and obviously better than `ServerError`).
- Changes to authentication, authorization, or rate limiting on the endpoint.
- Audit of other controllers for similar issues (handled separately by the arch-review routine).
- Frontend UX changes for error display — only the type contract is in scope; visual treatment of the error state is a follow-up if needed.
- Changes to logging sinks, formats, or correlation IDs.

## Open Questions

1. **Global exception middleware**: Does the project already register a global exception handler that converts unhandled exceptions to a typed `BaseResponse`-shaped 500? If yes, the try/catch should be removed entirely instead of replaced. If no, the typed catch-block replacement is the correct change.
2. **Specific error code**: Is there a more specific `ErrorCodes` value (e.g. `ExternalServiceUnavailable`, `UpstreamFailure`) that better describes a SharePoint fetch failure than the generic `ServerError`? If one exists, prefer it; otherwise default to `ServerError`.
3. **Frontend consumers**: Does any existing frontend code currently read `error`/`message` fields from the 500 response? If yes, those call sites need to be updated to read `success`/`errorCode` instead. (A quick repo-wide search for `"Failed to fetch organizational structure"` and consumers of the orgchart endpoint should answer this.)

## Status: HAS_QUESTIONS