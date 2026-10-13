## Module
UserManagement

## Finding
`IDepartmentClient` and its return type `Department` are defined in `Anela.Heblo.Domain.Features.Analytics` (`backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs`, `Department.cs`) but have **zero Analytics consumers**:

- The Analytics sync service (`DepartmentSyncService`, `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs:15`) injects `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient` (the FlexiBee SDK client directly) and calls `_departmentClient.GetAsync()` — it does **not** use `Domain.Features.Analytics.IDepartmentClient`.
- The only consumer of the domain interface is `FlexiDepartmentQueryService` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs:9–10`), which implements `UserManagement`'s `IDepartmentQueryService`. It maps `Domain.Features.Analytics.Department` → `Application.Features.UserManagement.Contracts.DepartmentDto`.

So a domain type is placed in the Analytics module but exclusively serves the UserManagement feature's adapter chain.

## Why it matters
The analytics domain namespace implies Analytics module ownership, but the type has no Analytics users. Any developer maintaining the UserManagement → departments data flow must look in the Analytics domain to find the interface, violating the "feature cohesion" principle and making the module boundary misleading. It also means the Analytics domain carries a dependency surface it does not use.

## Suggested fix
Move `IDepartmentClient` and `Department` to `Anela.Heblo.Domain.Features.UserManagement/` (creating the folder if it does not exist), update `FlexiDepartmentClient`'s `using` and `implements` declarations, and adjust the DI registration namespace reference in `FlexiAdapterServiceCollectionExtensions`. The `DepartmentSyncService` is unaffected since it references the FlexiBee SDK client, not the domain interface.

---
_Filed by daily arch-review routine on 2026-09-17._
