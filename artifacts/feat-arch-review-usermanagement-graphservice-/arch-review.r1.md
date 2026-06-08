I have enough context. Writing the architecture review now.

```markdown
# Architecture Review: Fix Error Handling in GetGroupMembers Flow

## Skip Design: true

This is a backend-only error-handling refactor. No new UI components, screens, or visual design decisions are required. The frontend may eventually surface the new `ErrorCode` to users, but that work is explicitly **out of scope** for this change (per spec).

## Architectural Fit Assessment

The proposal aligns cleanly with the codebase's prevailing pattern for cross-cutting error handling:

- **`BaseResponse` envelope** (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs:6-62`) — all MediatR responses inherit `Success` / `ErrorCode` / `Params`. There is **no `Result<T>` convention** anywhere in the codebase; every handler uses exception-based flow plus this envelope. Spec correctly excludes Option B.
- **`ErrorCodes` is an `enum`, not string constants** (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:11`). Each value carries an `[HttpStatusCode]` attribute that `BaseApiController.HandleResponse` uses to derive the HTTP status. The spec text refers to the field as `string?` — this is a documentation slip; the actual type is `ErrorCodes?`.
- **Catch-at-handler-boundary precedent exists**: `Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs:38-63` is the model — the service throws `MsalException` / `HttpRequestException` / domain-specific exceptions; the handler catches each and maps to a specific `ErrorCodes` value. This refactor brings `GetGroupMembers` into conformance with that established pattern.
- **Global exception middleware exists** (`UnauthorizedAccessExceptionHandler` at `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs:14`) — it maps any *uncaught* `UnauthorizedAccessException` to a bare 401 ProblemDetails. Because MediatR runs inside the controller, the handler's `catch` runs first and intercepts the exception before the middleware sees it. Conclusion: **no conflict**, but the spec must keep `UnauthorizedAccessException` in the handler's catch list — letting it escape would silently change the HTTP code from 403 (Forbidden via `BaseResponse`) to 401 (Unauthorized via the global handler).
- **MockGraphService never throws** (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`) — refactor is transparent in mock auth mode.

Main integration points:
1. `GraphService.GetGroupMembersAsync` — internal `try/catch` blocks rewritten to log-and-rethrow.
2. `GetGroupMembersHandler` — catches expand from one block to four typed catches plus a fallback.
3. `GraphArticleUserResolver` — currently has no defense; once exceptions propagate, `BackfillArticleRequestedByHandler` will fault. Must be hardened (FR-4).

The fit is good. One amendment needed (see below).

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────┐    ┌──────────────────────────────────────┐
│ UserManagementController             │    │ ArticlesController                   │
│ HandleResponse() → HTTP status       │    │ (backfill endpoint)                  │
└────────────────┬─────────────────────┘    └────────────────┬─────────────────────┘
                 │ MediatR                                   │ MediatR
                 ▼                                           ▼
┌──────────────────────────────────────┐    ┌──────────────────────────────────────┐
│ GetGroupMembersHandler               │    │ BackfillArticleRequestedByHandler    │
│ ─ try { service call }               │    │ ─ try { resolver call }              │
│ ─ catch MsalException                │    │ ─ catch (Exception) → degrade        │
│   → ConfigurationError (500)         │    │   (Unresolved++ / log / continue)    │
│ ─ catch ODataError                   │    │                                      │
│   → ExternalServiceError (503)       │    │ Uses IArticleUserResolver,           │
│ ─ catch UnauthorizedAccessException  │    │ NOT IGraphService directly.          │
│   → Forbidden (403)                  │    └────────────────┬─────────────────────┘
│ ─ catch Exception                    │                     │
│   → InternalServerError (500)        │                     ▼
│                                      │    ┌──────────────────────────────────────┐
│ Logs once with {GroupId,UseCase}.    │    │ GraphArticleUserResolver              │
└────────────────┬─────────────────────┘    │ (thin adapter, no catch)              │
                 │                          └────────────────┬─────────────────────┘
                 │                                           │
                 └─────────────────┬─────────────────────────┘
                                   ▼
                  ┌────────────────────────────────────┐
                  │ IGraphService.GetGroupMembersAsync │
                  │                                    │
                  │ GraphService (real)                │
                  │ ─ logs MSAL/OData diagnostics      │
                  │ ─ throws (no catch-and-swallow)    │
                  │                                    │
                  │ MockGraphService                   │
                  │ ─ returns mock list, never throws  │
                  └────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Catch location for the backfill path
