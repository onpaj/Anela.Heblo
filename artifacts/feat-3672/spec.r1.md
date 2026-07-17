# Specification: Remove unused `IConfiguration` parameter from `AddOrgChartAdapter`

## Summary
`OrgChartAdapterServiceCollectionExtensions.AddOrgChartAdapter` accepts an `IConfiguration configuration` parameter that is never used in the method body — it exists only for a speculative future need, per the inline comment `// reserved for future base-URL configuration`. This is a mechanical YAGNI cleanup: drop the unused parameter and update its single call site.

## Background
This finding was raised by the daily architecture-review routine (2026-07-16) against `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`. Dead parameters marked "reserved for future use" mislead readers into thinking configuration is being consumed when it isn't, and add no value until that future need actually materializes — at which point re-adding the parameter is trivial. No behavior of `AddOrgChartAdapter` depends on the `configuration` value today; it registers a typed `HttpClient<IOrgChartService, OrgChartService>()` and nothing else.

## Functional Requirements

### FR-1: Remove the unused parameter from `AddOrgChartAdapter`
In `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`, change the method signature from:
```csharp
public static IServiceCollection AddOrgChartAdapter(
    this IServiceCollection services,
    IConfiguration configuration) // reserved for future base-URL configuration
```
to:
```csharp
public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)
```
The method body (`services.AddHttpClient<IOrgChartService, OrgChartService>(); return services;`) is unchanged. Remove the now-unused `using Microsoft.Extensions.Configuration;` directive if nothing else in the file requires it.

**Acceptance criteria:**
- `AddOrgChartAdapter` has a single parameter: `this IServiceCollection services`.
- The `// reserved for future base-URL configuration` comment is removed.
- No other line in the method body changes.
- The file compiles without the `Microsoft.Extensions.Configuration` using directive if it is otherwise unused.

### FR-2: Update the call site in `Program.cs`
In `backend/src/Anela.Heblo.API/Program.cs` (line 128), change:
```csharp
builder.Services.AddOrgChartAdapter(builder.Configuration);
```
to:
```csharp
builder.Services.AddOrgChartAdapter();
```

**Acceptance criteria:**
- The call site no longer passes `builder.Configuration`.
- No other lines in `Program.cs` are touched (lines 126–127 and 130, the adjacent `AddPlaudAdapter`/`AddMicrosoft365Adapter`/`AddSingleton` calls, remain exactly as-is).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — no runtime behavior changes; this is a compile-time signature change only.

### NFR-2: Security
Not applicable — no configuration, secrets, or auth handling is affected. `AddOrgChartAdapter` never read `IConfiguration` before, so no code path is losing access to configuration it actually relied on.

## Data Model
Not applicable — no entities or persistence involved.

## API / Interface Design
Internal DI extension method signature change only, scoped to a single assembly (`Anela.Heblo.Adapters.OrgChart`) and its single consumer (`Anela.Heblo.API`). No public HTTP API, contract, or DTO is affected.

## Dependencies
None beyond the two files already identified. No other file in the repository calls `AddOrgChartAdapter` (confirmed: `Program.cs:128` is the only call site).

## Out of Scope
- Any change to `OrgChartService`, `IOrgChartService`, or the `AddHttpClient<IOrgChartService, OrgChartService>()` registration itself.
- Introducing actual base-URL configuration for the OrgChart adapter. If/when that need arises, it should be a separate, deliberate change with its own spec — not reintroduced speculatively here.
- Changes to `AddPlaudAdapter`, `AddMicrosoft365Adapter`, or any other adapter registration in `Program.cs`, even though they follow a similar `(builder.Configuration)` pattern.

## Open Questions
None.

## Status: COMPLETE
