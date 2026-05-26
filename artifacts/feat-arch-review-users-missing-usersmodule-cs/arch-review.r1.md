# Architecture Review: UsersModule.cs for Users Module DI Registration

## Skip Design: true

## Architectural Fit Assessment

This refactor brings the `Users` feature module into full compliance with the project's documented **Module Registration** convention (`docs/architecture/development_guidelines.md` §"Module Registration", lines 100–129). Verified state of the codebase:

- **Convention is universal.** 37 sibling feature modules under `backend/src/Anela.Heblo.Application/Features/` each have a `{Feature}Module.cs` file with an `Add{Feature}Module(this IServiceCollection)` extension. `Users` is the sole exception — its folder contains only `CurrentUserService.cs`.
- **Boundary violation confirmed.** `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` performs an Application-layer binding (`ICurrentUserService → CurrentUserService`) from an API-layer composition-root utility. The two `using` directives on lines 9 and 11 (`Application.Features.Users`, `Domain.Features.Users`) are referenced *only* by line 130 — verified by grep — so they become unused after the move.
- **No cross-cutting fallout.** `ICurrentUserService` has ~32+ consumers across handlers (Journal, Packaging, MeetingTasks, Catalog.Inventory, etc.). All inject the interface, none reference `CurrentUserService` directly outside tests. A pure registration move is observable to none of them.
- **Aggregation site is unambiguous.** `ApplicationModule.AddApplicationServices` already aggregates all 37 sibling modules; adding a 38th call is a one-line insertion matching the existing block (line 86, adjacent to `AddUserManagement`).

Integration points: `IHttpContextAccessor` (registered in `AddCrossCuttingServices`, line 124). This dependency relationship is the only architectural subtlety and is addressed below.

## Proposed Architecture

### Component Overview

```
Program.cs
  └── AddApplicationServices()        [Anela.Heblo.Application/ApplicationModule.cs]
        ├── AddJournalModule()
        ├── AddGridLayoutsModule()
        ├── AddUserManagement(cfg)
        ├── AddUsersModule()          ← NEW: registers ICurrentUserService → CurrentUserService (Singleton)
        └── ... (35 other modules)

  └── AddCrossCuttingServices()       [Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs]
        ├── AddHttpContextAccessor()  ← stays; API-layer concern (consumed by CurrentUserService)
        ├── AddSingleton(TimeProvider.System)
        ├── [DELETED] AddSingleton<ICurrentUserService, CurrentUserService>()
        └── ...
```

Resolution path at request time: handler → `ICurrentUserService` (singleton resolved from `IServiceProvider`) → `CurrentUserService` constructor → `IHttpContextAccessor` (singleton, ambient `HttpContext` via `AsyncLocal<T>`).

### Key Design Decisions

#### Decision 1: Module file placement matches sibling layout
**Options considered:**
- (A) `Application/Features/Users/UsersModule.cs` — matches all 37 siblings.
- (B) Move `CurrentUserService` to `Application/Common/` first, drop the module entirely.
- (C) Wait until issue #1716 resolves the Users folder location, then create the module.

**Chosen approach:** (A).
**Rationale:** Option B widens scope beyond a structural compliance fix and creates a per-service exception to the module pattern. Option C blocks a trivial fix on an unrelated refactor; if #1716 later moves the folder, `UsersModule.cs` moves with it as a single file in a directory rename — zero added cost. (A) is what the spec prescribes and what every sibling does.

#### Decision 2: Preserve `Singleton` lifetime verbatim
**Options considered:**
- (A) Keep `AddSingleton<ICurrentUserService, CurrentUserService>()` (current behavior).
- (B) Downgrade to `Scoped` to match per-request semantics conceptually.

**Chosen approach:** (A).
**Rationale:** Behavior-parity is an explicit acceptance criterion (FR-5, NFR-1). The singleton lifetime is safe because `CurrentUserService` holds no per-request state — it captures `IHttpContextAccessor` (itself singleton, ambient context via `AsyncLocal<T>`). Changing the lifetime in this refactor would conflate two changes and break the "surgical change" constraint (NFR-3). If a future maintainer adds *new* services to `UsersModule` that hold per-request state, **they must choose `Scoped` for those services individually** — singleton is not the module-wide default and must not be assumed by future additions (see Risks table).

#### Decision 3: Leave `IHttpContextAccessor` registration in the API layer
**Options considered:**
- (A) Keep `AddHttpContextAccessor()` in `AddCrossCuttingServices` (current state).
- (B) Move it into `UsersModule` since `CurrentUserService` is the primary consumer.

**Chosen approach:** (A).
**Rationale:** `IHttpContextAccessor` is an ASP.NET Core HTTP-pipeline primitive — its conceptual home is the API/composition layer, not a feature module. The existing `UserManagementModule.cs:36` documents this exact convention: *"Note: HttpContextAccessor must be registered in the API layer."* Option B would introduce inconsistency for a 5-character saving. Explicitly out of scope per spec.

## Implementation Guidance

