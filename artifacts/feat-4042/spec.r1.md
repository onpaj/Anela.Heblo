# Specification: Extend MCP Bad-Request Diagnostics to POST /mcp

## Summary
The existing `McpBadRequestMiddleware` instruments only the `GET /mcp` path, leaving a chronic ~30-38% 400 Bad Request rate on `POST /mcp` (the actual JSON-RPC channel) invisible in telemetry. This feature extends that middleware so the same diagnostic header/content logging fires for POST 400s, turning a silent baseline failure into an attributable root cause (stale session vs. malformed request vs. scanner/probe) without touching any MCP SDK behavior.

## Background
Application Insights shows a flat 30-38% 400 rate across the full p7d window (2026-08-26 - 2026-09-02): 59 of 165 `POST /mcp` requests rejected. Latencies are 2-23 ms and no exceptions/traces correlate by `operation_Id`, indicating the MCP SDK's own request validation is rejecting before any handler runs. Caller identity (User-Agent, Origin, Mcp-Session-Id) is never recorded, so we cannot tell whether these are reconnecting clients sending stale session IDs, version-mismatched clients, or external scanners.

Issue #593 already fixed this visibility gap for `GET /mcp` in `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`, but the filter predicate (`IsMcpGetRequest`) scopes it to HTTP GET only. POST - the primary MCP call channel - was not included.

## Functional Requirements

### FR-1: Log diagnostic context for POST /mcp 400 responses
When a request to `POST /mcp` (exact path and any subpath that `GET /mcp` currently covers) returns HTTP 400, the middleware MUST emit a single structured warning log entry identical in shape to the existing GET diagnostic, capturing:

- `HTTPMethod` (always `POST` for this FR)
- `Path` (full request path including query string)
- `StatusCode` (always 400 for this FR)
- `UserAgent` (presence + value, or `(missing)`)
- `Origin` (presence + value, or `(missing)`)
- `Accept` (presence + value, or `(missing)`)
- `ContentType` (presence + value, or `(missing)`)
- `McpSessionIdPresent` (bool) - flag only, not the raw value
- `McpSessionIdPrefix` (first 8 chars of the `Mcp-Session-Id` header or `(missing)`) - enough to group reconnection attempts without leaking full session identifiers
- `RemoteIp` (from `HttpContext.Connection.RemoteIpAddress`, respecting any proxy header forwarding already configured)
- `ElapsedMs` (request duration)

**Acceptance criteria:**
- Every `POST /mcp` response with status 400 produces exactly one warning-level log entry with all fields above.
- Headers that are absent are logged as `(missing)`, not an empty string.
- Log category and severity match the existing GET handler exactly so a single Application Insights Kusto query can union both.
- The log appears in Application Insights `traces` table with a consistent `EventId`/`EventName` that downstream alerts/dashboards can filter on.
- 200 and non-400 responses to `POST /mcp` produce NO additional log output from this middleware (no noise on the happy path).

### FR-2: Do not short-circuit POST requests
The existing GET flow rewrites headerless probes (no `Accept`/`User-Agent`) to 404 so scanners do not pollute the (400) signal. POST MUST NOT be short-circuited the same way. Legitimate MCP POST traffic carries a JSON-RPC body and the SDK must be allowed to run its own validation; blindly 404'ing empty-body POSTs could mask genuine client bugs.

**Acceptance criteria:**
- The POST code path only OBSERVES the response status and logs; it never rewrites status, short-circuits, or alters the body.
- All POST requests - including headerless probes - continue to be handled by the MCP SDK exactly as before this change.

### FR-3: Preserve existing GET behavior
The change MUST NOT regress the current GET /mcp diagnostics or its headerless-probe -> 404 short-circuit.

**Acceptance criteria:**
- All existing unit tests for `McpBadRequestMiddleware` continue to pass unchanged.
- Log field names for GET entries are identical to the current output (no renames).

### FR-4: Unit test coverage
New and/or reorganized tests MUST cover the POST path.

**Acceptance criteria:**
- Unit test: POST /mcp with no `Mcp-Session-Id` header and response status 400 -> one warning logged with `McpSessionIdPresent=false`.
- Unit test: POST /mcp with an `Mcp-Session-Id` header and response status 400 -> one warning logged with `McpSessionIdPresent=true` and `McpSessionIdPrefix` = first 8 characters.
- Unit test: POST /mcp with response status 200 -> NO warning log emitted.
- Unit test: POST /mcp with response status 500 -> NO warning log emitted (feature scope is 400 only).
- Unit test: GET /mcp with headerless probe -> still returns 404 (existing behavior preserved).
- Unit test: POST /mcp with headerless bodyless request and response status 400 -> one warning log (NOT rewritten to 404).
- Unit test: non-`/mcp` paths are ignored regardless of method/status.

## Non-Functional Requirements

### NFR-1: Performance
The middleware runs on every request, so:
- Path/method filtering MUST short-circuit before any header reads for non-MCP requests.
- Header reads/allocations for the log entry happen ONLY when status == 400.
- Total added overhead for a non-400 MCP POST MUST be < 50us p99 (equivalent to a no-op delegate call).

### NFR-2: Security / PII
- Full `Mcp-Session-Id` values MUST NOT be written to telemetry; only presence + an 8-char prefix.
- No request body content is logged.
- Request headers are logged verbatim only for the allow-listed set in FR-1; Authorization/Cookie/API-key headers are NEVER read or logged.
- User-Agent/Origin logging follows the same PII-neutral treatment already performed by the GET handler.

### NFR-3: Observability
Log entries MUST be structured (MEL) with typed properties so Kusto queries can project columns directly. A sample Kusto query segregating POST 400s by `UserAgent` / `McpSessionIdPresent` MUST be included in the PR description.

## Data Model
No persisted data changes. Log fields above (FR-1) define the telemetry schema the Application Insights `traces` table will carry as `customDimensions`.

## API / Interface Design
The HTTP contract of `POST /mcp` does NOT change. Response statuses, bodies, and headers returned to clients are identical. The sole change is a server-side log write on 400 responses.

### Suggested code shape (non-binding)
Extend the existing `McpBadRequestMiddleware` with a sibling predicate `IsMcpPostRequest` and a shared `LogBadMcpRequestAsync` helper so GET and POST emit identically shaped events. The GET short-circuit (headerless probe -> 404) is preserved verbatim; only the log-on-400 branch is broadened to also fire when the incoming request was a POST to `/mcp`.

## Dependencies
- Existing `McpBadRequestMiddleware` and its registration in `Program.cs`.
- `Microsoft.Extensions.Logging` / Application Insights sink already wired.
- MCP SDK behavior is not touched; no support-channel coordination required.

## Out of Scope
- Changing how the MCP SDK validates requests or returns 400.
- Auto-remediating stale sessions (e.g. telling clients to re-initialize).
- Emitting Application Insights alerts or dashboards for the new signal (follow-up work).
- Logging 200s, 5xxs, or non-400 client errors.
- Any change to the existing GET short-circuit behavior.
- Persisting the 400 count/rate to a database table or internal metric.

## Open Questions
None.

## Status: COMPLETE
