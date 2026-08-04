# Development: Remove dead MockCatalogRepository from Persistence assembly

## What was implemented

Deleted `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` — a 436-line, unregistered `ICatalogRepository` implementation returning hardcoded/fabricated Czech catalog data. It had zero DI registration and zero callers anywhere in the repo, confirmed by repo-wide grep both before and after the change.

This matches plan-01.md / design-01.md / architecture-01.md exactly: a pure single-file deletion, no relocation, no new test double (a correct one, `TestCatalogRepository`, already exists in the test project per the architecture review). No other files were touched.

## Files changed

- `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` — deleted (`git rm`).

`git status` confirms this is the only change:
```
deleted:    backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs
```

## Verification performed

1. **Reference check** — `grep -rln 'MockCatalogRepository' . --include='*.cs'` returns no results after deletion (only the file's own definition matched before).
2. **Build** — `dotnet build Anela.Heblo.sln`: 0 Errors, 250 pre-existing warnings (unrelated to this change), succeeded.
3. **Tests** — `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Catalog"`: 807 passed, 2 failed, 809 total.
   - The 2 failures (`InventorySummaryTileBaseTests.LoadDataAsync_ItemAt365Days_CountsAsMedium`, `CatalogMergeSchedulerTests.ScheduleMerge_BurstOfCalls_CollapseToSingleCallback`) are pre-existing timing-sensitive flaky tests unrelated to this change — both use `Mock<ICatalogRepository>` (Moq), not the deleted `MockCatalogRepository` class. One test's own code comment acknowledges wall-clock drift sensitivity; the other asserts on a 150ms debounce timer under machine load. Re-ran both in isolation and both passed (`Failed: 0, Passed: 2`), confirming flakiness rather than a regression from this deletion.
4. **Format** — `dotnet format backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --verify-no-changes`: exit 0, no formatting issues.

## How to verify

```bash
grep -rln 'MockCatalogRepository' . --include='*.cs'   # expect: no output
dotnet build Anela.Heblo.sln                            # expect: 0 Errors
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Catalog"
```
