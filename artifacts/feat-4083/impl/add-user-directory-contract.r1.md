# Implementation: add-user-directory-contract

## What was implemented
Added a new consumer-owned abstraction, `IUserDirectorySource`, in the `Application/Shared/Users` slice. This is a pure contract (interface + a provider-agnostic DTO) with no references to the Authorization module. It will be implemented by the Authorization module via an adapter in a later task, letting `UserDisplayNameResolver` depend on this interface instead of directly on Authorization's repository/entities.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs` — defines `IUserDirectorySource` (single method `GetAllAsync(CancellationToken)` returning `IReadOnlyList<UserDirectoryEntry>`) and the sealed class `UserDirectoryEntry` (nullable `EntraObjectId`, `Email`, `DisplayName` properties). Matches the task spec verbatim, including XML doc comments.

## Tests
None — interface only, no behavior to test. No other files were touched (no consumers wired up yet; that's a later task).

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — build succeeds with 0 errors (139 pre-existing warnings in unrelated files, none introduced by this change).
2. Inspect `backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs` to confirm it matches the spec and has no `using` referencing another module's namespace.
3. `git show --stat HEAD` confirms exactly one file was added.

## Notes
- No deviations from the spec — the file content is copied verbatim from the task description.
- `UserDirectoryEntry` is a plain class with `init`-only properties (not a DTO crossing the API boundary), so the project's "DTOs are classes, never records" rule is satisfied trivially either way; used a class per the exact spec text.
- This task intentionally does not wire `IUserDirectorySource` into `UserDisplayNameResolver` or add an Authorization adapter — those are separate tasks in the plan.
- `artifacts/feat-4083/state.json` had a pre-existing uncommitted modification in the worktree before this task started; it was left untouched and unstaged, per instructions to commit only the source file.

## PR Summary

Adds a new, consumer-owned `IUserDirectorySource` contract (with a minimal `UserDirectoryEntry` projection) under `Application/Shared/Users/Contracts`, giving the Users slice an abstraction over "list all directory users" that does not reference the Authorization module. This is the first step in decoupling `UserDisplayNameResolver` from directly consuming Authorization's repository/entities; a later task will add an Authorization-side adapter implementing this interface and rewire the resolver to depend on it instead.

### Changes
- `backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs` — new contract interface `IUserDirectorySource` and DTO `UserDirectoryEntry`

## Status
DONE
