# Design: Align GiftSettings Module Boundaries with Logistics

## Component Design

This is a pure namespace/folder relocation of the GiftSettings Application layer. No component behavior, responsibility, or public shape changes — only the C# namespace and physical location of the Application-layer types move, to match the `GiftPackageManufacture` precedent and close the gap identified by `ModuleBoundariesTests.cs`.

### Relocated components (`Application/Features/GiftSettings/` → `Application/Features/Logistics/UseCases/GiftSettings/`)

- **`GiftSettingsModule`** (`GiftSettingsModule.cs`)
  Responsibility: DI registration extension (`AddGiftSettingsModule()`) — registers `IGiftSettingRepository` → `GiftSettingRepository`, the `SetGiftSettingCommand` FluentValidation validator, and binds the `ValidationBehavior` pipeline.
  Contract change: namespace only, `Anela.Heblo.Application.Features.GiftSettings` → `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings`. Extension method name, signature, and registration contents are unchanged. Its internal `using` of `Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting` updates to `...Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting`. Its `using Anela.Heblo.Persistence.Logistics.GiftSettings;` is untouched (already correct).

- **`GiftSettingDto`** (`Dto/GiftSettingDto.cs`)
  Responsibility: flat read projection of the `GiftSetting` aggregate for the `GET` response.
  Contract change: namespace only, `...Features.GiftSettings.Dto` → `...Features.Logistics.UseCases.GiftSettings.Dto`. Members unchanged.

- **`GetGiftSettingQuery` / `GetGiftSettingHandler`** (`UseCases/GetGiftSetting/`)
  Responsibility: MediatR query + handler that reads the singleton `GiftSetting` row via `IGiftSettingRepository.GetAsync()` and maps it to `GiftSettingDto`.
  Contract change: namespace only. `using` of the Dto namespace updates to the new prefix. `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` is untouched.

- **`SetGiftSettingCommand` / `SetGiftSettingHandler` / `SetGiftSettingResponse` / `SetGiftSettingValidator`** (`UseCases/SetGiftSetting/`)
  Responsibility: MediatR command + handler that validates and persists updated `IsEnabled` / `ThresholdCzk` / `Text`, resolving the acting user via `ICurrentUserService` and upserting through `IGiftSettingRepository.SaveAsync()`; validator enforces field constraints (e.g. `Text` max 50 chars); response follows the shared `BaseResponse` shape (`Success` / `ErrorCode` / `Params`).
  Contract change: namespace only. The existing redundant self-referential `using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;` in `SetGiftSettingHandler.cs` is either updated to the new namespace or dropped (dead weight, no behavior impact — implementer's choice per arch review).

### Unchanged components (call-site `using`-only edits)

- **`GiftSettingsController`** (`API/Controllers/GiftSettingsController.cs`) — route (`api/gift-settings`), HTTP verbs, and `[FeatureAuthorize(Feature.Warehouse_Logistics[, AccessLevel.Write])]` attributes unchanged. Only its `using` directives for `GetGiftSetting`/`SetGiftSetting` sub-namespaces are updated to the new prefix.
- **`ApplicationModule`** (`Application/ApplicationModule.cs`) — `services.AddGiftSettingsModule();` call site and its position in the registration sequence unchanged. Only the `using Anela.Heblo.Application.Features.GiftSettings;` import is updated (optionally relocated adjacent to the `GiftPackageManufacture` using line for readability — cosmetic, not required).
- **`GiftSetting` / `IGiftSettingRepository`** (`Domain/Features/Logistics/GiftSettings/`) — untouched; already namespaced under `Anela.Heblo.Domain.Features.Logistics.GiftSettings`.
- **`GiftSettingConfiguration` / `GiftSettingRepository`** (`Persistence/Logistics/GiftSettings/`) — untouched; already namespaced under `Anela.Heblo.Persistence.Logistics.GiftSettings`.
- **Test files** (`test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`, `SetGiftSettingHandlerTests.cs`, `SetGiftSettingValidatorTests.cs`) — folder location stays flat (matches the `GiftPackageManufacture` test-layout precedent); only `using` references to the moved production types are updated. Test assertions, arrangements, and mocks are unchanged.

### Post-move architecture boundary

After the move, `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings*` falls under the existing `Anela.Heblo.Application.Features.Logistics` prefix already modeled by `ModuleBoundariesTests.cs` (`Catalog -> Logistics`, `ExpeditionList -> Logistics`, `ShoptetApi Adapters -> Logistics`, `Logistics -> Manufacture`, `Logistics -> Catalog`, `Logistics_types_should_not_reference_Purchase_owned_namespaces`). GiftSettings' only cross-module dependency, `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`), is already sanctioned per ADR-005 and is not Manufacture/Catalog/Purchase-owned, so no allowlist changes are expected. If an unexpected cross-module reference surfaces, resolve it via the existing contract-inversion pattern or add a justified allowlist entry — never suppress silently.

## Data Schemas

No data model, database schema, migration, or wire-contract changes. Included for reference (unchanged):

### Domain entity — `GiftSetting` (`Domain/Features/Logistics/GiftSettings/GiftSetting.cs`)

Singleton-style aggregate (`Id` is always `1`), mapped to table `public.GiftSettings` via `GiftSettingConfiguration`:

| Field | Type | Notes |
|---|---|---|
| `Id` | int | always `1` |
| `IsEnabled` | bool | |
| `ThresholdCzk` | decimal | |
| `Text` | string | max 50 chars |
| `ModifiedAt` | `DateTimeOffset?` | nullable |
| `ModifiedBy` | string? | nullable |

`IGiftSettingRepository`: `GetAsync()` (returns the single row, or `GiftSetting.CreateDefault()` if none exists), `SaveAsync(GiftSetting)` (upsert against the single row).

### API shapes (namespace relocates, wire shape identical)

- `GET /api/gift-settings` → `GetGiftSettingQuery` (MediatR request, no parameters) → `GiftSettingDto` (flat projection of `GiftSetting`: `IsEnabled`, `ThresholdCzk`, `Text`, `ModifiedAt`, `ModifiedBy`).
- `PUT /api/gift-settings` (requires `AccessLevel.Write` on `Feature.Warehouse_Logistics`) → `SetGiftSettingCommand` (`IsEnabled`, `ThresholdCzk`, `Text`) → `SetGiftSettingResponse` (`BaseResponse`: `Success`, `ErrorCode`, `Params`).

Namespace relocation only:

| Type | Old namespace | New namespace |
|---|---|---|
| `GiftSettingsModule` | `Anela.Heblo.Application.Features.GiftSettings` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings` |
| `GiftSettingDto` | `Anela.Heblo.Application.Features.GiftSettings.Dto` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.Dto` |
| `GetGiftSettingQuery`, `GetGiftSettingHandler` | `Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.GetGiftSetting` |
| `SetGiftSettingCommand`, `SetGiftSettingHandler`, `SetGiftSettingResponse`, `SetGiftSettingValidator` | `Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting` |

The generated OpenAPI spec and TypeScript client must be byte-for-byte identical for GiftSettings operations — Swashbuckle derives operation IDs and schema names from controller/DTO class names and route, not C# namespaces, so this relocation has no effect on the public contract.
