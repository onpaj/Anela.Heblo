## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs:88` — `path` is built manually as `req.Path.Value + req.QueryString.Value`. `HttpRequest.GetEncodedPathAndQuery()` (from `Microsoft.AspNetCore.Http.Extensions`) already does this and handles encoding consistently; consider reusing it instead of the ad-hoc concatenation.
- `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs:54-56` — `TruncateId` still duplicates the truncation logic now centralized in `McpTelemetryHelpers.TruncateSessionId` (differs only in appending `***`). A `TODO(#4042)` marks this for later consolidation, which is fine for this PR's scope, but worth tracking as a fast-follow so the two truncation behaviors don't drift.

## Verification
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — succeeded, 0 errors.
- `dotnet test --filter "FullyQualifiedName~MCP"` — 120/120 passed, including all new GET/POST diagnostics, PII-guard, and non-`/mcp` tests from FR-4.

## Spec alignment notes
- FR-1: shared `LogBadMcpRequest` helper emits one structured warning with all required fields (`HTTPMethod`, `Path`, `StatusCode`, `UserAgent`, `Origin`, `Accept`, `ContentType`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, `ElapsedMs`); missing headers render `(missing)`; verified.
- FR-2: `HandlePostAsync` only observes the response after `await _next(context)` — no status rewrite, no short-circuit for POST (including headerless/bodyless probes). Verified against `PostHeaderlessBodyless_400_LoggedButNotRewritten`.
- FR-3: GET short-circuit (headerless probe → 404) and field names are preserved; existing GET tests pass unchanged (with `GetBadRequest_Log_IncludesAllUnionFields` added, not modifying the short-circuit itself). `EventId` changed from default `(0, null)` to `(5931, "McpBadRequest")` for GET — this is an additive/intentional part of the "consistent EventId/EventName" requirement, not a field rename, and is exercised by updated test assertions.
- FR-4: all seven required unit-test scenarios are present and passing.
- NFR-1: `IsMcpPostRequest`/`IsMcpGetRequest` are cheap method+path checks evaluated before any header access; header reads for the log line are gated behind `StatusCode == 400` for both GET and POST paths.
- NFR-2: `Authorization`/`Cookie`/`X-Api-Key` are never read; only an 8-char session-id prefix is logged; enforced by `PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer`.
