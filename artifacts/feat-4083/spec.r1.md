# Specification: Decouple UserDisplayNameResolver from the Authorization Module

## Summary
`UserDisplayNameResolver` (in the cross-cutting `Application/Shared/Users` layer, consumed by four
feature modules — KnowledgeBase, Article, Leaflet, Smartsupp) currently depends directly on the
Authorization module's internal repository contract (`IAuthorizationRepository`) and domain entity
(`AppUser`). This inverts the documented cross-module ownership rule, where the *consumer* must own
the contract and the *provider* implements an adapter. This spec defines a minimal, consumer-owned
`IUserDirectorySource` contract and an Authorization-owned adapter that implements it, with no
change to `UserDisplayNameResolver`'s external behavior, caching semantics, or public API.

## Background
`Application/Shared/Users/UserDisplayNameResolver.cs` (lines 18–20) injects `IAuthorizationRepository`
(defined in `Domain/Features/Authorization/`) and consumes `AppUser` entities returned by
`GetAllUsersAsync()`. `UserDisplayNameResolverTests.cs` further imports
`Anela.Heblo.Domain.Features.Authorization.Entities` to construct test fixtures, confirming the
compile-time coupling.

This is the only cross-module dependency of this kind not already covered by
`ModuleBoundariesTests` (see `docs/architecture/development_guidelines.md`, "Cross-Module
Communication Example: `ILeafletKnowledgeSource`", and the already-enforced Leaflet↔KnowledgeBase,
Article↔KnowledgeBase, and Smartsupp↔KnowledgeBase rules in `ModuleBoundariesTests.cs`). Any change
to `IAuthorizationRepository`'s shape (added/removed methods, renamed return types) or to `AppUser`
today breaks compilation of the shared `Shared/Users` layer, and by extension all four consumer
modules — a hidden, untested coupling.

## Functional Requirements

### FR-1: Consumer-owned directory contract
Define `IUserDirectorySource` inside `Application/Shared/Users/` (in a `Contracts/` subfolder, per
the documented pattern), exposing exactly one read-only operation that returns the minimal projection
`UserDisplayNameResolver` needs — no speculative members.

```csharp
namespace Anela.Heblo.Application.Shared.Users.Contracts;

public interface IUserDirectorySource
{
    Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);
}

public sealed class UserDirectoryEntry
{
    public string? EntraObjectId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
}
```

