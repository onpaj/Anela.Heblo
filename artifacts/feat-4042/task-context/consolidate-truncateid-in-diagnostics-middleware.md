### task: consolidate-truncateid-in-diagnostics-middleware

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`

Optional but recommended (arch-review Decision 4, option C). Replace the private `TruncateId` helper in `McpDiagnosticsMiddleware` with a call to the new `McpTelemetryHelpers.TruncateSessionId`. This aligns the two middlewares' truncation semantics for Kusto union queries.

If this refactor would touch code you would rather leave alone, SKIP this task and instead leave a `// TODO(#4042): consolidate with McpTelemetryHelpers.TruncateSessionId` comment above the existing `TruncateId` method. The rest of the feature still ships correctly.

- [ ] **Step 1: Read the current `TruncateId` and its caller**

Run: `grep -n "TruncateId\|Mcp-Session-Id\|SessionId" backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`
Note the caller line(s) and the current sentinel for absent IDs (the design says `"***"`).

- [ ] **Step 2: Check whether existing tests assert the old sentinel**

Run: `grep -rn "\\*\\*\\*" backend/test/Anela.Heblo.Tests/MCP/`
Expected: If ANY test asserts on the `"***"` sentinel from `McpDiagnosticsMiddleware`, note it — those tests will need their expected value updated to `"(missing)"`.

If tests exist and cover diverse behavior, consider aborting this task in favor of the TODO-comment fallback described in the task header. Otherwise proceed.

- [ ] **Step 3: Delete the private `TruncateId` method and update its caller**

Edit `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs`:
- Remove the private `TruncateId` method.
- Replace its single call site with `McpTelemetryHelpers.TruncateSessionId(...)`, passing the same input string.
- Add `using Anela.Heblo.API.MCP;` if the caller is in a different namespace (it likely isn't — both files are in the same namespace).

- [ ] **Step 4: Update any tests that pinned the `"***"` sentinel**

If Step 2 found matches, change the expected value in those tests from `"***"` to `"(missing)"`.

- [ ] **Step 5: Build and run all MCP tests**

Run: `cd backend && dotnet build`
Expected: 0 errors.

Run: `cd backend && dotnet test --filter "FullyQualifiedName~MCP"`
Expected: All MCP-namespaced tests pass — `McpBadRequestMiddlewareTests`, `McpDiagnosticsMiddlewareTests` (if present), `McpTelemetryHelpersTests`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs \
        backend/test/Anela.Heblo.Tests/MCP/
git commit -m "refactor(mcp): consolidate session-id truncation into shared helper

Removes the private TruncateId method in McpDiagnosticsMiddleware and
uses McpTelemetryHelpers.TruncateSessionId instead. Absent-session
sentinel is unified to '(missing)' across both middlewares for Kusto
union queries.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---
