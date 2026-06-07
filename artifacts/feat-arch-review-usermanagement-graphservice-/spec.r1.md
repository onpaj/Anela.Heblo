# Specification: Fix Error Handling in GetGroupMembers Flow

## Summary
The `GraphService.GetGroupMembersAsync` method currently swallows all exceptions and returns an empty list, making the handler's error path dead code and preventing callers from distinguishing "empty group" from "Graph API failure". This specification defines a refactor that establishes a single, clear error-handling boundary so the `Success` flag accurately reflects the outcome of the operation.

## Background
The `UserManagement` module exposes a `GetGroupMembers` use case backed by Microsoft Graph. Today the failure-handling responsibility is duplicated and incorrect:

- `GraphService.GetGroupMembersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:50-193`) catches `MsalException`, `ODataError`, `UnauthorizedAccessException`, and the outer `Exception` and returns `new List<UserDto>()` in every branch.
- `GetGroupMembersHandler` (`backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs:18-43`) wraps the call in another `try/catch` that sets `Success = false`, but this branch never fires for real Graph failures.

Consequences:
- The API contract is misleading: consumers (frontend, MCP tool) always observe `Success = true` regardless of Graph errors.
- `GetGroupMembersHandlerTests.Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` exercises a path production never reaches, giving false confidence.
- Layers duplicate the same catch-all behavior, violating single-level-of-abstraction and single-responsibility principles.

This refactor was filed by the daily architectural review routine on 2026-06-05.

## Functional Requirements

### FR-1: Service Propagates Exceptions
`GraphService.GetGroupMembersAsync` must no longer swallow Graph-related exceptions. It logs the failure (preserving current log context — exception type, group id, correlation id where available) and then rethrows so the caller can react.

**Acceptance criteria:**
- The method contains no `catch` block that returns `new List<UserDto>()`.
- Each formerly-caught exception type (`MsalException`, `ODataError`, `UnauthorizedAccessException`, and the general `Exception`) either has no catch at all, or has a catch that logs and rethrows (using `throw;` to preserve the stack trace).
- Logging output for failure cases is at least as informative as today (same log level, same fields).
- Successful calls still return the populated `List<UserDto>` unchanged.

### FR-2: Handler is the Single Catch-and-Convert Boundary
`GetGroupMembersHandler` is the single place that translates exceptions into a `GetGroupMembersResponse`. It maps known failure modes to specific error codes and falls back to `InternalServerError` for unexpected exceptions.

**Acceptance criteria:**
- The handler catches exceptions from the service and returns `Success = false` with an appropriate `ErrorCode`.
- Exception-to-`ErrorCode` mapping:
  - `MsalException` → an authentication error code (new or existing, e.g. `ExternalAuthenticationFailed`).
  - `ODataError` → `ExternalServiceError` (or equivalent existing code) with the OData error message preserved in logs.
  - `UnauthorizedAccessException` → `Forbidden` (or equivalent existing code).
  - Any other `Exception` → `InternalServerError`.
- On failure, `Members` is `new List<UserDto>()` (preserving the existing response shape) and the response includes a non-null `ErrorCode`.
- The exception is logged once, at the handler level, with the request payload and the exception details.

### FR-3: Distinguishable Empty vs. Failed Responses
Callers must be able to differentiate "group is genuinely empty" from "the Graph call failed".

**Acceptance criteria:**
- A genuinely empty group returns `Success = true`, `Members = []`, `ErrorCode = null`.
- A Graph failure returns `Success = false`, `Members = []`, `ErrorCode != null`.
- A successful fetch with members returns `Success = true`, `Members = [...]`, `ErrorCode = null`.

### FR-4: Test Coverage Reflects Production Behavior
The handler and service tests cover the real exception paths now that exceptions propagate.

**Acceptance criteria:**
- `GetGroupMembersHandlerTests` contains test cases for each mapped exception type (`MsalException`, `ODataError`, `UnauthorizedAccessException`, generic `Exception`), each asserting the expected `Success`, `ErrorCode`, and that `Members` is empty.
- The existing `Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` test is kept (covering generic `Exception`) or split into the specific cases above; no test depends on the service's old swallow-and-return-empty behavior.
- New or updated `GraphService` unit tests assert that the service rethrows for each of the four exception types instead of returning an empty list.
- Test coverage for the touched files remains at or above 80%.

