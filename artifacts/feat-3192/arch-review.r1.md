# Architecture Review: Move OrgChartService to Dedicated Adapter Project

## Skip Design: false

## Architectural Fit Assessment

This is a pure structural refactor with no functional change. The violation is well-understood and well-scoped: `OrgChartService` is a concrete `HttpClient`-based class that was placed inside `Anela.Heblo.Application/Features/OrgChart/Infrastructure/` — a directory that does not exist as a pattern in any other feature module in the Application project. Every other I/O-bound service in the codebase already lives under `backend/src/Adapters/`, each in its own project with its own `ServiceCollectionExtensions` class. The fix is mechanical: create a new adapter project, move one file, create one extensions class, update three project files (new `.csproj`, `API.csproj`, `Anela.Heblo.sln`), and trim one registration call from `OrgChartModule`.

Verified against the codebase:

- `OrgChartService.cs` uses `HttpClient` directly (constructor-injected typed client). The class namespace is `Anela.Heblo.Application.Features.OrgChart.Infrastructure`.
- `OrgChartModule.cs` registers both `AddOptions<OrgChartOptions>()` and `AddHttpClient<IOrgChartService, OrgChartService>()`. After this change only the options registration remains.
- `Anela.Heblo.Application.csproj` does **not** list `Microsoft.Extensions.Http` as an explicit dependency; `HttpClient` compiles today only through transitive resolution. NFR-3 (removing the direct dependency) is satisfied by moving the concrete class — no package removal is needed in the Application csproj because no explicit reference exists.
- The solution file registers all adapter projects under the `Adapters` solution folder (GUID `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`) with a `NestedProjects` entry. The new project must follow this same registration pattern.
- `Program.cs` has a clearly delimited Adapters section (lines 104–119). `AddOrgChartAdapter` belongs immediately after `AddMicrosoft365Adapter` at line 119 to maintain alphabetical-ish grouping.
- The minimal csproj pattern for a simple typed-HttpClient adapter (no Polly, no domain-specific libraries) is best matched by `Anela.Heblo.Adapters.Cups` — two NuGet packages (`Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`) plus a project reference to `Anela.Heblo.Application`.

The spec is correct and complete. No amendments are required.

---

## Proposed Architecture

### Component Overview

```
backend/src/
  Adapters/
    Anela.Heblo.Adapters.OrgChart/               ← NEW project
      Anela.Heblo.Adapters.OrgChart.csproj       ← NEW
      OrgChartService.cs                         ← MOVED from Application
      OrgChartAdapterServiceCollectionExtensions.cs  ← NEW

  Anela.Heblo.Application/
    Features/OrgChart/
      Infrastructure/                            ← DELETED (empty after move)
      OrgChartModule.cs                          ← MODIFIED (remove AddHttpClient call)
      OrgChartOptions.cs                         ← UNCHANGED
      Services/IOrgChartService.cs               ← UNCHANGED
      Contracts/                                 ← UNCHANGED
      UseCases/                                  ← UNCHANGED

  Anela.Heblo.API/
    Anela.Heblo.API.csproj                       ← MODIFIED (add project reference)
    Program.cs                                   ← MODIFIED (add using + AddOrgChartAdapter call)

Anela.Heblo.sln                                  ← MODIFIED (register new project)
```

### Key Design Decisions

#### Decision 1: No `OrgChartOptions` in the Adapter Project

**Options considered:**
- Move `OrgChartOptions` into the adapter project alongside `OrgChartService`.
- Keep `OrgChartOptions` in Application; let the adapter read it via `IOptions<OrgChartOptions>` through the project reference.

**Chosen approach:** Keep `OrgChartOptions` in Application.

