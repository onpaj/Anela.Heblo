# Architecture Review: Decouple UserDisplayNameResolver from the Authorization Module

## Skip Design: true
Pure backend dependency-inversion refactor. No new or changed UI components, screens, layouts, or
visual design decisions — `IUserDisplayNameResolver`'s public contract (consumed by four feature
modules' handlers) is unchanged, so nothing reaches the frontend.

## Architectural Fit Assessment
This aligns exactly with an established, already-enforced pattern in this codebase: consumer owns the
contract, provider implements an adapter, provider registers the DI binding in its own module, a
`ModuleBoundariesTests` rule pins the boundary going forward. I verified this directly:

- **Reference pattern**: `Anela.Heblo.Application.Features.Leaflet.Contracts.ILeafletKnowledgeSource`
  (consumer-owned, Leaflet) implemented by `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.KnowledgeBaseLeafletSourceAdapter`
  (provider-owned, KnowledgeBase, `internal sealed`), registered in `KnowledgeBaseModule`.
- **Documented rule**: `docs/architecture/development_guidelines.md` § "Cross-Module Communication
  Example: `ILeafletKnowledgeSource`" — three numbered steps (contract in consumer's `Contracts/`,
  adapter in provider's `Infrastructure/`, DI binding in provider's `{Module}.cs`) — match the spec's
  FR-1/FR-2/FR-3 exactly.
- **Confirmed violation**: read `UserDisplayNameResolver.cs` directly — it injects
  `IAuthorizationRepository` (`Anela.Heblo.Domain.Features.Authorization`) and iterates `AppUser`
  properties (`EntraObjectId`, `Email`, `DisplayName`). `UserDisplayNameResolverTests.cs` mocks
  `IAuthorizationRepository` and constructs `AppUser` fixtures directly, confirming the coupling
  extends into the test suite.
- **Confirmed gap**: read `ModuleBoundariesTests.cs` in full — it has one rule per already-decoupled
  boundary (Leaflet→KnowledgeBase, Article→KnowledgeBase, Smartsupp→KnowledgeBase, Authorization→UserManagement,
  Catalog→{Logistics,Purchase,Manufacture}, etc.), but **no rule inspects `Shared.Users`**. The gap is real.
- **One structural wrinkle**: `Shared/Users` is not itself a `Features/*` vertical slice — it is a
  cross-cutting layer with four *upstream* consumers (KnowledgeBase, Article, Leaflet, Smartsupp
  handlers) and, after this change, one *downstream* provider (Authorization). The existing pattern's
  language ("module A", "module B") assumes two feature modules; here one side is a shared library.
  This does not change the mechanics (contract ownership still inverts correctly) but it does mean
  `Shared/Users` needs its own `Contracts/` subfolder — a new but directly analogous structure, not a
  deviation from convention.

No conflicting or superseding pattern exists. This is a low-risk, mechanically well-precedented change.

## Proposed Architecture

### Component Overview
```
┌─────────────────────────────┐        ┌──────────────────────────────────────────┐
│  Application.Shared.Users    │        │  Application.Features.Authorization       │
│                              │        │                                          │
│  UserDisplayNameResolver ────┼───────▶│  AuthorizationUserDirectorySourceAdapter  │
│    depends on:               │  impl. │    (Infrastructure/, internal sealed)     │
│    IUserDirectorySource ◀────┼────────┼─── implements IUserDirectorySource        │
│    (Contracts/, owned here)  │        │    delegates to ─┐                        │
└──────────────────────────────┘        │                  ▼                        │
                                          │  IAuthorizationRepository.GetAllUsersAsync│
                                          │    (Domain.Features.Authorization,       │
                                          │     unchanged)                           │
                                          └──────────────────────────────────────────┘

DI wiring: AuthorizationModule.AddAuthorizationModule() registers
  IUserDirectorySource -> AuthorizationUserDirectorySourceAdapter
ApplicationModule.cs keeps its existing
  IUserDisplayNameResolver -> UserDisplayNameResolver registration, untouched.

Consumers (unchanged, still depend only on IUserDisplayNameResolver):
  Features.KnowledgeBase.GetFeedbackListHandler
  Features.Article.GetArticleFeedbackListHandler
  Features.Leaflet.GetLeafletFeedbackListHandler
  Features.Smartsupp.GetDraftReplyFeedbackListHandler
```

### Key Design Decisions

