## Module
Dashboard

## Finding

`EnableTileHandler` (`Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs`) and `DisableTileHandler` (`Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs`) are structurally identical:

- Same 4 constructor dependencies: `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator`
- Same provisioning call before the lock (lines 37–38 in both): `await _mediator.Send(new GetUserSettingsRequest { UserId = userId }, cancellationToken);`
- Same lock acquisition pattern (lines 40 in both)
- Same repository lookup and null-guard

The only semantic difference is `IsVisible = true` vs `IsVisible = false`, plus `EnableTileHandler` appends a new tile row when the tile doesn't yet exist (lines 55–65) while `DisableTileHandler` does nothing when the tile is absent.

## Why it matters

Real duplication: if the provisioning strategy, locking logic, or `TimeProvider` usage changes it must be updated in two places. The pattern will be copied a third time if a "move tile" or "pin tile" feature is added.

## Suggested fix

Extract the shared scaffold — provisioning call, lock acquisition, repository fetch, null-guard, `settings.LastModified` update, and `UpdateAsync` call — into a private base class or a package-private `UserDashboardSettingsMutator` service. Both handlers inject that collaborator and supply only the diff logic (set visibility, optionally append row).

Alternatively, collapse both into a single `SetTileVisibilityHandler` that accepts a `bool isVisible` on the request; the two controller actions already exist to maintain the clean HTTP surface.

---
_Filed by daily arch-review routine on 2026-06-01._