**Rationale:** `OrgChartOptions` contains only the `DataSourceUrl` config key — it is a pure configuration shape with a `[Required]` data annotation. The options class carries no `HttpClient` or infrastructure dependency. Keeping it in Application means `GetOrganizationStructureHandler` (which also reads options indirectly via the service) stays co-located with all other handler-level config. More importantly, this is consistent with how `OrgChartOptions.SectionName` is referenced in `OrgChartModule.AddOrgChartServices` — options registration and options class stay together. Every other adapter that owns its own options class (Comgate's `ComgateSettings`, Cups' `CupsOptions`, Anthropic's `AnthropicOptions`) does so because those options are consumed exclusively in the adapter. `OrgChartOptions` is read by `OrgChartService` through the `IOptions<T>` abstraction, which is resolved at runtime across the project boundary; the Application layer never touches it directly at runtime.

#### Decision 2: Typed `HttpClient` Registration (`AddHttpClient<IOrgChartService, OrgChartService>()`)

**Options considered:**
- Named `HttpClient` via `IHttpClientFactory` (as used by Anthropic and Cups).
- Typed `HttpClient` where DI injects `HttpClient` directly into the service constructor.

**Chosen approach:** Keep the typed `HttpClient` registration — `services.AddHttpClient<IOrgChartService, OrgChartService>()` — exactly as it exists today in `OrgChartModule`.

**Rationale:** This is the existing registration; changing it would be scope creep and would alter the `HttpClient` lifecycle (typed clients are scoped, named clients are transient from factory). The spec explicitly prohibits behaviour changes. The typed pattern is also appropriate here: `OrgChartService` is the only consumer of this `HttpClient`, and typed clients provide the cleanest injection surface for single-consumer cases.

#### Decision 3: No Polly Packages in the New Project

**Options considered:**
- Include `Polly` and `Polly.Extensions` in the new csproj to match Comgate and Anthropic adapters.
- Omit Polly; add it only if resilience is introduced in a follow-up.

**Chosen approach:** Omit Polly.

**Rationale:** The spec explicitly excludes resilience policies. Adding Polly now would be dead weight — unused package references increase build surface. The Cups adapter (the best structural match for this new project) omits Polly. When resilience is added in a follow-up, the package can be added at that time.

#### Decision 4: Solution File Registration

**Options considered:**
- Rely on directory-based project discovery (if the repo uses that mechanism).
- Add the project explicitly to `Anela.Heblo.sln`.

**Chosen approach:** Add the project explicitly to `Anela.Heblo.sln` under the existing `Adapters` solution folder.

**Rationale:** Inspection of `Anela.Heblo.sln` confirms the repo uses explicit solution-file registration, not directory-based discovery. All 13 existing adapter projects are listed with individual `Project(...)` entries and `NestedProjects` mappings. A new project that is not in the solution file will not appear in IDE solution views and will not be built by `dotnet build Anela.Heblo.sln`. The new project must be added to the `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}` Adapters folder in the solution.

---

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/
  Anela.Heblo.Adapters.OrgChart.csproj
  OrgChartService.cs
  OrgChartAdapterServiceCollectionExtensions.cs
```

No subdirectories. The adapter has one service class and one registration class — flat layout matches Comgate and SendGrid adapters at this scale.

### Interfaces and Contracts

Nothing changes externally. For reference, the unchanged contracts:

- **`IOrgChartService`** — `Anela.Heblo.Application.Features.OrgChart.Services` — single method `Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken)`.
- **`OrgChartOptions`** — `Anela.Heblo.Application.Features.OrgChart` — one property: `string DataSourceUrl`.
- **`OrgChartResponse` / `OrganizationDto` / `PositionDto` / `EmployeeDto`** — `Anela.Heblo.Application.Features.OrgChart.Contracts` — unchanged.

The new extension method signature:

```csharp
// Anela.Heblo.Adapters.OrgChart.OrgChartAdapterServiceCollectionExtensions
public static IServiceCollection AddOrgChartAdapter(
    this IServiceCollection services,
    IConfiguration configuration)
```

The `IConfiguration` parameter is required by the spec and matches all peer adapters. For this particular adapter the `configuration` parameter is not read inside `AddOrgChartAdapter` itself (options binding lives in `OrgChartModule`), but it is included for consistency and future use (e.g., if a base URL is ever configured on the `HttpClient` directly).

### Data Flow

No change to runtime data flow. For clarity:

```
HTTP request → OrgChartController
  → GetOrganizationStructureRequest (MediatR)
    → GetOrganizationStructureHandler (Application)
      → IOrgChartService.GetOrganizationStructureAsync()
        → OrgChartService (Adapters.OrgChart) — resolved by DI
          → HttpClient (typed, registered by AddOrgChartAdapter)
            → external JSON data source (DataSourceUrl from OrgChartOptions)
