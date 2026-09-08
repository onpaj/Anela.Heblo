# Architecture Review: Extend MCP Bad-Request Diagnostics to POST /mcp

## Skip Design: true

Backend-only observability enhancement — no UI/UX changes. The HTTP contract of `POST /mcp` is unchanged; the only artifact for humans is a new Kusto query pattern in the PR description.

## Architectural Fit Assessment

This feature is a low-risk extension of an existing, well-scoped middleware (`McpBadRequestMiddleware` at `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`). The infrastructure is already in place:

- **Middleware pipeline**: `McpBadRequestMiddleware` is registered in `ApplicationBuilderExtensions.cs:128`, immediately before `McpDiagnosticsMiddleware` (line 132) and `MapMcp("/mcp")` (line 136). Adding POST-observation logic here is the natural home — no new middleware, no new registration.
- **Precedent for structured logging**: The sibling `McpDiagnosticsMiddleware` already establishes the pattern for MEL structured logging with a `TruncateId` helper for session-ID prefix truncation (line 54). That helper is a direct model for the `McpSessionIdPrefix` field required in FR-1.
- **Existing test scaffold**: `McpBadRequestMiddlewareTests` already has helpers (`CreateContext`, `VerifyNoLogCalled`) that accept a `method` parameter, so extending coverage to POST cases needs no new test infrastructure.

The feature is well-aligned with the codebase's conventions. There is one modest tension worth calling out: `McpDiagnosticsMiddleware` and `McpBadRequestMiddleware` both instrument the same endpoint but for different signals (404 vs. 400) and different methods (both GET-only today). After this change, `McpBadRequestMiddleware` becomes the "400 observer for GET and POST" and its name/summary comments need updating. Splitting the class further (e.g. a separate `McpPostBadRequestMiddleware`) is rejected below — it would duplicate the shared header-reading and log-shape logic.

## Proposed Architecture

### Component Overview

```
                              ┌─────────────────────────────────────────┐
Request ─► ASP.NET pipeline ─►│  McpBadRequestMiddleware                │─► MapMcp("/mcp")
                              │  ┌───────────────────────────────────┐  │      │
                              │  │ 1. Non-/mcp path?      → pass    │  │      │
                              │  │ 2. GET /mcp?                     │  │      │
                              │  │    a) invalid Accept → 404+log   │  │      │
                              │  │    b) else → next; on 400: log   │  │      │
                              │  │ 3. POST /mcp? (NEW)              │  │      ▼
                              │  │    → next; on 400: log           │  │  MCP SDK
                              │  │ 4. Other methods → pass          │  │  (JSON-RPC)
                              │  └───────────────────────────────────┘  │
                              └─────────────────────────────────────────┘
                                            │
                                            └── LogBadMcpRequestAsync
                                                (shared, structured MEL)
                                                        │
                                                        ▼
                                              Application Insights `traces`
```

### Key Design Decisions

#### Decision 1: Single middleware, extended predicate

**Options considered:**
- (A) Add a sibling `McpPostBadRequestMiddleware` class and register it alongside the existing one.
- (B) Extend `McpBadRequestMiddleware` with a broader predicate (`IsMcpBadRequestCandidate`) and a shared `LogBadMcpRequestAsync` helper.

**Chosen approach:** (B).

**Rationale:** The spec itself (§ "Suggested code shape") points here, and it minimizes surface area: one class, one registration in `ApplicationBuilderExtensions.cs`, one Kusto `EventId` to filter on. Splitting yields two classes that would share ~80% of their body (Accept/UserAgent/Origin/SessionId reads, log shape). Renaming the class is unnecessary — its current name still fits after generalization; only the XML `<summary>` comment needs updating to say "GET and POST" instead of "GET".

#### Decision 2: One `EventId` per log site, but a shared `EventName`

**Options considered:**
- (A) Reuse the existing implicit `EventId(0)` for both GET and POST 400 logs.
- (B) Introduce explicit named `EventId`s and unify GET+POST behind one `EventName` for AI queries.

