# Design: Extend MCP Bad-Request Diagnostics to POST /mcp

## Component Design

### McpBadRequestMiddleware (modified)

**File:** `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`

**Responsibility:** Observe HTTP 400 responses on `/mcp` for both GET and POST requests and emit structured warning-level log entries to Application Insights. For GET-only: additionally short-circuit headerless probes to 404.

**Interface changes:**

```csharp
public class McpBadRequestMiddleware
{
    // Existing
    private static readonly EventId GetBadRequestEvent  = new(5931, "McpBadRequest");
    // New
    private static readonly EventId PostBadRequestEvent = new(5932, "McpBadRequest");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMcpPath(context))           { await _next(context); return; }
        if (IsMcpGetRequest(context))      { await HandleGetAsync(context); return; }  // existing
        if (IsMcpPostRequest(context))     { await HandlePostAsync(context); return; } // new
        await _next(context);
    }

    // New POST handler — observe only, never rewrite status or body
    private async Task HandlePostAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        await _next(context);
        if (context.Response.StatusCode == StatusCodes.Status400BadRequest)
            LogBadMcpRequest(context, PostBadRequestEvent,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    // Shared log helper (widened to cover fields required for GET/POST union query)
    private void LogBadMcpRequest(HttpContext ctx, EventId eventId, double elapsedMs) { ... }

    private static bool IsMcpPath(HttpContext ctx)
        => ctx.Request.Path.StartsWithSegments("/mcp");
    private static bool IsMcpGetRequest(HttpContext ctx)
        => ctx.Request.Method == HttpMethods.Get
           && ctx.Request.Path.StartsWithSegments("/mcp");
    private static bool IsMcpPostRequest(HttpContext ctx)
        => ctx.Request.Method == HttpMethods.Post
           && ctx.Request.Path.StartsWithSegments("/mcp");
}
```

**Invariants:**
- Path and method filter evaluates before any header reads. Non-`/mcp` requests pay only two string comparisons.
- `Stopwatch.GetTimestamp()` is captured once per POST-candidate request; `GetElapsedTime` is called only inside the `status == 400` branch.
- The POST handler NEVER modifies `context.Response.StatusCode`, body, or headers.
- The GET `HandleGetAsync` implementation (probe short-circuit → 404 + log) is unchanged.
- The existing GET 400 log call is migrated to `LogBadMcpRequest` so both methods emit identically-shaped rows. Fields added to the shared helper are additive; no existing field is renamed or removed.

---

### McpTelemetryHelpers (new, optional)

**File:** `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs`

**Responsibility:** Provide a single `TruncateSessionId` static method shared by `McpBadRequestMiddleware` and `McpDiagnosticsMiddleware`, eliminating the risk of per-class drift in session-ID truncation logic.

**Interface:**

```csharp
internal static class McpTelemetryHelpers
{
    /// <summary>
    /// Returns the first 8 characters of <paramref name="value"/> if non-empty,
    /// or "(missing)" if null/empty.  Never returns more than 8 characters.
    /// </summary>
    public static string TruncateSessionId(string? value)
        => string.IsNullOrEmpty(value)
            ? "(missing)"
            : value.Length <= 8 ? value : value[..8];
}
```

**Contracts:**
- Input `null` → `"(missing)"`.
- Input `""` (empty string) → `"(missing)"`. A header present but empty is treated as absent per FR-1.
- Input shorter than 8 characters → returned verbatim (no `IndexOutOfRangeException`).
- Input ≥ 8 characters → exactly 8 characters returned.
- No trailing marker (unlike the pre-existing `TruncateId` in `McpDiagnosticsMiddleware`). Update `McpDiagnosticsMiddleware`'s single caller to use this helper; if the developer prefers to leave `McpDiagnosticsMiddleware` untouched, duplicate with a code comment marking it as a follow-up to consolidate.

---

### McpDiagnosticsMiddleware (touch-only if unifying TruncateId)

**File:** `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`

**Change:** Replace the private `TruncateId` helper with a call to `McpTelemetryHelpers.TruncateSessionId`. Behavior is identical for the existing caller; the only delta is that an absent session ID now returns `"(missing)"` instead of the previous `"***"` sentinel. This aligns the two middlewares' output for Kusto union queries. **This change is optional** — if keeping it deferred is preferred, annotate the duplication with a `// TODO` comment.

---

### McpBadRequestMiddlewareTests (modified)

