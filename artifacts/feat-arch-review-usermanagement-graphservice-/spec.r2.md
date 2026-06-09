# Specification: Fix Error Handling in GetGroupMembers Flow

## Summary
The `GraphService.GetGroupMembersAsync` method currently swallows all exceptions and returns an empty list, making the handler's error path dead code and preventing callers from distinguishing "empty group" from "Graph API failure". This specification defines a refactor that propagates exceptions from the service and translates them at the handler boundary so the `Success` flag and `ErrorCode` accurately reflect the outcome.

## Background
The `UserManagement` module exposes a `GetGroupMembers` use case backed by Microsoft Graph. Today the failure-handling responsibility is duplicated and incorrect:

- `GraphService.GetGroupMembersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:50-193`) catches `MsalException`, `ODataError`, `UnauthorizedAccessException`, and the outer `Exception` and returns `new List<UserDto>()` in every branch.
- `GetGroupMembersHandler` (`backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs:18-43`) wraps the call in another `try/catch` that sets `Success = false`, but this branch never fires for real Graph failures.

Consequences:
- The API contract is misleading: consumers (frontend, MCP tool) always observe `Success = true` regardless of Graph errors.
- `GetGroupMembersHandlerTests.Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` exercises a path production never reaches, giving false confidence.
- Layers duplicate the same catch-all behavior, violating single-level-of-abstraction and single-responsibility principles.
- A second production caller — `GraphArticleUserResolver` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs:19`), invoked by `BackfillArticleRequestedByHandler` (`backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs:37`) — also relies on the swallow-and-return-empty contract and has no try/catch of its own.

This refactor was filed by the daily architectural review routine on 2026-06-05.

## Functional Requirements

### FR-1: Service Propagates Exceptions
`GraphService.GetGroupMembersAsync` must no longer swallow Graph-related exceptions. Each currently-caught exception type is logged at `LogError` with Graph-/MSAL-specific structured fields and then rethrown so the caller can react.

**Acceptance criteria:**
- The method contains no `catch` block that returns `new List<UserDto>()`.
- Each of the four formerly-caught exception types (`MsalException`, `Microsoft.Graph.Models.ODataErrors.ODataError`, `UnauthorizedAccessException`, and the outer `Exception`) is either left uncaught or caught only to log-and-rethrow using `throw;` (preserving the stack trace).
- Logging emitted from the service preserves today's structured fields where applicable:
  - `GroupId` for every failure path,
  - MSAL `ErrorCode` for `MsalException`,
  - OData `Error.Code` for `ODataError`,
  - scope and request URL when available.
- Successful calls still return the populated `List<UserDto>` unchanged.

### FR-2: Handler is the Catch-and-Convert Boundary
`GetGroupMembersHandler` is the single place that translates exceptions into a `GetGroupMembersResponse`. It maps known failure modes to specific `ErrorCodes` constants that already exist in the codebase and falls back to `InternalServerError` for unexpected exceptions.

**Acceptance criteria:**
- The handler catches exceptions thrown by `IGraphService.GetGroupMembersAsync` and returns `Success = false` with a populated `ErrorCode`.
- Exception-to-`ErrorCode` mapping (all values already exist in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`):
  - `MsalException` → `ErrorCodes.ConfigurationError` (0012, HTTP 500). Token acquisition for application permissions failing indicates a broken Entra app registration, client secret, or Key Vault configuration — a server-side configuration problem.
  - `Microsoft.Graph.Models.ODataErrors.ODataError` → `ErrorCodes.ExternalServiceError` (9001, HTTP 503).
  - `UnauthorizedAccessException` → `ErrorCodes.Forbidden` (0014, HTTP 403). The Entra app is authenticated but lacks the required `Group.Read.All` consent.
  - Any other `Exception` → `ErrorCodes.InternalServerError` (0010, HTTP 500).
- No new `ErrorCodes` constants are introduced.
- On failure, `Members` is `new List<UserDto>()` (preserving the existing response shape) and `ErrorCode` is non-null.
- The handler logs each caught exception once at `LogError` with request-scoped context (e.g. `"Failed to handle GetGroupMembers for {GroupId}"`) and includes the exception object. It deliberately omits the Graph-/MSAL-specific structured fields that the service already captured.

### FR-3: Distinguishable Empty vs. Failed Responses
Callers must be able to differentiate "group is genuinely empty" from "the Graph call failed".

**Acceptance criteria:**
- A genuinely empty group returns `Success = true`, `Members = []`, `ErrorCode = null`.
- A Graph failure returns `Success = false`, `Members = []`, `ErrorCode != null`.
- A successful fetch with members returns `Success = true`, `Members = [...]`, `ErrorCode = null`.

### FR-4: Other Caller Tolerates Propagated Exceptions
`GraphArticleUserResolver` and its caller `BackfillArticleRequestedByHandler` must continue to function once exceptions propagate from `GraphService.GetGroupMembersAsync`.

