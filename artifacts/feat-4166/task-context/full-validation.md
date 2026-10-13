### task: full-validation

**Files:** None created or modified — this task runs the project's full validation suite against everything the previous three tasks changed.

- [ ] **Step 1: Full backend build**

Run: `dotnet build`
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 2: Full backend test suite**

Run: `dotnet test`
Expected: All tests pass, including the new `PackingMaterialMapperTests` (5 tests) and every pre-existing test under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` (`PackingMaterialCrudHandlerTests`, `GetPackingMaterialsListHandlerTests`, `PackingMaterialsControllerNotFoundTests`, `PackingMaterialLogPersistenceTests`, `PackingMaterialsListQueryCountTests`, `PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests`, `PackingMaterialRepositoryConsumptionHistoryTests`, `PackingMaterialRepositoryRecentLogsTests`). No test count should be lower than before this feature's changes; exactly 5 more than baseline (the new mapper tests).

- [ ] **Step 3: Format check**

Run: `dotnet format --verify-no-changes`
Expected: No formatting violations. If violations are reported: run `dotnet format` (not `--verify-no-changes`) once to auto-fix, review the diff to confirm it touches only files this feature changed, then re-run `dotnet format --verify-no-changes` to confirm clean. Commit any resulting fix separately in Step 5 below (per repository convention: prefer a new commit over amending a previous one).

- [ ] **Step 4: Confirm no `new PackingMaterialDto` object initializers remain outside the mapper**

Run: `grep -rn "new PackingMaterialDto" backend/src/`
Expected: Exactly one match, inside `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`. Zero matches in any of the four handler files. If any handler still contains `new PackingMaterialDto`, that handler's refactor step was missed or reverted — go back and fix it before proceeding.

- [ ] **Step 5: Final commit (only if Step 3 produced a format fix; otherwise skip — nothing to commit)**

```bash
git add -A
git commit -m "chore(packing-materials): apply dotnet format"
```
