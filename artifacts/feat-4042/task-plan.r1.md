# Extend MCP Bad-Request Diagnostics to POST /mcp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing `McpBadRequestMiddleware` to observe and log HTTP 400 responses on `POST /mcp` (in addition to the existing GET-only path), turning a chronic ~30-38% silent bad-request rate into an attributable, queryable telemetry signal without touching MCP SDK behavior.

**Architecture:** Modify a single existing middleware (`McpBadRequestMiddleware`) to add a POST branch that observes the response status after the pipeline runs and, on 400, emits a structured warning log with the same shape as the GET branch. Introduce one small shared helper (`McpTelemetryHelpers.TruncateSessionId`) to keep session-ID prefix truncation consistent between the two MCP middlewares. Widen the GET 400 log payload with the same fields so a single Kusto query on `EventName == "McpBadRequest"` can union both channels. No infrastructure, config, DI, or HTTP-contract changes.

**Tech Stack:** .NET 8, ASP.NET Core middleware pipeline, `Microsoft.Extensions.Logging` (structured), Application Insights sink (already wired), xUnit + Moq for tests.

---

### task: read-existing-middleware-and-tests

**Files:**
- Read: `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
- Read: `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`
- Read: `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`
- Read: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

This is a read-only orientation step. No code changes. The goal is to confirm the exact current shape of the middleware, the existing GET 400 log call, the `TruncateId` helper on `McpDiagnosticsMiddleware`, and the current test scaffold (`CreateContext`, `VerifyNoLogCalled`) before you touch anything.

- [ ] **Step 1: Read `McpBadRequestMiddleware.cs`**

Run: `cat backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
Confirm:
- The class is registered as a normal middleware (`InvokeAsync(HttpContext context)` or `Invoke`).
- There is an existing predicate `IsMcpGetRequest` (or similar).
- There is a GET short-circuit path for headerless probes that returns 404.
- The existing GET 400 log call site is a single method that reads headers and emits one warning log entry.

- [ ] **Step 2: Read `McpDiagnosticsMiddleware.cs`**

Run: `cat backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`
Confirm:
- A private static `TruncateId` helper exists around the line referenced by the arch-review (line 54).
- Note the current sentinel it uses for absent session IDs (design says it currently substitutes `"***"`).
- Note who its single caller is (you may need to update it in a later task).

- [ ] **Step 3: Read middleware registration**

Run: `cat backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs | sed -n '110,140p'`
Confirm:
- `McpBadRequestMiddleware` is registered before `McpDiagnosticsMiddleware` and before `MapMcp("/mcp")`.
- No new registration is required by this feature.

- [ ] **Step 4: Read existing tests**

Run: `cat backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`
Confirm:
- Helpers `CreateContext(method, path, ...)` and `VerifyNoLogCalled` exist and accept a `method` parameter.
- The test class uses `Mock<ILogger<McpBadRequestMiddleware>>` (or the same `ILogger<T>` pattern) so the new POST tests can reuse the exact same verification style.
- Note the exact `It.Is<...>` / `Verify(...)` incantation used to assert one warning was logged with specific state properties — new tests must match this style.

- [ ] **Step 5: Record findings in a scratch note (no commit)**

Write to `/tmp/claude-0/-home-user-worktrees-feature-4042-Post-Mcp-35-8-Bad-Request-Rate-59-165-In-P7d-No-Di/18eb13b0-74ef-5955-b000-efd0ad1992ba/scratchpad/mcp-notes.md`:
- Exact name of the current GET log helper method (if any).
- Exact name of the current predicate for GET requests.
- Exact `Verify(...)` shape used by existing tests.
- Whether the existing test's `CreateContext` supports setting a response status (or if we need to introduce a fake `RequestDelegate` that sets `context.Response.StatusCode`).

This is a working note only; it will not be committed.

- [ ] **Step 6: Confirm baseline build and tests pass**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
Expected: All tests pass (baseline).

No commit — this is orientation only.

---

### task: add-mcp-telemetry-helpers

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs`
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs`

