# Specification: Fix silently-swallowed HTTP error in `GraphService.GetGroupMembersAsync`

## Summary
`GraphService.GetGroupMembersAsync` currently returns an empty list when Microsoft Graph responds with a non-success HTTP status (403, 404, 429, 500, etc.), instead of throwing the `GraphServiceException` that its interface contract (`IGraphService`) documents and that downstream callers already catch. This spec covers making the non-success branch throw `GraphServiceException`, consistent with the sibling `ODataError` catch block a few lines below it, and updating the one existing test that currently locks in the broken behavior.

## Background
`IGraphService.GetGroupMembersAsync` is documented to throw `GraphServiceException` "when Microsoft Graph returns an OData error response," and callers are written against that contract:

- `GetGroupMembersHandler` catches `GraphServiceException` and maps it to `ErrorCode = ExternalServiceError` with `Success = false`.
- `GraphArticleUserResolver.ResolveByGroupAsync` catches `GraphServiceException` and rethrows it as `ArticleUserResolverServiceException`.

`GraphService.GetGroupMembersAsync` (backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:140-150) correctly follows this contract for the `ODataError` exception case (line 174, re-thrown as `GraphServiceException`), but the `!response.IsSuccessStatusCode` branch just above it (line 149) logs the error and returns `new List<UserDto>()` instead. This is an unintended asymmetry: both branches represent the same class of failure (Graph rejected the request), but only one of them honors the interface contract.

The practical effect: on throttling (429), permission changes (403), a deleted group (404), or a Graph outage (5xx), callers receive `{ Success: true, Members: [] }` (via `GetGroupMembersHandler`) or a silently-empty resolver result (via `GraphArticleUserResolver`) — indistinguishable from "the group genuinely has zero members." The failure is logged server-side but invisible to the caller and to the end user, which is a Liskov Substitution Principle violation against the documented `IGraphService` contract and makes real outages hard to diagnose.

This is a narrowly-scoped bug fix identified by the daily architecture-review routine (2026-07-21), not a redesign of `GraphService` or its callers.

## Functional Requirements

### FR-1: Throw `GraphServiceException` on non-success HTTP response in `GetGroupMembersAsync`
In `GraphService.GetGroupMembersAsync`, replace the `return new List<UserDto>();` statement in the `!response.IsSuccessStatusCode` branch (GraphService.cs:140-150) with a thrown `GraphServiceException`, mirroring the existing `ODataError` catch block's exception message style and using the already-read `errorContent` / `response.StatusCode` / `groupId` values.

The existing `_logger.LogError(...)` call (and the `LogDebug` header dump) in that branch must be retained as-is — only the `return` is replaced.

**Acceptance criteria:**
- When the Graph HTTP call returns any non-success status code (400, 401, 403, 404, 429, 500, 503, etc.), `GetGroupMembersAsync` throws `GraphServiceException` instead of returning an empty list.
- The thrown `GraphServiceException`'s message includes the HTTP status code and the `groupId`, matching the style suggested in the brief (e.g. `"Microsoft Graph returned {(int)response.StatusCode} for group {groupId}."`).
- The `GraphServiceException` wraps a meaningful inner exception (e.g. `new HttpRequestException(errorContent)`) so the raw Graph error body is preserved for diagnostics without being lost.
- No successful-response behavior changes: `IsSuccessStatusCode == true` continues to parse, cache, and return members exactly as before.
- The existing `_logger.LogError` call in this branch continues to fire before the exception is thrown, preserving current log-based diagnosability.
- This is the *only* code change in `GetGroupMembersAsync`. The `ODataError` catch block, the cache-hit path, the `MsalException` / `UnauthorizedAccessException` / generic `Exception` catch blocks, and the method signature are unchanged.

### FR-2: Update the existing test that locks in the swallow behavior
`GraphServiceTests.GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList` (backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs:438-450) currently asserts `result.Should().BeEmpty()` for an HTTP 403 response. This test encodes the bug and must be updated to assert the new, correct behavior.

