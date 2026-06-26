# Specification: Relocate OrgChartService HTTP adapter to Infrastructure

## Summary
Move the concrete `OrgChartService` HTTP-client adapter from `Features/OrgChart/Services/` to `Features/OrgChart/Infrastructure/` and update its namespace accordingly. The `IOrgChartService` interface stays in `Services/` as the domain-facing contract. This is a structural refactor only — no behavior, no logic, no API contract changes.

## Background
`docs/architecture/filesystem.md` defines two distinct folders inside a feature module:

- `Features/{Feature}/Services/` — **Domain services and business logic**
- `Features/{Feature}/Infrastructure/` — **Feature infrastructure**

`backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` is a pure HTTP-client adapter: it issues `HttpClient.GetAsync` against `OrgChartOptions.DataSourceUrl`, deserializes JSON into `OrgChartResponse`, and logs/translates HTTP and JSON failures. It contains no domain rules, calculations, or policy. It is, by the project's own definition, feature infrastructure misfiled under the domain-services folder.

This violates the conventions documented in `docs/architecture/filesystem.md` and blurs the Clean-Architecture boundary the codebase otherwise maintains. Every other feature module in the application (e.g. `ExpeditionList`, `MeetingTasks`, `Invoices`, `Bank`, `Catalog`, `FileStorage`, `PackingMaterials`, `Leaflet`, `KnowledgeBase`, `DataQuality`) already has an `Infrastructure/` folder for exactly this kind of adapter — `OrgChart` is the outlier.

Filed by daily arch-review routine on 2026-05-19.

## Functional Requirements

### FR-1: Move OrgChartService.cs to the Infrastructure folder
The concrete class `OrgChartService` (the `IOrgChartService` implementation) must be relocated from the `Services/` folder to a new `Infrastructure/` folder inside the OrgChart feature module.

**Acceptance criteria:**
- File `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` no longer exists.
- File `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` exists.
- File contents are byte-identical to the original except for the `namespace` declaration (see FR-2).
- The `Features/OrgChart/Infrastructure/` directory exists and contains the moved file.
- The move is performed with `git mv` (or equivalent) so git history is preserved.

### FR-2: Update the concrete class's namespace
The relocated class's namespace must match its new physical folder.

**Acceptance criteria:**
- The `namespace` declaration in `Infrastructure/OrgChartService.cs` reads exactly:
  ```csharp
  namespace Anela.Heblo.Application.Features.OrgChart.Infrastructure;
  ```
- No other `namespace` declaration in the file.
- The `using Anela.Heblo.Application.Features.OrgChart.Contracts;` directive is preserved (the class still depends on `OrgChartResponse`).

### FR-3: Keep IOrgChartService in Services/
The `IOrgChartService` interface is the domain-facing contract consumed by `GetOrganizationStructureHandler`. It must remain where it is — in `Features/OrgChart/Services/` — and keep its current namespace `Anela.Heblo.Application.Features.OrgChart.Services`.

**Acceptance criteria:**
- File `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/IOrgChartService.cs` is unchanged (path, content, namespace).
- `GetOrganizationStructureHandler.cs` continues to compile without modifying its `using Anela.Heblo.Application.Features.OrgChart.Services;` directive.

### FR-4: Update OrgChartModule.cs to reference both namespaces
`OrgChartModule.cs` references both `IOrgChartService` (interface, in `Services`) and the concrete `OrgChartService` (class, now in `Infrastructure`) via `services.AddHttpClient<IOrgChartService, OrgChartService>();`. It currently has a single `using Anela.Heblo.Application.Features.OrgChart.Services;` directive that covers both. After the move, it needs both namespaces in scope.

**Acceptance criteria:**
- `OrgChartModule.cs` contains both:
  ```csharp
  using Anela.Heblo.Application.Features.OrgChart.Services;
  using Anela.Heblo.Application.Features.OrgChart.Infrastructure;
  ```
- The `AddHttpClient<IOrgChartService, OrgChartService>()` registration line is unchanged.
- No other code in `OrgChartModule.cs` is modified.

### FR-5: No other source files require changes
A repository-wide search confirms the only other references to `OrgChartService` / `OrgChart.Services` are:
- `Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` — references `IOrgChartService` (interface, unchanged).
- `Anela.Heblo.Application/ApplicationModule.cs` — calls `services.AddOrgChartServices(configuration)` (extension method, unchanged).

