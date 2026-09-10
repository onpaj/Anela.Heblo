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
