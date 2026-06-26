# Design: Move OrgChartService to a Dedicated Adapter Project

## Component Design

### New project: `Anela.Heblo.Adapters.OrgChart`

**Location:** `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/`

**Responsibility:** Owns the HTTP-based implementation of `IOrgChartService`. Isolates the `HttpClient` dependency from the Application layer, matching the pattern of all other adapter projects (Comgate, Cups, Anthropic, HomeAssistant).

**Project configuration:**
- Target framework: `net8.0`
- `Nullable`: `enable`
- `ImplicitUsings`: `enable`
- `RootNamespace`: `Anela.Heblo.Adapters.OrgChart`
- Project reference: `Anela.Heblo.Application`
- NuGet packages: `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`
- Registered in solution file alongside the other adapter projects

**Files:**

| File | Description |
|------|-------------|
| `Anela.Heblo.Adapters.OrgChart.csproj` | Project file with dependencies above |
| `OrgChartService.cs` | Moved verbatim from Application; namespace changed to `Anela.Heblo.Adapters.OrgChart` |
| `OrgChartAdapterServiceCollectionExtensions.cs` | New; provides `AddOrgChartAdapter` extension method |

---

### `OrgChartService.cs`

**Source:** `backend/src/Application/Features/OrgChart/Infrastructure/OrgChartService.cs`  
**Destination:** `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`

Only change: namespace declaration updated from whatever it currently is to `Anela.Heblo.Adapters.OrgChart`. Implementation body is untouched.

After the move, `backend/src/Application/Features/OrgChart/Infrastructure/` directory is deleted.

---

### `OrgChartAdapterServiceCollectionExtensions.cs`

**Location:** `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`

**Interface:**

```csharp
namespace Anela.Heblo.Adapters.OrgChart;

public static class OrgChartAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOrgChartAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<IOrgChartService, OrgChartService>();
        return services;
    }
}
```

The `HttpClient` typed-client registration previously in `OrgChartModule` moves here and is removed from `OrgChartModule`.

---

### `OrgChartModule.cs` (Application layer — modified)

**Location:** `backend/src/Application/Features/OrgChart/OrgChartModule.cs` (existing file)

**Change:** Remove the `services.AddHttpClient<IOrgChartService, OrgChartService>()` call (or equivalent typed-client registration). `OrgChartOptions` registration and any other module-level wiring remain untouched.

---

### `Program.cs` (API layer — modified)

**Location:** `backend/src/Api/Program.cs` (existing file)

**Change:** Add `builder.Services.AddOrgChartAdapter(builder.Configuration);` immediately after the existing `AddMicrosoft365Adapter` call.

---

### `Anela.Heblo.Api.csproj` (modified)

**Change:** Add a `<ProjectReference>` to `Anela.Heblo.Adapters.OrgChart.csproj` alongside the other adapter references.

---

### Solution file (modified)

Register the new project in `Anela.Heblo.sln` under the same solution folder as the other adapter projects.

---

## Data Schemas

No new API endpoints, request/response shapes, database schemas, or event payloads are introduced. This is a pure structural refactor — all runtime contracts remain identical.
