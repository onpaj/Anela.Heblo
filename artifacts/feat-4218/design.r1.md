# Design: Move `IDepartmentClient`/`Department` from Analytics to UserManagement Domain

## Component Design

This is a same-behavior namespace relocation; no new component is introduced and no consumer's public contract changes. The affected components:

- **`Anela.Heblo.Domain.Features.UserManagement`** (new namespace, folder `backend/src/Anela.Heblo.Domain/Features/UserManagement/`) — becomes the owner of the `Department` domain model and the `IDepartmentClient` domain interface. Responsibility: define the department-lookup abstraction and its data shape for the UserManagement feature, mirroring the existing `Application.Features.UserManagement` namespace the same way every other module pairs `Domain.Features.X` with `Application.Features.X`. Previously this content lived under `Domain.Features.Analytics`, which had no real consumer of it.
- **`FlexiDepartmentClient`** (`Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs`) — implementation of `IDepartmentClient` backed by the FlexiBee SDK, with an in-memory cache (10 min TTL, unchanged). Its `using` and base-interface reference move from `Domain.Features.Analytics.IDepartmentClient` to `Domain.Features.UserManagement.IDepartmentClient`. The pre-existing alias `using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;` (needed to disambiguate the FlexiBee SDK's own `IDepartmentClient`) is untouched.
- **`FlexiDepartmentQueryService`** (`Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`) — implements `Application.Features.UserManagement.Services.IDepartmentQueryService`, consumes `IDepartmentClient`, maps `Department` → `DepartmentDto`. Only its `using` statement changes; mapping logic is unchanged.
- **`FlexiAdapterServiceCollectionExtensions`** — DI registration (`services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();`) resolves through the updated `using`; the registration line itself is unchanged.
- **`DepartmentSyncService`** and **`Persistence.Analytics.Entities.Department`** — explicitly out of scope; these depend on the FlexiBee SDK's own `IDepartmentClient` and a distinct EF-mapped entity, and are unaffected by this move.

Data flow (unchanged in shape, namespace-corrected in the middle):

```
GetDepartmentsHandler (Application/UserManagement)
   -> IDepartmentQueryService (Application/UserManagement/Services)
   -> FlexiDepartmentQueryService (Adapters.Flexi)
   -> IDepartmentClient  [Domain.Features.UserManagement, moved]
   -> FlexiDepartmentClient (Adapters.Flexi)
   -> FlexiBee SDK's own IDepartmentClient
```

## Data Schemas

No persisted data model, database schema, or external API shape changes. The only "schema" affected is the in-memory domain type's namespace location.

**Before:**
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}
```

**After** (identical shape, new namespace and file location):
```csharp
// backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs
namespace Anela.Heblo.Domain.Features.UserManagement;

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

```csharp
// backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs
namespace Anela.Heblo.Domain.Features.UserManagement;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}
```

Unaffected, distinct type (not renamed, not moved, kept for disambiguation): `Anela.Heblo.Persistence.Analytics.Entities.Department` (EF entity with `FlexiId`, `Code`, `LastModified`, `RawPayload`, etc.), owned by `DepartmentSyncService`.

No controller routes, MediatR contracts, or DTOs (`DepartmentDto`, `IDepartmentQueryService`) change shape or namespace as part of this move.