### FR-5: API Consumer Compatibility
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

### NFR-3: Observability
- Every failure path must emit a single, structured log entry at `Error` (or the existing level used today, whichever is higher) including: exception type, group id, correlation id (if available), and exception message + stack trace.
- Avoid double-logging: if the handler logs the exception, the service should not also log the same exception with the same severity. Choose one canonical log site (recommended: the handler, since it has full request context).

### NFR-4: Backward Compatibility
The wire contract (response field names and types) is unchanged. Only the runtime values of `Success` and `ErrorCode` change in failure scenarios, which is the intended fix.

### NFR-5: Maintainability
- Each layer has a single, clear responsibility: the service performs the Graph call; the handler translates outcomes (including exceptions) into the response.
- No `try/catch` block exists solely to convert an exception into an empty collection.

## Data Model
No data-model changes. The affected types are:

- `UserDto` — unchanged.
- `GetGroupMembersResponse` — unchanged shape; fields `Success`, `ErrorCode`, `Members` now carry their intended semantics in all paths.
- `ErrorCodes` — may gain a small number of new constants (e.g. `ExternalAuthenticationFailed`, `ExternalServiceError`) if equivalents do not already exist. Implementer should reuse existing codes wherever they cover the case.

## API / Interface Design

### `IGraphService.GetGroupMembersAsync`
Signature unchanged:
```csharp
Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken);
```
Behavior change: throws on failure (previously returned empty list on any failure).

### `GetGroupMembersHandler.Handle`
Signature unchanged. Behavior:
1. Call `IGraphService.GetGroupMembersAsync`.
2. On success → return `Success = true, Members = result, ErrorCode = null`.
3. On exception → log once with full context; map to `ErrorCode` per FR-2; return `Success = false, Members = [], ErrorCode = <mapped>`.

### `GetGroupMembersResponse` (consumer-facing contract)
| Field | Type | Empty group | Graph failure |
|-------|------|-------------|---------------|
| `Success` | `bool` | `true` | `false` |
| `Members` | `List<UserDto>` | `[]` | `[]` |
| `ErrorCode` | `string?` | `null` | non-null |

## Dependencies
- Microsoft Graph SDK (existing).
- MSAL (existing).
- The project's logging abstraction (existing — likely `ILogger<T>`).
- Existing `ErrorCodes` constants in the application layer (extend if needed).
- Existing handler test infrastructure (xUnit/NUnit + Moq or equivalent — implementer should follow project convention).

No new external dependencies are required.

## Out of Scope
- Changing the `GetGroupMembersResponse` shape (e.g. introducing a `Result<T>` envelope across the API).
- Refactoring other methods on `GraphService` that exhibit the same anti-pattern. Those should be filed as separate tickets so this PR stays focused and reviewable.
- Frontend or MCP-tool changes to *surface* the new `ErrorCode` to end users (the backend will return it correctly; UI uplift is a follow-up).
- Introducing a retry policy for transient Graph failures (e.g. Polly). Retries can be layered on later once the error path is honest.
- Changing the broader project convention between exception-based and Result-based error handling.

## Open Questions

1. **Specific `ErrorCode` values for MSAL / OData / Unauthorized exceptions.** The codebase likely already has a partial set in `ErrorCodes`. Implementer should reuse existing codes where they fit; if none exist for `ExternalAuthenticationFailed` / `ExternalServiceError` / `Forbidden`, confirm with the team before adding new constants.
2. **Canonical log site.** This spec recommends the handler as the single log site (since it has the request context and is the catch-and-convert boundary). Confirm that the service does not have other callers that would lose logging if the service's catches are removed entirely.
3. **Assumption flagged: chose Option A (exceptions propagate, handler catches once).** Option B (service returns `Result<T>`) was deferred because it would require a broader project-wide convention change and would touch every consumer of `IGraphService`. If the team has already adopted a Result/discriminated-union convention elsewhere, revisit this choice before implementation.

## Status: HAS_QUESTIONS