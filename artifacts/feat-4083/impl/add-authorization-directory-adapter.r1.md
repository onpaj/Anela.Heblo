# Implementation: add-authorization-directory-adapter

## What was implemented
Added `AuthorizationUserDirectorySourceAdapter`, an internal adapter class in the Authorization module's `Infrastructure/` folder that implements the consumer-owned `IUserDirectorySource` contract (added in the prior `add-user-directory-contract` task) by delegating to `IAuthorizationRepository.GetAllUsersAsync`. Registered the adapter's DI binding in `AuthorizationModule.AddAuthorizationModule`. This follows the "consumer owns contract, provider implements adapter" cross-module communication pattern documented in `docs/architecture/development_guidelines.md` (the `ILeafletKnowledgeSource` example), mirroring it for `IUserDirectorySource`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs` — new internal sealed class implementing `IUserDirectorySource.GetAllAsync`, mapping each `AppUser` returned by `IAuthorizationRepository.GetAllUsersAsync` to a `UserDirectoryEntry` (EntraObjectId, Email, DisplayName). Matches the task spec verbatim.
- `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs` — added `using` statements for `Anela.Heblo.Application.Features.Authorization.Infrastructure` and `Anela.Heblo.Application.Shared.Users.Contracts`, and one new line `services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();` immediately after the existing `IAuthorizationRepository` registration. No other existing lines changed.

## Tests
None — no test task was specified for this step (mechanical adapter + DI wiring; behavior is exercised indirectly once the resolver is rewired to consume the contract in the next task).

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — build succeeded with 0 errors (139 pre-existing warnings in unrelated files, none introduced by this change).
2. Inspect `AuthorizationUserDirectorySourceAdapter.cs` — confirms it implements `IUserDirectorySource` and only references `IAuthorizationRepository` (its own module's repository) plus the consumer's contract namespace.
3. Inspect `AuthorizationModule.cs` — confirms the new registration line is present immediately after `IAuthorizationRepository`'s, and no other existing lines were altered.
4. `git show --stat HEAD` confirms exactly the two expected files changed (one created, one modified).

## Notes
- No deviations from the task spec — file content and DI registration line copied verbatim from the task description.
- `AuthorizationUserDirectorySourceAdapter` is `internal sealed` per the spec's code snippet; it is only ever consumed via the `IUserDirectorySource` DI registration, never referenced by name outside the Authorization module.
- This task intentionally does not touch `UserDisplayNameResolver` — rewiring the resolver to depend on `IUserDirectorySource` instead of `IAuthorizationRepository` directly is the next task (`update-user-display-name-resolver`) in the plan.

## PR Summary

Implements the Authorization-side half of the `IUserDirectorySource` contract introduced in the previous task: a new `AuthorizationUserDirectorySourceAdapter` in `Features/Authorization/Infrastructure/` adapts `IAuthorizationRepository.GetAllUsersAsync` to the consumer-owned interface, and `AuthorizationModule` registers it via DI (`services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>()`). This follows the codebase's established cross-module communication pattern (consumer owns the contract, provider implements an adapter — see the `ILeafletKnowledgeSource`/`KnowledgeBaseLeafletSourceAdapter` example in `development_guidelines.md`). `UserDisplayNameResolver` is not yet rewired to use this — that's the next task in the plan.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs` — new adapter implementing `IUserDirectorySource` via `IAuthorizationRepository`
- `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs` — registers the new adapter's DI binding

## Status
DONE
