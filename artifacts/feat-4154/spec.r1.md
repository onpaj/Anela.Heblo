# Specification: Decouple OrgChart Adapter Deserialization from Application Contracts

## Summary
`OrgChartService` (Adapters layer) currently deserializes the external org-chart JSON document directly into `OrgChartResponse`, an Application-layer response contract that inherits `Success`/`ErrorCode`/`Params` from `BaseResponse`. This spec defines an internal Adapters-layer model for the external JSON shape and an explicit mapping step from that model to the existing `OrgChartResponse`/`OrganizationDto`/`PositionDto`/`EmployeeDto` contracts, removing the implicit coupling between the external data format and the Application API contract. This is an internal refactor with no change to the public `IOrgChartService` contract, the external data format, or observable behavior.

## Background
`OrgChartService.GetOrganizationStructureAsync` fetches JSON from a configurable URL (`OrgChart:DataSourceUrl`) and calls `JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions)`. `OrgChartResponse : BaseResponse` was designed as an Application-layer API response DTO, not an external I/O model. Because the external JSON has no `Success`/`ErrorCode`/`Params` fields, those inherited members silently take on whatever `BaseResponse`'s parameterless constructor sets (`Success = true`, `ErrorCode = null`, `Params = null`), regardless of what the external source actually returned. This also means any future change to `BaseResponse`'s construction semantics, or any renaming of `OrgChartResponse`/`OrganizationDto` members, would silently change how the external JSON is parsed — with no compiler or test signal — violating the Clean Architecture separation between the Adapters layer (external I/O) and the Application layer (internal contracts).

The fix isolates the external JSON shape behind adapter-internal model classes and performs an explicit, reviewable mapping into the existing Application contracts. No consumer of `IOrgChartService` (currently `GetOrganizationStructureHandler`) is affected.

## Functional Requirements

### FR-1: Introduce Adapters-layer external data models
Add a set of `internal` POCO classes in the `Anela.Heblo.Adapters.OrgChart` project (suggested location: `Models/OrgChartJsonModel.cs`, or one file per class) that mirror the external JSON shape exactly and independently of the Application contracts:

- `OrgChartJsonModel` — `Organization` (`OrgChartJsonOrganization?`)
- `OrgChartJsonOrganization` — `Name` (`string?`), `Positions` (`List<OrgChartJsonPosition>?`)
- `OrgChartJsonPosition` — `Id` (`string?`), `Title` (`string?`), `Description` (`string?`), `Level` (`int?`), `ParentPositionId` (`string?`), `Department` (`string?`), `Employees` (`List<OrgChartJsonEmployee>?`), `Url` (`string?`)
- `OrgChartJsonEmployee` — `Id` (`string?`), `Name` (`string?`), `Email` (`string?`), `StartDate` (`string?`), `IsPrimary` (`bool`), `Url` (`string?`)

These classes carry **no** members from `BaseResponse` (no `Success`, `ErrorCode`, `Params`) — they represent only what the external JSON document actually contains. `internal` visibility is sufficient: no other project needs `InternalsVisibleTo` access, since these types are consumed only within `OrgChartService` and are not asserted against directly by any existing or planned test (see FR-3).

**Acceptance criteria:**
- The Adapters project defines the four classes above (or an equivalent internal structure covering the same fields) as plain classes with `internal` (or `public`, if simpler) visibility, with no dependency on `Anela.Heblo.Application.Shared.BaseResponse`.
- These classes are not referenced from any Application-layer or Core-layer project.

