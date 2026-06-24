# Implementation: add-batch-tests

## What was implemented

Added four `[Fact]` test methods plus supporting infrastructure to `GraphServiceTests.cs` to verify the `$batch`-based user resolution in `GetAppRoleMembersAsync`. These tests exercise the N=1 and N=21 chunking paths, non-200 sub-response skipping, and batch-level HTTP failure handling.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — added `SequentialFakeHttpMessageHandler` private class, `BuildServiceSequential` helper method, canned JSON constants (`SpResponse`, `AssignmentsPageResponse`, `AssignmentsPage21Response`, `BatchResponse1User`, `BatchResponseFor21Users`, `BatchResponseWithOneNon200`), and four `[Fact]` test methods

## Tests

- `GetAppRoleMembersAsync_SingleUser_IssuesOneBatchCall` — 1 user: verifies exactly 3 HTTP calls (SP lookup, assignments, one `$batch` POST), result count, user fields, and that the batch URL is relative (not absolute)
- `GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls` — 21 users: verifies 4 HTTP calls (SP, assignments, two `$batch` POSTs), first chunk has 20 requests, second has 1
- `GetAppRoleMembersAsync_NonTwoHundredSubResponse_SkipsUserAndLogsWarning` — batch sub-response status 404: result is empty, `ILogger<GraphService>` warned once with message containing "Could not resolve user"
- `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError` — HTTP 500 on `$batch`: result is empty, `ILogger<GraphService>` errored once with message containing "Graph $batch request failed"

## How to verify

```
dotnet test /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetAppRoleMembersAsync" -v normal
```

All 4 new tests pass. All 15 `GraphServiceTests` pass (no regressions).

## Notes

- `LogLevel` is ambiguous in this file (both `Microsoft.Identity.Client.LogLevel` and `Microsoft.Extensions.Logging.LogLevel` are in scope via `using` directives). Used fully qualified `Microsoft.Extensions.Logging.LogLevel.Warning/Error` to resolve.
- The pre-existing test `GetAppRoleMembersTests.GetAppRoleMembersAsync_HappyPath_ReturnsResolvedUserDtos` (in `GetAppRoleMembersTests.cs`) was already failing on this branch before these changes — it is not affected by this task.
- `GetAppRoleMembersAsync` calls `_httpClientFactory.CreateClient` once and reuses the same `HttpClient` for all requests, so `SequentialFakeHttpMessageHandler` correctly intercepts all calls in order.

## PR Summary

Added unit tests covering the `$batch`-based user resolution path introduced in the previous commit. The test suite now documents and guards the batching contract: chunk size of 20, relative URLs in batch requests, per-response error handling, and batch-level failure recovery.

### Changes

- `GraphServiceTests.cs` — `SequentialFakeHttpMessageHandler` (sequential response queue), `BuildServiceSequential` helper, canned JSON constants, 4 `[Fact]` tests for batch resolution

## Status

DONE