**Chosen approach:** (B) — declare two `EventId` constants (`GetBadRequest = 5931`, `PostBadRequest = 5932`) that share `EventName = "McpBadRequest"`. Any Kusto query filtering by `customDimensions.EventName == "McpBadRequest"` covers both, while the numeric `EventId` still distinguishes the method channel for finer analysis.

**Rationale:** FR-1 requires downstream alerts to be able to filter on a consistent event and requires the GET and POST log shapes to be unionable in one Kusto query. An explicit shared `EventName` is the cleanest way to satisfy both. Numeric IDs prefixed with the source issue number (593x) match the informal convention already in the comments.

#### Decision 3: Log after `_next` completes; never buffer or wrap the response

**Options considered:**
- (A) Wrap `HttpResponse.Body` to inspect the body payload (potentially useful for debugging).
- (B) Only observe `context.Response.StatusCode` after the pipeline runs.

**Chosen approach:** (B).

**Rationale:** NFR-2 forbids logging request bodies and NFR-1 requires ≤50μs overhead on non-400 requests. Wrapping the response stream introduces per-request allocation and, worse, risks interfering with the MCP SDK's streaming response (`text/event-stream` is a long-lived response). The existing GET path already uses status-code-only observation — POST follows the same pattern verbatim.

#### Decision 4: `Mcp-Session-Id` treatment — presence bool + 8-char prefix

**Options considered:**
- (A) Reuse `McpDiagnosticsMiddleware.TruncateId(...)` which returns `"abcdef12***"` (with trailing marker).
- (B) Duplicate the helper inside `McpBadRequestMiddleware` with slightly different output (`"(missing)"` sentinel, no trailing `***`).
- (C) Promote `TruncateId` to a shared `McpTelemetryHelpers` static class in `MCP/`.

**Chosen approach:** (C).

**Rationale:** Two middlewares in the same folder now both need the same truncation semantics. Duplicating invites drift; promoting to a shared helper (single file, no DI, static method) keeps both middlewares honest without adding architectural weight. The helper returns `"(missing)"` when the header is absent per FR-1's explicit contract — update `McpDiagnosticsMiddleware`'s single caller to accept the same sentinel (it currently substitutes `"***"` for missing IDs, which is inconsistent).

Note: this is a small, in-scope refactor. If the developer prefers to keep the two helpers separate to avoid touching `McpDiagnosticsMiddleware`, that is acceptable — annotate the duplication as a follow-up.

#### Decision 5: `RemoteIp` sourced from `HttpContext.Connection.RemoteIpAddress`

The spec explicitly says "respecting any proxy header forwarding already configured". Verify `ForwardedHeaders` middleware is registered in `Program.cs`/`ApplicationBuilderExtensions.cs` before this middleware runs. If it is not, log `RemoteIpAddress?.ToString() ?? "(unknown)"` and note in a comment that behind Azure Web App the address will be the front-door IP until forwarded-headers processing is wired. Do NOT add `UseForwardedHeaders` as part of this feature — that is a separate infrastructure change with wider implications.

#### Decision 6: `ElapsedMs` measurement

Start a `long startTimestamp = Stopwatch.GetTimestamp()` immediately when the request is identified as an MCP candidate; compute `Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds` only inside the 400 branch. This satisfies NFR-1 (no allocation, one timestamp read on the hot path).

## Implementation Guidance

### Directory / Module Structure

All changes stay in the existing `backend/src/Anela.Heblo.API/MCP/` folder. No new files are strictly required; if Decision 4(C) is adopted, add one small helper file.

```
backend/src/Anela.Heblo.API/MCP/
├── McpBadRequestMiddleware.cs        (modified — extend predicate, add shared log helper)
├── McpDiagnosticsMiddleware.cs       (touch only if unifying TruncateId)
├── McpTelemetryHelpers.cs            (NEW, optional — shared TruncateSessionId)
└── ...

backend/test/Anela.Heblo.Tests/MCP/
└── McpBadRequestMiddlewareTests.cs   (modified — add POST cases per FR-4)
```

**No changes** to `Program.cs`, `ApplicationBuilderExtensions.cs`, `McpModule.cs`, or any `appsettings*.json`. The middleware is already registered.

### Interfaces and Contracts

