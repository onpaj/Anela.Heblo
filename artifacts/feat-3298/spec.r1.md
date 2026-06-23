# Specification: Move GraphService to Adapters.Microsoft365

## Summary

`GraphService` is an I/O-bound HTTP adapter for the Microsoft Graph API that currently lives in the Application layer (`Application/Features/UserManagement/Services/`), violating the I/O-layer boundary documented in `filesystem.md`. This refactor moves `GraphService` and its mock stub to `Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/`, aligns registration with the established `PhotobankGraphService` pattern, and removes the redundant `AddHttpClient("MicrosoftGraph")` call from `UserManagementModule`.

## Background

The project enforces Clean Architecture: the Application layer holds business logic and port interfaces; concrete I/O implementations live in adapter projects under `backend/src/Adapters/`. The `PhotobankGraphService` / `IPhotobankGraphService` pair is the established template: the interface stays in Application, the concrete HTTP class lives in `Adapters.Microsoft365`, and `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()` owns the registration.

`GraphService` was placed in the Application layer, causing two problems:

1. `Anela.Heblo.Application.csproj` carries `Microsoft.Identity.Web` and `Microsoft.Graph` NuGet references that should only be adapter concerns.
2. `UserManagementModule` registers `AddHttpClient("MicrosoftGraph")` in the else-branch, duplicating the named-client registration already owned by `AddMicrosoft365Adapter()`.

## Functional Requirements

### FR-1: Move GraphService to the adapter project

Move `GraphService.cs` from  
`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`  
to  
`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`.

Update the namespace from `Anela.Heblo.Application.Features.UserManagement.Services` to `Anela.Heblo.Adapters.Microsoft365.UserManagement`. All using directives and cross-references must be updated accordingly.

**Acceptance criteria:**
- `GraphService.cs` does not exist anywhere under `Anela.Heblo.Application/`.
- `GraphService.cs` exists at `Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` with the correct namespace.
- `dotnet build` passes with no errors or warnings related to this move.

### FR-2: Move MockGraphService to the adapter project

Move `MockGraphService.cs` from  
`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`  
to  
`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`.

Update the namespace to `Anela.Heblo.Adapters.Microsoft365.UserManagement`. `MockGraphService` depends only on `ILogger<MockGraphService>` and `IGraphService` — no infrastructure dependencies — so the move is straightforward.

**Acceptance criteria:**
- `MockGraphService.cs` does not exist anywhere under `Anela.Heblo.Application/`.
- `MockGraphService.cs` exists at `Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs` with the correct namespace.
- `dotnet build` passes.

### FR-3: Keep IGraphService in the Application layer

`IGraphService` must remain at  
`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`  
with namespace `Anela.Heblo.Application.Features.UserManagement.Services`. This is the port contract consumed by Application-layer handlers and resolvers. It must not move.

**Acceptance criteria:**
- `IGraphService.cs` remains unchanged in its current location.
- `GraphService` (in the adapter) and `MockGraphService` (in the adapter) both implement `IGraphService` imported from the Application project reference.

### FR-4: Register IGraphService inside AddMicrosoft365Adapter

Add `IGraphService` → `GraphService` and `IGraphService` → `MockGraphService` registration to `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()`, following the exact pattern used for `IPhotobankGraphService`:

- Inside the `if (!useMockAuth && !bypassJwt)` block: `services.AddScoped<IGraphService, GraphService>()`
- Inside the corresponding else block (or a new one): `services.AddScoped<IGraphService, MockGraphService>()`

The mock registration must mirror `PhotobankModule`'s pattern: the mock is registered when `useMockAuth || bypassJwt` is true, and the real implementation when both are false.

**Acceptance criteria:**
- `AddMicrosoft365Adapter()` registers `IGraphService → GraphService` when mock flags are off.
- `AddMicrosoft365Adapter()` registers `IGraphService → MockGraphService` when `UseMockAuth=true` or `BypassJwtValidation=true`.
- Resolving `IGraphService` from the DI container in a production-configuration test returns a `GraphService` instance.
- Resolving `IGraphService` in a mock-configuration test returns a `MockGraphService` instance.

### FR-5: Remove IGraphService registration from UserManagementModule

Remove from `UserManagementModule.AddUserManagement()`:
- The `if (useMockAuth || bypassJwtValidation)` branch that registers `MockGraphService`.
- The `else` branch that calls `services.AddHttpClient("MicrosoftGraph")` and registers `GraphService`.

`UserManagementModule` must retain only:
- `services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>()`
- The FluentValidation and pipeline-behavior registrations that are already present.

The `using Microsoft.Graph;` import in `UserManagementModule.cs` must also be removed if it becomes unused.

**Acceptance criteria:**
- `UserManagementModule.cs` contains no reference to `GraphService`, `MockGraphService`, `AddHttpClient`, or `Microsoft.Graph`.
- `dotnet build` passes.
- `dotnet format` produces no diff.

### FR-6: Remove Microsoft.Identity.Web and Microsoft.Graph from Application.csproj (if no longer needed)

After the move, audit `Anela.Heblo.Application.csproj` to determine whether `Microsoft.Identity.Web` (v3.14.1) and `Microsoft.Graph` (v5.92.0) are still referenced by any remaining file in the Application project. If they are unused, remove both `<PackageReference>` entries.

**Acceptance criteria:**
- If no Application-layer file uses types from `Microsoft.Identity.Web` or `Microsoft.Graph` after the move, both package references are removed from `Anela.Heblo.Application.csproj`.
- `dotnet build` passes after removal.
- If any remaining Application file still imports from these packages, the references must be kept and this criterion is noted as N/A.

