### task: final-verification

**Files:** none (verification only, no code changes expected)

- [ ] **Step 1: Confirm no remaining references to the old namespace**

Run:
```bash
grep -rn "UserManagement.Contracts.GraphServiceAuthException\|UserManagement.Contracts.GraphServiceException" backend/ || echo "clean"
```
Expected: `clean` (no fully-qualified references to the old namespace remain). This also implicitly checks that no file still relies on the old `Contracts` location for these two types.

- [ ] **Step 2: Confirm the full set of 8 referencing files still resolves correctly**

Run:
```bash
grep -rl "GraphServiceAuthException\|GraphServiceException" backend/ | sort
```
Expected output: exactly these 9 paths (the 2 moved files themselves plus the 7 consumers, i.e. 8 consumers from the inventory above, since `ModuleBoundariesTests.cs` matches on text, not a `using`, but is expected to still appear here since the class names remain in its comment/message text):
```
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
```
If any other path appears, investigate before proceeding — it means a reference was missed by this plan's inventory.

- [ ] **Step 3: Full build and format check**

Run: `cd backend && dotnet build`
Expected: 0 errors, 0 new warnings (in particular, no unused-`using` warnings — this catches any file where a `Contracts` using should have been removed but wasn't, e.g. `GraphArticleUserResolver.cs`).

Run: `dotnet format --verify-no-changes`
Expected: no formatting diffs. If it reports diffs, run `dotnet format` (no `--verify-no-changes`) and re-commit.

- [ ] **Step 4: Full test suite**

Run: `cd backend && dotnet test`
Expected: all tests pass, no failures or skips introduced by this change.

- [ ] **Step 5: Final commit (only if `dotnet format` made changes in Step 3)**

```bash
git add -A
git commit -m "style(usermanagement): dotnet format after Graph exception relocation"
```
If `dotnet format --verify-no-changes` passed cleanly in Step 3, skip this step — there is nothing to commit.
