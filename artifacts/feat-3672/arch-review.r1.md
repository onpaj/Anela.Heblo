# Architecture Review: Remove unused `IConfiguration` parameter from `AddOrgChartAdapter`

## Skip Design: true

## Architectural Fit Assessment
Trivial, in-pattern cleanup — not a feature. `AddOrgChartAdapter` is a private-assembly DI extension method with exactly one call site (`Program.cs:128`). Verified directly against the code:

- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs:9-15` — the `IConfiguration configuration` parameter is accepted but never referenced in the method body, which only calls `services.AddHttpClient<IOrgChartService, OrgChartService>()`.
- `backend/src/Anela.Heblo.API/Program.cs:126-128` — three sibling adapter registrations (`AddPlaudAdapter`, `AddMicrosoft365Adapter`, `AddOrgChartAdapter`) all currently take `builder.Configuration`, but the spec scopes this change to `AddOrgChartAdapter` only.

No architectural pattern is introduced or broken. This is a signature simplification within a single vertical slice's adapter registration, with no cross-module, persistence, API contract, or UI surface.

## Proposed Architecture

### Component Overview
No new components. Existing shape is unchanged:

```
Program.cs ──calls──> AddOrgChartAdapter(services) ──registers──> HttpClient<IOrgChartService, OrgChartService>
```

The only change is removal of the unused `IConfiguration` edge into `AddOrgChartAdapter`.

### Key Design Decisions

#### Decision 1: Remove the parameter now vs. leave it "reserved"
**Options considered:**
1. Leave as-is ("reserved for future base-URL configuration").
2. Remove the parameter and re-add it later if/when OrgChart actually needs configuration.

**Chosen approach:** Option 2, per the spec.

**Rationale:** YAGNI. The parameter currently has zero readers and misleads maintainers into thinking configuration is wired up. Re-adding a single parameter later, when a real base-URL requirement exists, is a one-line change with no migration cost. There is no cost to removing it now beyond touching two lines.

## Implementation Guidance

### Directory / Module Structure
No new files, no structural changes. Edit in place:

1. `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`
   - Line 9-11: change signature from
     ```csharp
     public static IServiceCollection AddOrgChartAdapter(
         this IServiceCollection services,
         IConfiguration configuration) // reserved for future base-URL configuration
     ```
     to
     ```csharp
     public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)
     ```
   - Line 2: remove `using Microsoft.Extensions.Configuration;` — confirmed nothing else in the file uses it (the file has no other references to `IConfiguration` or that namespace).
   - Line body (line 13, `services.AddHttpClient<IOrgChartService, OrgChartService>();`) stays untouched.

2. `backend/src/Anela.Heblo.API/Program.cs`
   - Line 128: change `builder.Services.AddOrgChartAdapter(builder.Configuration);` to `builder.Services.AddOrgChartAdapter();`
   - Lines 126-127 and 130 (sibling `AddPlaudAdapter`, `AddMicrosoft365Adapter`, and the following `AddSingleton` call) are explicitly out of scope — do not touch them, even though they share the same call pattern.

### Interfaces and Contracts
Internal DI extension method only; not part of any public API surface, OpenAPI contract, or DTO. No client regeneration needed. No test doubles or mocks reference this signature (confirmed no other call sites exist in the repo besides `Program.cs:128`).

### Data Flow
Unchanged. `AddOrgChartAdapter` registers a typed `HttpClient<IOrgChartService, OrgChartService>` at startup; no runtime data flow is affected by removing an unused parameter.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed call site elsewhere in repo | Low | Confirmed via grep: `Program.cs:128` is the only caller. `dotnet build` will fail loudly if any other call site exists. |
| Someone later needs OrgChart base-URL config and re-adds this exact "reserved" anti-pattern | Low | Out-of-scope note in spec already flags this — future config need should be its own deliberate change, not a speculative parameter. |

## Specification Amendments
None. The spec's FR-1/FR-2 already name the exact two files and line ranges verified above; no discrepancy found between spec and actual code.

## Prerequisites
None. No migrations, config, or infrastructure changes needed — pure compile-time signature edit. Standard validation applies: `dotnet build` + `dotnet format` on the backend after the change.