#### Decision 1: Contract location — `Shared/Users/Contracts/` vs. bare `Shared/Users/`
**Options considered:**
(a) Put `IUserDirectorySource`/`UserDirectoryEntry` directly in `Application/Shared/Users/` (as the
    issue's suggested-fix snippet shows), or
(b) put them in a new `Application/Shared/Users/Contracts/` subfolder, mirroring
    `Features/Leaflet/Contracts/`.

**Chosen approach:** (b) — `Application/Shared/Users/Contracts/IUserDirectorySource.cs`.

**Rationale:** `development_guidelines.md` states the contract lives in "its own `Contracts/` folder."
Every existing example (`ILeafletKnowledgeSource`) follows this. `Shared/Users` currently has no
subfolders, but adding `Contracts/` is a one-file, additive change with zero migration cost, and it
keeps the convention greppable and consistent for the next person who adds a second cross-cutting
contract here. Overrides the spec's noted "Open Question" — resolved in favor of (b).

#### Decision 2: Adapter location — `Features/Authorization/Infrastructure/`
**Options considered:** (a) place the adapter next to `UserDisplayNameResolver` in `Shared/Users/`
(consumer-side), or (b) place it in `Features/Authorization/Infrastructure/` (provider-side).

**Chosen approach:** (b), exactly as the issue proposes and as `KnowledgeBaseLeafletSourceAdapter`
precedents — the adapter is provider-owned code and must live where the provider module owns its
wiring (ADR-004: "a repository's DI binding is always declared in its owning module's
`{Feature}Module.cs`"; the adapter that consumes that repository follows the same ownership).

**Rationale:** Keeps `Shared/Users` free of any Authorization-aware code, satisfying the
`ModuleBoundariesTests` invariant this change exists to establish.

#### Decision 3: `ModuleBoundaryRule` shape and allowlist
**Options considered:** (a) a strict empty-allowlist rule (no exceptions permitted at all,
including the adapter), or (b) an empty-allowlist rule where the adapter is *exempted structurally*
by living outside the inspected namespace prefix.

**Chosen approach:** (b). Because the adapter lives in
`Anela.Heblo.Application.Features.Authorization.Infrastructure` — **outside**
`InspectedNamespacePrefix: "Anela.Heblo.Application.Shared.Users"` — it is never inspected by this
rule at all, so the allowlist can and should be empty (`new HashSet<string>(StringComparer.Ordinal)`),
matching the pattern for `LeafletAllowlist`, `ArticleAllowlist`, and `SmartsuppKnowledgeBaseAllowlist`
(all empty, "all violations fixed"). This is cleaner than the `AuthorizationUserManagementAllowlist`
precedent (which allowlists one adapter *inside* the inspected namespace) because, unlike that case,
nothing in `Shared/Users` needs to reference both sides.

**Rationale:** An empty allowlist gives the strongest possible regression guard — any future direct
Authorization reference anywhere under `Shared/Users/*` fails CI immediately, with no exception to
maintain.

## Implementation Guidance

### Directory / Module Structure
New files:
- `backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs` — interface + `UserDirectoryEntry` class (both types, one file, matching `ILeafletKnowledgeSource.cs`'s one-interface-per-file convention is not strict here since `KnowledgeSearchResult`-style co-location is not used there; check `Leaflet/Contracts/` for whether result types get separate files — if the codebase convention is one type per file, split `UserDirectoryEntry` into its own file: `UserDirectoryEntry.cs`).
- `backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs`

Changed files:
- `backend/src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs` — constructor + body per FR-4.
- `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs` — add the `AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>()` line, plus a `using Anela.Heblo.Application.Shared.Users.Contracts;`.
- `backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs` — mock `IUserDirectorySource`, build `UserDirectoryEntry` fixtures, drop the Authorization Domain `using`s.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add the new rule (empty allowlist field + one `ModuleBoundaryRule` entry in `Rules()`).

No change to `ApplicationModule.cs`'s existing `IUserDisplayNameResolver` registration or `using`
list (it can in fact drop its now-unused `Anela.Heblo.Domain.Features.Authorization` usings if that
namespace was only imported for this — verify before removing, since `ApplicationModule.cs` wires
many unrelated modules and likely needs the Authorization usings for other registrations already
present in that file).

### Interfaces and Contracts
```csharp
// Application/Shared/Users/Contracts/IUserDirectorySource.cs
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
`UserDirectoryEntry` is a plain class per the repo-wide "DTOs are classes, never records" rule
(`CLAUDE.md`) — this also applies to internal cross-module contract DTOs, not just OpenAPI-facing ones,
since the convention exists to avoid record-related generator/parameter-order pitfalls broadly and
keeping one rule for all contract-shaped types avoids a second, inconsistent convention.

```csharp
// Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs
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

```csharp
// UserDisplayNameResolver.cs — changed constructor and lookup build
public sealed class UserDisplayNameResolver : IUserDisplayNameResolver
{
    private readonly IUserDirectorySource _directorySource;
    private readonly IMemoryCache _cache;

    public UserDisplayNameResolver(IUserDirectorySource directorySource, IMemoryCache cache)
    {
        _directorySource = directorySource;
        _cache = cache;
    }
    // GetLookupAsync: replace `_repository.GetAllUsersAsync(cancellationToken)` with
    // `_directorySource.GetAllAsync(cancellationToken)`; the `foreach (var user in users)` body
    // is unchanged field-for-field (EntraObjectId, Email, DisplayName all exist verbatim on
    // UserDirectoryEntry).
}
```

```csharp
// AuthorizationModule.cs — one added line
services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();
```

```csharp
// ModuleBoundariesTests.cs — new allowlist field + new Rules() entry
private static readonly HashSet<string> SharedUsersAuthorizationAllowlist = new(StringComparer.Ordinal);

// inside Rules():
new ModuleBoundaryRule(
    Name: "Shared.Users -> Authorization",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Shared.Users",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Authorization",
        "Anela.Heblo.Application.Features.Authorization",
        "Anela.Heblo.Persistence.Features.Authorization",
    },
    Allowlist: SharedUsersAuthorizationAllowlist),
```

### Data Flow
1. A handler (e.g. `GetFeedbackListHandler`) calls `IUserDisplayNameResolver.ResolveAsync(identifiers)` — unchanged entry point.
2. `UserDisplayNameResolver` checks its 5-minute `IMemoryCache` entry; on miss, calls `IUserDirectorySource.GetAllAsync(ct)`.
3. `AuthorizationUserDirectorySourceAdapter.GetAllAsync` calls `IAuthorizationRepository.GetAllUsersAsync(ct)` (unchanged repository call) and maps each `AppUser` to a `UserDirectoryEntry`.
4. `UserDisplayNameResolver` builds the identifier→display-name dictionary from `UserDirectoryEntry` fields exactly as it did from `AppUser` fields today, caches it, and returns the requested subset.

No new round trips, no new database query — the same single `GetAllUsersAsync` call, now reached through one additional in-process interface hop.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `UserDirectoryEntry` field types silently diverge from `AppUser` over time (e.g. someone adds a field to `AppUser` that the resolver later needs) | Low | The adapter is the single, obvious mapping point; a missing field surfaces as a compile-time gap in the adapter's `Select`, not a runtime one. |
| New `ModuleBoundaryRule` has a typo in `ForbiddenNamespacePrefixes` (e.g. missing the `.Features.` segment, as this review had to correct once already) | Medium | Verify the exact Persistence namespace (`Anela.Heblo.Persistence.Features.Authorization`, confirmed via `AuthorizationRepository.cs`) before merging; run `dotnet test --filter ModuleBoundariesTests` and confirm the new theory case reports zero violations, not "no types found" (an empty-namespace false pass). |
| Existing `UserDisplayNameResolverTests.cs` five tests are ported incorrectly, losing coverage | Low | FR-5 requires all five existing tests to remain, 1:1, with only the mock/fixture type swapped — no logic change means no new test-writing risk. |
| `ApplicationModule.cs`'s Authorization-namespace `using`s are removed incorrectly, breaking other registrations in that large file | Low | Guidance above: don't remove any `using` from `ApplicationModule.cs` unless verified unused after the change — this file wires many unrelated modules. |

## Specification Amendments
1. **FR-1 location resolved**: use `Application/Shared/Users/Contracts/` (Decision 1) — the spec's Open Question is resolved, not left open.
2. **FR-6 namespace correction**: the third forbidden-prefix in the new `ModuleBoundaryRule` is `Anela.Heblo.Persistence.Features.Authorization` (not `Anela.Heblo.Persistence.Authorization` as first drafted) — confirmed by reading `AuthorizationRepository.cs`'s actual namespace declaration.
3. **FR-6 allowlist resolved**: empty allowlist (Decision 3) — no allowlist entry is needed because the adapter lives outside the inspected namespace prefix.
4. No other functional requirement changes; FR-2 through FR-5 are approved as specified.

## Prerequisites
None — no migration, no config, no infrastructure change, no feature flag. This can be implemented and merged independently of any other in-flight work.
