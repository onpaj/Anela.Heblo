# Architecture Review: POST /mcp 400 diagnostics on exception path

## Skip Design: true
Backend-only middleware change plus docs; no UI.

## Architectural Fit Assessment
Fits the existing pattern: `McpBadRequestMiddleware` already owns MCP request diagnostics and is registered in `ApplicationBuilderExtensions.cs` after routing/auth/rate-limit and before `MapMcp`. The exception handler chain (`UseExceptionHandler`) sits outside it, so any exception thrown by the MCP endpoint bypasses the post-`_next` status check. The fix is local to the middleware; no new component is needed.

## Proposed Architecture

### Component Overview
```
UseExceptionHandler -> UseRouting -> Auth -> RateLimiter -> Timeouts
   -> McpBadRequestMiddleware  (try { next } catch { log 5933; throw; }; if 400 -> log 5932)
   -> McpDiagnosticsMiddleware -> MCP endpoint (SDK)
```

### Key Design Decisions

#### Decision 1: Catch-log-rethrow in the middleware
**Options considered:** (a) catch-log-rethrow in McpBadRequestMiddleware; (b) log inside each IExceptionHandler; (c) set status 400 in the middleware.
**Chosen approach:** (a).
**Rationale:** The handlers are shared by all API routes and must stay MCP-agnostic; (c) would change response behaviour, which the #4052 contract forbids. Rethrow preserves the existing 400 mapping.

#### Decision 2: Separate EventId 5933, log exception type only
**Rationale:** Lets telemetry distinguish "400 returned" (5932) from "threw, status decided later" (5933); the final status is unknown at catch time. Type-only avoids leaking input via messages; full details are in the `exceptions` table.

#### Decision 3: Shared private log helper
Refactor `LogBadMcpRequest` to accept an optional exception type string and level-compatible template; keep the 5932 message text unchanged so existing tests and queries still match. A second template is used for 5933 (adds `ExceptionType`).

## Implementation Guidance

### Directory / Module Structure
- Modify `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`.
- Modify `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`.
- Modify `docs/integrations/mcp-server.md` (Diagnostics section).

### Interfaces and Contracts
- `new EventId(5933, "McpRequestException")`.
- Catch `Exception ex` (no filter), log, `throw;`. Do not catch `OperationCanceledException` when `context.RequestAborted.IsCancellationRequested` (client disconnects are noise): use an exception filter `when (!(ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested))`.
- Logger session-id handling must continue to use `McpTelemetryHelpers.TruncateSessionId`.

### Data Flow
POST /mcp -> SDK throws BadHttpRequestException/JsonException/ArgumentException -> middleware logs 5933 + rethrows -> ExceptionHandler maps to 400/500 -> request telemetry shows the status; join by `operation_Id`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| H3 is wrong and 400s come from elsewhere (e.g. SDK writes 400 and a sampling/deploy issue hides 5932) | Medium | Runbook in docs: confirm deployed SHA, staging repro; telemetry check of `exceptions` table. Change is harmless if H3 is wrong |
| Log volume from scanners | Low | Same volume as existing 5932 path; Warning level on a small endpoint |
| Client-abort noise | Low | Exception filter on aborted cancellation |
| Process-doc CI gate | Low | `docs/processes` has no MCP-transport owner doc; verify with `python3 scripts/process-docs/check.py index` |

## Specification Amendments
None.

## Prerequisites
None. Manual, non-code follow-up: confirm Production runs a build containing #4052 (`11091d4`).
