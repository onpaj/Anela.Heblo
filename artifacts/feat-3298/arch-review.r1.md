# Architecture Review: Move GraphService to Adapters.Microsoft365

## Skip Design: true

## Architectural Fit Assessment

This is a straightforward structural refactor with a single, well-defined precedent: `PhotobankGraphService` / `IPhotobankGraphService`. The violation is clear — `GraphService` is an HTTP adapter (acquires OAuth tokens, creates `HttpClient`, calls Graph REST API over the network) living in the Application layer. The project's `filesystem.md` states explicitly: "Concrete implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`."

Key integration points:
- `IGraphService` stays in `Application/Features/UserManagement/Services/` — Application layer consumers (`GetGroupMembersHandler`, `GraphArticleUserResolver`) depend on the interface, not the concrete type. No handler files change.
- `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()` gains the `IGraphService` registrations, following the exact pattern used for `IPhotobankGraphService`.
- `UserManagementModule.AddUserManagement()` loses its concrete-type registrations and its duplicated `AddHttpClient("MicrosoftGraph")` call.
- The test project `Anela.Heblo.Tests` already has a `ProjectReference` to `Anela.Heblo.Adapters.Microsoft365`, so no new project reference is needed there.
- `ParseMembersFromJson` is `internal static` on `GraphService`. Tests in `Anela.Heblo.Tests` call it directly via `GraphService.ParseMembersFromJson(...)`. The test project gets `InternalsVisibleTo` access from `Anela.Heblo.Application` today; after the move it must get `InternalsVisibleTo` access from `Anela.Heblo.Adapters.Microsoft365`.

The `Anela.Heblo.Application.csproj` currently carries `Microsoft.Graph` (5.92.0) and `Microsoft.Identity.Web` (3.14.1). Inspection confirms both are consumed exclusively by `GraphService.cs` within the Application project — the `using Microsoft.Identity.Web` and `using Microsoft.Identity.Client` (re-exported by Identity.Web) appear only in `GraphService.cs`. Once moved, both package references must be removed from `Application.csproj`. `Adapters.Microsoft365.csproj` already declares both at the same versions, so no version change is needed there.

