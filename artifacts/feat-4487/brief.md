## Module
Dashboard

## Finding
`GetUserSettingsHandler` is a MediatR query handler (dispatched by `GET /api/dashboard/settings`) that unconditionally writes to the database on every invocation:

- **Lines 47–69** (`GetUserSettingsHandler.cs`): when no settings row exists for the user, the handler creates a full `UserDashboardSettings` + tiles aggregate and persists it via `_repository.AddAsync`.
- **Lines 72–96** (`GetUserSettingsHandler.cs`): when settings already exist, the handler backfills any newly-registered AutoShow tiles and persists the result via `_repository.UpdateAsync`.

Because the handler writes, every caller that only needs to *read* the current settings is implicitly forced to trigger database writes. This is why both `GetTileDataHandler` (line 40, `GetTileDataHandler.cs`) and `UserDashboardSettingsMutator.MutateAsync`/`MutateBulkAsync` (lines 37, 100 of `UserDashboardSettingsMutator.cs`) dispatch `GetUserSettingsRequest` via `IMediator` *solely to trigger provisioning* before doing their own work — the provisioning side effect has leaked out into all consumers.

## Why it matters
A read query with write side effects:
1. **Hides write paths from callers** — `GetTileDataHandler` and `UserDashboardSettingsMutator` both inject `IMediator` to chain a separate handler, rather than injecting a simple repository or service. This makes the call graph opaque and the handlers harder to unit-test (mocking `IMediator` instead of a typed service).
2. **Makes the handler non-reentrant without careful locking** — the handler acquires `IUserDashboardSettingsLock` internally, so callers that hold the same lock must dispatch `GetUserSettingsRequest` *before* acquiring their own lock (documented in `IUserDashboardSettingsMutator` remarks). The lock constraint is now a cross-cutting concern driven by one handler's side effect.
3. **Read-only contexts break silently** — a future read-only database replica, a test that only mocks the read path, or a cached handler result will silently skip the provisioning write, leaving the user with no settings row.

## Suggested fix
Extract provisioning into a dedicated `IUserDashboardSettingsProvisioner` service (or an `EnsureUserDashboardProvisionedCommand` handler) with a single responsibility: guarantee that a `UserDashboardSettings` row exists for the given user, backfilling AutoShow tiles if needed. 

- `GetUserSettingsHandler` calls the provisioner (or the repository directly) but no longer writes itself — it becomes a pure query.
- `GetTileDataHandler` can inject the provisioner directly instead of dispatching `GetUserSettingsRequest` via mediator.
- `UserDashboardSettingsMutator` calls the provisioner instead of `_mediator.Send(new GetUserSettingsRequest(), ...)`.

This separates the "ensure settings exist" concern from the "read settings" concern, removes the IMediator chains inside infrastructure code, and makes each handler independently testable with only the dependencies it actually uses.

---
_Filed by daily arch-review routine on 2026-10-13._