### FR-7: Consolidate the MicrosoftGraph named-client registration

The `AddHttpClient("MicrosoftGraph")` call in `AddMicrosoft365Adapter()` (line 22–26 of `Microsoft365AdapterServiceCollectionExtensions.cs`) already configures `AllowAutoRedirect = true` and is guarded by the `!useMockAuth && !bypassJwt` condition. After removing the duplicate from `UserManagementModule`, no further changes to the named-client registration are needed.

**Acceptance criteria:**
- Only one `AddHttpClient("MicrosoftGraph")` call exists in the entire backend codebase.
- That call lives in `Microsoft365AdapterServiceCollectionExtensions.cs`.
- `IHttpClientFactory.CreateClient("MicrosoftGraph")` in `GraphService` resolves to an `HttpClient` with `AllowAutoRedirect = true`.

### FR-8: Update unit tests for the DI registration

`GraphServiceTests.cs` contains two tests that exercise `UserManagementModule.AddUserManagement()` for DI resolution:
- `AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService`
- `AddUserManagement_MockBranch_RegistersMockGraphService`

These tests must be updated to call `AddMicrosoft365Adapter(configuration)` instead of (or in addition to) `AddUserManagement(configuration)`, because registration is now owned by the adapter. Alternatively, replace them with equivalent tests in a new `Microsoft365AdapterTests.cs` file under `backend/test/Anela.Heblo.Tests/`. The existing tests must not be left in a failing or vacuously-passing state.

**Acceptance criteria:**
- `dotnet test` passes with no skipped or failed tests in `GraphServiceTests.cs` (or its replacement).
- The production-branch test verifies that `IGraphService` resolves to `GraphService` and that `IHttpClientFactory.CreateClient("MicrosoftGraph")` returns a non-null client.
- The mock-branch test verifies that `IGraphService` resolves to `MockGraphService`.

### FR-9: Preserve ParseMembersFromJson accessibility for existing tests

`GraphService.ParseMembersFromJson` is declared `internal static`. `ParseMembersFromJsonTests.cs` calls it directly via `GraphService.ParseMembersFromJson(...)`. After the move, this method is in the `Anela.Heblo.Adapters.Microsoft365` assembly.

The test project (`Anela.Heblo.Tests`) must be able to access `internal` members of `Anela.Heblo.Adapters.Microsoft365`. Add an `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` entry to `Anela.Heblo.Adapters.Microsoft365.csproj`.

**Acceptance criteria:**
- `ParseMembersFromJsonTests.cs` compiles and all its tests pass without modification.
- `Anela.Heblo.Adapters.Microsoft365.csproj` contains `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`.

## Non-Functional Requirements

### NFR-1: No behavior change

This is a pure structural refactor. Observable runtime behavior — Graph API calls, caching logic, token acquisition, mock responses, error handling — must be identical before and after. No logic may be altered as part of this change.

### NFR-2: Build and format clean

`dotnet build` and `dotnet format` must both pass with zero errors and zero diffs on the affected projects (`Anela.Heblo.Application`, `Anela.Heblo.Adapters.Microsoft365`, `Anela.Heblo.Tests`).

### NFR-3: Test suite green

All existing tests in `backend/test/Anela.Heblo.Tests/Features/UserManagement/` must pass after the change, including:
- `GraphServiceTests.cs`
- `GraphServiceSearchTests.cs`
- `ParseMembersFromJsonTests.cs`
- `MockGraphServiceTests.cs`
- `GetGroupMembersHandlerTests.cs`
- `GetGroupMembersValidationPipelineTests.cs`
- `GetAppRoleMembersTests.cs`

## Data Model

No data model changes. This refactor affects only project/file structure and DI wiring.

## API / Interface Design

No API surface changes. `IGraphService` contract is unchanged:

```csharp
// Stays in: Anela.Heblo.Application.Features.UserManagement.Services
public interface IGraphService
{
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
    Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
}
```

Target file layout after the change:

```
backend/src/
  Anela.Heblo.Application/
    Features/UserManagement/
      Services/
        IGraphService.cs          ← unchanged (interface stays here)
        GraphService.cs           ← DELETED (moved to adapter)
        MockGraphService.cs       ← DELETED (moved to adapter)

  Adapters/Anela.Heblo.Adapters.Microsoft365/
    UserManagement/               ← NEW subdirectory
      GraphService.cs             ← MOVED here
      MockGraphService.cs         ← MOVED here
    Microsoft365AdapterServiceCollectionExtensions.cs  ← updated
```

## Dependencies

- `Anela.Heblo.Adapters.Microsoft365.csproj` already references `Microsoft.Identity.Web` and `Microsoft.Graph` — no new package references needed in the adapter project.
- `Anela.Heblo.Adapters.Microsoft365.csproj` already has a `<ProjectReference>` to `Anela.Heblo.Application` — `IGraphService` and `UserDto` are already resolvable from the adapter.
- The test project `Anela.Heblo.Tests` must gain `InternalsVisibleTo` access to `Anela.Heblo.Adapters.Microsoft365` (see FR-9).

## Out of Scope

- Changing the logic inside `GraphService` (caching, token acquisition, HTTP calls, error handling).
- Changing the `IGraphService` interface contract.
- Migrating from raw `HttpRequestMessage` to the `GraphServiceClient` SDK.
- Any changes to frontend code.
- Changing the `GraphArticleUserResolver` or any MediatR handler.
- Adding new Graph API capabilities.

## Open Questions

None.

## Status: COMPLETE