Introduce a small shared helper for session-ID prefix truncation. Both `McpBadRequestMiddleware` (this feature) and `McpDiagnosticsMiddleware` (existing) need identical semantics; a single helper eliminates per-class drift.

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs`:

```csharp
using Anela.Heblo.API.MCP;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpTelemetryHelpersTests
{
    [Fact]
    public void TruncateSessionId_Null_ReturnsMissingSentinel()
    {
        Assert.Equal("(missing)", McpTelemetryHelpers.TruncateSessionId(null));
    }

    [Fact]
    public void TruncateSessionId_Empty_ReturnsMissingSentinel()
    {
        Assert.Equal("(missing)", McpTelemetryHelpers.TruncateSessionId(string.Empty));
    }

    [Fact]
    public void TruncateSessionId_ShorterThan8_ReturnsFullValue()
    {
        Assert.Equal("abc", McpTelemetryHelpers.TruncateSessionId("abc"));
    }

    [Fact]
    public void TruncateSessionId_Exactly8_ReturnsFullValue()
    {
        Assert.Equal("abcdefgh", McpTelemetryHelpers.TruncateSessionId("abcdefgh"));
    }

    [Fact]
    public void TruncateSessionId_LongerThan8_ReturnsFirstEightChars()
    {
        Assert.Equal("abcdefgh", McpTelemetryHelpers.TruncateSessionId("abcdefgh12345678"));
    }

    [Fact]
    public void TruncateSessionId_32CharValue_NeverExceedsEightChars()
    {
        var sessionId = new string('x', 32);
        var result = McpTelemetryHelpers.TruncateSessionId(sessionId);
        Assert.Equal(8, result.Length);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpTelemetryHelpersTests"`
Expected: FAIL — `McpTelemetryHelpers` type does not exist (compilation error).

- [ ] **Step 3: Create the helper**

Create `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs`:

```csharp
namespace Anela.Heblo.API.MCP;

/// <summary>
/// Small helpers shared across MCP middleware for consistent telemetry values.
/// </summary>
internal static class McpTelemetryHelpers
{
    /// <summary>
    /// Returns the first 8 characters of <paramref name="value"/> if non-empty,
    /// or "(missing)" if null/empty. Never returns more than 8 characters.
    /// A header present but empty is treated as absent per FR-1.
    /// </summary>
    public static string TruncateSessionId(string? value)
        => string.IsNullOrEmpty(value)
            ? "(missing)"
            : value.Length <= 8 ? value : value[..8];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpTelemetryHelpersTests"`
Expected: PASS — all 6 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs \
        backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs
git commit -m "feat(mcp): add shared TruncateSessionId helper for MCP telemetry

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: extract-log-helper-widen-fields

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
- Modify: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Extract the existing GET-only 400 log call into a shared `LogBadMcpRequest` method that emits ALL fields required by both GET and POST branches. This is additive on the GET side — no field is renamed or removed, so all existing GET tests continue to pass. Introduce `EventId` constants with the shared `EventName = "McpBadRequest"`.

Do NOT add the POST branch yet — that is the next task. This task only refactors the log call for reuse and widens the GET payload.

- [ ] **Step 1: Update the existing GET tests to assert the new fields (widening)**

Open `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` and locate the existing test that asserts a GET 400 emits a log. Add assertions that the log's state includes the new keys: `ContentType`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, `ElapsedMs`. Use the same `Verify(...)` incantation the existing tests use to inspect log state — check the scratchpad note from the previous task.

Add one new focused test in the same class to make the widening intent explicit:

```csharp
[Fact]
public async Task GetBadRequest_Log_IncludesAllUnionFields()
{
    // Arrange: GET /mcp that reaches next() and gets a 400 back
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("GET", "/mcp");
    context.Request.Headers["Accept"] = "application/json, text/event-stream";
    context.Request.Headers["User-Agent"] = "test-agent";
    context.Request.Headers["Mcp-Session-Id"] = "abcdef1234567890";

    // Act
    await middleware.InvokeAsync(context);

    // Assert — one warning log call, whose state contains every union-schema field.
    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest"),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("HTTPMethod") &&
            state.ToString()!.Contains("Path") &&
            state.ToString()!.Contains("StatusCode") &&
            state.ToString()!.Contains("UserAgent") &&
            state.ToString()!.Contains("Origin") &&
            state.ToString()!.Contains("Accept") &&
            state.ToString()!.Contains("ContentType") &&
            state.ToString()!.Contains("McpSessionIdPresent") &&
            state.ToString()!.Contains("McpSessionIdPrefix") &&
            state.ToString()!.Contains("RemoteIp") &&
            state.ToString()!.Contains("ElapsedMs")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

Note: if the scratchpad shows the existing tests use a different verification pattern (e.g. capturing a `FormattedLogValues` state and iterating its keys), rewrite the assertion above using that same pattern for consistency. The intent is the same — one warning, whose structured state contains all 11 keys.

- [ ] **Step 2: Run tests to verify the new/widened GET test fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
Expected: The new `GetBadRequest_Log_IncludesAllUnionFields` test FAILS (fields like `ContentType`, `McpSessionIdPresent`, `RemoteIp`, `ElapsedMs` are not in the current GET log). All previously-passing tests still pass.

- [ ] **Step 3: Refactor the middleware — extract shared `LogBadMcpRequest` and widen the GET payload**

Open `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs` and:

1. Add these `using`s at the top if not present:
   ```csharp
   using System.Diagnostics;
   using Microsoft.AspNetCore.Http;
   using Microsoft.Extensions.Logging;
   ```
2. Add these constants inside the class (adjust visibility to match the class's style):
   ```csharp
   private static readonly EventId GetBadRequestEvent  = new(5931, "McpBadRequest");
   private static readonly EventId PostBadRequestEvent = new(5932, "McpBadRequest");
   ```
3. Add the shared log helper. Update or add:
   ```csharp
   private void LogBadMcpRequest(HttpContext ctx, EventId eventId, double elapsedMs)
   {
       var req = ctx.Request;
       var sessionIdRaw = req.Headers["Mcp-Session-Id"].ToString();
       var sessionIdPresent = !string.IsNullOrEmpty(sessionIdRaw);
       var path = (req.Path.Value ?? string.Empty) + (req.QueryString.Value ?? string.Empty);

       _logger.Log(
           LogLevel.Warning,
           eventId,
           "MCP bad request: {HTTPMethod} {Path} -> {StatusCode} (UA={UserAgent}, Origin={Origin}, Accept={Accept}, ContentType={ContentType}, SidPresent={McpSessionIdPresent}, SidPrefix={McpSessionIdPrefix}, IP={RemoteIp}, ElapsedMs={ElapsedMs})",
           req.Method,
           path,
           ctx.Response.StatusCode,
           req.Headers.UserAgent.Count > 0 ? req.Headers.UserAgent.ToString() : "(missing)",
           req.Headers.Origin.Count > 0 ? req.Headers.Origin.ToString() : "(missing)",
           req.Headers.Accept.Count > 0 ? req.Headers.Accept.ToString() : "(missing)",
           req.Headers.ContentType.Count > 0 ? req.Headers.ContentType.ToString() : "(missing)",
           sessionIdPresent,
           McpTelemetryHelpers.TruncateSessionId(sessionIdRaw),
           ctx.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
           elapsedMs);
   }
   ```
   Note: `_logger.Log(LogLevel.Warning, eventId, "template", args...)` is the overload that produces a `FormattedLogValues` state whose keys equal the message-template placeholder names. If the existing GET code uses `_logger.LogWarning(...)` today, migrate it to this overload so the `EventId` (with `EventName`) is attached.

4. Replace the GET branch's existing inline log call with:
   ```csharp
   // Inside the existing GET flow, at the point where 400 is currently logged:
   LogBadMcpRequest(context, GetBadRequestEvent, elapsedMs);
   ```
   The GET path already runs after `_next(context)` returns (per the current code). Capture `Stopwatch.GetTimestamp()` early in the GET flow if it isn't already, and pass `Stopwatch.GetElapsedTime(start).TotalMilliseconds` here.

   If the existing GET flow does not currently measure elapsed time, add it: right at the top of the GET branch, before the header inspection / probe short-circuit:
   ```csharp
   var getStart = Stopwatch.GetTimestamp();
   // ... existing logic ...
   // when logging the 400:
   LogBadMcpRequest(context, GetBadRequestEvent,
       Stopwatch.GetElapsedTime(getStart).TotalMilliseconds);
   ```

5. Do NOT delete or rename any previously-logged field name. If the existing log used, e.g., the placeholder `{Method}`, keep emitting that too or (safer) rename in the template only after searching the codebase and any dashboards for consumers. The scratchpad note captured the existing field names — cross-check and preserve them.

- [ ] **Step 4: Run tests to verify the widened GET log passes and no existing test regresses**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
Expected: PASS — the new `GetBadRequest_Log_IncludesAllUnionFields` test now passes; every pre-existing GET test still passes.

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings from this project.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs \
        backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "refactor(mcp): extract shared LogBadMcpRequest, widen GET log fields

Prepares for POST diagnostic logging by unifying the log payload so a
single Kusto query on EventName='McpBadRequest' can union GET and POST.
No field is removed or renamed — additive only.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: add-post-diagnostics-logging

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add the POST branch to `McpBadRequestMiddleware`: observe the response status after `_next(context)` returns and, on 400, call the shared `LogBadMcpRequest` with `PostBadRequestEvent`. NEVER rewrite status, body, or headers on the POST path (FR-2).

Write the P1 test (POST 400 with no session header) first, watch it fail, implement the branch, watch it pass. Then commit. Additional POST test cases (P2–P12) come in later tasks so each test/implementation pair stays bite-sized.

- [ ] **Step 1: Write the failing test for POST 400 with no `Mcp-Session-Id`**

Add to `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`:

```csharp
[Fact]
public async Task PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent()
{
    // Arrange
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["User-Agent"] = "test-client";
    context.Request.Headers["Accept"] = "application/json";
    // Deliberately no Mcp-Session-Id.

    // Act
    await middleware.InvokeAsync(context);

    // Assert — exactly one warning, EventName=McpBadRequest, McpSessionIdPresent=false.
    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest" && e.Id == 5932),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("HTTPMethod") &&
            state.ToString()!.Contains("POST") &&
            state.ToString()!.Contains("SidPresent=False")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);

    // POST response was NOT rewritten.
    Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
}
```

If the scratchpad noted a different verification style used in this file (e.g. capturing state entries by key), rewrite the assertion the same way — the intent is unchanged.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests.PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent"`
Expected: FAIL — the middleware today does not observe POST; no warning is logged.

- [ ] **Step 3: Add the POST branch to the middleware**

Open `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`.

1. Add the POST predicate near the GET predicate:
   ```csharp
   private static bool IsMcpPostRequest(HttpContext ctx)
       => ctx.Request.Method == HttpMethods.Post
          && ctx.Request.Path.StartsWithSegments("/mcp");
   ```

2. Add the POST handler:
   ```csharp
   private async Task HandlePostAsync(HttpContext context)
   {
       var start = Stopwatch.GetTimestamp();
       await _next(context);
       if (context.Response.StatusCode == StatusCodes.Status400BadRequest)
       {
           LogBadMcpRequest(
               context,
               PostBadRequestEvent,
               Stopwatch.GetElapsedTime(start).TotalMilliseconds);
       }
   }
   ```

3. Wire it into `InvokeAsync` between the GET branch and the final `await _next(context)` fallthrough. The full ordering must be:
   ```csharp
   public async Task InvokeAsync(HttpContext context)
   {
       if (!context.Request.Path.StartsWithSegments("/mcp"))
       {
           await _next(context);
           return;
       }

       if (IsMcpGetRequest(context))
       {
           await HandleGetAsync(context);   // existing
           return;
       }

       if (IsMcpPostRequest(context))
       {
           await HandlePostAsync(context);  // new
           return;
       }

       await _next(context);
   }
   ```
   If the current `InvokeAsync` inlines the GET flow (rather than calling a `HandleGetAsync`), leave the GET code inline and slot the POST predicate/handler either as a peer inline block or extract both — do whichever produces the smaller diff. The correctness constraint is: POST /mcp goes into the new branch; every other request path/method behaves exactly as before.

4. Verify the POST handler NEVER writes to `context.Response.StatusCode`, `.Body`, or `.Headers`. Read your diff and confirm.

- [ ] **Step 4: Run the failing test to verify it now passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests.PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent"`
Expected: PASS.

- [ ] **Step 5: Run the full middleware test class**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
Expected: PASS — all existing tests plus the new one.

- [ ] **Step 6: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs \
        backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "feat(mcp): observe POST /mcp 400 responses in bad-request middleware

Adds a POST branch to McpBadRequestMiddleware that logs a single
structured warning on 400. Never rewrites status/body/headers — the
MCP SDK's own validation is preserved verbatim (FR-2).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: post-session-header-present-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add FR-4 coverage: POST with an `Mcp-Session-Id` header logs `McpSessionIdPresent=true` and `McpSessionIdPrefix` equal to the first 8 characters.

- [ ] **Step 1: Write the failing test**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Fact]
public async Task PostBadRequest_WithSessionHeader_LogsPresenceAndEightCharPrefix()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["Mcp-Session-Id"] = "abcdef1234567890";

    await middleware.InvokeAsync(context);

    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest" && e.Id == 5932),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("SidPresent=True") &&
            state.ToString()!.Contains("SidPrefix=abcdef12")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

