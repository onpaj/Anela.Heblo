# Design: Dashboard settings provisioning separation

## Component Design
- `UserDashboardSettingsDefaults` (internal static): `BuildDefault(userId, autoShowTiles, now)` returns a new `UserDashboardSettings` with back-referenced tiles; `BackfillMissing(settings, autoShowTiles, now)` appends missing tiles and returns bool changed; `AutoShowTiles(ITileRegistry)` filters `DefaultEnabled && AutoShow`.
- `IUserDashboardSettingsProvisioner` / `UserDashboardSettingsProvisioner` (scoped): lock -> `GetByUserIdAsync` -> null: `BuildDefault` + `AddAsync`; else `BackfillMissing` and `UpdateAsync` only if changed (sets `settings.LastModified`). Returns settings.
- `IUserDashboardSettingsReader` / `UserDashboardSettingsReader` (scoped): `GetByUserIdAsync`; null -> project `BuildDefault`; else clone-free projection of existing plus missing AutoShow tiles (computed on a non-tracked copy, never calling repository writes). Returns `UserDashboardSettingsDto`.
- `GetUserSettingsHandler`: resolves user id (anonymous fallback), calls reader, wraps in `GetUserSettingsResponse`. Deps: reader, `ICurrentUserService`.
- `GetTileDataHandler`: replace `IMediator` with reader; resolve user id identically.
- `UserDashboardSettingsMutator`: replace `IMediator` with provisioner; call `EnsureProvisionedAsync(resolvedUserId, ct)` before lock.
- `DashboardModule`: `AddScoped` for provisioner and reader.

Caution: the projection must not mutate tracked EF entities (backfill on a loaded entity would be saved by a later SaveChanges); build the projected DTO list directly rather than adding to `settings.Tiles`.

## Data Schemas
No database or API changes. `UserDashboardSettingsDto { Tiles: UserDashboardTileDto[] {TileId, IsVisible, DisplayOrder}, LastModified }` unchanged.