**Options considered:**
- A. Catch inside `GraphArticleUserResolver` (thin adapter swallows everything, returns empty).
- B. Catch inside `BackfillArticleRequestedByHandler` (the use case decides how to degrade).
- C. No catch; let exceptions bubble — backfill becomes a 500.

**Chosen approach:** **B — catch in `BackfillArticleRequestedByHandler`.**

**Rationale:**
- The whole point of the refactor is to stop hiding failures inside thin pass-through layers. Re-introducing a swallow inside `GraphArticleUserResolver` repeats the original sin one layer down.
- The use-case handler already owns the response envelope (`BackfillArticleRequestedByResponse : BaseResponse`) and the structured outcome counters (`Resolved`, `Unresolved`, `UnresolvedRows`). It is the right layer to translate "could not reach Graph" into a graceful outcome — e.g. an empty `members` lookup, treating every row as `Unresolved` with reason `"Graph unavailable: <ErrorCode>"`, or short-circuiting with a non-`Success` envelope.
- `GraphArticleUserResolver` remains a one-line adapter, faithful to its purpose.

**Recommended behavior in the handler:** catch `MsalException`, `ODataError`, `UnauthorizedAccessException`, and generic `Exception` around the `_userResolver.ResolveByGroupAsync(...)` call (`BackfillArticleRequestedByHandler.cs:37`). Log once with `{GroupId}` and the exception, then return a `BackfillArticleRequestedByResponse(errorCode)` constructed via the existing error-code constructor (`BackfillArticleRequestedByResponse.cs:9-12`). Map the same way as `GetGroupMembersHandler` so behavior is consistent across both Graph callers. This bounds the change and matches the BaseResponse precedent.

#### Decision 2: Use existing `ErrorCodes` enum values
**Options considered:**
- A. Reuse `ConfigurationError` / `ExternalServiceError` / `Forbidden` / `InternalServerError`.
- B. Add Graph-specific codes (`GraphTokenFailed`, `GraphApiError`, …).

**Chosen approach:** **A — reuse existing values.**

**Rationale:** Spec explicitly forbids new codes (Out of Scope). The existing codes already carry the correct HTTP status via `[HttpStatusCode]` attributes (`ErrorCodes.cs:37,41,33,391`), and `BaseApiController.HandleResponse` reads those attributes. No code change in the controller layer is required.

#### Decision 3: Log split — service emits diagnostics, handler emits context
**Chosen approach:** Service logs Graph/MSAL-specific fields (`GroupId`, MSAL `ErrorCode`, OData `Error.Code`, scope, request URL) at `LogError` immediately before rethrowing. Handler logs once at `LogError` with use-case context (`GroupId`, use-case name) and the exception object.

**Rationale:** Matches spec NFR-3. The split prevents duplicated structured fields, but the exception object is logged at both sites — that is intentional so the stack trace and Graph-specific fields appear together regardless of which log query a developer runs. The second `IGraphService` caller (`GraphArticleUserResolver` / `BackfillArticleRequestedByHandler`) benefits from the service's diagnostics without needing to know Graph internals.

#### Decision 4: `MsalException` → `ConfigurationError` (not `Unauthorized`)
**Rationale:** Token acquisition failure for **application permissions** (`GetAccessTokenForAppAsync`) signals a broken Entra app registration, expired client secret, missing Key Vault binding, or wrong tenant — none of which the end user can fix. `Unauthorized` (0013) would mislead the caller into thinking it is a user-auth problem. `ConfigurationError` (0012, HTTP 500) is correct.