```

The only change is which assembly `OrgChartService` is compiled into. DI resolution is identical.

### Step-by-Step Implementation Order

Execute in this order to keep the solution buildable at each step:

1. **Create** `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Anela.Heblo.Adapters.OrgChart.csproj`
   - `TargetFramework=net8.0`, `Nullable=enable`, `ImplicitUsings=enable`, `RootNamespace=Anela.Heblo.Adapters.OrgChart`
   - NuGet: `Microsoft.Extensions.Http 8.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 8.0.0`
   - Project reference: `..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj`

2. **Move** `OrgChartService.cs` to `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`
   - Update namespace: `Anela.Heblo.Adapters.OrgChart`
   - All `using` statements remain valid through the project reference

3. **Create** `OrgChartAdapterServiceCollectionExtensions.cs` in the new project
   - Single method: `AddOrgChartAdapter(this IServiceCollection services, IConfiguration configuration)`
   - Body: `services.AddHttpClient<IOrgChartService, OrgChartService>(); return services;`
   - Usings: `Anela.Heblo.Application.Features.OrgChart.Services`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`

4. **Modify** `OrgChartModule.cs` in Application
   - Remove: `using Anela.Heblo.Application.Features.OrgChart.Infrastructure;`
   - Remove: `services.AddHttpClient<IOrgChartService, OrgChartService>();`
   - Remove: `using Microsoft.Extensions.DependencyInjection;` only if it is no longer needed (check — `IServiceCollection` extension methods are still there, so it stays)
   - The options registration block is unchanged

5. **Delete** `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/` directory (now empty)

6. **Modify** `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
   - Add: `<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.OrgChart\Anela.Heblo.Adapters.OrgChart.csproj" />`

7. **Modify** `backend/src/Anela.Heblo.API/Program.cs`
   - Add using: `using Anela.Heblo.Adapters.OrgChart;`
   - Add after line 119 (`AddMicrosoft365Adapter`): `builder.Services.AddOrgChartAdapter(builder.Configuration);`

8. **Modify** `Anela.Heblo.sln`
   - Add a `Project(...)` block for `Anela.Heblo.Adapters.OrgChart` (generate a fresh GUID)
   - Add configuration platforms entries for all 6 build configurations
   - Add a `NestedProjects` entry mapping the new project GUID to the Adapters folder GUID `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`

9. **Verify**: `dotnet build` at solution root → 0 errors, 0 new warnings. `dotnet format` → no changes.

---

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Forgetting to delete the `Infrastructure/` subdirectory, leaving a dangling empty folder | Low | Step 5 explicitly covers deletion. Verify with `find` after the move. |
| Compiler resolving `HttpClient` transitively in Application even after the move, masking any residual reference | Low | After the move, `grep -r "HttpClient" backend/src/Anela.Heblo.Application/` should return zero hits (excluding test files). |
| Solution file GUID collision if generated non-randomly | Low | Use `dotnet new classlib` to create the project so the SDK auto-generates a safe GUID, then move the file to the correct path, or use `Guid.NewGuid()` manually. Do not reuse any existing GUID from the solution file. |
| `OrgChartModule.cs` still compiles after removing `AddHttpClient` because it no longer references `IOrgChartService` or `OrgChartService` directly at the type level | None (positive) | Confirm the `using Anela.Heblo.Application.Features.OrgChart.Infrastructure;` is removed and no CS8019 unused-using warning surfaces with Nullable enabled. |
| `AddOrgChartAdapter` parameter `IConfiguration configuration` is unused, triggering a lint warning | Low | Add `// reserved for future base-URL configuration` comment, or suppress with `_ = configuration;` if the linter flags it. Check peer adapters — Comgate and Cups both use their `configuration` parameter, so this is the one structural deviation. Simplest fix: omit the parameter and match the call site to a no-arg overload, but that breaks consistency with every other adapter signature. Keep the parameter; document it. |

---

## Specification Amendments

None. The spec is accurate and complete. One observation for the implementer that is not in the spec:

The `IConfiguration configuration` parameter in `AddOrgChartAdapter` will be accepted but unused in the initial implementation (options binding stays in `OrgChartModule`). This is intentional per FR-3 and FR-4. The parameter is kept for signature consistency with all other adapter extension methods and to avoid a breaking change if base-URL configuration is added to the `HttpClient` registration in a future change.

---

## Prerequisites

None. This is a self-contained structural refactor with no external dependencies, no database changes, no frontend changes, and no configuration changes. The task can be executed immediately on a clean branch from `main`.
