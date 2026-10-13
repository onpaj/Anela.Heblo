### task: remove-old-refresh-service-and-verify

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`

- [ ] **Step 1: Confirm nothing still references `CatalogDataRefreshService`**

Run: `cd backend && grep -rn "CatalogDataRefreshService" --include="*.cs" .`
Expected: only the file `src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` and the test file `test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs` themselves are listed. If any other file appears, stop — the previous task's Step 5 missed a call site; go fix it there before continuing.

- [ ] **Step 2: Delete both files**

```bash
git rm backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs
git rm backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs
```

- [ ] **Step 3: Build the whole solution**

Run: `cd backend && dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors, 0 warnings about unused usings introduced by this change (each new file's using list in the earlier tasks was scoped to only the types it actually uses).

- [ ] **Step 4: Run `dotnet format` and confirm no diff**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 (no formatting violations). If it reports violations, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-run `--verify-no-changes` to confirm, then re-stage the affected files.

- [ ] **Step 5: Run the full backend test suite one final time**

Run: `cd backend && dotnet test Anela.Heblo.sln`
Expected: all tests pass. Count the total `[Fact]`/`[Theory]` cases across `CatalogHistoryRefreshServiceTests.cs` (6), `CatalogStockRefreshServiceTests.cs` (1), and `CatalogReferenceRefreshServiceTests.cs` (3) — 10 total, matching the 10 cases that existed in the now-deleted `CatalogDataRefreshServiceTests.cs`, confirming 1:1 coverage preservation (spec FR-4).

- [ ] **Step 6: Manually verify the constructor parameter counts documented in the spec/design**

Confirm by inspection: `CatalogHistoryRefreshService` has 10 constructor parameters, `CatalogStockRefreshService` has 8, `CatalogMetaRefreshService` has 8, `CatalogReferenceRefreshService` has 5 — all comfortably under the spec's "no more than 10" acceptance criterion (FR-1), versus the original 22.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(catalog): remove CatalogDataRefreshService now that its methods live in four cohesive services"
```
