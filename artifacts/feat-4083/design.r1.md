# Design: Decouple UserDisplayNameResolver from the Authorization Module

## Component Design

### `IUserDirectorySource` (new, consumer-owned)
- **Location**: `Anela.Heblo.Application.Shared.Users.Contracts`
- **Responsibility**: The single read-only operation `Shared/Users` needs from any user directory
  provider — "give me every user, as a minimal projection." Owns nothing about how the directory is
  stored or queried.
- **Members**: `Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)`.
- **Consumers**: `UserDisplayNameResolver` (the only consumer today).

### `UserDirectoryEntry` (new, consumer-owned)
- **Location**: same namespace as `IUserDirectorySource`.
- **Responsibility**: An immutable projection of a user directory record — exactly the three fields
  `UserDisplayNameResolver` reads today (`EntraObjectId`, `Email`, `DisplayName`). A plain class with
  `init`-only properties (never a `record`, per repo convention).
- **Non-responsibility**: Carries no group membership, permission, packing-eligibility, or audit
  fields from `AppUser` — those are Authorization-internal and out of scope for this contract.

### `AuthorizationUserDirectorySourceAdapter` (new, provider-owned)
- **Location**: `Anela.Heblo.Application.Features.Authorization.Infrastructure`, `internal sealed`.
- **Responsibility**: Implements `IUserDirectorySource` by calling the existing
  `IAuthorizationRepository.GetAllUsersAsync` and mapping each returned `AppUser` to a
  `UserDirectoryEntry`, field for field, with no filtering or transformation.
- **Boundary role**: This is the one place in the codebase permitted to know both that
  `IUserDirectorySource` exists and that `AppUser`/`IAuthorizationRepository` exist — everything on
  the `Shared/Users` side stays ignorant of Authorization's internals.

### `UserDisplayNameResolver` (changed)
- **Location**: unchanged, `Anela.Heblo.Application.Shared.Users`.
- **Responsibility**: unchanged — caches an identifier→display-name lookup for 5 minutes and answers
  `ResolveAsync` queries against it.
- **Change**: its single dependency moves from `IAuthorizationRepository` to `IUserDirectorySource`;
  every other member, the cache key, the TTL, and the empty-display-name→email fallback are untouched.
- **Public contract** (`IUserDisplayNameResolver.ResolveAsync`): unchanged — this is invisible to the
  four consumer handlers (KnowledgeBase, Article, Leaflet, Smartsupp feedback-list handlers).

### `AuthorizationModule` (changed)
- **Responsibility**: adds one DI registration —
  `services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();` — alongside
  its existing Authorization-module registrations. No other change to this module's composition root.

### `ModuleBoundariesTests` (changed)
- **Responsibility**: adds one `ModuleBoundaryRule("Shared.Users -> Authorization", ...)` entry with
  an empty allowlist, inspecting `Anela.Heblo.Application.Shared.Users` for any reference to
  `Anela.Heblo.Domain.Features.Authorization`, `Anela.Heblo.Application.Features.Authorization`, or
  `Anela.Heblo.Persistence.Features.Authorization`. This is the regression guard that makes the
  decoupling permanent rather than a one-time cleanup.

## Data Schemas

No database schema changes (no migration). One new in-process contract shape:

```csharp
public sealed class UserDirectoryEntry
{
    public string? EntraObjectId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
}
```

| Field | Type | Nullable | Source (from `AppUser`) |
|---|---|---|---|
| `EntraObjectId` | `string?` | yes (local/packing-only users have none) | `AppUser.EntraObjectId` |
| `Email` | `string?` | mapped as nullable for symmetry with the interface contract, though `AppUser.Email` is non-null today | `AppUser.Email` |
| `DisplayName` | `string?` | mapped as nullable for symmetry; `AppUser.DisplayName` is non-null today, blank-string fallback handled unchanged in `UserDisplayNameResolver` | `AppUser.DisplayName` |

No request/response DTO, no API endpoint, no OpenAPI contract, and no event payload is introduced or
changed by this feature — it is entirely internal to the `Anela.Heblo.Application` assembly.
