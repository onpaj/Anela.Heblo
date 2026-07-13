# Specification: Rename `LogisticsModule.AddTransportModule()` to `AddLogisticsModule()`

## Summary
`LogisticsModule.cs` exposes its DI-registration extension method as `AddTransportModule()`, breaking the codebase-wide `{Feature}Module` → `Add{Feature}Module()` naming convention. This is a pure rename: the method, its one call site, and one documentation example are updated to `AddLogisticsModule()`. No behavior changes.

## Background
Every feature module in `Anela.Heblo.Application.Features.*` follows the pattern `{Feature}Module.Add{Feature}Module(...)` (e.g. `CatalogModule.AddCatalogModule()`, `PurchaseModule.AddPurchaseModule()`). `LogisticsModule` is the sole exception, still named `AddTransportModule()` — an apparent leftover from before the module was renamed from "Transport" to "Logistics". This inconsistency was flagged by the daily arch-review routine (2026-07-12) and makes the module harder to find by convention and undermines `development_guidelines.md` as a reliable naming template.

## Functional Requirements

### FR-1: Rename the extension method
Rename `AddTransportModule()` to `AddLogisticsModule()` in `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` (line 17). No change to the method body, signature parameters, or return type — only the identifier changes.

**Acceptance criteria:**
- `LogisticsModule` no longer declares `AddTransportModule`.
- `LogisticsModule` declares `public static IServiceCollection AddLogisticsModule(this IServiceCollection services)` with identical body to the current method.

### FR-2: Update the call site
Update `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (line 92) from `services.AddTransportModule();` to `services.AddLogisticsModule();`.

**Acceptance criteria:**
- No remaining reference to `AddTransportModule()` in `ApplicationModule.cs`.
- `services.AddLogisticsModule();` is called during application service registration, preserving its current position in the registration sequence.

### FR-3: Update the documentation example
Update the API Composition example in `docs/architecture/development_guidelines.md` (around line 158) to replace `.AddTransportModule()` with `.AddLogisticsModule()`.

**Acceptance criteria:**
- The code block no longer contains `AddTransportModule()`.
- The code block contains `AddLogisticsModule()` in the same position within the fluent chain.

### FR-4: Update the second documentation example
Update the equivalent API Composition example in `docs/architecture/infrastructure.md` (around line 143) to replace `.AddTransportModule()` with `.AddLogisticsModule()`. This occurrence is the same stale-example issue as FR-3, in a sibling doc; fixing it keeps both docs consistent with the renamed method.

**Acceptance criteria:**
- The code block no longer contains `AddTransportModule()`.
- The code block contains `AddLogisticsModule()` in the same position within the fluent chain.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — identifier rename only, no logic or execution path changes.

### NFR-2: Security
Not applicable — no change to authorization, data handling, or exposed surface.

### NFR-3: Backward compatibility
This is an internal, non-public-API rename (backend DI composition method used only within this solution). No external consumers or published NuGet/API contracts reference `AddTransportModule()`, so no deprecation shim or compatibility alias is required.

## Data Model
None. No data model changes.

## API / Interface Design
Single C# extension method signature change:

```csharp
// Before
public static IServiceCollection AddTransportModule(this IServiceCollection services)

// After
public static IServiceCollection AddLogisticsModule(this IServiceCollection services)
```

One call site updated accordingly in `ApplicationModule.cs`.

## Dependencies
None. Self-contained rename within the Logistics feature module and its documentation.

## Out of Scope
- Any change to the method body, registered services, or DI behavior of `LogisticsModule`.
- Renaming any other identifiers within the Logistics feature (e.g. `TransportBoxRepository`, `ITransportBoxCompletionService`, namespace `Anela.Heblo.Domain.Features.Logistics.Transport`) — the brief scopes this fix strictly to the module-registration method name.
- `docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md`, which references `LogisticsModule.AddTransportModule()` as a historical/dated record of a completed plan — not updated, as historical plan documents are not living documentation.
- Any build/test tooling changes; existing tests referencing the module registration (if any) are covered under FR-2's acceptance criteria implicitly via successful build.

## Open Questions
None. Resolved: `docs/architecture/infrastructure.md:143`'s matching stale example is now in scope as FR-4 — same bug class, fixed for consistency across both docs.

## Status: COMPLETE
