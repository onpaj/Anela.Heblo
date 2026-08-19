### task: full-suite-and-coverage-verification

**Files:**
- None created or modified — verification only.

- [ ] **Step 1: Run the full new test class**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 2: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet test`
Expected: all tests pass (no new failures introduced by the added file; the file is additive-only and does not touch shared fixtures).

- [ ] **Step 3: Run `dotnet format` and `dotnet build` per repository validation requirements**

Run: `cd backend && dotnet format && dotnet build`
Expected: `dotnet format` reports no changes needed (or auto-fixes whitespace/using-order in the new file only); `dotnet build` succeeds with 0 errors.

- [ ] **Step 4: Confirm coverage improvement**

If the project's coverage tooling is run locally (e.g. `dotnet test /p:CollectCoverage=true` or the CI coverage script referenced by the original issue), confirm `DeleteManufactureDifficultyHandler.cs` line coverage now exceeds the 60% CI filter threshold (all three branches — not-found, happy path with sequencing, both exception cases — are now exercised, which covers effectively 100% of the handler's lines).

- [ ] **Step 5: Final commit (if step 3 produced formatting changes)**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): apply dotnet format to DeleteManufactureDifficultyHandlerTests" || true
```

---

## Self-Review

**1. Spec coverage:** FR-1 → `not-found-path-test`. FR-2 → `happy-path-cache-refresh-test` (including the ordering requirement via `MockSequence`, and the exact-`ProductCode` requirement via `Verify(existing.ProductCode, ...)`). FR-3 case A and case B → `exception-path-tests`. NFR-1/NFR-2 are N/A per spec, no task needed. All FRs have a corresponding task.

**2. Placeholder scan:** No "TBD"/"implement later"/"add appropriate error handling" phrases present. Every step shows complete, runnable code or an exact command with expected output.

**3. Type consistency:** `DeleteManufactureDifficultyRequest.Id` (int), `ManufactureDifficultySetting.ProductCode` (string), `DeleteManufactureDifficultyResponse.Success`/`Message` are used identically across all four test methods and match the production types read directly from `DeleteManufactureDifficultyHandler.cs`, `DeleteManufactureDifficultyRequest.cs`, `DeleteManufactureDifficultyResponse.cs`, and `IManufactureDifficultyRepository.cs` during architecture review. No naming drift between tasks.
