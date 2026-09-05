# Implementation: post-happy-path-no-log-test

## What was implemented
Added FR-4 test coverage to `McpBadRequestMiddlewareTests`: a new `PostSuccess200_EmitsNoLog` fact that sends a POST `/mcp` request with a valid `Mcp-Session-Id` header through `McpBadRequestMiddleware`, has `next` return HTTP 200, and asserts the logger's `Log` method is never invoked while the response status stays 200. This guards against the middleware becoming noisy on the POST hot path.

The existing `VerifyNoLogCalled()` helper in the file is parameterless and asserts against the fixture's instance-level `_loggerMock` field. The new test — like the two existing `PostBadRequest_*` tests in the same file — constructs its own local `loggerMock`/`middleware` (not the shared fixture instance), so it could not reuse that helper as-is. Per the task's own fallback instruction, and to match the file's existing convention for local-mock POST tests, the verification is inlined using the same `loggerMock.Verify(...)` pattern already used by `PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent` and `PostBadRequest_WithSessionHeader_LogsPresenceAndEightCharPrefix`. No new shared helper was added, and no existing code was modified beyond inserting the new test method.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added the `PostSuccess200_EmitsNoLog` `[Fact]` in the "POST /mcp 400 diagnostics logging" section, right after `PostBadRequest_WithSessionHeader_LogsPresenceAndEightCharPrefix` and before the `HasValidMcpAcceptHeader` static-helper tests.

## Tests
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs::PostSuccess200_EmitsNoLog` (new) — verifies a POST `/mcp` request that succeeds with 200 produces zero logger invocations and leaves the response status untouched.
- Full file re-run: all 22 tests in `McpBadRequestMiddlewareTests` pass (`Passed! - Failed: 0, Passed: 22, Skipped: 0, Total: 22`).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PostSuccess200_EmitsNoLog"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"
```
Both commands passed cleanly in this sandbox.

## Notes
- There is no root-level `.sln` in `backend/`, so `dotnet test` must target the test `.csproj` directly (`test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`), not the plain `--filter` invocation form given in the task (which assumed a discoverable project/solution in the current directory).
- In this sandboxed environment, plain `dotnet test`/`dotnet build` invocations reproducibly deadlocked (zero CPU progress indefinitely) right after the `Anela.Heblo.API` project's `GenerateAccessMatrix` MSBuild target, which shells out to a nested `dotnet run --project ...AccessMatrixGen` from inside the build. This looks like an MSBuild node-reuse / build-server self-deadlock between the outer build and the nested `dotnet run`. Passing `MSBUILDDISABLENODEREUSE=1` and `-p:UseSharedCompilation=false` to the outer `dotnet test` invocation avoided the deadlock and let the build/test run complete normally. This is an environment quirk of this sandbox, not a code issue, and no source files were changed to work around it — only the local shell invocation used for verification.
- No other tests were touched or affected.

## PR Summary
Adds FR-4 test coverage confirming that a successful (200) POST `/mcp` request emits no log output from `McpBadRequestMiddleware`, guarding against the middleware becoming noisy on the hot path.

### Changes
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — added `PostSuccess200_EmitsNoLog` test

## Status
DONE
