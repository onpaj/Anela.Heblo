# Implementation: pii-guard-authorization-header-test

## What was implemented
Added the NFR-2 (arch-review amendment 4) guard test: a POST 400 with sensitive headers
(`Authorization: Bearer …`, `Cookie`, `X-Api-Key`) present on the request must never surface
those header values anywhere in the log call `McpBadRequestMiddleware` emits — cheap insurance
against a future refactor accidentally widening the header read to arbitrary/unlisted headers.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added a new `[Fact]`
  test `PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer`, added verbatim from the
  task spec, matching the file's existing local-`loggerMock`/`next`/`middleware` construction
  style used by neighboring POST-path tests.

## Tests
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs::PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer`
  — sets `Authorization`, `Cookie`, and `X-Api-Key` headers with distinct secret values on a
  POST /mcp request that returns 400, then asserts no `ILogger.Log` call's formatted state ever
  contains any of those secret substrings.

## How to verify
```bash
cd backend
dotnet test --filter "FullyQualifiedName~PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer"
```
Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

Full test class was also run to confirm no regression:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"
```
Result: `Passed! - Failed: 0, Passed: 32, Skipped: 0, Total: 32`.

`dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` was run first to confirm a
clean compile: `0 Error(s)` (82 pre-existing warnings, unrelated to this change).

## Notes
No adaptation was needed — the middleware's `LogBadMcpRequest` only ever reads the explicit
allow-listed headers (`UserAgent`, `Origin`, `Accept`, `ContentType`, `Mcp-Session-Id`; verified by
inspection of `McpBadRequestMiddleware.cs`), never `Authorization`, `Cookie`, or `X-Api-Key`, so
the test passed on the first run with no implementation change required — exactly the "cheap
insurance" the task description describes.

## PR Summary
Adds a regression test to `McpBadRequestMiddlewareTests` proving the MCP bad-request logging
middleware never leaks `Authorization`, `Cookie`, or `X-Api-Key` header values into its log output,
guarding against a future refactor that widens the header read beyond the current allow-list.

### Changes
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — new
  `PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer` fact test (NFR-2 coverage)

## Status
DONE