**Acceptance criteria:**
- The test (renamed to reflect the new behavior, e.g. `GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException`) asserts that calling `GetGroupMembersAsync` with a mocked HTTP 403 (or similar non-success) response throws `GraphServiceException` (e.g. via `Assert.ThrowsAsync<GraphServiceException>(...)`).
- No other existing test in `GraphServiceTests.cs`, `GetGroupMembersHandlerTests.cs`, or any `GraphArticleUserResolver` test regresses. In particular, tests that already assert `catch (GraphServiceException)` handling in `GetGroupMembersHandler` continue to pass unchanged, since those callers were already written against the documented contract.
- (Recommended, not required) Add a second test case covering a different non-success status (e.g. 429 or 500) to confirm the fix isn't status-code-specific, and/or assert the thrown exception's message contains the status code and group id.

## Non-Functional Requirements

### NFR-1: No behavior change to unrelated call paths
`GraphService.SearchUsersAsync` and `GraphService.GetAppRoleMembersAsync` have their own independent non-success-response handling (each already wrapped in broad `catch (Exception)` blocks that return an empty list) and are **out of scope** for this fix — the brief identifies only `GetGroupMembersAsync`'s non-success branch. `GetAppRoleMembersAsync` internally calls `GetGroupMembersAsync` (GraphService.cs:383) to expand nested-group members; because that call site is inside `GetAppRoleMembersAsync`'s own top-level `catch (Exception ex)` (GraphService.cs:448-452), a newly-thrown `GraphServiceException` from a failing nested group will still result in `GetAppRoleMembersAsync` returning an empty list overall — the same externally-observable outcome as before, just via a caught exception instead of a propagated empty list. No code change is needed there, but this interaction should be covered by regression testing (existing `GetAppRoleMembersAsync_*` tests must still pass).

### NFR-2: Diagnosability
Server-side logs must continue to record the Graph status code, request URL, and response body on failure (already implemented via the existing `_logger.LogError` in the branch being fixed) — this requirement is satisfied by retaining the existing logging call, not by adding new logging.

## Data Model
No data model changes. No new types beyond using the existing `GraphServiceException` (backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs), which already takes `(string message, Exception innerException)`.

## API / Interface Design
No public API surface changes. `IGraphService.GetGroupMembersAsync`'s signature and XML-doc contract are unchanged — this fix makes the implementation conform to the contract that already exists. Downstream HTTP-facing behavior changes only in the failure case:

- Before: Graph HTTP failure → `GetGroupMembersHandler` returns `200 OK` with `{ Success: true, Members: [] }`.
- After: Graph HTTP failure → `GraphServiceException` is thrown → caught by `GetGroupMembersHandler`'s existing `catch (GraphServiceException ex)` block → returns `{ Success: false, ErrorCode: ExternalServiceError, Members: [] }` (no handler code change required; this path already exists and is already tested).

Similarly for `GraphArticleUserResolver.ResolveByGroupAsync`: Graph HTTP failure now reaches the existing `catch (GraphServiceException ex)` block and is rethrown as `ArticleUserResolverServiceException`, instead of silently returning an empty `IReadOnlyList<ArticleUserMatch>`.

## Dependencies
- No new external dependencies.
- Depends on the existing `GraphServiceException` type and the existing catch blocks in `GetGroupMembersHandler` and `GraphArticleUserResolver` — both already implemented and already exercised by tests for the `ODataError` path, so no changes are needed in those two files.

## Out of Scope
- Changes to `SearchUsersAsync` or `GetAppRoleMembersAsync`'s own non-success-response handling (they have separate, pre-existing swallow patterns not flagged by this brief).
- Any change to `GetGroupMembersHandler` or `GraphArticleUserResolver` — their `catch (GraphServiceException)` blocks already exist and require no modification.
- Retry, circuit-breaker, or backoff logic for Graph throttling (429) — this fix only ensures the failure surfaces as an exception, not that it's retried.
- Broader error-contract or exception-hierarchy redesign for `IGraphService`.

## Open Questions
None.

## Status: COMPLETE
