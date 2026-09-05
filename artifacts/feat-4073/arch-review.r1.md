# Architecture Review: Move GiftPackageManufacture and GiftSettings DI registration into LogisticsModule

## Skip Design: true

## Architectural Fit Assessment
This change is a pure DI-composition refactor that brings the Logistics module back into compliance with the documented pattern in `docs/architecture/development_guidelines.md` §Dependency Injection Patterns: *"Each module must have a `Module.cs` file"* registered from the composition root as a single call. Verified against the actual code:

- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` line 98 calls `services.AddLogisticsModule();`, then line 99 separately calls `services.AddGiftPackageManufactureModule();`, and line 118 separately calls `services.AddGiftSettingsModule();`.
- Both `GiftPackageManufactureModule.AddGiftPackageManufactureModule()` (`Features/Logistics/UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs`) and `GiftSettingsModule.AddGiftSettingsModule()` (`Features/Logistics/UseCases/GiftSettings/GiftSettingsModule.cs`) live under the `Anela.Heblo.Application.Features.Logistics.UseCases.*` namespace — they are sub-features of Logistics, not sibling top-level modules like `Catalog` or `Manufacture`.
- `LogisticsModule.AddLogisticsModule()` (`Features/Logistics/LogisticsModule.cs`) already registers a mix of Logistics-owned services (transport box repository/completion service, cross-module adapter contracts owned by Logistics for Catalog and ExpeditionList, dashboard tiles, a background refresh task) — i.e. it is exactly the right place for other Logistics-owned sub-feature registration to be folded in too.
- No other module in `ApplicationModule.cs` is split across multiple direct calls this way — every other line is a single `Add{X}Module()` call. This confirms the finding is a genuine, isolated deviation, not an established alternate pattern used elsewhere that would need reconciling.

Fit: high. No new abstractions, no new patterns — just moving two existing extension-method invocations one level down the call graph, mirroring how the module already composes its own sub-registrations. This directly matches the pattern's intent and removes the encapsulation leak identified in the brief.

## Proposed Architecture

### Component Overview
No new components. The relationship between the three static classes changes from "sibling calls under `ApplicationModule`" to "nested call under `LogisticsModule`":

```
Before:
  ApplicationModule.AddApplicationServices()
    ├─ AddLogisticsModule()                  (LogisticsModule.cs)
    ├─ AddGiftPackageManufactureModule()     (GiftPackageManufactureModule.cs)   <- leaked
    └─ ... (other modules) ...
    └─ AddGiftSettingsModule()               (GiftSettingsModule.cs)            <- leaked

After:
  ApplicationModule.AddApplicationServices()
    └─ AddLogisticsModule()                  (LogisticsModule.cs)
         ├─ (existing Logistics registrations: TransportBox repo/completion
         │    service, ICatalogTransportSource adapter, IExpeditionPickingSource
         │    adapter, dashboard tiles, background refresh task)
         ├─ AddGiftPackageManufactureModule()  (GiftPackageManufactureModule.cs)
         └─ AddGiftSettingsModule()            (GiftSettingsModule.cs)
```

### Key Design Decisions

#### Decision 1: Where inside `AddLogisticsModule()` to place the two calls
**Options considered:**
1. Insert at the top of the method, before the existing registrations.
2. Append at the bottom, just before `return services;`.
3. Interleave alongside logically related existing lines.

**Chosen approach:** Append both calls at the bottom of `AddLogisticsModule()`, immediately before `return services;`, as two consecutive lines (`services.AddGiftPackageManufactureModule();` then `services.AddGiftSettingsModule();`), preserving the order they currently run in relative to each other. Add a short comment above them, e.g. `// Register Logistics sub-feature modules`, consistent with the existing inline comments in the file (e.g. `// Register dashboard tiles`).

**Rationale:** DI registration order does not matter here (no two registrations for the same service type collide — `IServiceCollection` composition is order-independent for distinct service types, and there is no interface both sub-modules register). Appending at the bottom is the lowest-risk, most legible placement: it reads as "here is Logistics' own wiring, and here are its sub-feature modules," matches the existing file's habit of grouping registrations by concern with a leading comment, and requires touching the fewest surrounding lines (minimizing diff noise per the project's "surgical changes" convention).

#### Decision 2: Whether to keep `AddGiftPackageManufactureModule()` / `AddGiftSettingsModule()` as extension methods at all, vs. inlining their bodies into `LogisticsModule.cs`
**Options considered:**
1. Inline the bodies of both methods directly into `AddLogisticsModule()` and delete the two `Module.cs` files.
2. Keep both as separate extension methods in their own files, only moving the *call site*.

