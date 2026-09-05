### task: read-existing-middleware-and-tests

**Files:**
- Read: `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
- Read: `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`
- Read: `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`
- Read: `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`

This is a read-only orientation step. No code changes. The goal is to confirm the exact current shape of the middleware, the existing GET 400 log call, the `TruncateId` helper on `McpDiagnosticsMiddleware`, and the current test scaffold (`CreateContext`, `VerifyNoLogCalled`) before you touch anything.

- [ ] **Step 1: Read `McpBadRequestMiddleware.cs`**

Run: `cat backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
Confirm:
- The class is registered as a normal middleware (`InvokeAsync(HttpContext context)` or `Invoke`).
- There is an existing predicate `IsMcpGetRequest` (or similar).
- There is a GET short-circuit path for headerless probes that returns 404.
- The existing GET 400 log call site is a single method that reads headers and emits one warning log entry.

- [ ] **Step 2: Read `McpDiagnosticsMiddleware.cs`**

Run: `cat backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`
Confirm:
- A private static `TruncateId` helper exists around the line referenced by the arch-review (line 54).
- Note the current sentinel it uses for absent session IDs (design says it currently substitutes `"***"`).
- Note who its single caller is (you may need to update it in a later task).

- [ ] **Step 3: Read middleware registration**

Run: `cat backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs | sed -n '110,140p'`
Confirm:
- `McpBadRequestMiddleware` is registered before `McpDiagnosticsMiddleware` and before `MapMcp("/mcp")`.
- No new registration is required by this feature.

- [ ] **Step 4: Read existing tests**

Run: `cat backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`
Confirm:
- Helpers `CreateContext(method, path, ...)` and `VerifyNoLogCalled` exist and accept a `method` parameter.
- The test class uses `Mock<ILogger<McpBadRequestMiddleware>>` (or the same `ILogger<T>` pattern) so the new POST tests can reuse the exact same verification style.
- Note the exact `It.Is<...>` / `Verify(...)` incantation used to assert one warning was logged with specific state properties — new tests must match this style.

- [ ] **Step 5: Record findings in a scratch note (no commit)**

Write to `/tmp/claude-0/-home-user-worktrees-feature-4042-Post-Mcp-35-8-Bad-Request-Rate-59-165-In-P7d-No-Di/18eb13b0-74ef-5955-b000-efd0ad1992ba/scratchpad/mcp-notes.md`:
- Exact name of the current GET log helper method (if any).
- Exact name of the current predicate for GET requests.
- Exact `Verify(...)` shape used by existing tests.
- Whether the existing test's `CreateContext` supports setting a response status (or if we need to introduce a fake `RequestDelegate` that sets `context.Response.StatusCode`).

This is a working note only; it will not be committed.

- [ ] **Step 6: Confirm baseline build and tests pass**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
Expected: All tests pass (baseline).

No commit — this is orientation only.

---