By contrast, `UnauthorizedAccessException` indicates the app is authenticated but lacks the required Graph permission (`Group.Read.All`) — a permissions/consent problem, not a config problem — so it maps to `Forbidden` (0014, HTTP 403). Both mappings match the spec.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. All changes are in-place edits:

```
backend/src/Anela.Heblo.Application/Features/
├── UserManagement/
│   ├── Services/
│   │   └── GraphService.cs                          ← MODIFY: remove swallow-and-return
│   └── UseCases/GetGroupMembers/
│       └── GetGroupMembersHandler.cs                ← MODIFY: catch typed exceptions
└── Article/Admin/
    └── BackfillArticleRequestedByHandler.cs         ← MODIFY: catch & degrade gracefully

backend/test/Anela.Heblo.Tests/
├── Features/UserManagement/
│   ├── GetGroupMembersHandlerTests.cs               ← MODIFY: 4 new typed catch tests
│   └── GraphServiceTests.cs                         ← NEW (if absent) or MODIFY:
│                                                        verify rethrow + log fields
└── Article/Admin/
    └── BackfillArticleRequestedByHandlerTests.cs    ← ADD: Graph-failure degrade test
```

No DI registration changes — `UserManagementModule.AddUserManagement` is untouched.

### Interfaces and Contracts

**`IGraphService.GetGroupMembersAsync` — signature unchanged, contract changed:**
```csharp
// Before: returns [] on any Graph/MSAL/auth failure.
// After:  throws MsalException, Microsoft.Graph.Models.ODataErrors.ODataError,
//         UnauthorizedAccessException, or generic Exception on failure.
//         Returns the populated List<UserDto> on success (including empty list
//         for a genuinely empty group).
Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
```

**`GetGroupMembersHandler.Handle` — signature unchanged, behavior tightened:**
```csharp
// On success         → Success=true, Members=result, ErrorCode=null
// On MsalException   → Success=false, Members=[], ErrorCode=ConfigurationError
// On ODataError      → Success=false, Members=[], ErrorCode=ExternalServiceError
// On Unauthorized…   → Success=false, Members=[], ErrorCode=Forbidden
// On Exception       → Success=false, Members=[], ErrorCode=InternalServerError
```

**`GetGroupMembersResponse` — wire shape unchanged.** Note the actual `ErrorCode` field type is `ErrorCodes?` (nullable enum) inherited from `BaseResponse`, not `string?`. The serialized JSON is the enum **name** (default System.Text.Json behavior unless a converter is configured) — the wire format is identical to today.

**`BackfillArticleRequestedByHandler.Handle` — signature unchanged, new catch around line 37:**
- Either return an error envelope via `new BackfillArticleRequestedByResponse(mappedErrorCode)` (mirrors the existing `ValidationError` branch at `BackfillArticleRequestedByHandler.cs:32-35`), **or** treat the failure as "no resolved users" and let the existing per-row `Unresolved` path tag every row with reason `"Graph unavailable"`.

  **Recommendation:** error envelope. Backfill is an admin operation; running it across thousands of rows after Graph is unreachable produces a misleading "all unresolved" report and may stamp rows incorrectly on a future re-run if the caller misreads the success flag. Fail loudly via the envelope.

### Data Flow

**Happy path — group with members:**
```
Controller → MediatR → GetGroupMembersHandler.Handle
  → IGraphService.GetGroupMembersAsync(groupId)
    → token acquisition → HTTP GET /groups/{id}/members → parse → List<UserDto>
  ← List<UserDto> (populated)
← GetGroupMembersResponse { Success=true, Members=[...], ErrorCode=null }
← Ok(response)
```

**Happy path — empty group (the case the bug previously masked):**
```
Controller → MediatR → GetGroupMembersHandler.Handle
  → IGraphService.GetGroupMembersAsync(groupId)
    → token → HTTP 200 with `value: []` → empty List<UserDto>
  ← []
← GetGroupMembersResponse { Success=true, Members=[], ErrorCode=null }
← Ok(response)
```

