# Implementation: extract-log-helper-widen-fields

## What was implemented
Extracted the previously-inline GET-only 400 log call in `McpBadRequestMiddleware` into a shared `LogBadMcpRequest(HttpContext, EventId, double elapsedMs)` method that emits all 11 fields required by both the GET and (future) POST branches: `HTTPMethod`, `Path`, `StatusCode`, `UserAgent`, `Origin`, `Accept`, `ContentType`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, `ElapsedMs`. Added `GetBadRequestEvent` (id 5931) and `PostBadRequestEvent` (id 5932) `EventId` constants sharing the `EventName = "McpBadRequest"`, so a single Kusto query on `EventName == "McpBadRequest"` will later union both channels. The GET branch now calls `LogBadMcpRequest(context, GetBadRequestEvent, elapsedMs)`, measuring elapsed time via `Stopwatch.GetTimestamp()`/`Stopwatch.GetElapsedTime(...)` captured at the top of the GET flow. `PostBadRequestEvent` is unused until the next task (`add-post-diagnostics-logging`) adds the POST branch — no POST behavior was added here.

No field is removed or renamed from the previous GET log — the change is additive only, so all previously-passing GET tests continue to pass unmodified.

The log message template writes each field as a literal `Name=Value` pair (e.g. `HTTPMethod={HTTPMethod} ... McpSessionIdPresent={McpSessionIdPresent} ...`) rather than abbreviated prefixes, so the formatted `FormattedLogValues.ToString()` state contains every field's exact key name as a substring — this is what the task's own `GetBadRequest_Log_IncludesAllUnionFields` test asserts against, and matching field names (not abbreviations like "UA=" or "SidPresent=") was necessary for that assertion to pass.

## Files created/modified
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs` — added `using System.Diagnostics;`, `GetBadRequestEvent`/`PostBadRequestEvent` EventId constants, extracted `LogBadMcpRequest` helper, widened the GET branch to call it with elapsed-time measurement.
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — widened `InvokeAsync_GetMcpWith400Response_LogsWarningWithDiagnostics` to assert the new fields and the `McpBadRequest` EventId name; added new `GetBadRequest_Log_IncludesAllUnionFields` test asserting all 11 union-schema field names appear in the logged state.

## Tests
`McpBadRequestMiddlewareTests` — 19 facts/theories, all passing (`Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19`), including:
- `InvokeAsync_GetMcpWith400Response_LogsWarningWithDiagnostics` (widened)
- `GetBadRequest_Log_IncludesAllUnionFields` (new)
- All 17 pre-existing tests unchanged and still passing.

TDD sequence followed: widened the existing test and added the new test first, confirmed both failed (`Failed: 2, Passed: 17, Total: 19` — missing widened fields, then a second failure where the message template used abbreviated key prefixes instead of the exact field names the test checks for), then fixed the template to use exact `Name=Value` literal pairs, re-ran, confirmed `Failed: 0, Passed: 19, Total: 19`.

## How to verify
```
cd backend
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~McpBadRequestMiddlewareTests" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```
Expect all 19 tests to pass.

```
cd backend
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```
Expect `0 Error(s)`. (159 pre-existing warnings from unrelated files in other projects were observed; none reference the MCP files touched here.)

## Notes
- Hit the documented `dotnet build`/`dotnet test` nodeReuse deadlock again (see `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`); the `dotnet build-server shutdown` + `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 -m:1 -nodeReuse:false -p:UseSharedCompilation=false` flag combo from that note worked cleanly on the first retry both times.
- The task spec's own Step 3 code snippet used abbreviated literal prefixes (`UA=`, `SidPresent=`, `SidPrefix=`, `IP=`) for the message template, but the task's own Step 1 test (`GetBadRequest_Log_IncludesAllUnionFields`, given verbatim in the spec) asserts the formatted log state's `ToString()` contains the full field names (`UserAgent`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, etc.) as literal substrings. Since `.NET`'s `FormattedLogValues.ToString()` renders the template with placeholders substituted by their *values*, not their *names*, the abbreviated-prefix version of the template does not satisfy that assertion. Resolved by using the exact field name as the literal prefix for every field (`HTTPMethod={HTTPMethod} Path={Path} ... McpSessionIdPresent={McpSessionIdPresent} ...`), which satisfies the task's own test as given. Flagging this because the next task's own example test (`add-post-diagnostics-logging`, not implemented here) currently asserts `Contains("SidPresent=False")` rather than `Contains("McpSessionIdPresent=False")` — that assertion as written will need updating to match the actual field-name-based template established here when that task is implemented.
- `PostBadRequestEvent` (id 5932) is intentionally added now (per the task's explicit instruction) but unused until `add-post-diagnostics-logging` wires up the POST branch; it produced no unused-field compiler warning.

## PR Summary
Extracts the GET-only 400 diagnostics log call in `McpBadRequestMiddleware` into a shared `LogBadMcpRequest` helper and widens its payload to 11 fields (`HTTPMethod`, `Path`, `StatusCode`, `UserAgent`, `Origin`, `Accept`, `ContentType`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, `ElapsedMs`), all under a shared `EventName = "McpBadRequest"` EventId. This is additive-only on the existing GET path — no field is renamed or removed — and prepares the middleware for POST /mcp diagnostic logging in the next task, so both channels can share one log shape and one Kusto query.

### Changes
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs` — extracted `LogBadMcpRequest`, added `GetBadRequestEvent`/`PostBadRequestEvent` EventId constants, widened GET log fields, added elapsed-time measurement.
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` — widened the existing GET-400 log assertion and added a dedicated test asserting all 11 union-schema fields are present.

## Status
DONE