- [ ] **Step 2: Run to verify it passes immediately (no impl change needed)**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~PostBadRequest_WithSessionHeader_LogsPresenceAndEightCharPrefix"`
Expected: PASS — the middleware from the previous task already handles this case; this test locks the behavior in.

If it FAILS, inspect the failure — the placeholder names or values used in the log template may not match the test's substring assertions. Adjust the assertion (or, if there is a genuine bug in truncation, fix `McpTelemetryHelpers` — but the previous task's helper tests should have caught that).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): POST 400 with session header logs 8-char prefix

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: post-happy-path-no-log-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add FR-4 coverage: POST 200 responses produce NO log output. Guards against the middleware becoming noisy on the hot path.

- [ ] **Step 1: Write the failing test**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Fact]
public async Task PostSuccess200_EmitsNoLog()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["Mcp-Session-Id"] = "abcdef1234567890";

    await middleware.InvokeAsync(context);

    VerifyNoLogCalled(loggerMock);
    Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
}
```

If `VerifyNoLogCalled` does not exist in the current suite, use:

```csharp
loggerMock.Verify(l => l.Log(
    It.IsAny<LogLevel>(),
    It.IsAny<EventId>(),
    It.IsAny<It.IsAnyType>(),
    It.IsAny<Exception>(),
    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Never);
```

- [ ] **Step 2: Run and confirm it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~PostSuccess200_EmitsNoLog"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): POST 200 does not emit a bad-request log

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: post-500-no-log-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add FR-4 coverage: POST 500 produces NO log — the feature scope is strictly 400.

- [ ] **Step 1: Write the failing test**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Fact]
public async Task PostServerError500_EmitsNoLog()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");

    await middleware.InvokeAsync(context);

    loggerMock.Verify(l => l.Log(
        It.IsAny<LogLevel>(),
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

- [ ] **Step 2: Run and confirm it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~PostServerError500_EmitsNoLog"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): POST 500 does not emit a bad-request log (scope is 400 only)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: post-headerless-probe-not-rewritten-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add FR-2 acceptance test: a headerless bodyless POST must reach the MCP SDK unchanged; on 400 it is logged, NOT rewritten to 404 (which is the GET behavior).

- [ ] **Step 1: Write the failing test**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Fact]
public async Task PostHeaderlessBodyless_400_LoggedButNotRewritten()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    // No Accept, no User-Agent, no body — the GET equivalent short-circuits to 404;
    // POST must NOT short-circuit.
    var context = CreateContext("POST", "/mcp");

    await middleware.InvokeAsync(context);

    Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest" && e.Id == 5932),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

- [ ] **Step 2: Run and confirm it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~PostHeaderlessBodyless_400_LoggedButNotRewritten"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): headerless POST /mcp is logged but not rewritten to 404

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: non-mcp-path-ignored-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add FR-4 coverage: non-`/mcp` paths never trigger the middleware, regardless of method or status.

- [ ] **Step 1: Write the failing test**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Theory]
[InlineData("GET", "/api/foo", 400)]
[InlineData("POST", "/api/foo", 400)]
[InlineData("POST", "/health", 500)]
[InlineData("POST", "/mcpx", 400)]  // path prefix must be a segment match, not a raw prefix
public async Task NonMcpPath_NeverLogs(string method, string path, int status)
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = status;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext(method, path);

    await middleware.InvokeAsync(context);

    loggerMock.Verify(l => l.Log(
        It.IsAny<LogLevel>(),
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

- [ ] **Step 2: Run and confirm it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~NonMcpPath_NeverLogs"`
Expected: PASS on all four theory rows. `StartsWithSegments("/mcp")` treats `/mcpx` as a non-match, so the `/mcpx` row confirms the path filter is segment-based rather than raw-prefix.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): non-/mcp paths never trigger the bad-request middleware

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: post-session-header-edge-cases

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add the three amendments from `arch-review.r1.md` §"Specification Amendments (3)": lowercase header key, short session ID, empty-string session ID.

