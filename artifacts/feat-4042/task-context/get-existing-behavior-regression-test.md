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
