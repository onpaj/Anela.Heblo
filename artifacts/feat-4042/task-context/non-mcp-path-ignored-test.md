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
