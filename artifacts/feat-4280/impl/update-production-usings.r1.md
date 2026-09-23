# Implementation: update-production-usings

## What was implemented

Added the `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;`
directive to the five production files that reference `GraphServiceAuthException` or
`GraphServiceException` (via doc comments, catch clauses, or throw sites), following the
relocation of those exception types out of the `UserManagement.Contracts` namespace in the
prior task (`relocate-exception-files`). Each file's existing `Contracts` using was kept
except in `GraphArticleUserResolver.cs`, where it was replaced outright since that file has
no other reason to import `Contracts`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — added the new using; `Contracts` retained (needed for `UserDto`)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` — added the new using; `Contracts` retained
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` — added the new using; `Contracts` retained
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — replaced the `Contracts` using with the new one (no other symbol from `Contracts` was referenced)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — added the new using; `Contracts` retained

## Tests

None required for this task — it is a pure using-directive fix for production code. Test project usings are addressed by the next task (`update-test-usings-and-comments`).

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet build src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj
```

Both build with 0 errors (pre-existing, unrelated nullable-reference warnings only). The test
project (`Anela.Heblo.Application.Tests` or equivalent) is still expected to fail to build
until the next task fixes its usings — that is expected per the task spec.

## Notes

No deviations from the task-context steps. All 5 edits matched the exact before/after
snippets given in the task context.

## PR Summary
Fixed production-code `using` directives across five files in the UserManagement feature so
they resolve `GraphServiceAuthException` and `GraphServiceException` against their new home
(`UserManagement.Infrastructure.Exceptions`) after the previous task relocated those
exception types out of `UserManagement.Contracts`. No behavioral changes — using-directive
adjustments only.

### Changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

## Status
DONE
