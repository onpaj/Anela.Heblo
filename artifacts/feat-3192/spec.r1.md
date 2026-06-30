# Specification: Move OrgChartService to a Dedicated Adapter Project

## Summary

`OrgChartService`, a concrete `HttpClient`-based implementation, currently resides in `Anela.Heblo.Application/Features/OrgChart/Infrastructure/`, violating the Clean Architecture rule that the Application layer must not carry infrastructure dependencies. This task extracts it into a new `Anela.Heblo.Adapters.OrgChart` project, following the identical pattern already established by every other I/O adapter in the codebase (Comgate, Cups, Anthropic, HomeAssistant, etc.). The change is a pure structural refactor — no runtime behaviour changes.

## Background

The `Anela.Heblo.Application` project is the Application layer: it owns MediatR handlers, domain interfaces, and DTOs. All thirteen existing I/O adapters live under `backend/src/Adapters/` and reference `Anela.Heblo.Application` unidirectionally. `OrgChartService` was placed in `Features/OrgChart/Infrastructure/` (still within the Application project), making `Anela.Heblo.Application.csproj` depend on `Microsoft.Extensions.Http` at runtime and pulling network I/O into a layer that should be infrastructure-free.

The consequence is two-fold: the Application project cannot be compiled or tested in isolation without an `HttpClient` harness, and the pattern inconsistency increases cognitive overhead when navigating the codebase. The fix restores consistency and satisfies the documented architecture rule in `docs/architecture/filesystem.md`.

## Functional Requirements

### FR-1: Create the `Anela.Heblo.Adapters.OrgChart` project

Create a new class library project at `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/` mirroring the structure of peer adapter projects.

**Acceptance criteria:**
- `Anela.Heblo.Adapters.OrgChart.csproj` exists with `TargetFramework=net8.0`, `Nullable=enable`, `ImplicitUsings=enable`, and `RootNamespace=Anela.Heblo.Adapters.OrgChart`.
- The project references `Anela.Heblo.Application.csproj` (same as Comgate, Anthropic, etc.).
- NuGet packages include `Microsoft.Extensions.Http` and `Microsoft.Extensions.Options.ConfigurationExtensions` (minimum required; add `Polly`/`Polly.Extensions` only if resilience is added — see FR-5).
- The project file is added to the solution (or to the existing directory-based project discovery, whichever mechanism the repo uses).

### FR-2: Move `OrgChartService.cs` into the new adapter project

Move `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` to `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`.

**Acceptance criteria:**
- The moved file updates its namespace from `Anela.Heblo.Application.Features.OrgChart.Infrastructure` to `Anela.Heblo.Adapters.OrgChart`.
- All existing `using` statements that reference types from `Anela.Heblo.Application` (contracts, services, options) remain valid via the project reference established in FR-1.
- The class implementation is unchanged: constructor signature, `GetOrganizationStructureAsync` logic, logging calls, and exception wrapping are identical.
- The `Infrastructure/` subdirectory inside the Application project is deleted (it will be empty after the move).

### FR-3: Move `HttpClient` registration from `OrgChartModule` to `OrgChartAdapterServiceCollectionExtensions`

Create `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs` that owns the `AddHttpClient<IOrgChartService, OrgChartService>()` call, following the pattern of `ComgateAdapterServiceCollectionExtensions` and `CupsAdapterServiceCollectionExtensions`.

**Acceptance criteria:**
- The extension method is named `AddOrgChartAdapter(this IServiceCollection services, IConfiguration configuration)`.
- It registers `services.AddHttpClient<IOrgChartService, OrgChartService>()` (typed HttpClient, same as the current registration in `OrgChartModule`).
- It does NOT register `OrgChartOptions` (that stays in Application — see FR-4).
- `OrgChartModule.cs` in Application is updated: the `services.AddHttpClient<IOrgChartService, OrgChartService>()` call is removed; options registration and the method signature remain.

### FR-4: Keep `OrgChartOptions` and `OrgChartModule` in Application

