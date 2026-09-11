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
