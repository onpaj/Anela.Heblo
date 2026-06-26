# Implementation: introduce-graph-service-exception-wrappers

## What was implemented

Two sealed application-level exception classes were created for the UserManagement service boundary. `GraphService.GetGroupMembersAsync` was updated to rethrow SDK exceptions as these typed wrappers instead of bare `throw;`, following the same pattern used by `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException`. The `IGraphService` interface received XML `<exception>` doc tags on `GetGroupMembersAsync`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs` — new sealed exception class wrapping MSAL auth failures
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs` — new sealed exception class wrapping Graph OData errors
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — added `<exception>` XML doc tags to `GetGroupMembersAsync` referencing both new types
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — replaced bare `throw;` in `MsalException` and `ODataError` catch blocks with typed rethrows; the `using` for contracts namespace was already present

## How to verify

1. Run `dotnet build Anela.Heblo.sln` from the repo root — should produce 0 errors (253 pre-existing warnings, none introduced by this task).
2. Inspect `GetGroupMembersAsync` in `GraphService.cs`: the `catch (MsalException)` block now throws `GraphServiceAuthException` and the `catch (ODataError)` block throws `GraphServiceException`, each with the original exception as `innerException`.
3. Inspect `IGraphService.cs`: `GetGroupMembersAsync` has two `<exception>` XML doc tags.
4. Verify both exception classes are `sealed`, extend `Exception`, and have a `(string message, Exception innerException)` constructor.

## Notes

- The `using Anela.Heblo.Application.Features.UserManagement.Contracts;` directive was already present at line 1 of `GraphService.cs`, so no additional using was needed.
- The `catch (UnauthorizedAccessException ...)` and `catch (Exception ...)` blocks in `GetGroupMembersAsync` were left untouched per the task spec.
- All 253 warnings visible in the build output are pre-existing; none are introduced by this change.

## Status
DONE
