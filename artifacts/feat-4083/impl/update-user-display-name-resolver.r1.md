# Implementation: update-user-display-name-resolver

## What was implemented
`UserDisplayNameResolver` was repointed from the Authorization module's
`IAuthorizationRepository` (via `AppUser`) to the Shared/Users-owned
`IUserDirectorySource` contract (via `UserDirectoryEntry`), removing the
cross-module dependency that previously reached directly into
`Anela.Heblo.Domain.Features.Authorization`. Behavior is unchanged: identifiers
(Entra object id or email) are resolved to a display name (falling back to
email when the display name is blank), results are cached for 5 minutes in
`IMemoryCache`, and an empty input short-circuits without querying the
directory source.

Prerequisites were verified present before starting:
- `IUserDirectorySource` / `UserDirectoryEntry` — `backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs`
- The Authorization-side adapter — `backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs` (named `AuthorizationUserDirectorySourceAdapter`, not `AuthorizationUserDirectoryAdapter` as the task text guessed — same responsibility)
- DI registration — `services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();` in `AuthorizationModule.cs`

`UserDirectoryEntry` is a sealed class with init-settable properties
(`EntraObjectId`, `Email`, `DisplayName`), matching the task's assumed shape
exactly, so the test file was used as specified without adaptation.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs` — constructor now takes `IUserDirectorySource` instead of `IAuthorizationRepository`; lookup building now iterates `UserDirectoryEntry` instead of `AppUser`.
- `backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs` — mocks `IUserDirectorySource` and builds `UserDirectoryEntry` fixtures instead of `IAuthorizationRepository`/`AppUser`.

No other files needed changes: `IUserDisplayNameResolver`'s public interface is
unchanged, DI registration in `ApplicationModule.cs` already registers the
concrete `UserDisplayNameResolver` via constructor injection (no direct
`new UserDisplayNameResolver(...)` call sites exist anywhere in the codebase),
and no other module constructs this type directly.

## Tests
`backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs` — 6 tests, all passing:
- `ResolveAsync_MapsEntraObjectIdToDisplayName`
- `ResolveAsync_MapsEmailIdentifierToDisplayName`
- `ResolveAsync_UnknownIdentifier_ResolvesToNull`
- `ResolveAsync_FallsBackToEmail_WhenDisplayNameMissing`
- `ResolveAsync_EmptyInput_DoesNotQueryRepository`
- `ResolveAsync_CachesLookup_AcrossCalls`

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserDisplayNameResolverTests"
# => Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6

cd ..
dotnet build Anela.Heblo.sln
# => 0 Error(s) (94 pre-existing warnings, unrelated to this change)

cd backend
dotnet format Anela.Heblo.sln --no-restore \
  --include src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs \
            test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs
# => clean, no diagnostics
```

## Notes
- Confirmed via `grep -rn "new UserDisplayNameResolver("` and a search for all
  `UserDisplayNameResolver` references that the only construction site is the
  DI registration in `ApplicationModule.cs` (`AddScoped<IUserDisplayNameResolver, UserDisplayNameResolver>()`),
  which resolves constructor parameters by type and needed no change.
- `artifacts/feat-4083/state.json` had a pre-existing uncommitted modification
  (pipeline orchestration bookkeeping, task status/timestamps) present before
  this task started. It was left out of this commit as unrelated to the code
  change, per the surgical-changes rule.
- No deviations from the task's prescribed file contents — both files were
  written exactly as specified in the task context.

## PR Summary
This change repoints `UserDisplayNameResolver` at the module-boundary-respecting
`IUserDirectorySource` contract (Shared/Users) instead of reaching directly into
`IAuthorizationRepository` (Authorization's persistence abstraction). This
removes a direct cross-module dependency: Shared/Users no longer needs to know
about `AppUser` or Authorization's repository shape, and instead depends only
on the small, provider-agnostic `UserDirectoryEntry` projection, with
`AuthorizationUserDirectorySourceAdapter` (already registered in DI from a
prior task) bridging the two. Behavior is unchanged — same caching strategy
(5-minute `IMemoryCache` TTL), same identifier normalization (case-insensitive,
matches by both Entra object id and email), and the same fallback from a blank
display name to email.

### Changes
- `backend/src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs` — swap `IAuthorizationRepository`/`AppUser` for `IUserDirectorySource`/`UserDirectoryEntry`.
- `backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs` — mock `IUserDirectorySource` and build `UserDirectoryEntry` fixtures instead of `IAuthorizationRepository`/`AppUser`.

## Status
DONE
