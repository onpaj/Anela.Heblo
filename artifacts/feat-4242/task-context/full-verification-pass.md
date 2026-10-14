### task: full-verification-pass

**Title:** Full verification pass

**Files:** none created or modified — this task only runs verification commands across the whole solution, per `CLAUDE.md`'s "Validation before completion" checklist.

- [ ] **Step 1: Full backend build**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Full backend test suite**

Run: `dotnet test Anela.Heblo.sln`
Expected: All tests pass, including the full `Anela.Heblo.Tests` project (not just the Article-filtered subset run in Tasks 1–2), so any incidental breakage elsewhere (e.g. another test asserting on `ArticlesController`'s old action signature) is caught.

- [ ] **Step 3: Format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: No formatting differences. If it reports differences, run `dotnet format Anela.Heblo.sln`, review the diff is limited to the files touched in Tasks 1–2, and re-run `--verify-no-changes` to confirm.

- [ ] **Step 4: Confirm no other callers depend on the old controller action signatures**

Run: `grep -rn "ArticlesController" backend/test --include=*.cs`
Expected: Only `backend/test/Anela.Heblo.Tests/Controllers/ArticlesControllerTests.cs` (already reviewed during planning — it does not call `List` or `FeedbackList` directly, only `Generate`) references the controller by name. If this turns up any other reference exercising `List`/`FeedbackList` via HTTP, read it and confirm it still passes after Step 2 before proceeding (it will, since query parameter names/defaults are unchanged — this step is a final confirmation, not expected to require code changes).

- [ ] **Step 5: Commit (if Step 3 produced formatting changes; otherwise skip — nothing to commit)**

```bash
git add -A
git commit -m "chore(article): apply dotnet format after paging validation changes"
```
