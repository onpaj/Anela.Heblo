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
