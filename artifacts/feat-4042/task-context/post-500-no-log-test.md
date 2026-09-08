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
