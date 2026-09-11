# Implementation: non-mcp-path-ignored-test

## What was implemented
Added FR-4 test coverage confirming that `McpBadRequestMiddleware` never triggers (never logs) for requests whose path is not `/mcp`, regardless of HTTP method or response status code — including a `/mcpx` case that verifies the path check is a proper segment match (`StartsWithSegments("/mcp")`), not a raw string-prefix check.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added a new `[Theory]` test `NonMcpPath_NeverLogs` with four `[InlineData]` rows: `GET /api/foo` (400), `POST /api/foo` (400), `POST /health` (500), and `POST /mcpx` (400). Each row asserts `ILogger.Log` is never invoked. The test was added verbatim from the task spec, matching the file's existing local-`loggerMock`/`next`/`middleware` construction style used by neighboring POST-path tests (e.g. `PostSuccess200_EmitsNoLog`).

## Tests
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs::NonMcpPath_NeverLogs` (Theory, 4 cases) — covers non-`/mcp` paths across GET/POST and various status codes, plus the `/mcpx` segment-boundary edge case.

## How to verify
```bash
cd backend
dotnet test --filter "FullyQualifiedName~NonMcpPath_NeverLogs"
```
Result: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

Full solution build was also run to confirm nothing else broke:
```bash
dotnet build Anela.Heblo.sln
```
Result: `0 Error(s)` (94 pre-existing warnings, unrelated to this change).

## Notes
No adaptation was needed — the middleware's existing `StartsWithSegments("/mcp")` check and the file's existing test conventions (`CreateContext` helper, local `Mock<ILogger<...>>`/`RequestDelegate`/`McpBadRequestMiddleware` construction pattern used by other POST-focused tests) matched the task spec exactly, so the provided test code was added as-is. No existing test already covered this combination of non-`/mcp` paths/methods/statuses in one theory.

## PR Summary
Adds a theory-based unit test to `McpBadRequestMiddlewareTests` proving the MCP bad-request logging middleware only ever acts on `/mcp` paths: four cases (GET/POST to unrelated paths at various status codes, plus a `/mcpx` case) confirm no log call is ever made, and the `/mcpx` row specifically confirms the path match is segment-based (`StartsWithSegments`) rather than a naive prefix check that would incorrectly match `/mcpx`.

### Changes
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — new `NonMcpPath_NeverLogs` theory test (FR-4 coverage)

## Status
DONE