**Acceptance criteria:**
- `IUserDirectorySource` and `UserDirectoryEntry` live under `Anela.Heblo.Application.Shared.Users.Contracts` and reference no Authorization-owned type.
- `UserDirectoryEntry` is a class (project convention: DTOs/contract types are classes, never C# records — see `docs/architecture/development_guidelines.md` and `CLAUDE.md`).
- The interface exposes only `GetAllAsync` — no other Authorization repository members are surfaced.

### FR-2: Authorization-owned adapter
Add an adapter in `Application/Features/Authorization/Infrastructure/` that implements
`IUserDirectorySource` by delegating to `IAuthorizationRepository.GetAllUsersAsync` and mapping
`AppUser` → `UserDirectoryEntry`.

```csharp
namespace Anela.Heblo.Application.Features.Authorization.Infrastructure;

internal sealed class AuthorizationUserDirectorySourceAdapter : IUserDirectorySource
{
    private readonly IAuthorizationRepository _repository;

    public AuthorizationUserDirectorySourceAdapter(IAuthorizationRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _repository.GetAllUsersAsync(cancellationToken);
        return users
            .Select(u => new UserDirectoryEntry
            {
                EntraObjectId = u.EntraObjectId,
                Email = u.Email,
                DisplayName = u.DisplayName,
            })
            .ToList();
    }
}
```

**Acceptance criteria:**
- The adapter is `internal sealed`, matching `KnowledgeBaseLeafletSourceAdapter`'s visibility convention.
- The adapter is the *only* type allowed to reference both `Shared.Users.Contracts` and Authorization-owned namespaces (mirrors the `ModuleBoundariesTests` allowlist convention used for `EntraAccessUserSourceAdapter`).
- Mapping preserves `EntraObjectId`, `Email`, `DisplayName` verbatim (no transformation).

### FR-3: DI registration in the provider module
`AuthorizationModule.AddAuthorizationModule` registers
`services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();`. The consumer
(`ApplicationModule.cs`, `Shared/Users`) never registers or references the adapter or
`IAuthorizationRepository`.

**Acceptance criteria:**
- The binding is added inside `AuthorizationModule.cs`, not `ApplicationModule.cs` or `PersistenceModule.cs` (ADR-004 in `development_guidelines.md`: repository/adapter bindings live in the owning module).
- `ApplicationModule.cs`'s existing `services.AddScoped<IUserDisplayNameResolver, UserDisplayNameResolver>();` line is unchanged.

### FR-4: Update UserDisplayNameResolver to depend on the new contract
`UserDisplayNameResolver` injects `IUserDirectorySource` instead of `IAuthorizationRepository`, calls
`GetAllAsync` instead of `GetAllUsersAsync`, and reads `EntraObjectId` / `Email` / `DisplayName` off
`UserDirectoryEntry` instead of `AppUser`. No other logic changes: the identifier→display-name lookup
construction, the `MemoryCache` key/TTL, the empty-input short-circuit, and the
email-fallback-when-DisplayName-blank behavior are all preserved exactly.

**Acceptance criteria:**
- `UserDisplayNameResolver.cs` no longer references `Anela.Heblo.Domain.Features.Authorization` (directly or transitively via `AppUser`/`IAuthorizationRepository`).
- All existing behavior in `UserDisplayNameResolverTests.cs` (Entra-object-id mapping, email mapping, unknown-identifier→null, display-name-blank→email fallback, empty-input short-circuit, single-call caching across repeated `ResolveAsync` calls) is preserved.

### FR-5: Update existing unit tests
`UserDisplayNameResolverTests.cs` is updated to mock `IUserDirectorySource` instead of
`IAuthorizationRepository`, and to construct `UserDirectoryEntry` instead of `AppUser`. No new test
cases are required — this FR is a mechanical adaptation of the five existing tests to the new seam.

**Acceptance criteria:**
- The test file no longer imports `Anela.Heblo.Domain.Features.Authorization.Entities` or
  `Anela.Heblo.Domain.Features.Authorization`.
- All five existing test methods pass unmodified in intent (same inputs/expected outputs), only the
  mocked dependency and fixture type change.

### FR-6: Enforce the boundary going forward
Add a new `ModuleBoundaryRule` to `ModuleBoundariesTests.cs` — e.g. `"Shared.Users -> Authorization"`
— with `InspectedNamespacePrefix: "Anela.Heblo.Application.Shared.Users"` and
`ForbiddenNamespacePrefixes` covering `Anela.Heblo.Domain.Features.Authorization`,
`Anela.Heblo.Application.Features.Authorization`, and `Anela.Heblo.Persistence.Features.Authorization`
(note: unlike KnowledgeBase, Authorization's persistence types live under the `Features.` sub-namespace
— verify against `Anela.Heblo.Persistence.Features.Authorization.AuthorizationRepository` before
finalizing), with an empty allowlist (or, if the adapter must live inside `Shared.Users` rather than
`Features.Authorization` — see Open Questions — an allowlist entry scoped to that one adapter class,
following the existing `AuthorizationUserManagementAllowlist` precedent).

**Acceptance criteria:**
- The new rule is added to `ModuleBoundariesTests.Rules()` following the existing `ModuleBoundaryRule` record shape.
- `dotnet test` on `Anela.Heblo.Tests` passes with the new rule active and zero violations.
- A future reintroduction of a direct Authorization reference inside `Shared/Users/*` (excluding the adapter, if it is allowlisted) fails this test.

## Non-Functional Requirements

### NFR-1: Performance
No behavior or performance change is intended. The existing 5-minute `IMemoryCache` TTL and
single-full-scan-per-cache-miss pattern in `UserDisplayNameResolver` are preserved unchanged; the
adapter adds one in-memory list mapping (`AppUser` → `UserDirectoryEntry`) per cache refresh, which is
O(n) over the user directory and negligible given the directory is described as "small."

### NFR-2: Security
No change. `IUserDirectorySource.GetAllAsync` exposes the same three fields (`EntraObjectId`, `Email`,
`DisplayName`) that `UserDisplayNameResolver` already reads off `AppUser` today — no new fields, no
new access surface, no change to authentication/authorization checks (this cross-cutting resolver has
none today and none are introduced).

### NFR-3: Backward compatibility
`IUserDisplayNameResolver.ResolveAsync` (the public contract consumed by the four feature modules) is
untouched — this is purely an internal dependency-inversion refactor with zero externally visible
API, DTO, or OpenAPI-client change. No frontend changes are required.

## Data Model
No new persisted data. One new in-memory contract type:

- **`UserDirectoryEntry`** (class, `Application/Shared/Users/Contracts/`): `EntraObjectId: string?`, `Email: string?`, `DisplayName: string?`. A projection of `AppUser`, owned by the consumer (`Shared/Users`), not by Authorization.

## API / Interface Design
- **New interface**: `IUserDirectorySource.GetAllAsync(CancellationToken)` → `IReadOnlyList<UserDirectoryEntry>`. Consumer-owned (`Shared/Users/Contracts`).
- **New adapter**: `AuthorizationUserDirectorySourceAdapter : IUserDirectorySource`. Provider-owned (`Features/Authorization/Infrastructure`), delegates to existing `IAuthorizationRepository.GetAllUsersAsync`.
- **Changed constructor**: `UserDisplayNameResolver(IUserDirectorySource directorySource, IMemoryCache cache)` (was `IAuthorizationRepository`).
- No HTTP endpoint, controller, or OpenAPI contract is added, removed, or changed. No frontend/TypeScript client regeneration is triggered by this change.

## Dependencies
- No new external libraries or services.
- No database migration.
- Depends on the existing `IAuthorizationRepository.GetAllUsersAsync` method remaining available and returning `AppUser` — no change requested to that repository in this spec.

## Out of Scope
- Renaming or restructuring `IAuthorizationRepository` itself.
- Introducing pagination, filtering, or any new query shape on the user directory (the existing "get all users" semantics are preserved as-is).
- Any change to the four consumer modules' (KnowledgeBase, Article, Leaflet, Smartsupp) own handlers — they consume `IUserDisplayNameResolver`, which is untouched, and require no code changes.
- Fixing any of the *other*, unrelated pre-existing allowlisted boundary violations already tracked in `ModuleBoundariesTests.cs` (Logistics→Manufacture, Catalog→Purchase, etc.) — out of scope for this issue.
- Performance optimization of the underlying full-table `GetAllUsersAsync` scan — unchanged from today.

## Open Questions
None. One judgment call is recorded as an explicit assumption for the architect to confirm or override:
this spec places `IUserDirectorySource`/`UserDirectoryEntry` in a new `Application/Shared/Users/Contracts/`
subfolder (mirroring `Features/Leaflet/Contracts/`) rather than directly in `Application/Shared/Users/`
as the issue's suggested-fix snippet showed, since `development_guidelines.md`'s documented pattern
requires the consumer-owned contract to live in a `Contracts/` folder. If the architect determines
`Shared/Users` (a cross-cutting layer, not a `Features/*` module) should deviate from that convention,
FR-1's namespace and FR-6's boundary-rule prefix should be adjusted accordingly — this is a pure
naming/location detail with no effect on FR-2 through FR-5.

## Status: COMPLETE