### Directory / Module Structure

**New file:** `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs`

**Modified files:**
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — add 1 `using`, add 1 method call.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — delete 1 line + comment, delete 2 `using` directives (both verified unused after deletion).

No other files touched.

### Interfaces and Contracts

**`UsersModule.cs`** — must match the exact shape of sibling modules for consistency. Use file-scoped namespace (matches `GridLayoutsModule.cs`, `UserManagementModule.cs`; `JournalModule.cs` uses block-scoped but file-scoped is the newer style and acceptable per `dotnet format` rules):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Users;

public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        return services;
    }
}
```

**Aggregation point in `ApplicationModule.cs`:**
- Add `using Anela.Heblo.Application.Features.Users;` to the existing import block.
- Insert `services.AddUsersModule();` adjacent to `services.AddUserManagement(configuration);` at line 86 (logical grouping with the other identity-adjacent module).

**Removal in `ServiceCollectionExtensions.cs`:**
- Delete lines 129–130 (the `// Register Current User Service` comment + binding).
- Delete `using Anela.Heblo.Application.Features.Users;` (line 9) — referenced only by deleted line 130.
- Delete `using Anela.Heblo.Domain.Features.Users;` (line 11) — referenced only by deleted line 130.
- Leave `AddHttpContextAccessor()` (line 124) untouched.

### Data Flow

Identical to today — no behavior change. Handler/service constructor receives `ICurrentUserService` from DI; on `GetCurrentUser()` or `IsInRole()`, the singleton reads the ambient `HttpContext` via `IHttpContextAccessor` and projects claims. Registration order in `Program.cs` (line 75 `AddApplicationServices` before line 78 `AddCrossCuttingServices`) remains correct: ASP.NET Core DI resolves at construction time, not registration time, so the order in which singletons are *declared* is irrelevant.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Duplicate registration if the API-layer deletion is missed, causing two `AddSingleton<ICurrentUserService, …>` calls — the last one wins, but it muddies the source of truth. | LOW | All three file changes are part of the single PR; CI build + `dotnet test` will catch any divergent behavior. Reviewer should grep `grep -rn "AddSingleton<ICurrentUserService" backend/src` after the change — must return exactly one hit. |
| Stale `using` directives remain in `ServiceCollectionExtensions.cs` after deletion, causing `dotnet format` to flag warnings. | LOW | Spec FR-3 explicitly requires pruning. Verified via grep: both directives are referenced **only** at the deletion site. Run `dotnet format` after the change. |
| Future contributor adds a *scoped* service to `UsersModule` and assumes the module's `Singleton` default applies — captive-dependency bug. | LOW | The current module body has exactly one line; the `AddSingleton` is local to that call, not a module-wide default. No mitigation needed beyond standard C# DI literacy, but a one-line comment in `UsersModule.cs` (e.g. `// CurrentUserService is singleton-safe because IHttpContextAccessor uses AsyncLocal.`) could be added if reviewer prefers — optional. |
| `Program.cs` ordering changes in a future refactor put `AddCrossCuttingServices` before `AddApplicationServices` and someone assumes `IHttpContextAccessor` must be registered first. | NEGLIGIBLE | Ordering is irrelevant for DI resolution; only relevant for `IServiceCollection.TryAdd*` semantics, which are not used here. No action. |
| Issue #1716 lands first and moves the Users folder, creating a merge conflict. | LOW | The conflict resolution is mechanical — the new module file follows the folder. No design-level conflict. Coordinate landing order with whoever owns #1716. |

## Specification Amendments

The spec (`spec.r1.md`) is complete and accurate. Minor confirmations from the codebase scan that should be noted in the PR description (not the spec itself):

1. **Confirmed `using` deletions are safe.** `using Anela.Heblo.Application.Features.Users;` and `using Anela.Heblo.Domain.Features.Users;` in `ServiceCollectionExtensions.cs` are referenced **only** by line 130 (grep-verified). Both must be removed; otherwise `dotnet format` will flag them.
2. **Namespace style suggestion.** Sibling modules are split between block-scoped (`JournalModule`) and file-scoped (`GridLayoutsModule`, `UserManagementModule`) namespaces. Use **file-scoped** for the new file — it's the newer, preferred style per modern `dotnet format` defaults and matches the two most recently created modules.
3. **`using Anela.Heblo.Domain.Features.Users;` is required in the new module file** to reference `ICurrentUserService`. The `Anela.Heblo.Application.Features.Users` namespace doesn't need to be imported because `CurrentUserService` lives in that same namespace as `UsersModule`.

No functional amendments required.

## Prerequisites

None. All preconditions are already met:

- `Anela.Heblo.Application` project already references `Anela.Heblo.Domain` (where `ICurrentUserService` lives).
- `Microsoft.Extensions.DependencyInjection.Abstractions` is already on the Application project's transitive graph (every sibling module uses it).
- No NuGet packages, no project references, no migrations, no configuration changes.
- No infrastructure or environment changes.

Implementation can begin immediately.