**Chosen approach:** Option 2 — keep `GiftPackageManufactureModule.cs` and `GiftSettingsModule.cs` completely untouched; only change *who calls* `AddGiftPackageManufactureModule()` / `AddGiftSettingsModule()`.

**Rationale:** The brief's finding and suggested fix are explicitly about the call site (`ApplicationModule` vs `LogisticsModule`), not about the existence of per-use-case `Module.cs` files — those are themselves consistent with development_guidelines.md's per-slice `Module.cs` convention (compare how `LogisticsModule.cs` itself is one of many `{Feature}Module.cs` files called from `ApplicationModule`). Inlining would be unrelated scope creep (explicitly out of scope per spec's "Out of Scope" section) and would make `LogisticsModule.cs` larger and harder to navigate for no benefit — nesting `Add{SubFeature}Module()` calls inside a parent `Add{Feature}Module()` is the established nesting idiom for a feature composed of sub-features, and no other module in the codebase inlines a sub-feature's registrations this way.

## Implementation Guidance

### Directory / Module Structure
No new files, no moved files, no renamed files. Two edits only:

1. `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — add two lines inside `AddLogisticsModule()`, before `return services;`:
   ```csharp
   // Register Logistics sub-feature modules
   services.AddGiftPackageManufactureModule();
   services.AddGiftSettingsModule();
   ```
   This requires two new `using` directives at the top of the file:
   ```csharp
   using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
   using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;
   ```

2. `backend/src/Anela.Heblo.Application/ApplicationModule.cs`:
   - Remove line 99 (`services.AddGiftPackageManufactureModule();`).
   - Remove line 118 (`services.AddGiftSettingsModule();`).
   - Remove the now-unused `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;` (line 35) and `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;` (line 40) directives. Confirm no other symbol from those two namespaces is referenced elsewhere in the file before deleting (a quick grep of the file for `GiftPackageManufacture` / `GiftSettings` after the two call-line removals is sufficient — expected to find zero remaining references).

No other file needs to change. `GiftPackageManufactureModule.cs` and `GiftSettingsModule.cs` are not touched.

### Interfaces and Contracts
No interfaces, contracts, or DTOs change. The only "contract" in play is the `IServiceCollection AddLogisticsModule(this IServiceCollection services)` signature, which is unchanged — it already returns `IServiceCollection` and already accepts no extra parameters, so no caller of `AddLogisticsModule()` needs to change beyond `ApplicationModule.cs` itself.

### Data Flow
No runtime data flow changes — this only affects the one-time DI container build during application startup (`AddApplicationServices()` → `AddLogisticsModule()` → now also → `AddGiftPackageManufactureModule()` / `AddGiftSettingsModule()`). The set of resolvable services at runtime is byte-for-byte identical to before.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Deleting the two `using` directives from `ApplicationModule.cs` accidentally removes a directive still needed by another symbol in the file | Low | Build (`dotnet build`) immediately after the edit; a stray unused-symbol reference would fail to compile. Also grep the file for `GiftPackageManufacture`/`GiftSettings` before deleting the usings to confirm no other reference exists. |
| Registration order change (sub-modules now run at the end of `AddLogisticsModule()` instead of immediately after/near `AddLogisticsModule()` in `ApplicationModule`) causes a subtle DI resolution-order dependency to break | Very low | `IServiceCollection` registration is declarative, not imperative-execution-order-sensitive for distinct service types (no `TryAdd`/decorator chaining is used by any of the three modules for the same interface), so reordering is safe. Full test suite run after the change confirms no regression. |
| A DI/composition test elsewhere in the suite asserts exact registration counts or exact call sequence in `ApplicationModule` | Very low | A repo-wide search for `AddGiftPackageManufactureModule` / `AddGiftSettingsModule` / `AddLogisticsModule` outside the three files above found zero test references — no test currently pins the old call site. Re-run `dotnet build` + full backend test suite as the acceptance gate regardless. |

## Specification Amendments
None. The spec (`spec.r1.md`) FR-1 through FR-4 and the "API / Interface Design" code sketch already match this architecture exactly — no changes needed. One clarification for the implementer, not a spec amendment: place the two new calls at the *bottom* of `AddLogisticsModule()` (see Decision 1 above), not interleaved with the existing registrations.

## Prerequisites
None. No migrations, no config, no new infrastructure. The change can be implemented and verified entirely with `dotnet build`, `dotnet format`, and the existing backend test suite — no external dependencies need to be provisioned first.