`OrgChartOptions` and `OrgChartModule.AddOrgChartServices` remain in `Anela.Heblo.Application`. `AddOrgChartServices` continues to register options only (`.AddOptions<OrgChartOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`). MediatR handler discovery is unaffected (scan-based).

**Acceptance criteria:**
- `OrgChartOptions.cs` namespace and location are unchanged.
- `OrgChartModule.cs` retains options registration and removes only the `AddHttpClient` call.
- `ApplicationModule.cs` continues to call `services.AddOrgChartServices(configuration)` without modification.

### FR-5: Wire the new adapter in `Program.cs`

Add `builder.Services.AddOrgChartAdapter(builder.Configuration)` in the Adapters section of `backend/src/Anela.Heblo.API/Program.cs`, alongside the other adapter registrations (after line 119, `AddMicrosoft365Adapter`).

**Acceptance criteria:**
- `using Anela.Heblo.Adapters.OrgChart;` is added to `Program.cs`.
- `builder.Services.AddOrgChartAdapter(builder.Configuration);` is present in the Adapters block.
- `Anela.Heblo.API.csproj` references `Anela.Heblo.Adapters.OrgChart.csproj`.

## Non-Functional Requirements

### NFR-1: Build integrity

The solution must compile cleanly after the refactor with no new warnings.

**Acceptance criteria:**
- `dotnet build` at solution or backend root produces 0 errors and 0 new warnings.
- `dotnet format` reports no formatting changes.

### NFR-2: No runtime behaviour change

The refactor is purely structural. The `HttpClient` lifecycle, option validation, and error handling must be identical before and after.

**Acceptance criteria:**
- The typed `HttpClient` is still registered as `AddHttpClient<IOrgChartService, OrgChartService>()` (same pooling, same scoping).
- `OrgChartOptions` validation-on-start still fires at application startup.
- The existing `GET /api/orgchart` endpoint returns the same response for the same upstream data.

### NFR-3: Testability

After this change, the Application project must be compilable and unit-testable without providing a real or stubbed `HttpClient` — only a mock `IOrgChartService` is needed.

**Acceptance criteria:**
- `Anela.Heblo.Application.csproj` no longer lists `Microsoft.Extensions.Http` as a direct dependency (it may remain as a transitive dep from other packages, but the explicit reference introduced by `OrgChartService` is removed).

### NFR-4: Consistency

The new adapter must follow the same file and naming conventions as existing adapters.

**Acceptance criteria:**
- Project name: `Anela.Heblo.Adapters.OrgChart`.
- Root namespace: `Anela.Heblo.Adapters.OrgChart`.
- Extension method class: `OrgChartAdapterServiceCollectionExtensions`.
- Extension method name: `AddOrgChartAdapter`.

## Data Model

No data model changes. All contracts (`OrgChartResponse`, `OrganizationDto`, `PositionDto`, `EmployeeDto`) remain in `Anela.Heblo.Application.Features.OrgChart.Contracts`. The interface `IOrgChartService` remains in `Anela.Heblo.Application.Features.OrgChart.Services`.

## API / Interface Design

No API surface changes. The existing `GET /api/orgchart` endpoint, `OrgChartController`, and `GetOrganizationStructureHandler` are untouched. The only interface-level change is the DI wiring: `IOrgChartService` is now implemented by a class in the adapter project rather than in the Application project, but the binding is identical.

## Dependencies

- **Existing**: `Anela.Heblo.Application` (project reference from the new adapter) — already the pattern for all other adapters.
- **NuGet**: `Microsoft.Extensions.Http` 8.0.0, `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0 — both already present in peer adapter projects.
- **No new external services or libraries** are introduced.

## Out of Scope

- Adding resilience policies (Polly retry, circuit-breaker) to the OrgChart HTTP client. This may be desirable in a follow-up but is explicitly excluded to keep this a pure structural refactor.
- Caching of the org chart response.
- Any changes to the frontend or E2E tests (no visible behaviour change).
- Moving `IOrgChartService` out of the Application layer (the interface belongs there by Clean Architecture convention).
- Changing the OrgChart configuration section name or key vault path.

## Open Questions

None.

## Status: COMPLETE