**Failure path — Graph 503 (ODataError):**
```
Controller → MediatR → GetGroupMembersHandler.Handle
  → IGraphService.GetGroupMembersAsync(groupId)
    → HTTP error → ODataError thrown by Graph SDK
    → service logs {GroupId, OData Error.Code}; rethrow
  ✗ ODataError caught in handler
    → handler logs {GroupId, "GetGroupMembers"} + exception
    → return { Success=false, Members=[], ErrorCode=ExternalServiceError }
← BaseApiController.HandleResponse reads [HttpStatusCode(ServiceUnavailable)]
← StatusCode 503 with envelope JSON
```

**Failure path — MSAL token failure:**
```
… → IGraphService.GetGroupMembersAsync
  → GetAccessTokenForAppAsync throws MsalException
  → service logs {GroupId, MSAL ErrorCode, Scope}; rethrow
✗ MsalException caught in handler
  → handler logs {GroupId, "GetGroupMembers"} + exception
  → return { Success=false, Members=[], ErrorCode=ConfigurationError }
← StatusCode 500 with envelope JSON
```

**Failure path — backfill caller sees Graph error:**
```
ArticlesController → MediatR → BackfillArticleRequestedByHandler
  → _userResolver.ResolveByGroupAsync(groupId)
    → GraphArticleUserResolver._graph.GetGroupMembersAsync(...)
      → throws (any of the four types)
  ✗ caught in BackfillArticleRequestedByHandler
    → log {GroupId} + exception
    → return new BackfillArticleRequestedByResponse(mappedErrorCode)
← HandleResponse → 500/503/403 per attribute
```

**MockGraphService path:** unaffected. Mock never throws; handler's catch never fires under mock auth, frontend continues to see `Success=true` with three mock users.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `UnauthorizedAccessException` escapes the handler → `UnauthorizedAccessExceptionHandler` middleware returns bare 401 instead of `Forbidden` 403 envelope. | High | Keep `catch (UnauthorizedAccessException)` in `GetGroupMembersHandler` and `BackfillArticleRequestedByHandler`. Verify with a dedicated unit test (FR-5). |
| `BackfillArticleRequestedByHandler` does not currently catch resolver errors; once Graph propagates, the endpoint starts returning 500s with stack traces unless FR-4 is implemented in the same PR. | High | FR-4 is part of this change set — do not split it into a follow-up PR. Block the merge if `BackfillArticleRequestedByHandlerTests` has no Graph-failure test. |
| Spec ambiguity: `ErrorCode` is `ErrorCodes?` (enum), not `string?`. A developer following the spec literally might introduce a string field. | Medium | Spec amendment below clarifies this. Existing code in the handler (`GetGroupMembersHandler.cs:39`) already uses the enum correctly. |
| Forbidden handling: `BaseApiController.HandleResponse` returns `Forbid()` (`BaseApiController.cs:50`) for `ErrorCodes.Forbidden`, which does **not** include the response body. Consumers that rely on parsing `ErrorCode` from the body cannot distinguish 403 reasons. | Medium | Out of scope for this PR — pre-existing behavior of the controller base. File a follow-up if frontend needs the `ErrorCode` in 403 responses. Mention in PR description so reviewers are aware. |
| OData status detection: spec maps `ODataError` → `ExternalServiceError` (503), but the SDK also raises `ODataError` for 4xx (e.g. 404 unknown group, 400 invalid groupId). Conflating those into 503 is technically wrong. | Medium | Acceptable for this iteration — spec explicitly enumerates the four exception types and one mapping each. If finer mapping is needed, file a follow-up that inspects `odataEx.ResponseStatusCode`. Do not add the branch in this PR; it grows scope. |
| `HttpRequestException` / `OperationCanceledException` / `TaskCanceledException` (from the raw `HttpClient.SendAsync` in `GraphService.cs:98`) are **not** in the spec's four-exception list — today they hit the generic `catch (Exception)` and are swallowed; after the refactor they propagate and land in the handler's `catch (Exception)` → `InternalServerError`. | Low | Acceptable. `InternalServerError` is the safest default; tightening the mapping (e.g. canceled → 499) is a future improvement. Note in the handler test that the generic-catch branch must cover at least one of these. |
| Existing test `Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` (`GetGroupMembersHandlerTests.cs:50-67`) only asserts `Success=false` and `ErrorCode != null`, which is too loose post-refactor. | Low | Spec FR-5 already requires splitting this into typed cases. The loose assertions are fine to keep as a smoke test if the typed tests are added alongside. |
| Cached members are returned **before** the try block (`GraphService.cs:41-45`). Refactor must not break cache short-circuit. | Low | Read of `_cache` is outside any catch — refactor only touches the inner try/catch structure. Verify with an existing-cache test (call twice; second call returns cached list without touching `_tokenAcquisition`). |

