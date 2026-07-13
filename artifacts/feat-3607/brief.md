## Module
Logistics / GiftSettings

## Finding
The domain entities for GiftSettings are nested under the Logistics module:
- `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/GiftSetting.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/IGiftSettingRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingRepository.cs`

But the application and API layers treat GiftSettings as an independent module:
- `backend/src/Anela.Heblo.Application/Features/GiftSettings/` (standalone folder, own `GiftSettingsModule.cs`)
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` (route: `/api/gift-settings`, separate from `/api/logistics/...`)

The split means that domain-layer ownership (Logistics) contradicts application-layer ownership (standalone GiftSettings). The persistence layer follows the domain (`Logistics/GiftSettings/`), but the application layer does not.

## Why it matters
Future code changes to GiftSettings lack a clear placement rule: does new logic belong in `Features/Logistics/` or `Features/GiftSettings/`? Developers following the domain structure will place code in one place; those following the application structure will place it in another. This boundary ambiguity is exactly what vertical-slice architecture is meant to prevent.

The `SetGiftSettingHandler` imports `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` (line 2), making the logical coupling to Logistics visible at the application layer even though the folder structure denies it.

## Suggested fix
Pick one canonical boundary and align all layers:

**Option A — make GiftSettings a full standalone module:**
Move domain types to `Domain/Features/GiftSettings/` and persistence to `Persistence/GiftSettings/`. Update namespaces. This is the larger change but produces the cleanest separation.

**Option B — merge GiftSettings back into Logistics application layer:**
Move `Application/Features/GiftSettings/` contents into `Application/Features/Logistics/` (e.g. under a `GiftSettings/` subfolder, matching the domain structure). Update the controller routing if needed. No domain or persistence changes required.

In either case, the goal is: domain, persistence, application, and API all agree on which module owns GiftSettings.

---
_Filed by daily arch-review routine on 2026-07-12._
