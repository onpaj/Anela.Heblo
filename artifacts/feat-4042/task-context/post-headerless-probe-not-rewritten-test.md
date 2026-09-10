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