Suggested internal shape (illustrative, not binding):

```csharp
public class McpBadRequestMiddleware
{
    private static readonly EventId GetBadRequestEvent  = new(5931, "McpBadRequest");
    private static readonly EventId PostBadRequestEvent = new(5932, "McpBadRequest");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMcpPath(context))
        {
            await _next(context);
            return;
        }

        if (IsMcpGetRequest(context))
        {
            await HandleGetAsync(context);       // existing GET flow: probe short-circuit + log
            return;
        }

        if (IsMcpPostRequest(context))
        {
            var start = Stopwatch.GetTimestamp();
            await _next(context);
            if (context.Response.StatusCode == StatusCodes.Status400BadRequest)
            {
                LogBadMcpRequest(context, PostBadRequestEvent,
                    Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            return;
        }

        await _next(context);
    }

    private void LogBadMcpRequest(HttpContext ctx, EventId eventId, double elapsedMs) { ... }
    private static bool IsMcpPath(HttpContext ctx)
        => ctx.Request.Path.StartsWithSegments("/mcp");
    private static bool IsMcpPostRequest(HttpContext ctx)
        => ctx.Request.Method == HttpMethods.Post
           && ctx.Request.Path.StartsWithSegments("/mcp");
}
```

**Log fields** (must match on both GET and POST branches for the union-query requirement):

| Field | Type | Source | Missing-sentinel |
|---|---|---|---|
| `HTTPMethod` | string | `context.Request.Method` | n/a |
| `Path` | string | `Request.Path + Request.QueryString` | n/a |
| `StatusCode` | int | `Response.StatusCode` | n/a |
| `UserAgent` | string | `Request.Headers.UserAgent` | `"(missing)"` |
| `Origin` | string | `Request.Headers.Origin` | `"(missing)"` |
| `Accept` | string | `Request.Headers.Accept.ToString()` | `"(missing)"` |
| `ContentType` | string | `Request.Headers.ContentType` | `"(missing)"` |
| `McpSessionIdPresent` | bool | `Request.Headers.ContainsKey("Mcp-Session-Id")` | — |
| `McpSessionIdPrefix` | string | first 8 chars of `Mcp-Session-Id` | `"(missing)"` |
| `RemoteIp` | string | `Connection.RemoteIpAddress?.ToString()` | `"(unknown)"` |
| `ElapsedMs` | double | `Stopwatch.GetElapsedTime` | — |

Update the existing GET 400 log to include the same fields so the union-query FR-1 acceptance criterion holds. This is a widening of the log payload, not a breaking rename — existing Kusto queries continue to work.

### Data Flow

