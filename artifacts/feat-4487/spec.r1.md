# Specification: Separate Dashboard settings provisioning from GetUserSettings query

## Summary
`GetUserSettingsHandler` (query for `GET /api/dashboard/settings`) currently creates and updates `UserDashboardSettings` rows as a side effect. Provisioning must move into a dedicated service so that the query is a pure read and write paths (tile mutations) provision explicitly, without chaining handlers through `IMediator`.

## Background
Arch-review finding on the Dashboard module. `GetTileDataHandler` and `UserDashboardSettingsMutator` dispatch `GetUserSettingsRequest` via `IMediator` solely to trigger provisioning (create default row, back-fill new AutoShow tiles). This hides write paths, couples lock ordering (the `IUserDashboardSettingsLock` is non-reentrant, so provisioning must happen before the caller's lock) to one handler, and breaks under read-only contexts (replica, caching).

## Functional Requirements

### FR-1: Provisioner service
Introduce `IUserDashboardSettingsProvisioner` (internal, Dashboard/Infrastructure) with `EnsureProvisionedAsync(string userId, CancellationToken)` that guarantees a `UserDashboardSettings` row exists and contains all `DefaultEnabled && AutoShow` registry tiles (FR-1 create-default and FR-2 back-fill behaviour of today's handler, unchanged: display order, IsVisible=true, timestamps from `TimeProvider`). It acquires `IUserDashboardSettingsLock` internally and returns the persisted `UserDashboardSettings`.
**Acceptance criteria:**
- New user: row + AutoShow tiles created (identical to current output).
- Existing user with missing AutoShow tiles: tiles appended after max DisplayOrder, `LastModified` bumped, `UpdateAsync` called.
- Existing, fully provisioned user: no write.
- Idempotent and safe under concurrency (lock held during check-and-write).

### FR-2: GetUserSettingsHandler is a pure query
The handler performs no repository writes, and does not take the lock. It loads settings by user id and returns the DTO. When no row exists, or AutoShow tiles are missing, it returns an in-memory default projection (same tiles/order the provisioner would persist) without persisting. The default-tile computation lives in one shared place used by both handler and provisioner.
**Acceptance criteria:**
- `AddAsync`/`UpdateAsync` are never called by the handler (verified by tests).
- API response for new and existing users is unchanged from the client's perspective.

### FR-3: GetTileDataHandler stops using IMediator for settings
Reads settings via the same pure read path (a shared read service or the repository plus shared projection helper), not `IMediator.Send(GetUserSettingsRequest)`. Remove the `IMediator` dependency.

### FR-4: UserDashboardSettingsMutator uses the provisioner
`MutateAsync` and `MutateBulkAsync` call `EnsureProvisionedAsync` before acquiring the lock (preserving the non-reentrant-lock invariant) instead of `_mediator.Send`. Remove `IMediator` dependency; update the interface remarks.

### FR-5: DI and tests
Register the provisioner in `DashboardModule` (scoped). Update/add unit tests: provisioner tests (moved from `GetUserSettingsHandlerTests` write cases), pure-query tests, `GetTileDataHandler` tests, mutator tests (mock provisioner instead of `IMediator`).

## Non-Functional Requirements

### NFR-1: Performance
No additional DB round trips on read path; reads become cheaper (no lock, no write). Mutations incur the same provisioning cost as today.

### NFR-2: Security
No change; user id still resolved from `ICurrentUserService`, "anonymous" fallback preserved.

## Data Model
No schema or migration changes. Entities: `UserDashboardSettings`, `UserDashboardTile`.

## API / Interface Design
No HTTP contract change. `GET /api/dashboard/settings` response shape unchanged; no OpenAPI/client regeneration expected. New internal interface `IUserDashboardSettingsProvisioner`.

## Dependencies
`ITileRegistry`, `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`.

## Out of Scope
Changing the lock implementation; frontend changes; refactoring Enable/Disable/Save handlers beyond the mutator dependency swap; the first-visit persistence of default rows by GET (now deferred to first mutation).

## Open Questions
None.

## Status: COMPLETE
