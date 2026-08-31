### task: full-suite-verification

**Files:**
- None (verification only)

- [ ] **Step 1: Run the whole new test class together**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 4` (all four `[Fact]`s from the tasks above)

- [ ] **Step 2: Run `dotnet format` and the full backend build, per repo validation requirements**

Run:
```bash
cd backend
dotnet format --verify-no-changes || dotnet format
dotnet build
```
Expected: `Build succeeded.` with 0 errors; `dotnet format` reports no remaining changes needed (or applies them cleanly).

- [ ] **Step 3: Run the full backend test suite to confirm no regressions elsewhere**

Run: `cd backend && dotnet test`
Expected: all suites pass, including the four new `GetIssuedInvoiceSyncStatsHandlerTests` facts and the pre-existing `GetIssuedInvoiceDetailHandlerTests` / `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` suites (unaffected, confirming no accidental production-code drift).