The `AddUserManagement()` `else` branch calls `services.AddHttpClient("MicrosoftGraph")` with no configuration. `AddMicrosoft365Adapter()` already calls the same named-client registration with an explicit `HttpClientHandler` (AllowAutoRedirect). The duplication is harmless today (repeated `AddHttpClient` calls are idempotent via `IHttpClientFactory`'s keyed registration), but the Application-layer call should be removed so the adapter owns it exclusively.

## Proposed Architecture

### Component Overview

```
Application layer (no I/O dependencies)
  Features/UserManagement/
    Services/
      IGraphService.cs          (unchanged — port interface, stays here)
    UserManagementModule.cs     (removes concrete registrations, keeps IArticleUserResolver)

Adapters layer (owns I/O)
  Adapters.Microsoft365/
    UserManagement/             (new subdirectory, mirrors Photobank/)
      GraphService.cs           (moved from Application)
      MockGraphService.cs       (moved from Application)
    Microsoft365AdapterServiceCollectionExtensions.cs  (gains IGraphService registrations)

Tests
  Anela.Heblo.Tests/
    (already has ProjectReference to Adapters.Microsoft365)
    (gains InternalsVisibleTo from Adapters.Microsoft365 for ParseMembersFromJson)
```

### Key Design Decisions

#### Decision 1: Subdirectory layout inside Adapters.Microsoft365
**Options considered:**
- Flat: `Adapters.Microsoft365/GraphService.cs` (alongside `PhotobankGraphService.cs`)
- Subdirectory: `Adapters.Microsoft365/UserManagement/GraphService.cs` (mirrors `Photobank/PhotobankGraphService.cs`)

**Chosen approach:** Subdirectory `UserManagement/` mirroring the existing `Photobank/` subdirectory.

**Rationale:** `PhotobankGraphService` lives in `Adapters.Microsoft365/Photobank/`. The established pattern uses a feature-named subdirectory. `GraphService` belongs in `Adapters.Microsoft365/UserManagement/` for the same reason. Flat placement would work but break the pattern already set by Photobank.

#### Decision 2: MockGraphService placement
**Options considered:**
- Keep `MockGraphService` in Application layer (it has no I/O dependencies — only logging)
- Move it to Adapters.Microsoft365 alongside GraphService

**Chosen approach:** Move `MockGraphService` to `Adapters.Microsoft365/UserManagement/`.

**Rationale:** The mock is the dev/test stand-in for `GraphService`. `AddMicrosoft365Adapter()` must register one or the other depending on `UseMockAuth`/`BypassJwt`. Centralising both registrations inside the adapter extension method means `UserManagementModule` has no knowledge of which implementation exists — correct dependency direction. Keeping the mock in Application would require Application to know about the production type's registration location, which splits the registration logic.

#### Decision 3: Handling the AddHttpClient("MicrosoftGraph") duplication
**Options considered:**
- Leave the call in `UserManagementModule` as a safety net
- Remove it from `UserManagementModule` entirely, relying on `AddMicrosoft365Adapter`

**Chosen approach:** Remove the `services.AddHttpClient("MicrosoftGraph")` call from `UserManagementModule.AddUserManagement()` entirely.

**Rationale:** The adapter already owns this registration with a richer configuration (explicit `HttpClientHandler`). `UserManagementModule`'s copy provides no configuration and its comment already acknowledges it "matches the shared client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules" — proof that the adapter is the authoritative owner. The Application layer must not own infrastructure registrations.

#### Decision 4: InternalsVisibleTo for ParseMembersFromJson tests
**Options considered:**
- Promote `ParseMembersFromJson` to `public` to avoid the InternalsVisibleTo dependency
- Add `InternalsVisibleTo` to `Adapters.Microsoft365.csproj`

**Chosen approach:** Add `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` to `Adapters.Microsoft365.csproj`. Keep the method `internal`.

**Rationale:** `ParseMembersFromJson` is a private parsing concern that should not be part of the public API of the adapter. `internal` + `InternalsVisibleTo` is the established pattern in this codebase (Application already uses it for tests). Making it `public` would leak an implementation detail.

#### Decision 5: Microsoft.Graph and Microsoft.Identity.Web in Application.csproj
**Options considered:**
- Leave the package references in Application.csproj for safety
- Remove them after the move

**Chosen approach:** Remove both package references from `Application.csproj` after confirming no other Application-layer file uses them.

**Rationale:** The entire motivation for this refactor is to move infrastructure dependencies out of the Application layer. Leaving the packages in `Application.csproj` would preserve the architectural violation even after the class moves. Before removing, verify with a quick search that no other file in the Application project imports `Microsoft.Identity.Web` or `Microsoft.Graph` namespaces.

## Implementation Guidance

### Directory / Module Structure

Files to create (move, updating namespace):
```
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs
```

Files to delete:
```
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs
```

Files to modify:
```
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj
backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs
backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

No files change in `backend/test/` other than verifying the test project still compiles (it already references `Adapters.Microsoft365`).

### Interfaces and Contracts

`IGraphService` is unchanged. Its namespace and location do not move:
```
Anela.Heblo.Application.Features.UserManagement.Services.IGraphService
```

New namespace for moved implementations:
```
Anela.Heblo.Adapters.Microsoft365.UserManagement
```

`GraphService` and `MockGraphService` update only their `namespace` declaration. All `using` statements, constructor signatures, and method implementations are unchanged.

`Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()` gains two additions in the `if (!useMockAuth && !bypassJwt)` block:
```csharp
services.AddScoped<IGraphService, GraphService>();
```
And in the `else` path (currently absent — add it):
```csharp
else
{
    services.AddScoped<IGraphService, MockGraphService>();
}
```
The existing `AddHttpClient("MicrosoftGraph")` registration in the production path already covers the `GraphService` HTTP client needs.

`UserManagementModule.AddUserManagement()` retains:
- `IArticleUserResolver` registration (no change)
- Validator and pipeline behavior registrations (no change)

It drops:
- The `if/else` block that registered `IGraphService → MockGraphService` and `IGraphService → GraphService`
- `services.AddHttpClient("MicrosoftGraph")`
- The `using Microsoft.Graph;` import (no longer needed)

`Adapters.Microsoft365.csproj` gains:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="Anela.Heblo.Tests" />
</ItemGroup>
```

`Application.csproj` drops:
```xml
<PackageReference Include="Microsoft.Graph" Version="5.92.0" />
<PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

### Data Flow

**Production registration path** (both `UseMockAuth` and `BypassJwtValidation` are false):

```
Program.cs
  → AddMicrosoft365Adapter()
      → AddHttpClient("MicrosoftGraph")      [owned here, not in UserManagementModule]
      → AddScoped<IPhotobankGraphService, PhotobankGraphService>()
      → AddScoped<IGraphService, GraphService>()     [NEW]
  → AddUserManagement()
      → AddScoped<IArticleUserResolver, GraphArticleUserResolver>()
      → validator + pipeline registrations
```

**Mock/bypass path:**

```
Program.cs
  → AddMicrosoft365Adapter()
      [no registrations — mock branch adds MockGraphService]
      → AddScoped<IGraphService, MockGraphService>()     [NEW]
  → AddUserManagement()
      → AddScoped<IArticleUserResolver, GraphArticleUserResolver>()
      → validator + pipeline registrations
```

**Runtime data flow** (unchanged from today):

```
GetGroupMembersRequest
  → GetGroupMembersHandler (Application)
      → IGraphService.GetGroupMembersAsync()
          → [resolves to] GraphService (Adapters.Microsoft365)
              → ITokenAcquisition.GetAccessTokenForAppAsync()
              → IHttpClientFactory.CreateClient("MicrosoftGraph")
              → HTTP GET graph.microsoft.com/v1.0/groups/{id}/members
              → ParseMembersFromJson()
          → returns List<UserDto>
      → GetGroupMembersResponse
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ParseMembersFromJson` tests fail after move because `InternalsVisibleTo` on Application no longer covers the moved type | High | Add `InternalsVisibleTo Include="Anela.Heblo.Tests"` to `Adapters.Microsoft365.csproj` before deleting the Application file |
| `AddMicrosoft365Adapter()` mock branch currently has no `else` — if forgotten, mock environments resolve no `IGraphService` and fail at runtime | High | The new `else` block that registers `MockGraphService` is a required addition. Verify via the existing `AddUserManagement_MockBranch_RegistersMockGraphService` test being updated to call `AddMicrosoft365Adapter()` instead |
| Another Application-layer file uses `Microsoft.Graph` or `Microsoft.Identity.Web` namespaces — removing the packages breaks the build | Medium | Before removing package references, run `grep -r "Microsoft\.Graph\|Microsoft\.Identity" backend/src/Anela.Heblo.Application/ --include="*.cs"` and confirm only GraphService.cs (now deleted) contained those usings |
| `using Microsoft.Graph;` in `UserManagementModule.cs` (line 12) becomes a dangling import after removing the registration | Low | Remove the `using Microsoft.Graph;` import from `UserManagementModule.cs` as part of the change |
| `GraphServiceTests.cs` test `AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService` calls `services.AddUserManagement()` and expects `IGraphService` to resolve — this will fail after the move | High | Update that test to call both `services.AddMicrosoft365Adapter(configuration)` and `services.AddUserManagement(configuration)`, mirroring how they are composed in production. The mock branch test similarly needs updating |

## Specification Amendments

**FR-4 / FR-5 refinement on the mock branch:** The spec says "Register `IGraphService` inside `AddMicrosoft365Adapter`" and "Remove `IGraphService` registration from `UserManagementModule`". The current `AddMicrosoft365Adapter()` has no `else` branch — the entire body is guarded by `if (!useMockAuth && !bypassJwt)`. The implementation must add an explicit `else` block that registers `MockGraphService`. Without it, mock environments will fail to resolve `IGraphService`. This is not called out explicitly in FR-2 or FR-4 of the spec.

**FR-8 scope:** The spec mentions "Update unit tests for the DI registration" but does not identify the specific tests that will break. Two tests in `GraphServiceTests.cs` directly exercise `AddUserManagement()` and assert on `IGraphService` resolution — these must be updated to compose `AddMicrosoft365Adapter()` alongside `AddUserManagement()`. The `MockGraphServiceTests.cs` and `ParseMembersFromJsonTests.cs` tests instantiate types directly and will not need changes other than verifying `InternalsVisibleTo` is in place.

## Prerequisites

No migrations, no infrastructure provisioning, no configuration changes. The `Adapters.Microsoft365` project already references `Application` and already carries the required NuGet packages. The test project already references `Adapters.Microsoft365`. This refactor can start immediately.
