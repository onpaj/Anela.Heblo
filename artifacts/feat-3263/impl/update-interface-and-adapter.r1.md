# Implementation: update-interface-and-adapter

## What was implemented
1. Added XML `<exception>` doc comments to `IArticleUserResolver.ResolveByGroupAsync` documenting `ArticleUserResolverAuthException`, `ArticleUserResolverServiceException`, and `UnauthorizedAccessException`.
2. Updated `GraphArticleUserResolver.ResolveByGroupAsync` to catch `MsalException` (from `Microsoft.Identity.Client`) and `Microsoft.Graph.Models.ODataErrors.ODataError`, wrapping them in the new domain exceptions with original exception as `InnerException`. `UnauthorizedAccessException` and generic `Exception` propagate unchanged.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs` — added XML doc to `ResolveByGroupAsync`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — added try/catch with exception translation; added `using Microsoft.Identity.Client;`

## Tests
No new tests in this task — the adapter translation is covered by the handler tests in task fix-handler-and-tests.

## How to verify
```
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
```
Build completes with 0 errors, 142 warnings (pre-existing).

## Notes
The `using Microsoft.Identity.Client;` was added to `GraphArticleUserResolver.cs` to reference `MsalException`. This is appropriate since this file is in the `UserManagement.Infrastructure` namespace, where SDK dependencies are permitted.

## Status
DONE
