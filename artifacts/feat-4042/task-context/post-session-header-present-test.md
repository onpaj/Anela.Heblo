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
