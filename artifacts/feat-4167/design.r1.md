# Design: Relocate Department and IDepartmentClient to the Analytics Domain Module

## Component Design

### `Anela.Heblo.Domain.Features.Analytics.Department` (new location)
A plain data-holder class (unchanged shape), relocated from `Features.InvoiceClassification`:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```
Responsibility: represents a department as returned by the Flexi accounting system. No behavior, no dependencies.

### `Anela.Heblo.Domain.Features.Analytics.IDepartmentClient` (new location)
Contract for fetching departments, relocated from `Features.InvoiceClassification`, referencing the co-located `Department`:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}
```
Responsibility: unchanged — the Domain-owned contract that the Flexi adapter implements. Only its namespace changes.

### `Anela.Heblo.Adapters.Flexi.Accounting.Departments.FlexiDepartmentClient` (unchanged responsibility, updated reference)
Still implements the Domain `IDepartmentClient`, still wraps `Rem.FlexiBeeSDK`'s own client with an in-memory cache. Only its base-interface reference moves from `Domain.Features.InvoiceClassification.IDepartmentClient` to `Domain.Features.Analytics.IDepartmentClient`. Its own local alias `using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;` for the injected SDK client is unchanged and untouched.

### `Anela.Heblo.Adapters.Flexi.Accounting.Departments.FlexiDepartmentQueryService` (unchanged responsibility, updated reference)
Still implements `IDepartmentQueryService` (Application/UserManagement — out of scope, untouched) by mapping `Department` → `DepartmentDto`. Its `using` for the Domain types moves to `Anela.Heblo.Domain.Features.Analytics`.

### `FlexiAdapterServiceCollectionExtensions` (unchanged responsibility, updated reference)
DI registration line `services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();` is unchanged in behavior; its `using Anela.Heblo.Domain.Features.InvoiceClassification;` becomes `using Anela.Heblo.Domain.Features.Analytics;`.

### Explicitly out of scope / unchanged component
`Anela.Heblo.Adapters.Flexi.Analytics.DepartmentSyncService` and its test keep using `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient` and `Anela.Heblo.Persistence.Analytics.Entities.Department` — distinct types with the same short names that are unrelated to this move. No code or reference in these two files changes.

## Data Schemas
No schema changes of any kind — no database entities, no EF migrations, no HTTP request/response DTOs, and no event payloads are affected. `Department` (Domain) and `DepartmentDto` (Application/UserManagement contract) keep their existing shapes; only the Domain `Department`/`IDepartmentClient` C# namespace changes.
