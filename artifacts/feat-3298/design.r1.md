# Design: Move GraphService to Adapters.Microsoft365

## Component Design

### GraphService (moved)

**Current location:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`
**New location:** `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

Implements `IGraphService` (which remains in the Application layer unchanged). No behavioral changes — move only.

### MockGraphService (moved)

**Current location:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`
**New location:** `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`

Implements `IGraphService`. No behavioral changes — move only.

### Microsoft365AdapterServiceCollectionExtensions (updated)

**File:** `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`

`AddMicrosoft365Adapter()` gains an `else` branch mirroring the pattern used for `PhotobankGraphService`:

```
if (!isMockEnabled)
{
    // existing: register real GraphService, AddHttpClient("MicrosoftGraph")
    services.AddScoped<IGraphService, GraphService>();
    services.AddHttpClient("MicrosoftGraph", ...);
    // existing PhotobankGraphService registrations remain unchanged
}
else
{
    services.AddScoped<IGraphService, MockGraphService>();
    // existing mock PhotobankGraphService registration remains unchanged
}
```

The named HTTP client `"MicrosoftGraph"` registration is consolidated here; the duplicate call in `UserManagementModule` is removed.

### UserManagementModule (updated)

**File:** `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

- Remove `using Microsoft.Graph;` (line 12)
- Remove `services.AddScoped<IGraphService, GraphService>()` / `services.AddScoped<IGraphService, MockGraphService>()` registrations
- Remove `services.AddHttpClient("MicrosoftGraph", ...)` call from the else-branch
- Module retains only Application-layer concerns (MediatR handlers, validators, etc.)

### Anela.Heblo.Application.csproj (updated)

Remove NuGet references:
- `Microsoft.Identity.Web`
- `Microsoft.Graph`

These are adapter-only concerns and must not appear in the Application project.

### Adapters.Microsoft365.csproj (updated)

Add InternalsVisibleTo so that `ParseMembersFromJson` (internal static) remains accessible from the test project:

```xml
<InternalsVisibleTo Include="Anela.Heblo.Tests" />
```

### GraphServiceTests.cs (updated)

**File:** `backend/tests/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

The two DI-wiring tests that currently call `AddUserManagement()` to resolve `IGraphService` must be updated to call `AddMicrosoft365Adapter()` instead (or alongside, if the test host requires both). `Anela.Heblo.Tests.csproj` already has a `ProjectReference` to `Adapters.Microsoft365`, so no project file change is needed here.

## Data Schemas

No data model changes. No API surface changes. `IGraphService` contract is unchanged.

### Final file layout

```
backend/src/
  Anela.Heblo.Application/
    Features/UserManagement/
      Services/
        IGraphService.cs          ← unchanged
        GraphService.cs           ← DELETED
        MockGraphService.cs       ← DELETED
  Adapters/Anela.Heblo.Adapters.Microsoft365/
    UserManagement/               ← new subdirectory
      GraphService.cs             ← moved here
      MockGraphService.cs         ← moved here
    Microsoft365AdapterServiceCollectionExtensions.cs  ← updated
```