**Acceptance criteria:**
- `GetOrganizationStructureHandler.cs` is not modified.
- `ApplicationModule.cs` is not modified.
- No other `.cs` file in the repository is modified.

### FR-6: Build and behavior must remain green
The refactor must not break the build, change runtime behavior, or alter the public API.

**Acceptance criteria:**
- `dotnet build` succeeds with zero new errors or warnings attributable to this change.
- `dotnet format` reports no formatting drift on the moved file.
- The `GET /api/orgchart/structure` endpoint (or whichever route `GetOrganizationStructureHandler` powers) returns the same payload before and after the change, given the same upstream response.
- DI resolution of `IOrgChartService` continues to yield a configured `HttpClient`-backed `OrgChartService` instance.
- Any existing tests touching `OrgChart*` continue to pass (currently none — see Dependencies).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. This is a compile-time relocation; runtime behavior is identical.

### NFR-2: Security
No security impact. No new code paths, no new I/O, no new configuration.

### NFR-3: Maintainability
After the change, the OrgChart feature must match the layout convention used by all other feature modules (e.g. `Catalog`, `Invoices`, `MeetingTasks`), with `Services/` reserved for domain logic and `Infrastructure/` reserved for adapters. This makes future static-analysis or architecture-test rules (e.g. NetArchTest) able to enforce the boundary uniformly.

### NFR-4: Git history
The move must preserve `OrgChartService.cs`'s history — use `git mv` so `git log --follow` still works on the relocated file.

## Data Model
No changes. `OrgChartResponse`, `OrganizationDto`, `PositionDto`, `EmployeeDto` (in `Features/OrgChart/Contracts/`) and `OrgChartOptions` are untouched.

## API / Interface Design

### Internal interface (unchanged)
```csharp
// Features/OrgChart/Services/IOrgChartService.cs  (stays here)
namespace Anela.Heblo.Application.Features.OrgChart.Services;

public interface IOrgChartService
{
    Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default);
}
```

### Concrete adapter (relocated)
```csharp
// Features/OrgChart/Infrastructure/OrgChartService.cs  (moved here, namespace updated)
namespace Anela.Heblo.Application.Features.OrgChart.Infrastructure;

public class OrgChartService : IOrgChartService
{
    // body unchanged
}
```

### Module registration (one using directive added)
```csharp
// Features/OrgChart/OrgChartModule.cs
using Anela.Heblo.Application.Features.OrgChart.Services;        // existing
using Anela.Heblo.Application.Features.OrgChart.Infrastructure;  // added
// ...
services.AddHttpClient<IOrgChartService, OrgChartService>();      // unchanged
```

### Public HTTP API
No change. The route, request shape, response shape, and status codes exposed by `GetOrganizationStructureHandler` are not affected.

## Dependencies
- **No external service dependencies.** Pure source-tree refactor.
- **No NuGet changes.** No package added, removed, or upgraded.
- **No database changes.**
- **No existing tests reference `OrgChartService` or `IOrgChartService` directly.** Repository-wide grep found zero test files referencing these symbols. The change therefore needs no test updates, but should be verified with the full backend test suite (`dotnet test`) to confirm no indirect breakage.
- Affected source files (3 total):
  1. `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` — deleted (moved).
  2. `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` — created (moved-to target).
  3. `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` — one `using` directive added.

## Out of Scope
- Renaming `OrgChartService` (the class name stays — only its folder/namespace changes).
- Renaming or relocating `IOrgChartService`.
- Refactoring `OrgChartService` internals (logging, error handling, retry/timeout/circuit-breaker policies, JSON options).
- Adding unit or integration tests for `OrgChartService` (a separate concern; this spec is scoped to the relocation only).
- Introducing an architecture-test (e.g. NetArchTest, ArchUnitNET) to enforce the Services-vs-Infrastructure boundary across all features.
- Auditing or relocating any other potentially-misplaced files in other feature modules.
- Frontend changes (none required).
- Documentation updates beyond what is required by the change itself (none — `docs/architecture/filesystem.md` already documents the correct convention).

## Open Questions
None.

## Status: COMPLETE