## Specification Amendments

1. **`ErrorCode` type clarification** — Spec §"API / Interface Design" describes `ErrorCode` as `string?`. The actual field on `BaseResponse` (`BaseResponse.cs:16`) is `ErrorCodes?` (nullable enum). Update spec text to read:
   - `ErrorCode` field type: `ErrorCodes?` (nullable enum from `Anela.Heblo.Application.Shared.ErrorCodes`).
   - The four mapped values (`ConfigurationError`, `ExternalServiceError`, `Forbidden`, `InternalServerError`) are enum members, accessed as `ErrorCodes.ConfigurationError`, etc.
   - The JSON wire format is the enum **name** (e.g. `"ConfigurationError"`) — this is the existing serialization behavior; no JSON contract change.

2. **`ErrorCodes` is an enum, not "constants"** — Spec calls them "constants" in FR-2. Replace "constants" with "enum members" to avoid confusion when developers grep for `static readonly` fields.

3. **FR-4 binding clarification** — Spec FR-4 offers two options ("handler catches" or "resolver absorbs"). Per Decision 1 above, the chosen approach is **handler catches in `BackfillArticleRequestedByHandler`**. The resolver stays a one-line adapter. Update FR-4 to remove the ambiguity.

4. **Backfill response on Graph failure** — Spec is silent on whether the backfill returns a successful "all unresolved" envelope or a non-`Success` envelope when Graph is unreachable. Per Decision 1, the implementation MUST return `new BackfillArticleRequestedByResponse(mappedErrorCode)` with `Success = false`, so the admin caller cannot mistake the report for valid data. Add this as a sub-clause of FR-4.

5. **Keep `UnauthorizedAccessException` in the handler's catch list explicitly** — Note in NFR-3 (or as a new FR-2 sub-clause) that letting `UnauthorizedAccessException` escape would be intercepted by the global `UnauthorizedAccessExceptionHandler` middleware and converted to a bare 401 ProblemDetails, breaking the `Forbidden`/403 + envelope contract. The handler's `catch` for this type is **load-bearing**, not defensive.

## Prerequisites

None. All of the following already exist and require no preparatory work:

- `ErrorCodes.ConfigurationError`, `ExternalServiceError`, `Forbidden`, `InternalServerError` — present in `ErrorCodes.cs`.
- `BaseApiController.HandleResponse` correctly maps each via `[HttpStatusCode]` attribute.
- `Microsoft.Graph.Models.ODataErrors.ODataError`, `Microsoft.Identity.Client.MsalException` — referenced packages already in `Anela.Heblo.Application.csproj` (in use today by `GraphService.cs:75-80,178-181`).
- `Microsoft.Extensions.Logging` is wired into both handlers and the service.
- Test infrastructure (xUnit + Moq) is present; `BackfillArticleRequestedByHandlerTests` already uses Moq for `IArticleUserResolver`.
- No DI changes, no migration, no Key Vault, no environment variable, no feature flag.

Implementation can begin immediately on this branch.
```