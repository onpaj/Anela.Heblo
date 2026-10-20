# Implementation: relocate-exception-files

## What was implemented
Moved `GraphServiceAuthException.cs` and `GraphServiceException.cs` from
`Features/UserManagement/Contracts/` to a new
`Features/UserManagement/Infrastructure/Exceptions/` folder using `git mv`
(preserving file history), and updated their namespace from
`Anela.Heblo.Application.Features.UserManagement.Contracts` to
`Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions`.
The `<see cref="IGraphService"/>` doc comments in both files were fully
qualified (`Anela.Heblo.Application.Features.UserManagement.Services.IGraphService`)
since `IGraphService` now lives in a sibling namespace.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs` — moved from `Contracts/`, namespace updated
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs` — moved from `Contracts/`, namespace updated

## Tests
None written for this task — pure file relocation, no behavior change. Test/production `using` updates are handled by the next tasks (`update-production-usings`, `update-test-usings-and-comments`).

## How to verify
```bash
git log --follow --oneline backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs
```
shows the rename history is preserved. `git status` shows nothing left under `Contracts/` for these two files.

## Notes
As documented in the task context, the build now fails with `CS0246: The type or namespace name 'GraphServiceAuthException'/'GraphServiceException' could not be found` in the four consumer files (`GetGroupMembersHandler.cs`, `GraphArticleUserResolver.cs`, `EntraAccessUserSourceAdapter.cs`, plus any test files). Confirmed via `dotnet build` — this is expected and will be fixed by the next task (`update-production-usings`).

## PR Summary
Moved `GraphServiceAuthException` and `GraphServiceException` out of `UserManagement/Contracts/` (reserved for DTOs) into a new `UserManagement/Infrastructure/Exceptions/` folder, matching the documented convention for exception types in a complex feature. No logic changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs` — moved from `Contracts/`, namespace updated
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs` — moved from `Contracts/`, namespace updated

## Status
DONE
