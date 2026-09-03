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
