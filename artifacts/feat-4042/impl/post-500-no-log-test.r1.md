# Implementation: post-500-no-log-test

## What was implemented
Added FR-4 test coverage to `McpBadRequestMiddlewareTests`: a new `PostServerError500_EmitsNoLog` fact that sends a POST `/mcp` request through `McpBadRequestMiddleware`, has `next` set the response status to 500 (Internal Server Error), and asserts the logger's `Log` method is never invoked. This confirms the feature's logging scope is strictly limited to 400 responses — a 500 must not trigger the bad-request diagnostics log path.

This exercises the same code path as the existing `PostBadRequest_*` tests (`HandlePostAsync`), but with a non-400 status, verifying the `if (context.Response.StatusCode == StatusCodes.Status400BadRequest)` guard in `McpBadRequestMiddleware.HandlePostAsync` correctly excludes 500 (and, by the same logic already covered elsewhere, 200) from logging.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added the `PostServerError500_EmitsNoLog` `[Fact]`, placed directly after `PostSuccess200_EmitsNoLog` and before the `HasValidMcpAcceptHeader` static-helper tests section, matching the task context's exact test body.

## Tests
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs::PostServerError500_EmitsNoLog` (new) — verifies a POST `/mcp` request whose downstream handler sets a 500 response produces zero logger invocations.
- Filtered run (`FullyQualifiedName~PostServerError500_EmitsNoLog`): `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.
- Full-file run (`FullyQualifiedName~McpBadRequestMiddlewareTests`) confirms the new test coexists cleanly with all existing tests in the file (see review artifact / verification notes for the exact count).

## How to verify
```bash
cd backend
MSBUILDDISABLENODEREUSE=1 dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false --filter "FullyQualifiedName~PostServerError500_EmitsNoLog"
MSBUILDDISABLENODEREUSE=1 dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"
```

## Notes
- As previously documented in this feature's artifacts, plain `dotnet test`/`dotnet build` invocations in this sandbox can deadlock after the `Anela.Heblo.API` project's `GenerateAccessMatrix` MSBuild target (a nested `dotnet run` shelled out from inside the build). `MSBUILDDISABLENODEREUSE=1 -p:UseSharedCompilation=false` avoids this; no source files were changed to work around it.
- No existing test or source code was modified beyond inserting the new fact.

## PR Summary
Adds FR-4 test coverage confirming that a POST `/mcp` request whose response is a 500 (Internal Server Error) emits no log output from `McpBadRequestMiddleware` — the bad-request diagnostics logging is scoped strictly to 400 responses.

### Changes
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added `PostServerError500_EmitsNoLog` test

## Status
DONE
