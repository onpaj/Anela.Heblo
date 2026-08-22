### task: final-validation

**Files:** none (validation-only; no further edits expected)

Runs the full project-standard validation gate from `CLAUDE.md` — `dotnet build` and `dotnet format` — plus the complete `Anela.Heblo.Adapters.Shoptet.Tests` project's test suite (not just the new class), to confirm the new file compiles cleanly, is correctly formatted, and does not regress any existing test in the project (including `Integration/ShoptetApiInvoiceSourceIntegrationTests.cs`, which stays inert/skipped without `Shoptet:ApiToken` configured, and every other `Unit/`/`Expedition/` test in the project).

- [ ] **Step 1: Build the solution**

Run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 2: Run dotnet format and check for changes**

Run:
```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```
Expected: exits with code 0 and no output listing changed files, meaning `ShoptetApiInvoiceSourceTests.cs` (as written in the previous tasks) is already compliant with the repo's formatting rules.

If it instead reports files needing formatting, run:
```bash
dotnet format Anela.Heblo.sln
```
then re-run `git diff` to inspect what changed. If the only file changed is `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`, proceed to Step 3; if `dotnet format` touches any other file, revert those unrelated changes with `git checkout -- <path>` before continuing (this task's scope is limited to the new test file — no other file should be touched).

- [ ] **Step 3: Run the full test project to confirm no regressions**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: overall run reports `Failed: 0`; the six `ShoptetApiInvoiceSourceTests` executions (FR-1 through FR-5, with FR-4 contributing two `InlineData` cases) are all `Passed`, and every pre-existing test in the project (including any integration tests that skip/no-op without `Shoptet:ApiToken` configured) is unaffected.

- [ ] **Step 4: Commit (only if Step 2 produced formatting changes)**

If `dotnet format` in Step 2 modified `ShoptetApiInvoiceSourceTests.cs`, commit that formatting fix:
```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "style: apply dotnet format to ShoptetApiInvoiceSourceTests"
```
If Step 2 reported no changes needed, skip this commit — there is nothing new to commit in this task.
