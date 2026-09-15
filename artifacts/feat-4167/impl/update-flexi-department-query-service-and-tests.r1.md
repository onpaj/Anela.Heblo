# Implementation: update-flexi-department-query-service-and-tests

## What was implemented
Updated `FlexiDepartmentQueryService` and its unit test to consume `IDepartmentClient`/`Department` from their new home in `Anela.Heblo.Domain.Features.Analytics` instead of the old `Anela.Heblo.Domain.Features.InvoiceClassification` namespace. Only the `using` directive changed in each file — no logic, mapping, or assertions were touched.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` — swapped `using Anela.Heblo.Domain.Features.InvoiceClassification;` for `using Anela.Heblo.Domain.Features.Analytics;`
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — same `using` swap

## Tests
No new tests were added/removed. Existing tests (`GetDepartmentsAsync_MapsDomainDepartmentsToDtos`, `GetDepartmentsAsync_WhenClientReturnsNoDepartments_ReturnsEmptyList`) are unchanged and now resolve `IDepartmentClient`/`Department` from the new namespace.

## How to verify
```bash
grep -n "InvoiceClassification" \
  backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs \
  backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
# expect: no output
```
Confirmed empty.

## Notes
Building the full solution (or just the `Anela.Heblo.Adapters.Flexi` project) still fails with 2 pre-existing errors in `FlexiAdapterServiceCollectionExtensions.cs` (`IDepartmentClient` unresolved / `FlexiDepartmentClient` not assignable), because that file's `using` directive and DI registration have not been updated yet. This is expected and out of scope here — it is exactly the work item for the next task, `update-di-registration-and-verify`. Verified via `git stash`/`git stash pop` that these are pre-existing failures unrelated to this task's two files: before this change, the build failed with 2 different errors located inside `FlexiDepartmentQueryService.cs` itself (unresolved `IDepartmentClient`); after this change, those are fixed and the only remaining failures are the already-known DI registration ones.

## PR Summary
Pointed `FlexiDepartmentQueryService` and its unit test at the relocated `IDepartmentClient`/`Department` types in `Domain.Features.Analytics`, replacing the stale `Domain.Features.InvoiceClassification` reference. Pure `using` directive change, no behavior change.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` — `using` swapped to `Anela.Heblo.Domain.Features.Analytics`
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — `using` swapped to `Anela.Heblo.Domain.Features.Analytics`

## Status
DONE
