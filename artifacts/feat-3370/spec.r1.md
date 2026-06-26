# Specification: Authorization–UserManagement Module Boundary Fix

## Summary

`GetEntraAccessUsersHandler` in the `Authorization` module directly injects `IGraphService`, an internal service interface belonging to the `UserManagement` module, violating the project rule that cross-module communication must go exclusively through `contracts/` directories. This refactor introduces a consumer-owned contract in `Authorization/Contracts/` and a provider-side adapter in `UserManagement/Infrastructure/`, mirroring the existing `IArticleUserResolver` / `GraphArticleUserResolver` pattern, thereby restoring module isolation with no functional change.

## Background

`development_guidelines.md` mandates that modules communicate exclusively through contracts: "Communication between modules **exclusively through `contracts/`**." The violation was identified by the daily arch-review routine on 2026-06-26.

`IGraphService` (`UserManagement/Services/IGraphService.cs`) is a UserManagement-internal abstraction that wraps Microsoft Graph calls. `GetEntraAccessUsersHandler` (`Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs`) imports it directly via `using Anela.Heblo.Application.Features.UserManagement.Services`, coupling Authorization to UserManagement's internal implementation shape.

The correct pattern is established by `IArticleUserResolver` / `GraphArticleUserResolver`:
- The consuming module (Article) defines a narrow contract in `Article/Contracts/IArticleUserResolver.cs` with only the methods it needs.
- The providing module (UserManagement) implements `GraphArticleUserResolver` in `UserManagement/Infrastructure/` as an `internal sealed` adapter, delegating to `IGraphService`.
- `UserManagementModule.cs` registers the binding: `services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>()`.

Authorization must follow the same shape.

## Functional Requirements

### FR-1: Consumer-owned contract in Authorization

A new interface `IEntraAccessUserSource` must be created at `backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/IEntraAccessUserSource.cs`.

The interface must expose exactly one method that satisfies `GetEntraAccessUsersHandler`'s current need: retrieving the list of users assigned the base app role.

```csharp
namespace Anela.Heblo.Application.Features.Authorization.Contracts;

public interface IEntraAccessUserSource
{
    Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct);
}

public sealed record EntraAccessUserRecord(string Id, string Email, string DisplayName);
```

The `EntraAccessUserRecord` value type is Authorization-owned. It must NOT reference any type from the `UserManagement` namespace (no `UserDto`, no `IGraphService`).

**Acceptance criteria:**
- File exists at `Authorization/Contracts/IEntraAccessUserSource.cs`.
- The interface and its supporting record type live in namespace `Anela.Heblo.Application.Features.Authorization.Contracts`.
- The interface has no `using` statements referencing any `UserManagement.*` namespace.
- The interface exposes exactly one method: `GetBaseMembersAsync(CancellationToken)` returning `Task<List<EntraAccessUserRecord>>`.

### FR-2: Provider-side adapter in UserManagement

A new class `EntraAccessUserSourceAdapter` must be created at `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`.

- It is `internal sealed` (consistent with `GraphArticleUserResolver`).
- It implements `IEntraAccessUserSource` (from `Authorization.Contracts`).
- It depends on `IGraphService` via constructor injection.
- Its `GetBaseMembersAsync` delegates to `_graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct)` and maps `UserDto` → `EntraAccessUserRecord`.

```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class EntraAccessUserSourceAdapter : IEntraAccessUserSource
{
    private readonly IGraphService _graph;

    public EntraAccessUserSourceAdapter(IGraphService graph) => _graph = graph;

    public async Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct)
    {
        var users = await _graph.GetAppRoleMembersAsync(AccessRoles.Base, ct);
        return users
            .Select(u => new EntraAccessUserRecord(u.Id, u.Email, u.DisplayName))
            .ToList();
    }
}
```

**Acceptance criteria:**
- File exists at `UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`.
- Class is `internal sealed`.
- Class implements `IEntraAccessUserSource` and injects only `IGraphService`.
- The adapter does not expose `IGraphService` or any `UserManagement.Services.*` type in its public surface.

### FR-3: DI registration in UserManagementModule

`UserManagementModule.AddUserManagement()` must register the new adapter:

```csharp
services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>();
```

This registration sits alongside the existing `IArticleUserResolver` registration.

