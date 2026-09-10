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
