# Implementation: update-handler

## What was implemented
Updated `GetEntraAccessUsersHandler` to inject `IEntraAccessUserSource` instead of `IGraphService`. Removed the cross-module using directive (`UserManagement.Services`) and the domain reference (`Domain.Features.Authorization`). The call site was updated from `_graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct)` to `_source.GetBaseMembersAsync(ct)`. The response mapping (Id, Email, DisplayName) and ordering by DisplayName are unchanged.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs` — replaced IGraphService dependency with IEntraAccessUserSource, removed cross-module usings, updated field name and call site

## Tests
N/A for this task (test update is a separate task).

## How to verify
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

## Notes
Build succeeded with 0 errors (139 pre-existing nullable warnings, none introduced by this change).

## Status
DONE
