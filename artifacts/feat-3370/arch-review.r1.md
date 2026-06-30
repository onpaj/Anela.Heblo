# Architecture Review: Authorization–UserManagement Module Boundary Fix

## Skip Design: true

## Architectural Fit Assessment

The violation is real and unambiguous. `GetEntraAccessUsersHandler` carries a `using Anela.Heblo.Application.Features.UserManagement.Services` import, directly referencing the internal `IGraphService` interface. This is the only Authorization handler with a cross-module import of this kind; all other Authorization handlers depend only on `IAuthorizationRepository`, `IPermissionResolver`, and domain types.

The established remedy pattern (`IArticleUserResolver` / `GraphArticleUserResolver`) is a clean fit here. The Article module's shape is exactly analogous: a consuming module defines a narrow contract in its own `Contracts/` directory, the UserManagement module implements an `internal sealed` adapter in its `Infrastructure/` directory that delegates to `IGraphService`, and `UserManagementModule.cs` owns the DI binding. The spec's proposed structure follows this pattern faithfully.

One meaningful difference from the Article pattern: `GraphArticleUserResolver` wraps two exception types (`MsalException`, `ODataError`) because `BackfillArticleRequestedByHandler` explicitly catches them. The current `GetEntraAccessUsersHandler` propagates all exceptions from `IGraphService` without catching. The adapter must not add exception translation unless required by the handler; it should not import the Article error-wrapping precedent.

The existing unit test (`GetEntraAccessUsersHandlerTests`) mocks `IGraphService` directly. That test must be rewritten to mock `IEntraAccessUserSource` instead — the spec omits this but it is a required change for the tests to compile.

The `Authorization/Contracts/` directory does not yet exist. It must be created.

## Proposed Architecture

### Component Overview

```
Authorization module
  UseCases/GetEntraAccessUsers/
    GetEntraAccessUsersHandler   -- injects IEntraAccessUserSource (was IGraphService)
  Contracts/
    IEntraAccessUserSource       -- NEW: consumer-owned contract
      GetBaseMembersAsync(CancellationToken) -> Task<List<EntraAccessUserRecord>>
    EntraAccessUserRecord        -- NEW: Authorization-owned value type (sealed record)

UserManagement module
  Infrastructure/
    GraphArticleUserResolver     -- existing, unchanged
    EntraAccessUserSourceAdapter -- NEW: internal sealed, implements IEntraAccessUserSource
  UserManagementModule.cs        -- adds one AddScoped line

Domain (unchanged)
  AccessRoles.Base               -- passed to IGraphService by the adapter
```

### Key Design Decisions

#### Decision 1: Contract file placement — interface and record in a single file

**Options considered:**
- One file per type (`IEntraAccessUserSource.cs` + `EntraAccessUserRecord.cs`)
- Combined in `IEntraAccessUserSource.cs`

**Chosen approach:** Single file, matching the `IArticleUserResolver.cs` precedent which co-locates `ArticleUserMatch` alongside the interface in one file.

**Rationale:** The record is a value type that only exists to satisfy the interface's return type. Splitting it adds file count with no navigability benefit. The pattern is already established in the codebase.

#### Decision 2: No exception translation in the adapter

**Options considered:**
- Wrap `MsalException` / `ODataError` (matching the Article adapter)
- Let exceptions propagate as-is from `IGraphService`

**Chosen approach:** No exception wrapping. The adapter delegates and maps; it does not catch.

**Rationale:** `GetEntraAccessUsersHandler` currently lets all exceptions from `IGraphService` propagate unhandled. The API layer or global exception middleware handles them. Adding exception translation in the adapter would change observable behavior (NFR-1). The Article adapter wraps because its consumer explicitly catches; the Authorization handler does not. Follow what the consumer requires, not what the reference implementation does.

#### Decision 3: Test rewrite scope

**Options considered:**
- Keep existing `GetEntraAccessUsersHandlerTests` structure, swap mock type only
- Write a new test class

**Chosen approach:** Rewrite the existing test class in-place. Replace `Mock<IGraphService>` with `Mock<IEntraAccessUserSource>`, replace `UserDto` list construction with `EntraAccessUserRecord` list construction, remove the `using` imports for `UserManagement.Contracts` and `UserManagement.Services`.

**Rationale:** The test assertions (ordering, field mapping, empty list) remain valid. Rewriting in-place keeps the test count identical and avoids dead test classes.

## Implementation Guidance

### Directory / Module Structure

Files to create:

```
backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/
  IEntraAccessUserSource.cs                   (new)

backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/
  EntraAccessUserSourceAdapter.cs             (new)
```

Files to modify:

