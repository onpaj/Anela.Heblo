# Specification: Relocate GraphService exception types out of UserManagement/Contracts

## Summary
`GraphServiceAuthException` and `GraphServiceException` currently live in `Features/UserManagement/Contracts/`, a folder documented as the home for shared DTOs, not exception types. This spec covers moving both files to the documented exceptions location, updating their namespace, and fixing all `using` statements that reference them. No behavioral or logic changes are involved.

## Background
`docs/architecture/filesystem.md` documents `Infrastructure/Exceptions/` as the location for feature-level exception types (see the `Infrastructure/` block: `├── Exceptions/`), while `Contracts/` is documented as holding shared DTOs (`{Entity}Dto.cs`, `[Other shared DTOs]`). The two Graph exception types are plain `Exception` subclasses wrapping SDK-specific failures from `IGraphService`, not DTOs, and their presence in `Contracts/` dilutes that folder's meaning and creates a discoverability gap for anyone looking for exception types in the documented `Infrastructure/Exceptions/` location.

The UserManagement feature currently has no `Infrastructure/Exceptions/` subfolder; `Infrastructure/` currently holds `EntraAccessUserSourceAdapter.cs` and `GraphArticleUserResolver.cs` directly.

## Functional Requirements

### FR-1: Move exception files to the documented location
Move both exception files from:
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs`

to a new folder:
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs`

This location matches the `Infrastructure/Exceptions/` pattern documented in `docs/architecture/filesystem.md`, rather than introducing a new sibling `Exceptions/` folder at the feature root (which is not the documented pattern for this repo).

**Acceptance criteria:**
- Both files exist at the new path and no longer exist at the old `Contracts/` path.
- File contents (class bodies, XML doc comments, constructors) are otherwise unchanged.

### FR-2: Update namespaces
Change the namespace declaration in both moved files from:
```
namespace Anela.Heblo.Application.Features.UserManagement.Contracts;
```
to:
```
namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
```

**Acceptance criteria:**
- Both moved files declare the new namespace.
- No other code in the two files changes.

### FR-3: Fix all references
Update every file that references these two exception types via a `using Anela.Heblo.Application.Features.UserManagement.Contracts;` import (where that import exists solely, or additionally, for these exception types) so the build succeeds under the new namespace. **Note:** the originating issue listed only four referencing files; a full-repository grep for `GraphServiceAuthException`/`GraphServiceException` performed during specification turned up four more (the actual `IGraphService` implementation and three test files). The complete, verified list is:

Production code:
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` (XML doc `<exception cref>` comments; also has an existing `using ... Contracts;` for `UserDto`)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` (catch blocks)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` (catch blocks)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` (catch blocks)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — the concrete `IGraphService` implementation; it `throw`s both exception types (not previously listed in the issue)

Test code (not previously listed in the issue):
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs`

Documentation/comments referencing the old location by name (not a compile dependency, but should be corrected for accuracy):
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — three comments (an allowlist header comment and an assertion failure message) explicitly state that `GraphServiceAuthException`/`GraphServiceException` are "defined in UserManagement.Contracts"; this becomes false after the move and should be updated to say `UserManagement.Infrastructure.Exceptions`. This test's actual enforcement logic is namespace-prefix-based (`Anela.Heblo.Application`) and reflection-driven, not a hardcoded `Contracts` string, so the test itself does not need logic changes — only its comments/message text.

All eight production/test files reference `UserDto` (or another `Contracts` DTO) by name **except** `GraphArticleUserResolver.cs`, which references `Contracts` solely for these two exception types.

**Acceptance criteria:**
- Every file that references `GraphServiceAuthException` or `GraphServiceException` (all eight files above) has a `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;` statement (or fully-qualifies the type).
- Files that also use DTOs from `Features.UserManagement.Contracts` (all eight files above except `GraphArticleUserResolver.cs`) retain that using statement unchanged.
- `GraphArticleUserResolver.cs`'s `using Features.UserManagement.Contracts;` is replaced (removed, since nothing else in that file needs it).
- The three stale "defined in UserManagement.Contracts" comments/messages in `ModuleBoundariesTests.cs` are updated to reference `UserManagement.Infrastructure.Exceptions`.
- `dotnet build` succeeds with no new warnings or errors introduced by this change.
- `dotnet test` passes for all UserManagement-related tests and `ModuleBoundariesTests`.

### FR-4: No behavioral change
This is a pure move/rename. No exception-handling logic, constructor signatures, class semantics, or call sites' catch/throw behavior change.

**Acceptance criteria:**
- `git diff` for `GetGroupMembersHandler.cs`, `EntraAccessUserSourceAdapter.cs`, `GraphArticleUserResolver.cs`, and `IGraphService.cs` shows only `using` statement changes (additions/removals), not logic changes.
- All existing UserManagement unit/integration tests pass unchanged.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a compile-time file/namespace reorganization with no runtime behavior change.

### NFR-2: Security
Not applicable — no security-sensitive code is touched; exception wrapping behavior around Graph SDK auth/service failures is unchanged.

## Data Model
Not applicable — no data model changes.

## API / Interface Design
Not applicable — no public API surface changes. The two exception types remain `public sealed class` with unchanged constructor signatures (`(string message, Exception innerException)`); only their namespace changes. Since these are internal Application-layer exception types (not part of any generated OpenAPI/DTO contract), this does not affect the DTO-generation rule in `docs/architecture/development_guidelines.md` (that rule concerns DTOs specifically, not internal exception types).

## Dependencies
None. This change is self-contained within `Anela.Heblo.Application/Features/UserManagement/`.

## Out of Scope
- Renaming or changing the exception classes themselves (message format, constructor overloads, base type).
- Any other `Contracts/` folder cleanup outside UserManagement.
- Moving `EntraAccessUserSourceAdapter.cs` or `GraphArticleUserResolver.cs` themselves — only the exception types move.

## Open Questions
None.

## Status: COMPLETE