**File:** `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

**Responsibility:** Extend the existing test suite to cover POST cases. No new test infrastructure is required — the existing `CreateContext(method, path, ...)` and `VerifyNoLogCalled` helpers accept a `method` parameter.

**New test cases required (FR-4 + arch-review amendments):**

| # | Scenario | Expected outcome |
|---|---|---|
| P1 | `POST /mcp`, no `Mcp-Session-Id`, status 400 | One warning logged; `McpSessionIdPresent=false` |
| P2 | `POST /mcp`, `Mcp-Session-Id: abcdef1234567890`, status 400 | One warning logged; `McpSessionIdPresent=true`, `McpSessionIdPrefix="abcdef12"` |
| P3 | `POST /mcp`, status 200 | No log emitted |
| P4 | `POST /mcp`, status 500 | No log emitted |
| P5 | `POST /mcp`, no `Accept`/`User-Agent`, status 400 | One warning logged (NOT rewritten to 404) |
| P6 | `GET /mcp`, headerless probe | Still returns 404 (existing behavior) |
| P7 | Non-`/mcp` path, any method, status 400 | No log emitted |
| P8 | `POST /mcp`, lowercase `mcp-session-id` header, status 400 | `McpSessionIdPresent=true` (case-insensitivity) |
| P9 | `POST /mcp`, `Mcp-Session-Id` shorter than 8 chars, status 400 | `McpSessionIdPrefix` equals the full value |
| P10 | `POST /mcp`, `Mcp-Session-Id` present but empty, status 400 | `McpSessionIdPresent=false`, `McpSessionIdPrefix="(missing)"` |
| P11 | `POST /mcp` with `Authorization: Bearer …`, status 400 | Log args contain no `"Bearer "` substring |
| P12 | `POST /mcp`, 32-char session ID, status 400 | `McpSessionIdPrefix` is exactly 8 characters |

All existing GET tests must continue to pass unchanged.

---

### No changes to

- `Program.cs`
- `ApplicationBuilderExtensions.cs` (middleware already registered at line 128)
- `McpModule.cs`
- Any `appsettings*.json`
- Any feature-flag configuration

---

## Data Schemas

### Application Insights `traces` — `customDimensions` schema

This is the sole "schema" introduced by the feature. No database tables or API contracts change.

**Log entry shape** (emitted by `LogBadMcpRequest` for both GET and POST 400 responses):

| `customDimensions` key | CLR type | Source | Missing sentinel |
|---|---|---|---|
| `EventName` | `string` | Constant `"McpBadRequest"` | — |
| `EventId` | `int` | `5931` (GET) / `5932` (POST) | — |
| `HTTPMethod` | `string` | `context.Request.Method` | — |
| `Path` | `string` | `Request.Path.Value + Request.QueryString.Value` | — |
| `StatusCode` | `int` | `Response.StatusCode` (always `400`) | — |
| `UserAgent` | `string` | `Request.Headers.UserAgent.ToString()` | `"(missing)"` |
| `Origin` | `string` | `Request.Headers.Origin.ToString()` | `"(missing)"` |
| `Accept` | `string` | `Request.Headers.Accept.ToString()` | `"(missing)"` |
| `ContentType` | `string` | `Request.Headers.ContentType.ToString()` | `"(missing)"` |
| `McpSessionIdPresent` | `bool` | `!string.IsNullOrEmpty(Request.Headers["Mcp-Session-Id"])` | — |
| `McpSessionIdPrefix` | `string` | `McpTelemetryHelpers.TruncateSessionId(Request.Headers["Mcp-Session-Id"])` | `"(missing)"` |
| `RemoteIp` | `string` | `Connection.RemoteIpAddress?.ToString()` | `"(unknown)"` |
| `ElapsedMs` | `double` | `Stopwatch.GetElapsedTime(start).TotalMilliseconds` | — |

**Notes:**
- `Authorization`, `Cookie`, and `X-Api-Key` headers are never read or logged.
- `Mcp-Session-Id` raw value is never written to telemetry; only presence (bool) and an 8-char prefix.
- Header lookup uses `IHeaderDictionary` indexer, which is case-insensitive by contract, covering clients that send `mcp-session-id` in lowercase.
- `RemoteIp` reflects `Connection.RemoteIpAddress` post-`ForwardedHeaders` middleware processing. Until `ForwardedHeaders` is confirmed wired, this may surface the Azure Front Door IP; a code comment should note this. No infrastructure change is part of this feature.

---

### Canonical Kusto query (required in PR description)

```kusto
traces
| where customDimensions.EventName == "McpBadRequest"
| extend Method   = tostring(customDimensions.HTTPMethod),
         UA       = tostring(customDimensions.UserAgent),
         SidPres  = tobool(customDimensions.McpSessionIdPresent),
         SidPref  = tostring(customDimensions.McpSessionIdPrefix),
         Origin   = tostring(customDimensions.Origin),
         RemoteIp = tostring(customDimensions.RemoteIp),
         Elapsed  = todouble(customDimensions.ElapsedMs)
| summarize count(), avg(Elapsed), max(Elapsed) by bin(timestamp, 1h), Method, SidPres, UA
| order by timestamp desc
```

A secondary query to identify potential scanner/probe traffic:

```kusto
traces
| where customDimensions.EventName == "McpBadRequest"
       and customDimensions.HTTPMethod == "POST"
| extend UA      = tostring(customDimensions.UserAgent),
         SidPres = tobool(customDimensions.McpSessionIdPresent),
         IP      = tostring(customDimensions.RemoteIp)
| where UA == "(missing)" and SidPres == false
| summarize probe_count = count() by bin(timestamp, 1h), IP
| order by probe_count desc
```

---

### Documentation update

**File:** `docs/integrations/mcp-server.md`

Add a "Diagnostics" section with:
- Log event name `McpBadRequest`, EventIds `5931` (GET) and `5932` (POST).
- List of `customDimensions` keys from the schema table above.
- The Kusto query skeleton.
- Note on `RemoteIp` accuracy depending on `ForwardedHeaders` middleware state.
