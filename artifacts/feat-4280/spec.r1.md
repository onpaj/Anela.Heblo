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
Update every file that references these two exception types via a `using Anela.Heblo.Application.Features.UserManagement.Contracts;` import (where that import exists solely, or additionally, for these exception types) so the build succeeds under the new namespace. Based on the current codebase, the following files reference the exception types and need review:
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` (references in XML doc comments; also has an existing `using ... Contracts;` for its DTOs)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` (catch blocks)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` (catch blocks)
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` (catch blocks)

Several of these files (`IGraphService.cs`, `EntraAccessUserSourceAdapter.cs`, `GraphArticleUserResolver.cs`, and the `GetGroupMembers` response/handler files) also use the existing `Contracts` namespace for actual DTOs (e.g. `GetGroupMembersResponse`, `GetDepartmentsResponse`). Where a file references both DTOs from `Contracts` and the relocated exception types, add a new `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;` alongside the existing `Contracts` using — do not remove the `Contracts` using where DTOs from that namespace are still referenced.

**Acceptance criteria:**
- Every file that references `GraphServiceAuthException` or `GraphServiceException` has a `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;` statement (or fully-qualifies the type).
- Files that also use DTOs from `Features.UserManagement.Contracts` retain that using statement unchanged.
- `dotnet build` succeeds with no new warnings or errors introduced by this change.

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
