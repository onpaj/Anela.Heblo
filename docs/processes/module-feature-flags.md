---
process: module-feature-flags
kind: module
module: feature-flags
summary: Runtime on/off switches (feature flags) that let an admin turn risky behaviour — auto-completing delivered Shoptet orders, physical label printing — on or off without a deploy; three flags exist today.
owns: []
verified_at: "f68c439ec"
related: []
---

# Feature flags

## Purpose
Some Heblo behaviour is risky enough that Anela wants to switch it on or off without shipping a
new version: changing order states in the live Shoptet store, or sending labels to the physical
Zebra printer. A **feature flag** is a named yes/no switch that business code asks before doing
such a thing. An administrator can flip a flag on the "Feature Flags" admin page and the change
takes effect within seconds (immediately on the instance that served the change, at most
30 seconds elsewhere); no restart is needed.

Each flag has a value from three layers, highest priority first:

1. **Database override** — set on the admin page (`FeatureFlagOverrides` table).
2. **Configuration** — `FeatureManagement:<flag-key>` in `appsettings*.json` (or any config
   source that overrides it, e.g. Key Vault / App Settings).
3. **Registry default** — the `DefaultValue` written in code (`FeatureFlagRegistry`).

"Reset" on the admin page deletes the database override, so the flag falls back to the
configuration value.

### Flag inventory (every flag that exists)

| Key | Registry default | `appsettings.json` | Staging (`appsettings.Staging.json`) | Read by | Effect |
|---|---|---|---|---|---|
| `is-delivered-order-completion-enabled` | false | false | (inherits false) | `CompleteDeliveredOrdersJob` (ShoptetOrders module) | On: the delivered-orders job really changes the Shoptet order state to "vyřízena" and writes the remark. Off: dry run — it only logs what it would do. |
| `is-delivered-order-completion-test-source-enabled` | false | false | (inherits false) | `CompleteDeliveredOrdersJob` | On: the job polls the test state 73 "Oprava-robot" instead of the production "handed to carrier" states 70/82, so the pipeline can be exercised on a hand-picked set of orders. |
| `is-label-printing-enabled` | true | true | **false** | `FeatureGatedLabelPrintingService` (decorator over every `ILabelPrintingService`, registered in `ServiceCollectionExtensions`) | On: ZPL label print jobs go to the physical CUPS label printer. Off: the physical print is skipped and logged; the surrounding operation still runs (e.g. material-container labels are still generated and stored as Unassigned). Off on Staging because there is no printer. |

The frontend also mirrors `is-label-printing-enabled` as `FeatureFlagKeys.LabelPrinting` in
`frontend/src/features/feature-flags/featureFlags.ts`, but no component currently reads it — all
three flags are only acted on by the backend.

The delivered-orders job and the label-printing workflows are documented by their own modules
(ShoptetOrders, and Catalog inventory for the lot / material-container label prints); this module only provides the switches.

## Users & screens
| Route | Who | What |
|---|---|---|
| `/admin/feature-flags` (sidebar Administrace → "Feature Flags") | `Admin_FeatureFlags` ("Feature příznaky") | Table of every registered flag: key, description, current value, default, whether it is overridden, who changed it and when. A toggle writes a DB override; "Reset" (shown only when overridden) deletes it. |

API (`FeatureFlagsController`, `/api/feature-flags`):
- `GET /api/feature-flags` — **anonymous** (`[AllowAnonymous]`); returns `{ flags: { key: bool } }`
  for every registered flag. The React app calls it once at start-up (`FeatureFlagProvider`)
  and loads the values into an in-memory OpenFeature provider; on failure it logs and uses
  defaults. Values are not refreshed until the page is reloaded.
- `GET /api/feature-flags/admin` — list with override metadata.
- `PUT /api/feature-flags/admin/{key}` body `{ isEnabled }` — upsert an override; 404 for a key
  that is not in the registry. `UpdatedBy` = the caller's display name.