- [ ] **Step 1: Write the failing tests**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Fact]
public async Task PostBadRequest_LowercaseSessionHeader_TreatedAsPresent()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["mcp-session-id"] = "abcdef1234567890"; // lowercase

    await middleware.InvokeAsync(context);

    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest"),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("SidPresent=True") &&
            state.ToString()!.Contains("SidPrefix=abcdef12")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}

[Fact]
public async Task PostBadRequest_ShortSessionId_LogsFullValueNoOverflow()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["Mcp-Session-Id"] = "abc"; // shorter than 8 chars

    await middleware.InvokeAsync(context);

    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest"),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("SidPresent=True") &&
            state.ToString()!.Contains("SidPrefix=abc")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}

[Fact]
public async Task PostBadRequest_EmptySessionId_TreatedAsAbsent()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["Mcp-Session-Id"] = ""; // present but empty

    await middleware.InvokeAsync(context);

    loggerMock.Verify(l => l.Log(
        LogLevel.Warning,
        It.Is<EventId>(e => e.Name == "McpBadRequest"),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("SidPresent=False") &&
            state.ToString()!.Contains("SidPrefix=(missing)")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

- [ ] **Step 2: Run the tests**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~PostBadRequest_LowercaseSessionHeader_TreatedAsPresent|FullyQualifiedName~PostBadRequest_ShortSessionId_LogsFullValueNoOverflow|FullyQualifiedName~PostBadRequest_EmptySessionId_TreatedAsAbsent"`
Expected: All three PASS. The lowercase test passes because `IHeaderDictionary` is case-insensitive; the short-ID test passes because `TruncateSessionId` returns the value verbatim when < 8 chars; the empty test passes because `string.IsNullOrEmpty` treats `""` the same as `null` and drives `McpSessionIdPresent=false` in `LogBadMcpRequest`.

If the empty-string case FAILS with `SidPresent=True`, the middleware is deriving `sessionIdPresent` from `Request.Headers.ContainsKey(...)` instead of `!string.IsNullOrEmpty(sessionIdRaw)`. Fix the middleware to use the string-check (already the pattern shown in the earlier task's `LogBadMcpRequest`).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): POST session-id edge cases (lowercase, short, empty)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: pii-guard-authorization-header-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add the NFR-2 (arch-review amendment 4) guard: a POST 400 with an `Authorization: Bearer …` header must NOT surface `"Bearer "` in the emitted log. Cheap insurance against a future refactor accidentally reading arbitrary headers.

- [ ] **Step 1: Write the failing test**

Add to `McpBadRequestMiddlewareTests`:

```csharp
[Fact]
public async Task PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var next = new RequestDelegate(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("POST", "/mcp");
    context.Request.Headers["Authorization"] = "Bearer supersecrettoken";
    context.Request.Headers["Cookie"] = "session=secret";
    context.Request.Headers["X-Api-Key"] = "topsecret";

    await middleware.InvokeAsync(context);

    // Assert that no log call contains any of the sensitive substrings anywhere in its
    // formatted state.
    loggerMock.Verify(l => l.Log(
        It.IsAny<LogLevel>(),
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((state, _) =>
            state.ToString()!.Contains("Bearer") ||
            state.ToString()!.Contains("supersecrettoken") ||
            state.ToString()!.Contains("session=secret") ||
            state.ToString()!.Contains("topsecret")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

- [ ] **Step 2: Run and confirm it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer"`
Expected: PASS — `LogBadMcpRequest` only reads the explicit allow-listed headers (`UserAgent`, `Origin`, `Accept`, `ContentType`, `Mcp-Session-Id`), never `Authorization`, `Cookie`, or `X-Api-Key`.

If it FAILS, the middleware is enumerating all headers somewhere in the log call — narrow the read to the allow-listed headers only.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): POST 400 log never leaks Authorization/Cookie/X-Api-Key values

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: get-existing-behavior-regression-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

Add FR-3 explicit test: GET /mcp with a headerless probe still short-circuits to 404. This locks in the existing behavior that MUST NOT regress.

- [ ] **Step 1: Confirm what an existing test already covers**

Run: `grep -n "404\|StatusCodes.Status404\|GetHeaderlessProbe\|Probe" backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`
Expected: At least one existing GET probe → 404 test appears. If one already exists and passes, skip to Step 3 and add only the reinforcing assertion below; otherwise write it fresh in Step 2.

- [ ] **Step 2: Add / reinforce the test**

If no dedicated test exists, add:

```csharp
[Fact]
public async Task GetHeaderlessProbe_StillReturns404_NotFallthrough()
{
    var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    var nextCalled = false;
    var next = new RequestDelegate(ctx =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });
    var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
    var context = CreateContext("GET", "/mcp");
    // Deliberately no Accept, no User-Agent — a canonical scanner/probe.

    await middleware.InvokeAsync(context);

    Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    Assert.False(nextCalled, "GET probe must short-circuit before MCP SDK runs");
}
```

- [ ] **Step 3: Run and confirm it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
Expected: PASS — full test class green.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs
git commit -m "test(mcp): lock in GET headerless-probe -> 404 short-circuit (FR-3)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: consolidate-truncateid-in-diagnostics-middleware

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`

Optional but recommended (arch-review Decision 4, option C). Replace the private `TruncateId` helper in `McpDiagnosticsMiddleware` with a call to the new `McpTelemetryHelpers.TruncateSessionId`. This aligns the two middlewares' truncation semantics for Kusto union queries.

If this refactor would touch code you would rather leave alone, SKIP this task and instead leave a `// TODO(#4042): consolidate with McpTelemetryHelpers.TruncateSessionId` comment above the existing `TruncateId` method. The rest of the feature still ships correctly.

- [ ] **Step 1: Read the current `TruncateId` and its caller**

Run: `grep -n "TruncateId\|Mcp-Session-Id\|SessionId" backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`
Note the caller line(s) and the current sentinel for absent IDs (the design says `"***"`).

- [ ] **Step 2: Check whether existing tests assert the old sentinel**

Run: `grep -rn "\\*\\*\\*" backend/test/Anela.Heblo.Tests/MCP/`
Expected: If ANY test asserts on the `"***"` sentinel from `McpDiagnosticsMiddleware`, note it — those tests will need their expected value updated to `"(missing)"`.

If tests exist and cover diverse behavior, consider aborting this task in favor of the TODO-comment fallback described in the task header. Otherwise proceed.

- [ ] **Step 3: Delete the private `TruncateId` method and update its caller**

Edit `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`:
- Remove the private `TruncateId` method.
- Replace its single call site with `McpTelemetryHelpers.TruncateSessionId(...)`, passing the same input string.
- Add `using Anela.Heblo.API.MCP;` if the caller is in a different namespace (it likely isn't — both files are in the same namespace).

- [ ] **Step 4: Update any tests that pinned the `"***"` sentinel**

If Step 2 found matches, change the expected value in those tests from `"***"` to `"(missing)"`.

- [ ] **Step 5: Build and run all MCP tests**

Run: `cd backend && dotnet build`
Expected: 0 errors.

Run: `cd backend && dotnet test --filter "FullyQualifiedName~MCP"`
Expected: All MCP-namespaced tests pass — `McpBadRequestMiddlewareTests`, `McpDiagnosticsMiddlewareTests` (if present), `McpTelemetryHelpersTests`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs \
        backend/test/Anela.Heblo.Tests/MCP/
git commit -m "refactor(mcp): consolidate session-id truncation into shared helper

Removes the private TruncateId method in McpDiagnosticsMiddleware and
uses McpTelemetryHelpers.TruncateSessionId instead. Absent-session
sentinel is unified to '(missing)' across both middlewares for Kusto
union queries.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: update-mcp-server-docs

**Files:**
- Modify: `docs/integrations/mcp-server.md`

Add a "Diagnostics" section describing the `McpBadRequest` log event, its `customDimensions`, and the canonical Kusto query. This is the arch-review's Amendment 6 — developers should be able to discover the observability signal from the MCP docs.

- [ ] **Step 1: Read the current MCP docs to find the right anchor**

Run: `cat docs/integrations/mcp-server.md | head -80`
Look for a natural insertion point: after the "Endpoints" or "Client config" section, before "Out of scope"/appendix material. Note the heading level used by peers in the file.

- [ ] **Step 2: Append the Diagnostics section**

Insert into `docs/integrations/mcp-server.md` (use `##` if peer sections use `##`, else adjust):

````markdown
## Diagnostics — Bad-request telemetry

`McpBadRequestMiddleware` (see `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`) emits a single structured warning-level log entry to Application Insights whenever `/mcp` returns HTTP 400. The middleware observes both `GET` and `POST` responses.

**Event identity**
- `EventName`: `McpBadRequest` (shared by both channels)
- `EventId`: `5931` (GET) / `5932` (POST)
- Log category: `Anela.Heblo.API.MCP.McpBadRequestMiddleware`
- Log level: `Warning`

**customDimensions**

| Key | Type | Notes |
|---|---|---|
| `HTTPMethod` | string | `GET` or `POST` |
| `Path` | string | Full request path including query string |
| `StatusCode` | int | Always `400` |
| `UserAgent` | string | `"(missing)"` sentinel when absent |
| `Origin` | string | `"(missing)"` sentinel when absent |
| `Accept` | string | `"(missing)"` sentinel when absent |
| `ContentType` | string | `"(missing)"` sentinel when absent |
| `McpSessionIdPresent` | bool | `false` if header absent or empty |
| `McpSessionIdPrefix` | string | First 8 chars of `Mcp-Session-Id`, or `"(missing)"` |
| `RemoteIp` | string | `Connection.RemoteIpAddress`, or `"(unknown)"`. May be the Azure Front Door IP until `ForwardedHeaders` middleware is wired — tracked separately. |
| `ElapsedMs` | double | Request duration |

**Not logged** (PII / secret hygiene): request body content, full `Mcp-Session-Id`, `Authorization`, `Cookie`, `X-Api-Key`.

**Canonical Kusto query**

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

**Scanner/probe segregation query**

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
````

- [ ] **Step 3: Commit**

```bash
git add docs/integrations/mcp-server.md
git commit -m "docs(mcp): document McpBadRequest diagnostic log and Kusto queries

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---

### task: final-validation

**Files:**
- (Validation only — no code edits.)

Run every required validation gate per `CLAUDE.md` and confirm the whole feature is green before hand-off.

- [ ] **Step 1: Backend build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Backend format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: Exit 0. If it reports diffs, run `dotnet format` (no `--verify-no-changes`) and commit the reformatting as a separate `chore: dotnet format` commit.

- [ ] **Step 3: All MCP-namespaced tests**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~MCP"`
Expected: All tests pass. Every test added by this feature (P1–P12 equivalents plus the widened GET assertion and the helper tests) is included in the run.

- [ ] **Step 4: Full backend test suite**

Run: `cd backend && dotnet test`
Expected: All tests pass. No regression outside MCP.

- [ ] **Step 5: Frontend gates (sanity — no FE change was expected, but CLAUDE.md requires the check when anything shipped)**

Run: `cd frontend && npm run build`
Expected: Build succeeds.

Run: `cd frontend && npm run lint`
Expected: 0 errors.

- [ ] **Step 6: Manual audit — files touched vs. spec's "No changes to" list**

Run: `git diff --stat origin/main...HEAD`
Expected file set (modulo the optional Decision 4 consolidation):
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
- `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs` (new)
- `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs` (optional, only if the consolidate task ran)
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`
- `backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs` (new)
- `docs/integrations/mcp-server.md`

Confirm no diff to: `Program.cs`, `ApplicationBuilderExtensions.cs`, `McpModule.cs`, any `appsettings*.json`, any feature-flag config, or any frontend file.

- [ ] **Step 7: PR-description checklist reminder**

Before the PR is opened by the pipeline, make sure the PR description body will include:
- The canonical Kusto query (from `docs/integrations/mcp-server.md`).
- A note that `McpBadRequestMiddleware` now covers both GET and POST for the `McpBadRequest` event.
- A note that `RemoteIp` may reflect Azure Front Door until `ForwardedHeaders` is wired (follow-up ticket).

No commit for this task — it is verification only.