```
backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/
  GetEntraAccessUsersHandler.cs               (swap dependency)

backend/src/Anela.Heblo.Application/Features/UserManagement/
  UserManagementModule.cs                     (add one AddScoped line)

backend/test/Anela.Heblo.Tests/Authorization/
  GetEntraAccessUsersHandlerTests.cs          (rewrite mocked dependency)
```

No other files need to change.

### Interfaces and Contracts

`IEntraAccessUserSource.cs` — place in namespace `Anela.Heblo.Application.Features.Authorization.Contracts`. No `using` statements that reference any `UserManagement.*` namespace.

```csharp
namespace Anela.Heblo.Application.Features.Authorization.Contracts;

public interface IEntraAccessUserSource
{
    Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct);
}

public sealed record EntraAccessUserRecord(string Id, string Email, string DisplayName);
```

`EntraAccessUserSourceAdapter.cs` — place in namespace `Anela.Heblo.Application.Features.UserManagement.Infrastructure`. Class is `internal sealed`. The only `using` additions needed are `Authorization.Contracts` (to satisfy the interface) and `Domain.Features.Authorization` (for `AccessRoles.Base`). `UserManagement.Services` is already in scope via the existing infrastructure namespace.

The mapping: `UserDto.Id` → `EntraAccessUserRecord.Id`, `UserDto.Email` → `.Email`, `UserDto.DisplayName` → `.DisplayName`. All three fields are present on `UserDto` (confirmed in `UserManagement/Contracts/UserDto.cs`).

`UserManagementModule.cs` registration goes after the existing `IArticleUserResolver` line:

```csharp
services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();
services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>();
```

`GetEntraAccessUsersHandler.cs` — after the change, its only non-system `using` is:

```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
using MediatR;
```

The `using Anela.Heblo.Domain.Features.Authorization` import (`AccessRoles`) is removed because the handler no longer calls any method that requires it — `AccessRoles.Base` moves into the adapter. The handler maps `EntraAccessUserRecord` → `EntraUserDto` using `.Id`, `.Email`, `.DisplayName`.

### Data Flow

```
HTTP GET /api/authorization/entra-access-users
  -> MediatR dispatches GetEntraAccessUsersRequest
  -> GetEntraAccessUsersHandler.Handle()
       calls IEntraAccessUserSource.GetBaseMembersAsync(ct)
       -> EntraAccessUserSourceAdapter.GetBaseMembersAsync(ct)
            calls IGraphService.GetAppRoleMembersAsync(AccessRoles.Base, ct)
            -> GraphService (Microsoft Graph)
            returns List<UserDto>
            maps each UserDto to EntraAccessUserRecord
            returns List<EntraAccessUserRecord>
       handler maps EntraAccessUserRecord -> EntraUserDto
       sorts by DisplayName
       returns GetEntraAccessUsersResponse
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file still imports `UserManagement.*` after handler is updated — build passes but boundary grep fails | Medium | Treat test rewrite as a required step, not optional. Verify `grep -r "UserManagement.Services" backend/` after all changes. |
| `EntraAccessUserRecord` property names diverge from `UserDto` (`Id` vs something else) | Low | Confirmed via source: `UserDto` has `Id`, `Email`, `DisplayName` — all match the record's positional parameters. |
| Second developer adds a method to `IEntraAccessUserSource` for a future Authorization use case without noticing FR-5 | Low | The interface comment (on `IArticleUserResolver` as precedent) already documents the single-consumer intent. A code comment in the interface reinforces this. |
| `AuthorizationModule.cs` is incorrectly assumed to need a registration | None | Confirmed: `AuthorizationModule.cs` must NOT register `IEntraAccessUserSource`. Only `UserManagementModule.cs` owns this binding, exactly as it owns `IArticleUserResolver`. |

## Specification Amendments

**Amendment 1 — Test file is a required deliverable.** The spec lists no test changes under FR-4. However `GetEntraAccessUsersHandlerTests.cs` will fail to compile after the handler is updated because it constructs `new GetEntraAccessUsersHandler(mock.Object)` where `mock` is `Mock<IGraphService>`. The test must be updated as part of this task. Add it to FR-4's acceptance criteria: "Existing unit tests compile and pass against `Mock<IEntraAccessUserSource>`."

**Amendment 2 — Handler no longer needs `using Anela.Heblo.Domain.Features.Authorization`.** The spec does not explicitly say to remove this import; it was present only because the handler called `AccessRoles.Base`. After the change, `AccessRoles.Base` is referenced only inside `EntraAccessUserSourceAdapter`. Remove the import from the handler to keep the file clean and avoid a `dotnet format` warning about unused usings.

## Prerequisites

None. All required types (`IGraphService`, `AccessRoles`, `UserDto`, `UserManagementModule`) already exist. No migrations, no configuration changes, no infrastructure work required before implementation can begin.