```
POST /mcp (JSON-RPC body) 
   │
   ├─► McpBadRequestMiddleware.InvokeAsync
   │      │
   │      ├─ IsMcpPostRequest? yes → t0 = Stopwatch.GetTimestamp()
   │      │
   │      ▼
   ├─► await _next(ctx)  [MCP SDK validates → sets status]
   │      │
   │      ▼
   ├─ ctx.Response.StatusCode == 400 ?
   │      │                     │
   │     no                    yes
   │      │                     │
   │      │                     ▼
   │      │            LogBadMcpRequest(ctx, PostEvent, elapsedMs)
   │      │                     │
   │      │                     ▼
   │      │            Application Insights `traces` row
   │      │            (structured customDimensions)
   │      ▼
   └─► return
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| MCP SDK sets a 400 for *authorized*, well-formed requests during session renegotiation, drowning the new log in noise identical to legitimate failures. | Medium | Log includes `McpSessionIdPresent` + `McpSessionIdPrefix` so Kusto can group renegotiation waves; no code mitigation needed — this is exactly the visibility this feature is designed to give. |
| Response body already sent (headers flushed) before we inspect status → we log a stale status. | Low | Not an issue for a rejected JSON-RPC 400: the SDK sets status before flushing. `context.Response.StatusCode` remains valid even after `HasStarted == true`. No `OnStarting` callback needed. |
| Overhead on the hot path of authenticated MCP POSTs (many tool calls/day) breaches NFR-1. | Low | Path/method check is a byte-comparison; header reads and log allocation are inside the `if (status == 400)` branch, so the 200 path pays only ~2 stopwatch reads and 2 string compares. |
| `Mcp-Session-Id` header capitalization differs across clients. | Low | ASP.NET `IHeaderDictionary` is case-insensitive by contract; use `context.Request.Headers["Mcp-Session-Id"]`. Add one unit test with lowercase `mcp-session-id`. |
| `ForwardedHeaders` middleware not registered → `RemoteIp` is Azure front-door, not the caller. | Low | Log the raw `RemoteIpAddress` and note in the code comment. Do not touch infrastructure in this feature. Follow-up ticket if the field turns out uninformative. |
| Full session ID leaks to telemetry via a debug-level log or missed truncation path. | Medium (PII) | Only ever emit `TruncateSessionId(...)` output; add one explicit unit test asserting that a 32-char session ID produces at most 8 chars in the emitted log-arguments. |
| Log spam if the MCP endpoint is scanned by a bot POSTing to `/mcp`. | Low | The volume today is ~9 400s/day (per brief). Even a scanner burst is bounded by request rate and produces one log line each — well within AI ingestion limits. |
| The GET-side log widening (added fields) breaks an existing Kusto dashboard that named a column that stopped existing. | Low | The change is **additive** — no field is removed or renamed. Keep every existing GET log field verbatim; only add. FR-3 acceptance requires existing tests pass unchanged. |

## Specification Amendments

1. **FR-1 field addition — `Path` should include query string.** The spec says "full request path including query string"; be explicit in the code that `Request.Path + Request.QueryString.Value` is the composed value (not `Path.Value` alone).

2. **FR-1 clarification — the GET 400 log must be widened to match.** FR-3 says "field names for GET entries are identical to the current output (no renames)" — that is compatible with adding new fields but the spec should make explicit that GET's 400 log gains `ContentType`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, and `ElapsedMs`, so a single Kusto query on `EventName == "McpBadRequest"` truly returns identically-shaped rows. Without this, FR-1's "single Kusto query can union both" requirement is only partially met.

3. **FR-4 additions.** Add three test cases the spec omits:
   - POST /mcp with **lowercase** `mcp-session-id` header → `McpSessionIdPresent=true` (case-insensitivity guard).
   - POST /mcp with a session ID **shorter than 8 characters** → `McpSessionIdPrefix` equals the full value (no `IndexOutOfRangeException`).
   - POST /mcp with `Mcp-Session-Id` **present but empty** → treated as absent (`McpSessionIdPresent=false`), documented in the log helper's contract.

4. **NFR-2 additions.** Explicitly note that the log helper must never read `Authorization`, `Cookie`, or `X-Api-Key` headers. A single test asserting no logger invocation carries a substring `"Bearer "` when the request has an Authorization header is cheap insurance.

5. **NFR-3 — Kusto query.** The PR must include the exact query. Suggested skeleton:
   ```kusto
   traces
   | where customDimensions.EventName == "McpBadRequest"
   | extend Method   = tostring(customDimensions.HTTPMethod),
            UA       = tostring(customDimensions.UserAgent),
            SidPres  = tobool(customDimensions.McpSessionIdPresent),
            SidPref  = tostring(customDimensions.McpSessionIdPrefix)
   | summarize count() by bin(timestamp, 1h), Method, SidPres, UA
   ```

6. **Documentation.** Add a short "Diagnostics" section to `docs/integrations/mcp-server.md` describing the log's `EventName`, its custom dimensions, and the Kusto query above. Current MCP docs are silent on observability; developers will not know this exists without a doc pointer.

## Prerequisites

None. All dependencies (middleware pipeline, MEL, Application Insights sink, existing middleware registration, test project) are already in place. No migrations, no config changes, no infrastructure changes are required to ship this feature.

Two soft prerequisites for full value (not blockers for merge):
- `ForwardedHeaders` middleware in production so `RemoteIp` is the real caller — verify state and file a follow-up ticket if missing.
- A Kusto dashboard / AI workbook consuming `EventName == "McpBadRequest"` — explicitly out of scope per the spec, but the PR description should link to the ticket that will track it.
