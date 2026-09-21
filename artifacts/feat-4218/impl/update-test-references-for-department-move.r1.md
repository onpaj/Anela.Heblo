# Implementation: update-test-references-for-department-move

## What was implemented

Updated `FlexiDepartmentQueryServiceTests.cs` to reference the relocated
`IDepartmentClient`/`Department` types under
`Anela.Heblo.Domain.Features.UserManagement` (moved there by the prior task,
`relocate-department-domain-types-and-update-production-consumers`), fixing
the CS0246 build break this move left in the Flexi test project. Confirmed
`DepartmentSyncServiceTests.cs` needs no change, since it exercises a
different, FlexiBee-SDK-owned `IDepartmentClient`/`Department` pair.

## Files created/modified

- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — changed line 2's `using` from `Anela.Heblo.Domain.Features.Analytics` to `Anela.Heblo.Domain.Features.UserManagement`. No other lines touched.

## Tests

- `FlexiDepartmentQueryServiceTests` (6 tests, all pass) — exercises the
  relocated domain `IDepartmentClient`/`Department` via the fixed `using`.
- `DepartmentSyncServiceTests` — verified unaffected (uses
  `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient`
  and `Persistence.Analytics.Entities.Department`, a distinct type pair);
  left untouched per spec FR-6.

## How to verify

```bash
dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~FlexiDepartmentQueryServiceTests|FullyQualifiedName~DepartmentSyncServiceTests"
```

Before the fix: `dotnet build` fails with `CS0246: The type or namespace
name 'IDepartmentClient' could not be found` in
`FlexiDepartmentQueryServiceTests.cs` (confirmed as step 1, red state).

After the fix: build succeeds (0 errors), and the filtered test run reports
`Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Notes

A full unfiltered `dotnet test` on the Flexi test project reports 72
pre-existing failures, all under the `Integration` namespace (e.g.
`FlexiCatalogSalesClientIntegrationTests`,
`FlexiManufactureClientIntegrationTests`,
`LedgerSyncIntegrationTests.SyncAsync_WithRealDatabase_*`). These require a
live FlexiBee connection / real database and fail with
`ArgumentNullException` inside `AddFlexiBee` DI setup — none reference
`Department`, `IDepartmentClient`, or the `Analytics`/`UserManagement`
namespaces touched by this task or the prior one, and they are unrelated
to this change. Flagging for visibility; not in scope to fix here.

## PR Summary
Fixed the Flexi adapter test project build break left by the prior task's
`IDepartmentClient`/`Department` relocation, by pointing
`FlexiDepartmentQueryServiceTests.cs`'s `using` at the new
`Domain.Features.UserManagement` namespace. Confirmed the look-alike
`DepartmentSyncServiceTests.cs` (FlexiBee-SDK's own, unrelated
`IDepartmentClient`) needed no change.

### Changes
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — one-line `using` namespace fix

## Status
DONE