**Acceptance criteria:**
- `UserManagementModule.cs` contains `services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>()`.
- No other module registers this binding.

### FR-4: Handler updated to inject IEntraAccessUserSource

`GetEntraAccessUsersHandler` must be updated to:
- Remove `using Anela.Heblo.Application.Features.UserManagement.Services;`
- Add `using Anela.Heblo.Application.Features.Authorization.Contracts;`
- Replace the `IGraphService _graphService` field with `IEntraAccessUserSource _entraSource`
- Replace the `GetAppRoleMembersAsync` call with `GetBaseMembersAsync`
- Map from `EntraAccessUserRecord` instead of `UserDto`

**Acceptance criteria:**
- `GetEntraAccessUsersHandler.cs` contains no `using` statement referencing `UserManagement.Services` or `UserManagement.Contracts`.
- The handler injects `IEntraAccessUserSource`, not `IGraphService`.
- The handler builds `EntraUserDto` from `EntraAccessUserRecord` fields (`Id`, `Email`, `DisplayName`).
- The handler's behavior (field mapping, `OrderBy(u => u.DisplayName)`) is functionally identical to the current implementation.

### FR-5: No other Authorization use cases added to IEntraAccessUserSource

The new contract must be scoped only to what `GetEntraAccessUsersHandler` needs. No additional `IGraphService` methods (e.g., `SearchUsersAsync`, `GetGroupMembersAsync`) may be added to `IEntraAccessUserSource` as part of this change.

**Acceptance criteria:**
- `IEntraAccessUserSource` declares exactly one method.

## Non-Functional Requirements

### NFR-1: No behavioral change

This is a pure structural refactor. The response payload of `GET /api/authorization/entra-access-users` (or equivalent) must remain identical before and after the change: same fields, same ordering, same data source.

**Acceptance criteria:**
- Existing unit or integration tests for `GetEntraAccessUsersHandler` pass without modification to their assertions.
- If no unit tests exist for this handler, a unit test must be added that verifies the handler maps `EntraAccessUserRecord` fields correctly and returns users ordered by `DisplayName`.

### NFR-2: Module boundary enforced

After the change, the `Authorization` feature directory must contain zero `using` statements referencing `UserManagement.Services.*`.

**Acceptance criteria:**
- `grep -r "UserManagement.Services" backend/src/Anela.Heblo.Application/Features/Authorization/` returns no results.

### NFR-3: Build passes

`dotnet build` and `dotnet format` must succeed after the change with no new warnings.

## Data Model

No data model changes. This refactor is entirely in the application/service layer. No database entities, migrations, or persistence changes are involved.

The data flow remains:

```
GetEntraAccessUsersHandler
  → IEntraAccessUserSource.GetBaseMembersAsync()          [Authorization contract]
    → EntraAccessUserSourceAdapter                         [UserManagement adapter]
      → IGraphService.GetAppRoleMembersAsync(AccessRoles.Base)
        → Microsoft Graph API
```

## API / Interface Design

No API changes. The existing MediatR request/response types (`GetEntraAccessUsersRequest`, `GetEntraAccessUsersResponse`, `EntraUserDto`) remain unchanged and in their current location (`Authorization/UseCases/GetEntraAccessUsers/`).

The new `EntraAccessUserRecord` is an internal intermediary type; it is not exposed via HTTP.

## Dependencies

- `IGraphService` — existing UserManagement-internal service; the adapter depends on it. No changes to `IGraphService` itself.
- `AccessRoles.Base` — existing constant in `Anela.Heblo.Domain.Features.Authorization`; the adapter references it directly (it is a domain constant, not a UserManagement-internal type, so this is acceptable).
- `UserManagementModule.cs` — requires one new `AddScoped` line.
- No new NuGet packages.

## Out of Scope

- Changes to `IGraphService` (adding, removing, or renaming methods).
- Refactoring other handlers in `Authorization` that may have separate issues.
- Changes to `UserManagement`-internal use cases that consume `IGraphService` directly (those are intra-module and do not violate the boundary rule).
- Frontend changes.
- E2E test changes.
- Any unrelated cleanup in `GetEntraAccessUsersHandler` (e.g., extracting mapping logic).

## Open Questions

None.

## Status: COMPLETE
