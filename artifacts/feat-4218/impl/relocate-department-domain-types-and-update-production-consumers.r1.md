# Implementation: relocate-department-domain-types-and-update-production-consumers

## What was implemented

Moved `Department` and `IDepartmentClient` from `Anela.Heblo.Domain.Features.Analytics` to
`Anela.Heblo.Domain.Features.UserManagement`, and updated the three production consumers that
referenced the old namespace.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs` — new file, identical
  shape to the original, namespace changed to `Anela.Heblo.Domain.Features.UserManagement`
- `backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs` — new file,
  identical shape to the original, namespace changed
- `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` — deleted (`git rm`; git
  recorded this as a rename to the new `UserManagement` file)
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs` — deleted (`git rm`;
  recorded as a rename)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs`
  — `using Anela.Heblo.Domain.Features.Analytics;` → `using Anela.Heblo.Domain.Features.UserManagement;`
  and the base interface reference `Domain.Features.Analytics.IDepartmentClient` →
  `Domain.Features.UserManagement.IDepartmentClient`. The FlexiBee SDK `IDepartmentClient` alias
  (line 3) was left untouched — it disambiguates an unrelated type.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`
  — same `using` swap; constructor and `Department` → `DepartmentDto` mapping untouched.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`
  — same `using` swap for the DI registration's `using` line. Verified no other type from
  `Domain.Features.Analytics` was referenced in this file before removing that `using`. The
  `Anela.Heblo.Adapters.Flexi.Analytics` (DepartmentSyncService, unrelated FlexiBee-SDK
  `IDepartmentClient`) and `Anela.Heblo.Persistence.Analytics` (`AddAnalyticsPersistenceServices`)
  `using` lines were left exactly as they were — unrelated types.

## Tests

No tests required by this task's context file (test-reference updates are a separate task:
`update-test-references-for-department-move`).

## How to verify

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj   # Build succeeded (confirmed)
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj   # Build succeeded, 0 errors (confirmed)
```

Followed the task context's prescribed red/green sequence: Domain project built green in
isolation after the move (no internal consumers), the Flexi adapter project then failed red
with CS0246/CS0234 pointing at exactly the three files named in the task context, and after the
three `using`/base-type edits it rebuilt green with 0 errors.

## Notes

`Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient` (the FlexiBee SDK type,
aliased locally as `IDepartmentClient` in `FlexiDepartmentClient.cs`, and used directly by
`DepartmentSyncService.cs`) is a distinct, unrelated type from the domain `IDepartmentClient`
that moved here — confirmed by inspection, left untouched, exactly as the task context specified.
`Anela.Heblo.Persistence.Analytics.Entities.Department` (a separate persistence entity) is also
untouched. No deviations from the task context's exact steps.

## PR Summary
Moved the orphaned `IDepartmentClient`/`Department` domain types out of
`Domain.Features.Analytics` (where they had no real consumers) into
`Domain.Features.UserManagement`, whose department-lookup flow (`FlexiDepartmentQueryService`)
is their only real consumer. Updated the three production files that referenced the old
namespace; left the unrelated FlexiBee-SDK `IDepartmentClient` and the persistence-layer
`Department` entity untouched.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs` — new (moved)
- `backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs` — new (moved)
- `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` — removed
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs` — removed
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs` — updated `using`/base type
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` — updated `using`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — updated `using`

## Status
DONE