**Acceptance criteria:**
- `BackfillArticleRequestedByHandler` either explicitly catches exceptions from `GraphArticleUserResolver` and degrades gracefully (matching today's "no resolved user" behavior), or `GraphArticleUserResolver` itself absorbs and logs exceptions so the backfill can proceed.
- The resolver/backfill code path does not relay Graph-specific exceptions to its own consumers as unhandled errors.
- Whichever approach is chosen is covered by a unit or integration test.

### FR-5: Test Coverage Reflects Production Behavior
The handler and service tests cover the real exception paths now that exceptions propagate.

**Acceptance criteria:**
- `GetGroupMembersHandlerTests` contains a test case for each mapped exception type (`MsalException` → `ConfigurationError`, `ODataError` → `ExternalServiceError`, `UnauthorizedAccessException` → `Forbidden`, generic `Exception` → `InternalServerError`), each asserting `Success = false`, the expected `ErrorCode`, and `Members` empty.
- The existing `Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` test is kept (covering the generic `Exception` → `InternalServerError` case) or split into the specific cases above; no test depends on the service's old swallow-and-return-empty behavior.
- New or updated `GraphService` unit tests assert that the service rethrows each of the four exception types instead of returning an empty list, and that the expected structured log fields are emitted before the rethrow.
- A test covers `FR-4`'s behavior for the `GraphArticleUserResolver` / `BackfillArticleRequestedByHandler` path.
- Test coverage for the touched files remains at or above 80%.

### FR-6: API Consumer Compatibility
Existing API and MCP-tool consumers continue to deserialize successful responses without code changes; failure responses now carry meaningful error information.

**Acceptance criteria:**
- The JSON shape of `GetGroupMembersResponse` is unchanged (same field names, same types).
- Existing successful integrations (frontend group-member list, MCP `get_group_members` tool) function identically against the refactored backend.
- Failure responses carry `Success = false` and a populated `ErrorCode`, allowing the frontend/MCP to surface a meaningful error to the user.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The refactor is purely about error-flow plumbing; the happy path executes the same Graph call and returns the same payload.

### NFR-2: Security
- No exception messages from Microsoft Graph or MSAL may leak to the API response body. Detailed exception data stays in server-side logs only; the response surfaces a stable `ErrorCode` plus a generic, user-safe message.
- No new credentials, tokens, or PII are introduced into logs beyond what is already logged today.

### NFR-3: Observability — Split Logging Policy
Logging is partitioned across the service and the handler so that each layer owns a non-overlapping set of structured fields:

- **Service (`GraphService.GetGroupMembersAsync`)** — logs at `LogError` immediately before rethrowing, with Graph-/MSAL-specific structured fields: `GroupId`, MSAL `ErrorCode`, OData `Error.Code`, scope, request URL.
- **Handler (`GetGroupMembersHandler`)** — logs at `LogError` once when it catches, with request-scoped context (e.g. `GroupId` plus use-case name); it does not duplicate the Graph-specific fields.

Both log sites include the exception object, so the stack trace may appear at most twice; the structured fields do not overlap. This matches the global C# style guidance for log-and-rethrow boundaries and ensures the other `IGraphService` caller (`GraphArticleUserResolver` / `BackfillArticleRequestedByHandler`) still benefits from MSAL/OData diagnostics without each consumer having to know Graph internals.

### NFR-4: Backward Compatibility
The wire contract (response field names and types) is unchanged. Only the runtime values of `Success` and `ErrorCode` change in failure scenarios, which is the intended fix.

### NFR-5: Maintainability
- Each layer has a single, clear responsibility: the service performs the Graph call and emits Graph-specific diagnostics on failure; the handler translates outcomes (including exceptions) into the response.
- No `try/catch` block exists solely to convert an exception into an empty collection.

## Data Model
No data-model changes. The affected types are:

- `UserDto` — unchanged.
- `GetGroupMembersResponse` — unchanged shape; fields `Success`, `ErrorCode`, `Members` now carry their intended semantics in all paths.
- `ErrorCodes` — no changes. Reuses existing constants: `ConfigurationError` (0012), `ExternalServiceError` (9001), `Forbidden` (0014), `InternalServerError` (0010).

## API / Interface Design

### `IGraphService.GetGroupMembersAsync`
Signature unchanged:
```csharp
Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken);
```
Behavior change: throws on failure (previously returned empty list on any failure). Logs Graph-/MSAL-specific structured fields before rethrowing.

### `GetGroupMembersHandler.Handle`
Signature unchanged. Behavior:
1. Call `IGraphService.GetGroupMembersAsync`.
2. On success → return `Success = true, Members = result, ErrorCode = null`.
3. On exception → log once with request-scoped context; map to `ErrorCode` per FR-2; return `Success = false, Members = [], ErrorCode = <mapped>`.

### `GraphArticleUserResolver` / `BackfillArticleRequestedByHandler`
Per FR-4, exceptions thrown by `GraphService.GetGroupMembersAsync` are handled at this layer so the backfill degrades gracefully.

### `GetGroupMembersResponse` (consumer-facing contract)
| Field | Type | Empty group | Graph failure |
|-------|------|-------------|---------------|
| `Success` | `bool` | `true` | `false` |
| `Members` | `List<UserDto>` | `[]` | `[]` |
| `ErrorCode` | `string?` | `null` | non-null (`ConfigurationError` / `ExternalServiceError` / `Forbidden` / `InternalServerError`) |

## Dependencies
- Microsoft Graph SDK (existing).
- MSAL (existing).
- The project's logging abstraction `ILogger<T>` (existing).
- Existing `ErrorCodes` constants in the application layer — no extensions required.
- Existing handler test infrastructure (xUnit + Moq, per project convention).

No new external dependencies are required.

## Out of Scope
- Changing the `GetGroupMembersResponse` shape or introducing a `Result<T>` envelope across the API. The codebase has no `Result<T>` / discriminated-union convention; every handler uses exception-based flow plus a `BaseResponse { Success, ErrorCode }` envelope. Option A (exceptions propagate, handler catches and converts) is the only choice consistent with the surrounding code.
- Refactoring other methods on `GraphService` that exhibit the same swallow-and-return-empty anti-pattern. Those should be filed as separate tickets so this PR stays focused.
- Frontend or MCP-tool changes to *surface* the new `ErrorCode` to end users (the backend will return it correctly; UI uplift is a follow-up).
- Introducing a retry policy for transient Graph failures (e.g. Polly).
- Adding any new `ErrorCodes` constants.
- Changing the broader project convention between exception-based and Result-based error handling.

## Open Questions
None.

## Status: COMPLETE