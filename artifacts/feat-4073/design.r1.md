# Design: Move GiftPackageManufacture and GiftSettings DI registration into LogisticsModule

## Component Design

Three existing static classes are involved; none are created, removed, or renamed. Only the caller of two extension methods changes.

### `LogisticsModule` (`backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`)
- **Responsibility (unchanged):** register all DI services owned by the Logistics module.
- **Change:** `AddLogisticsModule()` gains two additional calls at the end of its body, immediately before `return services;`:
  ```csharp
  // Register Logistics sub-feature modules
  services.AddGiftPackageManufactureModule();
  services.AddGiftSettingsModule();
  ```
- **New `using` directives required:**
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;
  ```
- **Contract:** `public static IServiceCollection AddLogisticsModule(this IServiceCollection services)` — signature unchanged.

### `GiftPackageManufactureModule` (`backend/.../UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs`)
- **Responsibility (unchanged):** register `IGiftPackageManufactureRepository` and `IGiftPackageManufactureService`.
- **Change:** none. Only who invokes `AddGiftPackageManufactureModule()` changes (from `ApplicationModule` to `LogisticsModule`).

### `GiftSettingsModule` (`backend/.../UseCases/GiftSettings/GiftSettingsModule.cs`)
- **Responsibility (unchanged):** register `IGiftSettingRepository`, `IValidator<SetGiftSettingCommand>`, and the `SetGiftSettingCommand`/`SetGiftSettingResponse` `ValidationBehavior` pipeline behavior.
- **Change:** none. Only who invokes `AddGiftSettingsModule()` changes (from `ApplicationModule` to `LogisticsModule`).

### `ApplicationModule` (`backend/src/Anela.Heblo.Application/ApplicationModule.cs`)
- **Responsibility (unchanged):** composition root — one `Add{X}Module()` call per module.
- **Change:** remove the two direct calls (`services.AddGiftPackageManufactureModule();` at current line 99, `services.AddGiftSettingsModule();` at current line 118) and the two now-unused `using` directives for `Features.Logistics.UseCases.GiftPackageManufacture` and `Features.Logistics.UseCases.GiftSettings` (current lines 35 and 40). The single `services.AddLogisticsModule();` call (current line 98) is unchanged and now covers all Logistics wiring.

## Data Schemas
Not applicable — no database schema, API request/response shape, or event payload is created or changed by this refactor. The set of types registered in the DI container is identical before and after; only the call site that triggers their registration moves.
