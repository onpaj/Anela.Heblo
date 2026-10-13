# Design: Decouple OrgChart Adapter Deserialization from Application Contracts

## Component Design

### `Anela.Heblo.Adapters.OrgChart.Models.OrgChartJsonModel` (new file: `Models/OrgChartJsonModel.cs`)
Adapter-internal, `internal sealed class` POCOs that mirror the external JSON document exactly. No base type, no dependency on `Anela.Heblo.Application.Shared.BaseResponse` or any Application contract. Referenced only from `OrgChartService`.

- `OrgChartJsonModel` — root document wrapper.
- `OrgChartJsonOrganization` — organization name and its positions.
- `OrgChartJsonPosition` — one org position, its metadata, and its employees.
- `OrgChartJsonEmployee` — one employee assigned to a position.

Responsibility: represent "what the external JSON actually contains," nothing more. Deserialization target only — never constructed by hand, never asserted against outside this adapter.

### `OrgChartService` (modified: `OrgChartService.cs`)
Existing Adapters-layer implementation of `IOrgChartService`. Responsibilities after the change:

1. Fetch the HTTP response body from `OrgChartOptions.DataSourceUrl` (unchanged).
2. Deserialize the body into `OrgChartJsonModel` (changed from `OrgChartResponse`).
3. Null-check the deserialized model; throw `InvalidOperationException("Failed to deserialize organizational structure")` (no inner exception) if null.
4. Map the `OrgChartJsonModel` graph into a newly constructed `OrgChartResponse` via three new `private static` methods:
   - `MapOrganization(OrgChartJsonOrganization?) : OrganizationDto`
   - `MapPosition(OrgChartJsonPosition) : PositionDto`
   - `MapEmployee(OrgChartJsonEmployee) : EmployeeDto`
   Position and employee lists are mapped via `.Select(...).ToList()`.
5. Return the mapped `OrgChartResponse` (default-constructed, so `Success = true`, `ErrorCode = null`, `Params = null`).
6. Keep the existing `LogInformation` call reading from the mapped `OrgChartResponse` (not the intermediate JSON model).
7. Keep the existing `try`/`catch` structure unchanged: `HttpRequestException` and `JsonException` wrapped as today; all other exceptions propagate; no `LogError` call is added.

No change to the public interface `IOrgChartService.GetOrganizationStructureAsync(CancellationToken) : Task<OrgChartResponse>`, to `OrgChartModule`, or to DI registration.

### Consumers (unchanged)
`GetOrganizationStructureHandler` continues to call `IOrgChartService.GetOrganizationStructureAsync` and receive `OrgChartResponse` exactly as today — no changes required or permitted in this component.

## Data Schemas

### New adapter-internal model (input to deserialization; not persisted, not exposed via any API)

```csharp
internal sealed class OrgChartJsonModel
{
    public OrgChartJsonOrganization? Organization { get; set; }
}

internal sealed class OrgChartJsonOrganization
{
    public string? Name { get; set; }
    public List<OrgChartJsonPosition>? Positions { get; set; }
}

internal sealed class OrgChartJsonPosition
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Level { get; set; }
    public string? ParentPositionId { get; set; }
    public string? Department { get; set; }
    public List<OrgChartJsonEmployee>? Employees { get; set; }
    public string? Url { get; set; }
}

internal sealed class OrgChartJsonEmployee
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? StartDate { get; set; }
    public bool IsPrimary { get; set; }
    public string? Url { get; set; }
}
```

### Mapping target (existing, unchanged Application contracts — `backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/`)

`OrgChartResponse : BaseResponse` → `Organization: OrganizationDto` → `Positions: List<PositionDto>` → `Employees: List<EmployeeDto>`.

### Field-level mapping rules

| Source (`OrgChartJson*`) | Target (`*Dto`) | Rule |
|---|---|---|
| `Organization.Name` (`string?`) | `OrganizationDto.Name` | `?? string.Empty` |
| `Organization.Positions` (`List<...>?`) | `OrganizationDto.Positions` | `?.Select(MapPosition).ToList() ?? new()` |
| `Position.Id` (`string?`) | `PositionDto.Id` | `?? string.Empty` |
| `Position.Title` (`string?`) | `PositionDto.Title` | `?? string.Empty` |
| `Position.Description` (`string?`) | `PositionDto.Description` | `?? string.Empty` |
| `Position.Level` (`int?`) | `PositionDto.Level` | pass through as-is, no coalescing |
| `Position.ParentPositionId` (`string?`) | `PositionDto.ParentPositionId` | `?? string.Empty` |
| `Position.Department` (`string?`) | `PositionDto.Department` | `?? string.Empty` |
| `Position.Url` (`string?`) | `PositionDto.Url` | `?? string.Empty` |
| `Position.Employees` (`List<...>?`) | `PositionDto.Employees` | `?.Select(MapEmployee).ToList() ?? new()` |
| `Employee.Id` (`string?`) | `EmployeeDto.Id` | `?? string.Empty` |
| `Employee.Name` (`string?`) | `EmployeeDto.Name` | `?? string.Empty` |
| `Employee.Email` (`string?`) | `EmployeeDto.Email` | `?? string.Empty` |
| `Employee.StartDate` (`string?`) | `EmployeeDto.StartDate` | `?? string.Empty` |
| `Employee.IsPrimary` (`bool`) | `EmployeeDto.IsPrimary` | pass through as-is, no coalescing |
| `Employee.Url` (`string?`) | `EmployeeDto.Url` | `?? string.Empty` |

`OrgChartResponse.Success`, `ErrorCode`, `Params` are never populated from the JSON model — they retain the parameterless-constructor defaults (`true`, `null`, `null`) on every successful call.

### Error payloads (unchanged shapes, unchanged trigger conditions)

- `HttpRequestException` during fetch → `InvalidOperationException("Failed to fetch organizational structure: {message}", ex)`.
- `JsonException` during `Deserialize<OrgChartJsonModel>` → `InvalidOperationException("Failed to parse organizational structure: {message}", ex)`.
- Deserialized `OrgChartJsonModel` is `null` → `InvalidOperationException("Failed to deserialize organizational structure")` (no inner exception).
- Any other exception type propagates unwrapped.
