I have enough context. Writing the architecture review.

```markdown
# Architecture Review: Fix OrgChartController Error Response Contract

## Skip Design: true

This is a pure backend correctness fix — replacing an anonymous-object error return with a typed `OrgChartResponse` — with no new visual components, screens, or design decisions. The frontend implications (regenerated TypeScript types) are mechanical and transparent; the rendered error UX in `OrgChartPage.tsx` is unchanged because the page already relies on React Query's rejection state, not on response body fields.

## Architectural Fit Assessment

**Excellent fit.** The proposed change brings `OrgChartController` into conformance with the project-wide error envelope pattern that is enforced everywhere else:

- Every other module in `Anela.Heblo.Application/Features/**` returns a `BaseResponse`-derived DTO with `Success`, `ErrorCode`, and `Params`. Confirmed across 10+ handlers (e.g. `CreateJournalTagHandler.cs:33`, `DeleteJournalEntryHandler.cs:33`, `ScanPackingOrderHandler.cs:49`, `UpsertFlagOverrideHandler.cs:23`).
- `OrgChartResponse` (`backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/OrgChartResponse.cs:17-18`) already exposes the `(ErrorCodes, Dictionary<string,string>?)` constructor that the rest of the codebase uses.
- `ErrorCodes.InternalServerError = 0010` carries `[HttpStatusCode(HttpStatusCode.InternalServerError)]` (`ErrorCodes.cs:32-33`), so the wire status code is preserved.

**Integration points (verified in code, not assumed):**

1. **Local try/catch must be retained.** The only registered `IExceptionHandler` is `UnauthorizedAccessExceptionHandler` (`ServiceCollectionExtensions.cs:136`). The default `app.UseExceptionHandler()` fallback emits `ProblemDetails`, not `OrgChartResponse`. Removing the controller's catch block would re-break the contract for the wrapped `InvalidOperationException` thrown by `OrgChartService` (`OrgChartService.cs:61, 65`).
2. **`OrgChartResponse.Organization` is non-nullable (`= new()`, `OrgChartResponse.cs:13`).** On the error path it will be a default-constructed `OrganizationDto` with `Name = ""` and `Positions = []`, not `null`. This is consistent with the rest of the codebase (handlers also leave default collections initialized) and clients should branch on `success` first.
3. **No collision with existing test coverage.** `OrgChartServiceTests` is the only test file (`backend/test/Anela.Heblo.Tests/Features/OrgChart/`); there is no `OrgChartControllerTests.cs` yet. One must be added.

There is a broader architectural opportunity here — a generic `BaseResponse`-aware `IExceptionHandler` — that the spec correctly defers to a follow-up. Doing it now would balloon the scope and is unnecessary for shipping this fix.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│ HTTP GET /api/orgchart/structure                                 │
└─────────────────────────┬────────────────────────────────────────┘
                          ▼
        ┌─────────────────────────────────────────┐
        │  OrgChartController                     │
        │  ─ try/catch RETAINED                   │
        │  ─ success: return Ok(result)           │
        │  ─ error:   StatusCode(500,             │
        │             new OrgChartResponse(       │
        │               ErrorCodes.               │
        │                 InternalServerError))   │
        │  ─ _logger.LogError(ex, …) RETAINED     │
        └─────────────────┬───────────────────────┘
                          │ MediatR.Send
                          ▼
        ┌─────────────────────────────────────────┐
        │  GetOrganizationStructureHandler        │
        │  (unchanged)                            │
        └─────────────────┬───────────────────────┘
                          │
                          ▼
        ┌─────────────────────────────────────────┐
        │  OrgChartService                        │
        │  ─ wraps HttpRequestException as        │
        │    InvalidOperationException            │
        │  ─ wraps JsonException as               │
        │    InvalidOperationException            │
        │  ─ rethrows other exceptions            │
        │  (unchanged)                            │
        └─────────────────────────────────────────┘
```

The only mutated node is the controller's catch arm. Every other component is left untouched.

### Key Design Decisions

#### Decision 1: Choice of `ErrorCodes` value
**Options considered:**
- `ErrorCodes.InternalServerError = 0010` — HTTP 500, generic, no leak path.
- `ErrorCodes.Exception = 0099` — HTTP 500, but `BaseResponse(Exception)` constructor stuffs `ex.Message` and `ex.ToString()` into `Params`. This is the exact leak the spec exists to prevent.
- `ErrorCodes.ExternalServiceError = 9001` — semantically attractive (the upstream is SharePoint), but `[HttpStatusCode(HttpStatusCode.ServiceUnavailable)]` would silently change the wire status from 500 to 503, breaking the existing `[ProducesResponseType(StatusCodes.Status500InternalServerError)]` documentation contract.
- A new `OrgChart`-prefixed enum value — adds enum surface area without giving the client any new actionable information.

**Chosen approach:** `ErrorCodes.InternalServerError`.

**Rationale:** Preserves the HTTP 500 contract, avoids the `Exception`-constructor leak path, and avoids introducing a new enum value purely for a generic 500. Matches `[ProducesResponseType(StatusCodes.Status500InternalServerError)]` already declared on the controller (`OrgChartController.cs:37`).

#### Decision 2: Retain local try/catch instead of removing it in favor of a global handler
**Options considered:**
- Retain controller-local try/catch with typed return.
- Remove try/catch, write a new `BaseResponse`-aware `IExceptionHandler` that emits a typed response for any controller returning a `BaseResponse`-derived DTO.

**Chosen approach:** Retain.

**Rationale:** The codebase has no general-purpose `IExceptionHandler` — only `UnauthorizedAccessExceptionHandler`. Writing one is a cross-cutting change with knock-on implications for every other controller that does typed error returns inline. The spec correctly tracks this as a follow-up. Shipping the surgical fix now is cheaper, lower-risk, and unblocks the leak immediately.

#### Decision 3: Single ownership of error logging
**Chosen approach:** Controller is the sole owner of `LogError(ex, …)`. `OrgChartService` continues to *not* log on its own catch arms (verified at `OrgChartService.cs:59-70` and asserted by `OrgChartServiceTests.VerifyNoErrorLog`).

**Rationale:** Avoids double-logging the same exception. Matches the convention already enforced by the existing test suite.

## Implementation Guidance

### Directory / Module Structure

No new files in production code. One new test file.

**Modified:**
- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` — lines 50-51 only.

**Created:**
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartControllerTests.cs` — controller-level tests for the catch branch (see Test Coverage below).

**Frontend regeneration (no hand-edits):**
- `frontend/src/api/generated/api-client.ts` (or wherever the build pipeline emits the OpenAPI client) — regenerated by `npm run build` in `frontend/`.

### Interfaces and Contracts

No new interfaces. The controller-handler-service chain keeps its existing signatures:

```csharp
// OrgChartController.cs (signature unchanged)
public async Task<ActionResult<OrgChartResponse>> GetOrganizationStructure(CancellationToken ct);

// OrgChartResponse.cs (constructor already exists, unchanged)
public OrgChartResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null);

// ErrorCodes.cs (value already exists, unchanged)
[HttpStatusCode(HttpStatusCode.InternalServerError)] InternalServerError = 0010
```

**Wire contract — error response (after fix):**

```json
HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{
  "success": false,
  "errorCode": "InternalServerError",
  "params": null,
  "organization": { "name": "", "positions": [] }
}
```

Note: `organization` is present (not `null`) because `OrgChartResponse.Organization` is initialized to `new OrganizationDto()`. This is acceptable — clients must branch on `success === false` before reading `organization`. Matches the rest of the codebase (other `*Response` types also leave default collections populated on error).

### Data Flow

**Success path (unchanged):**
1. Controller `Ok(result)` → 200 with full `OrgChartResponse` body.

**Error path (changed):**
1. `OrgChartService` throws (wrapped `InvalidOperationException` from `HttpRequestException`/`JsonException`, or generic exception rethrown).
2. `GetOrganizationStructureHandler` propagates.
3. Controller catches `Exception ex` → `LogError(ex, "Error fetching organizational structure")` (full detail, server-side only).
4. Controller returns `StatusCode(500, new OrgChartResponse(ErrorCodes.InternalServerError))`.
5. ASP.NET serializes `OrgChartResponse` → JSON with `success: false`, `errorCode: "InternalServerError"`, default `organization`.

### Test Coverage (new tests to add)

Create `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartControllerTests.cs` using `Mock<IMediator>` (no `WebApplicationFactory` needed for unit-level coverage of the catch arm):

| Test | Scenario | Assertions |
|------|----------|------------|
| `GetOrganizationStructure_ReturnsOk_WhenHandlerSucceeds` | Mediator returns a populated `OrgChartResponse` | `ActionResult` is `OkObjectResult`, body equals input |
| `GetOrganizationStructure_Returns500_WithTypedErrorResponse_WhenHandlerThrows` | Mediator throws `InvalidOperationException` | Result is `ObjectResult`, `StatusCode == 500`, body is `OrgChartResponse` with `Success == false`, `ErrorCode == ErrorCodes.InternalServerError` |
| `GetOrganizationStructure_DoesNotLeakExceptionMessage_WhenHandlerThrows` | Mediator throws with marker string `"SECRET-MARKER-http://internal-sharepoint/..."` in `Message` | Serialize the returned body to JSON; assert the marker substring is absent (covers FR-2) |
| `GetOrganizationStructure_LogsExceptionWithFullDetail_WhenHandlerThrows` | Mediator throws | Verify `_logger.LogError(ex, "Error fetching organizational structure")` invoked once at `LogLevel.Error` |

The existing `OrgChartServiceTests` are untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Regression: `BaseResponse(Exception)` constructor used by mistake during implementation, re-introducing the leak | High | Test `GetOrganizationStructure_DoesNotLeakExceptionMessage_WhenHandlerThrows` asserts marker string absence. Spec FR-1 explicitly forbids that constructor. |
| Silent HTTP status downgrade (500 → 503) from picking `ErrorCodes.ExternalServiceError` | Medium | Decision 1 above rejects 9001 with explicit rationale. Test asserts `StatusCode == 500`. |
| Frontend build fails because regenerated `OrgChartResponse` type collides with existing call site | Low | Verified — `useOrgChart.ts:8-19` returns the raw response object untouched; `OrgChartPage.tsx` doesn't read `error`/`message` body fields. The new shape is a strict superset of today's typed surface (`success`/`errorCode`/`params` were already on the success type via inheritance). |
| `OrgChartResponse.Organization` non-null on error confuses naïve clients that don't check `success` first | Low | Documented in spec & in this review. Matches existing project pattern; no client today inspects the body on error. |
| Wider audit: other controllers (e.g. `BankStatementsController:62, 67`) also return anonymous error objects | Low | Out of scope per spec. Track as separate arch-review finding — do **not** expand this PR. |
| Hidden coupling: a future global `IExceptionHandler` that catches everything could double-handle exceptions if the controller's try/catch is also present | Low | Acceptable for now: any future global handler will be additive and can short-circuit when the controller already produced a typed response. Spec correctly defers this. |

## Specification Amendments

The spec is solid. Two small corrections:

1. **FR-1 acceptance criteria mentions `Departments` and "root org node":**
   > the data-bearing properties (e.g. `Departments`, root org node) are `null`/empty rather than partially populated.

   `OrgChartResponse` does **not** have a `Departments` property — it has a single `Organization` property of type `OrganizationDto` (which contains `Name` and `Positions`). Replace with:

   > the data-bearing property `Organization` is a default-initialized `OrganizationDto` (empty `Name`, empty `Positions` list) rather than partially populated. (`Organization` is initialized to `new OrganizationDto()` by the response class default and cannot be `null` on the wire.)

2. **FR-5 — "snapshot of a successful response before and after the change is byte-identical":**
   The success response is **already** non-bit-identical to FR-5's wording: the existing success response already includes `success: true`, `errorCode: null`, `params: null` (inherited from `BaseResponse`). The controller change does not touch the success branch, so this trivially holds, but the phrasing "modulo any field set by `BaseResponse` that is already present" is correct and should be preserved verbatim. No content change needed — flagging for review.

## Prerequisites

None. All required types already exist:

- `OrgChartResponse(ErrorCodes errorCode)` constructor — present (`OrgChartResponse.cs:17`).
- `ErrorCodes.InternalServerError` enum value with `HttpStatusCode.InternalServerError` attribute — present (`ErrorCodes.cs:32-33`).
- Logging policy (`_logger.LogError(ex, …)` in controller, no error logging in service) — already in place and asserted by existing tests.
- OpenAPI/TypeScript regeneration is automatic on `npm run build` — no manual pipeline changes needed.

Implementation can begin immediately.
```