### FR-2: Deserialize into the adapter model, then map explicitly to Application contracts
Change `OrgChartService.GetOrganizationStructureAsync` to:
1. Deserialize the HTTP response body into `OrgChartJsonModel` (using the existing `JsonOptions`, i.e. `PropertyNameCaseInsensitive = true`) instead of `OrgChartResponse`.
2. Apply the existing null-check guard to the deserialized `OrgChartJsonModel` (see FR-3 for exact behavior).
3. Explicitly construct a new `OrgChartResponse` (via its parameterless constructor, so `Success = true`) and populate `Organization` by mapping each field of `OrgChartJsonModel`/`OrgChartJsonOrganization`/`OrgChartJsonPosition`/`OrgChartJsonEmployee` to the corresponding field of `OrganizationDto`/`PositionDto`/`EmployeeDto`.
4. Missing/null string fields in the source model map to `string.Empty` (matching each target DTO's own default), missing lists map to an empty list, and nullable value types (`Level`) pass through as-is.

**Acceptance criteria:**
- `OrgChartResponse`, `OrganizationDto`, `PositionDto`, and `EmployeeDto` are never passed to `JsonSerializer.Deserialize` anywhere in the Adapters project.
- For a given valid external JSON payload, the resulting `OrgChartResponse.Organization` graph (name, positions, and nested employees, in order, with all fields) is identical to what the current implementation produces today for the same payload — this is a pure refactor of *how* the response is built, not a change to its content.
- `OrgChartResponse.Success` is `true` and `ErrorCode`/`Params` are `null` for every successful (non-exception) call, exactly as today.

### FR-3: Preserve existing error-handling and logging behavior exactly
The existing `try`/`catch` structure, exception wrapping, and the "no `LogError` from this service" behavior (verified by `OrgChartServiceTests.VerifyNoErrorLog`) must be unchanged:
- `HttpRequestException` → wrapped as `InvalidOperationException("Failed to fetch organizational structure: {message}", ex)`.
- `JsonException` (thrown by `JsonSerializer.Deserialize<OrgChartJsonModel>`) → wrapped as `InvalidOperationException("Failed to parse organizational structure: {message}", ex)`.
- A `null` deserialization result (e.g. JSON body `"null"`) → `InvalidOperationException("Failed to deserialize organizational structure")` with **no** inner exception (this check moves from testing `OrgChartResponse == null` to testing `OrgChartJsonModel == null`, with the identical message and no-inner-exception behavior).
- Any other exception type continues to propagate unwrapped.
- No `ILogger.LogError`/equivalent call is added anywhere in this service (informational logging via `LogInformation` is unaffected, and continues to reference `orgChart.Organization.Positions` post-mapping, i.e. the final `OrgChartResponse`, not the intermediate JSON model).

**Acceptance criteria:**
- All four existing tests in `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` pass unmodified against the refactored implementation.
- `GetOrganizationStructureHandlerTests.cs` and `OrgChartModuleValidationTests.cs` pass unmodified (no change to `IOrgChartService`, `OrgChartModule`, or DI registration).

## Non-Functional Requirements

### NFR-1: Performance
The mapping step is an in-memory, single-pass transformation over the same data already held in memory after deserialization (typically tens to low hundreds of positions/employees for an org chart). No measurable performance regression versus the current single-deserialization approach is expected or required to be benchmarked; no explicit performance target beyond "no observable regression" applies.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. The external data source URL (`OrgChart:DataSourceUrl`) and its trust boundary are unchanged. No new secrets or credentials are introduced. Per the project's Shoptet/secrets rules, this feature does not touch Key Vault or external API documentation, as the OrgChart data source is unrelated to Shoptet.

## Data Model
New adapter-internal-only types (not persisted, not exposed via any API surface):

```
OrgChartJsonModel
└── Organization: OrgChartJsonOrganization?
    ├── Name: string?
    └── Positions: List<OrgChartJsonPosition>?
        ├── Id: string?
        ├── Title: string?
        ├── Description: string?
        ├── Level: int?
        ├── ParentPositionId: string?
        ├── Department: string?
        ├── Url: string?
        └── Employees: List<OrgChartJsonEmployee>?
            ├── Id: string?
            ├── Name: string?
            ├── Email: string?
            ├── StartDate: string?
            ├── IsPrimary: bool
            └── Url: string?
```

Mapping target (unchanged, existing Application contracts): `OrgChartResponse` (`: BaseResponse`) → `OrganizationDto` → `List<PositionDto>` → `List<EmployeeDto>`, as already defined in `backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/`.

## API / Interface Design
No public interface changes. `IOrgChartService.GetOrganizationStructureAsync(CancellationToken)` keeps its existing signature and return type (`Task<OrgChartResponse>`). The change is entirely internal to `OrgChartService`'s implementation:

```
HTTP GET (OrgChartOptions.DataSourceUrl)
  → JSON body
  → JsonSerializer.Deserialize<OrgChartJsonModel>   (was: <OrgChartResponse>)
  → [null check → InvalidOperationException]
  → explicit mapping: OrgChartJsonModel → OrgChartResponse (new construction)
  → return OrgChartResponse
```

## Dependencies
- No new NuGet packages or external services.
- No change to `Anela.Heblo.Adapters.OrgChart.csproj` project references.
- Depends only on the existing `System.Text.Json`, `HttpClient`, `OrgChartOptions`, and the existing Application-layer contracts (`OrgChartResponse`, `OrganizationDto`, `PositionDto`, `EmployeeDto`) as mapping targets.

## Out of Scope
- Any change to the external JSON data source's actual schema or content.
- Any change to `IOrgChartService`, `OrgChartModule`, DI registration, or `GetOrganizationStructureHandler`.
- Any change to `BaseResponse`, `OrgChartResponse`, `OrganizationDto`, `PositionDto`, or `EmployeeDto` themselves — these Application contracts are left exactly as-is; only what is deserialized *into* changes.
- Adding schema validation (e.g. FluentValidation, JSON Schema) for the external payload beyond the existing null-check.
- Any change to logging levels, log message content/format (beyond the object graph it reads from, which yields identical values), or retry/resilience behavior for the HTTP call.
- Documenting this endpoint in `docs/integrations/shoptet-api.md` — the OrgChart data source is not a Shoptet endpoint, so that project rule does not apply here.

## Open Questions
None.

## Status: COMPLETE
