# Implementation: register-adapter-in-di

## What was implemented
Added `IEntraAccessUserSource` → `EntraAccessUserSourceAdapter` DI registration to `UserManagementModule`, alongside its required `using` directive for `Anela.Heblo.Application.Features.Authorization.Contracts`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — added `using Anela.Heblo.Application.Features.Authorization.Contracts;` (alphabetically positioned) and `services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>();` after the existing `IArticleUserResolver` registration.

## Tests
None required for this task.

## How to verify
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

## Notes
Build completed with 0 errors (139 pre-existing warnings, none introduced by this change).

## Status
DONE
