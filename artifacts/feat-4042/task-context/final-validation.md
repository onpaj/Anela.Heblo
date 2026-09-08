### task: final-validation

**Files:**
- (Validation only — no code edits.)

Run every required validation gate per `CLAUDE.md` and confirm the whole feature is green before hand-off.

- [ ] **Step 1: Backend build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Backend format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: Exit 0. If it reports diffs, run `dotnet format` (no `--verify-no-changes`) and commit the reformatting as a separate `chore: dotnet format` commit.

- [ ] **Step 3: All MCP-namespaced tests**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~MCP"`
Expected: All tests pass. Every test added by this feature (P1–P12 equivalents plus the widened GET assertion and the helper tests) is included in the run.

- [ ] **Step 4: Full backend test suite**

Run: `cd backend && dotnet test`
Expected: All tests pass. No regression outside MCP.

- [ ] **Step 5: Frontend gates (sanity — no FE change was expected, but CLAUDE.md requires the check when anything shipped)**

Run: `cd frontend && npm run build`
Expected: Build succeeds.

Run: `cd frontend && npm run lint`
Expected: 0 errors.

- [ ] **Step 6: Manual audit — files touched vs. spec's "No changes to" list**

Run: `git diff --stat origin/main...HEAD`
Expected file set (modulo the optional Decision 4 consolidation):
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`
- `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs` (new)
- `backend/src/Anela.Heblo.API/MCP/McpDiagnosticsMiddleware.cs` (optional, only if the consolidate task ran)
- `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs`
- `backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs` (new)
- `docs/integrations/mcp-server.md`

Confirm no diff to: `Program.cs`, `ApplicationBuilderExtensions.cs`, `McpModule.cs`, any `appsettings*.json`, any feature-flag config, or any frontend file.

- [ ] **Step 7: PR-description checklist reminder**

Before the PR is opened by the pipeline, make sure the PR description body will include:
- The canonical Kusto query (from `docs/integrations/mcp-server.md`).
- A note that `McpBadRequestMiddleware` now covers both GET and POST for the `McpBadRequest` event.
- A note that `RemoteIp` may reflect Azure Front Door until `ForwardedHeaders` is wired (follow-up ticket).

No commit for this task — it is verification only.
