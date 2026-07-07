## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/DirectManufactureCostProviderTests.cs:27,76,103,125,145,163,182` — all seven `[Fact]` methods are declared `internal async Task ...`, whereas every sibling test file in `Features/Catalog` (e.g. `UpdateProductCompositionOrderHandlerTests.cs`, `GetCatalogListHandlerDiacriticsTests.cs`) uses `public async Task ...`. I confirmed via `dotnet test --filter FullyQualifiedName~DirectManufactureCostProviderTests` that xUnit still discovers and runs all 7 tests correctly under this project's test-runner configuration (7 passed, 0 failed), so this is not a functional defect — just an inconsistency with the established convention worth aligning for readability/maintenance.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/DirectManufactureCostProviderTests.cs:213-225` — the `VerifyLogged` mock-logger-assertion helper duplicates a pattern that already exists (in slightly different forms) in sibling files such as `CatalogMergeSchedulerTests.cs` and `SalesCostProviderTests.cs`. Consider factoring a shared logger-verification helper into a common test-utilities class to avoid re-implementing the same `Mock<ILogger<T>>.Verify(...)` boilerplate per test file (not blocking; matches existing repo convention of per-file helpers).

### Notes
- Verified the test file's logic against `DirectManufactureCostProvider.cs`: the `RefreshAsync` concurrency test correctly relies on the deterministic synchronous-until-first-suspension behavior of `SemaphoreSlim.WaitAsync(0, ct)` (no sleeps/timing), matches FR-1/NFR-1; the `try/finally` around the gate release and `await firstRefresh` correctly prevents the static `RefreshLock` from leaking into other tests, matching NFR-3.
- `GetCostsAsync` unhydrated-cache and `FilterByProductCodes` tests (FR-2/FR-3) match the actual source logic (`CostCacheData.IsHydrated` gate, `FilterByProductCodes` null/empty passthrough vs. subset filtering) with correct mock setups against `ICatalogRepository`/`IDirectManufactureCostCache`.
- Diff is test-only, as required by the spec's "Out of Scope" section (no changes to `DirectManufactureCostProvider.cs`).
- Full test run: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.
