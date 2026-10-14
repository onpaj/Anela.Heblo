# Implementation: update-test-usings-and-comments

## What was implemented
Updated three test files under `backend/test/Anela.Heblo.Tests/Features/UserManagement/`
to add the new `Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions`
using directive (needed after the Graph exception types moved out of `Contracts` in
earlier tasks of this feature), keeping the existing `Contracts` using where it is still
needed for other types (e.g. `UserDto`). Also fixed two stale text references (a comment
and an assertion message) in `ModuleBoundariesTests.cs` that still said the wrapper
exceptions live in `UserManagement.Contracts`, updating them to
`UserManagement.Infrastructure.Exceptions`. No other lines in `ModuleBoundariesTests.cs`
were touched — the `SdkExceptionAllowlist` entries and the reflection/enumeration logic
are unrelated and were left exactly as-is.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs` — added `Infrastructure.Exceptions` using, kept `Contracts`.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — added `Infrastructure.Exceptions` using, kept `Contracts`.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs` — added `Infrastructure.Exceptions` using, kept `Contracts`.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — fixed 2 stale text references (line ~908 header comment, line ~977 assertion message) from `UserManagement.Contracts` to `UserManagement.Infrastructure.Exceptions`. No logic changes.

## Tests
No new tests were written — this task only fixes compile-time usings and stale doc/message
text so the existing test suite (already covering the relocated exception types from prior
tasks) compiles and passes again.

## How to verify
```bash
cd backend && dotnet build ../Anela.Heblo.sln   # or: dotnet build <repo-root>/Anela.Heblo.sln
dotnet test <repo-root>/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UserManagement|FullyQualifiedName~ModuleBoundariesTests"
```
Result: full solution builds with 0 errors (248 pre-existing nullable-reference warnings,
unrelated to this change). Filtered test run: 96 passed, 0 failed, 0 skipped — including
`GetGroupMembersHandlerTests`, `GraphServiceTests`, `EntraAccessUserSourceAdapterTests`,
and `ModuleBoundariesTests.Application_types_should_not_catch_SDK_exception_types_directly`.

## Notes
The task context's suggested build command (`cd backend && dotnet build`) doesn't resolve
a project/solution on its own — the `.sln` lives at the repo root
(`Anela.Heblo.sln`), not under `backend/`. Built via `dotnet build Anela.Heblo.sln` from
the repo root instead; this is a command-path detail only, not a code change.

## PR Summary
Fixed the test project's usings and two stale doc/message strings so the suite compiles
and passes cleanly after the Graph service exceptions (`GraphServiceException`,
`GraphServiceAuthException`) moved from `UserManagement.Contracts` to
`UserManagement.Infrastructure.Exceptions` in earlier tasks of this feature.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs`
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

## Status
DONE
