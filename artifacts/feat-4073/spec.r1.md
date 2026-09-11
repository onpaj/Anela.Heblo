# Specification: Move GiftPackageManufacture and GiftSettings DI registration into LogisticsModule

## Summary
`AddGiftPackageManufactureModule()` and `AddGiftSettingsModule()` are two sub-feature DI registration calls that logically belong to the Logistics module, but are currently invoked directly from `ApplicationModule.AddApplicationServices()` alongside `AddLogisticsModule()`. This spec covers relocating both calls into `LogisticsModule.AddLogisticsModule()` so `ApplicationModule` only calls one entry point per module, restoring the module encapsulation described in `docs/architecture/development_guidelines.md` (§Dependency Injection Patterns).

## Background
`GiftPackageManufacture` and `GiftSettings` are both use-cases under `Anela.Heblo.Application.Features.Logistics.UseCases.*` — i.e. they are internal sub-features of Logistics, not independent top-level modules. Each has its own `{Feature}Module.cs` with an `Add{Feature}Module()` extension method, which is the correct per-slice pattern. The problem is only *where* those two extension methods are invoked from: today, `ApplicationModule.cs` calls `AddLogisticsModule()` (line 98) and then separately calls `AddGiftPackageManufactureModule()` (line 99) and, much later, `AddGiftSettingsModule()` (line 118). This means:
- The composition root (`ApplicationModule`) has to know that Logistics is internally split into three registration calls instead of one.
- Anyone adding a new Logistics sub-feature must remember to also wire it into `ApplicationModule` directly; if they don't, DI silently omits it (no compile error, only a runtime failure when a service can't be resolved).
- It's inconsistent with every other module in `ApplicationModule`, which is called via exactly one `Add{X}Module()` line.

The fix is mechanical: fold the two calls into `LogisticsModule.AddLogisticsModule()` and delete them from `ApplicationModule`. No behavior changes — the exact same services are registered in the exact same DI container, just triggered by one call instead of three.

## Functional Requirements

### FR-1: `LogisticsModule.AddLogisticsModule()` registers GiftPackageManufacture services
`AddLogisticsModule()` must call `services.AddGiftPackageManufactureModule();` internally, registering `IGiftPackageManufactureRepository` and `IGiftPackageManufactureService` exactly as they are registered today.
**Acceptance criteria:**
- After the change, calling `services.AddLogisticsModule()` alone (with no other calls) resolves `IGiftPackageManufactureRepository` and `IGiftPackageManufactureService` from the container.
- `GiftPackageManufactureModule.cs` itself is unchanged — only the call site moves.

### FR-2: `LogisticsModule.AddLogisticsModule()` registers GiftSettings services
`AddLogisticsModule()` must call `services.AddGiftSettingsModule();` internally, registering `IGiftSettingRepository`, `IValidator<SetGiftSettingCommand>`, and the `ValidationBehavior` pipeline behavior for `SetGiftSettingCommand`/`SetGiftSettingResponse` exactly as they are registered today.
**Acceptance criteria:**
- After the change, calling `services.AddLogisticsModule()` alone resolves `IGiftSettingRepository`, `IValidator<SetGiftSettingCommand>`, and the registered `IPipelineBehavior<SetGiftSettingCommand, SetGiftSettingResponse>`.
- `GiftSettingsModule.cs` itself is unchanged — only the call site moves.

### FR-3: `ApplicationModule.cs` no longer calls the two sub-module methods directly
`ApplicationModule.AddApplicationServices()` must no longer contain `services.AddGiftPackageManufactureModule();` (currently line 99) or `services.AddGiftSettingsModule();` (currently line 118). Only `services.AddLogisticsModule();` remains for the Logistics module.
**Acceptance criteria:**
- `ApplicationModule.cs` has exactly one call related to Logistics: `services.AddLogisticsModule();`.
- The now-unused `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;` and `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;` directives are removed from `ApplicationModule.cs` (they become dead usings once the calls are removed).

### FR-4: No change in resolved services / runtime behavior
The full set of services registered in the DI container after `AddApplicationServices()` runs must be identical before and after this change — same types, same lifetimes, same implementations. This is a pure refactor of *where* the registration calls live, not *what* gets registered.
**Acceptance criteria:**
- `dotnet build` succeeds with no new warnings.
- The full backend test suite (in particular any DI/composition/startup tests, e.g. `PersistenceModuleTests` and any `ApplicationModule`/`WebApplicationFactory`-based integration tests that build the service provider) passes unchanged.
- Manual/automated resolution of `IGiftPackageManufactureRepository`, `IGiftPackageManufactureService`, `IGiftSettingRepository`, `IValidator<SetGiftSettingCommand>`, and the `SetGiftSettingCommand` pipeline behavior succeeds identically to before the change.

## Non-Functional Requirements

### NFR-1: Performance
None — this is a compile-time reorganization of DI registration call sites; there is no measurable runtime performance impact.

### NFR-2: Security
None — no change to what is registered, how secrets are handled, or authorization/authentication wiring.

## Data Model
No data model changes. No entities, migrations, or persistence schema are touched.

## API / Interface Design
No public API surface changes (no controller/endpoint/DTO changes). The only "interface" touched is the internal DI composition:

```csharp
// LogisticsModule.cs (Application.Features.Logistics)
public static IServiceCollection AddLogisticsModule(this IServiceCollection services)
{
    // ...existing Logistics registrations (repository, completion service,
    // ICatalogTransportSource adapter, IExpeditionPickingSource adapter,
    // dashboard tiles, background refresh task)...

    services.AddGiftPackageManufactureModule();
    services.AddGiftSettingsModule();

    return services;
}
```

```csharp
// ApplicationModule.cs
services.AddLogisticsModule();
// (AddGiftPackageManufactureModule() and AddGiftSettingsModule() calls removed)
```

## Dependencies
- `GiftPackageManufactureModule.AddGiftPackageManufactureModule()` — `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs` (unchanged, only its call site moves).
- `GiftSettingsModule.AddGiftSettingsModule()` — `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/GiftSettingsModule.cs` (unchanged, only its call site moves).
- `LogisticsModule.AddLogisticsModule()` — `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` (target of the two new calls).
- `ApplicationModule.AddApplicationServices()` — `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (source the two calls are removed from).
- `docs/architecture/development_guidelines.md` §Dependency Injection Patterns — the documented pattern this change brings the module back into compliance with (single `{Feature}Module.cs` entry point per module registered from the composition root).

## Out of Scope
- Any change to what `AddGiftPackageManufactureModule()` or `AddGiftSettingsModule()` themselves register (their internal content is untouched).
- Renaming, moving files, or restructuring the `GiftPackageManufacture` / `GiftSettings` use-case folders.
- Auditing or fixing any other module in `ApplicationModule.cs` for similar violations — this issue is scoped to the two Logistics sub-features named in the brief only.
- Adding a regression test that asserts `ApplicationModule` calls exactly one method per module (could be a reasonable follow-up, but is not requested by the brief; noted as an open question below rather than assumed in scope).

## Open Questions
None.

## Status: COMPLETE
