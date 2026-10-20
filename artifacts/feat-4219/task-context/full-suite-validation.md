### task: full-suite-validation

**Files:** none created/modified — validation only.

- [ ] **Step 1: Confirm `GetAppRoleMembersAsync`'s body shrank as intended**

Run:
```bash
awk '/public async Task<List<UserDto>> GetAppRoleMembersAsync/,/^    }$/' backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs | wc -l
```
Expected: roughly 60–90 lines (down from the ~190 lines / 6 steps the issue reported), now consisting of guard clauses, the try/catch scaffolding, the 5-step orchestration (4 helper calls + the un-extracted group-expansion loop), and caching/logging. This is a sanity check, not a hard gate — if it's a little outside this range that's fine as long as no single method (orchestrator or any of the 4 new helpers) is doing more than one of the six original concerns.

- [ ] **Step 2: Confirm each new helper is `private`, and `IGraphService` is untouched**

Run:
```bash
grep -n "ResolveServicePrincipalAsync\|FindAppRoleId\|CollectRoleAssigneesAsync\|BatchResolveUserDtosAsync" backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
```
Expected: each of the 4 names appears exactly twice (its `private` declaration + its one call site inside `GetAppRoleMembersAsync`).

```bash
git diff --stat main...HEAD -- backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
```
Expected: no output (file untouched).

- [ ] **Step 3: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the same pre-existing warning count as on the base branch — this refactor must not introduce new warnings).

- [ ] **Step 4: Format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: no formatting violations. If violations are reported, run `dotnet format backend/Anela.Heblo.sln`, confirm the diff is whitespace-only, then re-run Step 3.

- [ ] **Step 5: Full UserManagement-area test run**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.UserManagement"
```
Expected: PASS — includes `GetAppRoleMembersTests` (3), `GraphServiceTests` (9, incl. the 6 `GetAppRoleMembersAsync_*` and 3 unrelated `GetGroupMembersAsync_*`/constructor tests), `GetGroupMembersHandlerTests`, `GetGroupMembersValidationPipelineTests`, `MockGraphServiceTests`, `ParseMembersFromJsonTests`, `EntraAccessUserSourceAdapterTests` — all unmodified, all green.

- [ ] **Step 6: Full test project run (regression check for anything elsewhere referencing `GraphService`)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: PASS, same total pass count as on the base branch (no test added, none removed, none broken).

- [ ] **Step 7: Confirm no unrelated files changed**

Run:
```bash
git diff --stat main...HEAD
```
Expected: only `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` plus this plan's own `artifacts/feat-4219/` files. No test file changes, no `IGraphService.cs` change, no `MockGraphService.cs` change, no frontend/OpenAPI-client regeneration (no public contract changed), no `docs/integrations/mcp-server.md` change (this method has no MCP tool wrapper).

- [ ] **Step 8: Final commit (only if Step 4's format run produced changes not already committed)**

```bash
git status
git add -A
git commit -m "chore(usermanagement): dotnet format after GetAppRoleMembersAsync extraction" --allow-empty
```

---

## Self-Review Notes (recorded after writing this plan)

**Spec coverage map** (spec requirement → task):
- FR-1 (extract `ResolveServicePrincipalAsync`) → task `extract-resolve-service-principal`
- FR-2 (extract `FindAppRoleId`/`FindAppRoleIdAsync`) → task `extract-find-app-role-id`
- FR-3 (extract `CollectRoleAssigneesAsync`) → task `extract-collect-role-assignees`
- FR-4 (keep group expansion inline, un-extracted) → preserved as-is across all four extraction tasks (never touched)
- FR-5 (extract `BatchResolveUserDtosAsync`) → task `extract-batch-resolve-user-dtos`
- FR-6 (orchestrator becomes a short sequence; existing tests pass unmodified) → verified incrementally at the end of every task, and exhaustively in `full-suite-validation`
- NFR-1 (no behavior change) → every extraction step moves code verbatim; verified by the unmodified 12-test regression run after each task
- NFR-2 (consistent failure-signaling shape) → nullable/tuple returns with helper-owned logging, applied uniformly across all 4 helpers (arch-review Decision 1)
- NFR-3 (helpers structured for future testability) → each helper takes its dependencies as parameters (`HttpClient`, `graphToken`, ids) rather than reading ambient state beyond `_logger`
- NFR-4 (code style) → fully-qualified `System.Text.Json.*` matching the file's existing style (no new `using System.Text.Json;`); `private async Task<T>` for I/O steps, plain `private static` for the synchronous `FindAppRoleId`

**Arch-review amendments applied:**
1. Regression coverage correction (both `GetAppRoleMembersTests.cs` and `GraphServiceTests.cs`) — the test command used throughout this plan runs both files together.
2. `JsonElement` lifetime fix (`.Clone()` before `ResolveServicePrincipalAsync` returns) — applied in task `extract-resolve-service-principal`, Step 2.
3. Decision 1 (nullable/tuple failure signaling, helper-owned logging) — applied uniformly to all 4 helpers.
4. Decision 2 (`FindAppRoleId` synchronous) — applied in task `extract-find-app-role-id`.
5. Decision 4 (group-expansion loop stays inline) — never extracted in any task; the loop's `// Step 4: expand group members (reuse existing method)` comment and body are untouched throughout.

**Placeholder scan:** No "TBD"/"TODO"/"similar to Task N" placeholders — every step shows the complete before/after code verbatim, since this plan's entire content is a mechanical move of already-existing, already-read source code (not new logic being designed).

**Type consistency check:** `ResolveServicePrincipalAsync` returns `(string? SpId, JsonElement? AppRoles)`; `FindAppRoleId` takes `JsonElement? appRoles` — matches. `CollectRoleAssigneesAsync` returns `(HashSet<string>? DirectUserIds, List<string>? GroupIdsToExpand)`; the orchestrator's null-check (`directUserIds is null || groupIdsToExpand is null`) matches both being populated together. `BatchResolveUserDtosAsync` takes `IReadOnlyCollection<string> userIds`; the orchestrator passes `directUserIds` (a `HashSet<string>`, which implements `IReadOnlyCollection<string>`) — matches.
