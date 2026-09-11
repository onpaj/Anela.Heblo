# Implementation: add-post-diagnostics-logging

## What was implemented
Added a POST branch to `McpBadRequestMiddleware`: `InvokeAsync` now checks
`IsMcpPostRequest` first and, for POST `/mcp`, delegates to a new
`HandlePostAsync` method. That method awaits `_next(context)` and, only if
the response status is 400, calls the existing `LogBadMcpRequest` helper
with the pre-existing `PostBadRequestEvent` (EventId 5932, name
`McpBadRequest`) and the elapsed time. The handler never writes to
`context.Response.StatusCode`, `.Body`, or `.Headers` — the MCP SDK's own
400 response is left untouched (FR-2). All other request paths (non-`/mcp`,
GET `/mcp`) behave exactly as before; the existing `!IsMcpGetRequest → await
_next(context); return;` fallthrough still handles POST-passthrough
implicitly via the new earlier POST check, and non-MCP paths unchanged.

Wrote the failing test first (`PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent`),
confirmed it failed against the pre-change middleware (0 invocations logged),
then implemented the branch and confirmed it passed.

## Files created/modified
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs` — added
  `IsMcpPostRequest` predicate and `HandlePostAsync` method; wired the POST
  check into `InvokeAsync` ahead of the existing GET/fallthrough logic.
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` —
  added `PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent`
  (P1 test case): POST `/mcp` with no `Mcp-Session-Id` header, `next` sets
  400, asserts exactly one `Warning`-level log with EventId 5932/name
  `McpBadRequest` whose formatted state contains `HTTPMethod`, `POST`, and
  `McpSessionIdPresent=False`, and asserts the response status code is
  still 400 (untouched by the middleware).

## Tests
- New: `McpBadRequestMiddlewareTests.PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent`
  — covers POST `/mcp` 400-with-no-session-header diagnostics logging and
  the "never rewrite the response" constraint.
- Full class `McpBadRequestMiddlewareTests`: 20/20 passed (19 pre-existing +
  1 new), 0 failed.

## How to verify
```bash
cd backend/test/Anela.Heblo.Tests
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
# Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20

cd /home/user/worktrees/feature-4042-Post-Mcp-35-8-Bad-Request-Rate-59-165-In-P7d-No-Di
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet build Anela.Heblo.sln -m:1 -nodeReuse:false -p:UseSharedCompilation=false
# 0 Error(s)
```
(Note: `dotnet build`/`dotnet test` must be run against the solution file
at the repo root, `Anela.Heblo.sln`, or from within a specific project
directory — `backend/` itself has no `.sln`/`.csproj`. Also reused the
`MSBUILDDISABLENODEREUSE`/`-nodeReuse:false`/`-m:1` flag combo noted in
`memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md` to avoid
the documented nodeReuse deadlock.)

## Notes
- The task-context test snippet asserted `state.ToString()!.Contains("SidPresent=False")`,
  but the middleware's actual field name (already established by earlier
  committed tasks) is `McpSessionIdPresent`, not `SidPresent`, so that
  substring never appears in the formatted log message. Per the task's own
  allowance to "rewrite the assertion the same way — the intent is
  unchanged" when the file's actual verification details differ from the
  snippet, I changed that one assertion line to check for
  `"McpSessionIdPresent=False"` instead — same intent (assert the session
  id was logged as absent), matching the real field name used by
  `LogBadMcpRequest`. No other part of the provided test was changed.
- Implementation structure: rather than inserting the POST branch strictly
  "between the GET branch and the final fallthrough" as one literal reading
  of the task's illustrative `InvokeAsync` sketch suggests, I placed the
  `IsMcpPostRequest` check first, ahead of the existing
  `if (!IsMcpGetRequest(context)) { await _next(context); return; }` guard
  (which is how this middleware's GET flow is actually inlined — confirmed
  in the prior read-only task). This produces the smaller diff explicitly
  permitted by the task instructions ("leave the GET code inline and slot
  the POST predicate/handler either as a peer inline block ... whichever
  produces the smaller diff") and preserves the required correctness
  property: POST `/mcp` → new branch; every other request/method → unchanged
  behavior. Verified via the full existing test suite (non-MCP pass-through,
  POST pass-through non-400 case, GET probe-blocking, GET Accept-header
  variants, GET 400/200/401 cases) all still passing.
- Did not touch `artifacts/feat-4042/state.json`, which showed as modified
  in the working tree before I started (an in-progress marker updated by
  the surrounding pipeline) — left it out of the commit per the task's
  explicit file list.

## Status
DONE