- `DELETE /api/feature-flags/admin/{key}` — clear an override; 404 when there was none.

All three admin endpoints require `Admin_FeatureFlags` at **Write** level. No MCP tool, no
dashboard tile.

## Processes
No process docs — the module has no scheduled job and writes nothing outside Heblo's database.
Plain CRUD (above): set override, clear override, list flags, evaluate flags for the frontend.

## Data owned
- `public."FeatureFlagOverrides"` — one row per overridden flag: `Key` (PK, ≤100 chars),
  `IsEnabled`, `UpdatedAt` (`timestamp without time zone`, UTC), `UpdatedBy` (display name,
  ≤200). A flag with no row is not overridden. No history is kept — an overwrite or reset loses
  the previous value and who set it.
- In-memory cache `feature_flag_overrides` (per app instance, `IMemoryCache`): the whole override
  table as a dictionary, absolute TTL 30 s, removed immediately by the upsert / clear handlers on
  the instance that handled the write.

## External systems
None. (The flags *gate* calls to Shoptet and CUPS, but this module itself talks to neither.)

## Dependencies
- Read by: **ShoptetOrders** (`CompleteDeliveredOrdersJob`, two flags) and the shared label
  printing service `FeatureGatedLabelPrintingService`, which today covers the Catalog inventory
  label handlers (`PrintLotLabels`, `PrintLotCalibrationLabel`, `PrintMaterialContainerLabels`,
  `FeedLotMedia`). The frontend reads all flags at start-up.
- Reads from: Heblo database; `IConfiguration` (`FeatureManagement` section); Users
  (`ICurrentUserService` for `UpdatedBy`).

## Known quirks
- **A DB error skips the configuration layer.** `HebloFeatureProvider` loads the overrides first;
  if that throws (DB unavailable), the catch returns the *caller-supplied default*, which
  `IFeatureFlagChecker.IsEnabledAsync(key)` sets to the **registry** default. So during a DB
  outage on Staging, `is-label-printing-enabled` resolves to `true` (registry) even though
  `appsettings.Staging.json` says `false`. The flag never throws (fail-open by design), it just
  lands on the code default, not the config value.
- **Menu vs endpoint permission mismatch.** The access matrix shows `/admin/feature-flags` to
  anyone with `Admin_FeatureFlags` **Read**, but every admin endpoint requires **Write**; a
  read-only user sees the menu item and gets 403 on the page.
- `[FeatureGate]` attributes (Microsoft.FeatureManagement) read configuration only and ignore DB
  overrides. No controller uses `[FeatureGate]` today.
- `docs/development/feature-flags.md` says the admin endpoints require the `super_user` role; the
  code uses the `Admin_FeatureFlags` permission (since #4130). Its "two steps" heading also lists
  three steps.
- The public `GET /api/feature-flags` exposes every flag's value without authentication. The
  current flags reveal nothing sensitive, but a future flag will be public too.
- The frontend loads flag values once per page load; a flag flipped while a user has Heblo open
  only reaches their browser on reload (backend code sees it within 30 s).
- Only boolean flags are supported; string/int/double/structure resolution throws
  `NotImplementedException`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs` — the flag list and defaults (source of truth)
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs` — key constants with behaviour notes
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — resolution order, cache, error fallback
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/IFeatureFlagChecker.cs` — the API business code uses
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — endpoints and permissions
- `backend/src/Anela.Heblo.Persistence/FeatureFlags/FeatureFlagOverrideRepository.cs` — override table access
- `backend/src/Anela.Heblo.Application/Shared/Printing/FeatureGatedLabelPrintingService.cs` — label-printing gate
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs` — delivered-order flags
- `frontend/src/features/feature-flags/FeatureFlagProvider.tsx`, `frontend/src/pages/FeatureFlagsAdminPage.tsx` — frontend side
- `docs/development/feature-flags.md` — developer guide for adding a flag
