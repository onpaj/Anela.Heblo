# Implementation: post-headerless-probe-not-rewritten-test

## What was implemented
Added the FR-2 acceptance test `PostHeaderlessBodyless_400_LoggedButNotRewritten` to
`McpBadRequestMiddlewareTests`. It asserts that a POST /mcp request with no Accept
header, no User-Agent, and no body — which, for the GET path, would short-circuit
to a 404 probe-block — instead reaches `next()` unchanged, and when `next()` sets
a 400 response the middleware logs a single Warning (`McpBadRequest`, EventId 5932)
without rewriting the status code away from 400.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added the
  new `[Fact]` test, placed in the existing "POST /mcp 400 diagnostics logging"
  section, directly before `PostSuccess200_EmitsNoLog`, matching the style of the
  neighboring `PostBadRequest_*` tests (own `Mock<ILogger<...>>`/`RequestDelegate`
  instance rather than the shared field, using the existing `CreateContext` helper
  which already supports headerless `POST "/mcp"`).

## Tests
- `McpBadRequestMiddlewareTests.PostHeaderlessBodyless_400_LoggedButNotRewritten`
  — new test covering FR-2: headerless/bodyless POST /mcp 400 response is logged
  once (Warning, EventId 5932, EventName "McpBadRequest") and the response status
  code remains 400 (not rewritten to 404 as the GET probe-blocking path would do).

## How to verify
```
cd backend
dotnet test --filter "FullyQualifiedName~PostHeaderlessBodyless_400_LoggedButNotRewritten"
```
Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes
No changes to `McpBadRequestMiddleware` production code were needed — the existing
implementation already treats POST differently from GET (no Accept-header
probe-blocking short-circuit for POST), so the new test passed on first run.
The unrelated pre-existing working-tree modification to `artifacts/feat-4042/state.json`
was left untouched and not included in this commit.

## Status
DONE
