All three commits are on the branch. Implementation complete.

---

# Implementation: Fix Error Handling in GetGroupMembers Flow

## What was implemented

Refactored the `UserManagement` module's Graph API error handling to eliminate the swallow-and-return-empty anti-pattern. Exceptions now propagate from `GraphService` and are caught/converted at the appropriate handler boundaries, making `Success = false` and `ErrorCode` accurate in all failure scenarios. The second Graph caller (`BackfillArticleRequestedByHandler`) was also hardened against the newly propagated exceptions.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — Removed all swallow-and-return catch blocks; added log-and-rethrow for MsalException, ODataError, UnauthorizedAccessException, and generic Exception. Inner MSAL try/catch restructured into outer catch chain to avoid double-logging.
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` — Added four typed catches mapping to ConfigurationError / ExternalServiceError / Forbidden / InternalServerError with single log-per-catch pattern.
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — Added try/catch around resolver call with same four-exception pattern returning error envelope on Graph failure.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — Updated two tests from expecting empty list to expecting thrown exceptions.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs` — Added four typed exception tests + empty-group distinguishability test; tightened existing test to assert specific `InternalServerError`.
- `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — Added four Graph failure tests for FR-4 coverage.

## Tests

- `GraphServiceTests.cs` — 12 tests passing; two updated to assert thrown exceptions instead of empty list
- `GetGroupMembersHandlerTests.cs` — 7 tests passing; 4 new typed exception tests + empty-group test
- `BackfillArticleRequestedByHandlerTests.cs` — 12 tests passing (8 original + 4 new Graph failure tests)
- Full suite: 4492 tests pass

## How to verify

```bash
cd backend/
dotnet build
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GraphServiceTests|FullyQualifiedName~GetGroupMembersHandlerTests|FullyQualifiedName~BackfillArticleRequestedByHandlerTests"
```

## Notes

- The non-success HTTP response path in `GraphService` (lines 89–99) still returns empty list from an `if` statement — this is intentional per spec FR-1 which only targets `catch` blocks.
- `GraphArticleUserResolver` remains a one-line adapter with no exception handling — consistent with the design decision to keep thin adapters clean and let the use-case handler decide how to handle failures.
- `UnauthorizedAccessException` is kept in the handler's catch list explicitly; if it escaped it would be intercepted by the global `UnauthorizedAccessExceptionHandler` middleware and return a bare 401 instead of the correct Forbidden/403 envelope.

## PR Summary

Fixed the `GetGroupMembers` error-handling contract so `Success = false` and `ErrorCode` are set accurately when Microsoft Graph or MSAL fails, replacing the previous swallow-and-return-empty pattern that made all failure paths invisible to callers.

Each layer now owns exactly one responsibility: `GraphService` logs Graph/MSAL diagnostic fields and rethrows; `GetGroupMembersHandler` catches typed exceptions and maps to `ErrorCodes`; `BackfillArticleRequestedByHandler` catches the same four types from its resolver call and returns a non-Success envelope so the admin caller cannot mistake a "Graph unavailable" result for valid backfill data.

### Changes
- `GraphService.cs` — Log-and-rethrow replaces swallow-and-return-empty in all four exception types
- `GetGroupMembersHandler.cs` — Four typed catches mapping MsalException → ConfigurationError, ODataError → ExternalServiceError, UnauthorizedAccessException → Forbidden, Exception → InternalServerError
- `BackfillArticleRequestedByHandler.cs` — Same four-catch pattern wrapping resolver call (FR-4 hardening)
- `GraphServiceTests.cs` — Two tests updated: now assert exception thrown instead of empty list
- `GetGroupMembersHandlerTests.cs` — Four new typed exception tests + empty-group distinguishability test
- `BackfillArticleRequestedByHandlerTests.cs` — Four new Graph failure tests

## Status
DONE