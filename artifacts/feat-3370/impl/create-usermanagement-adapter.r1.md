# Implementation: create-usermanagement-adapter

## What was implemented
Created `EntraAccessUserSourceAdapter`, an `internal sealed` class implementing `IEntraAccessUserSource`. It injects `IGraphService` and maps `UserDto` results from `GetAppRoleMembersAsync(AccessRoles.Base, ct)` into `EntraAccessUserRecord` values using the `(Id, Email, DisplayName)` constructor.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` — adapter implementing `IEntraAccessUserSource` via `IGraphService`

## Tests
None required for this task.

## How to verify
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

## Notes
Field names (`Id`, `Email`, `DisplayName`) on `UserDto` and the `EntraAccessUserRecord` record constructor parameter order (`Id`, `Email`, `DisplayName`) were verified against source before implementation. No deviations from the task template.

## Status
DONE
