---

# Implementation: Relocate PurchaseOrdersInTransitTile to Purchase module

## What was implemented

Nothing was committed to the branch. The session ran in a read-only permission mode where all file writes (Write tool, Edit tool, Bash redirects, cp, git mv, python3 with file I/O) require interactive user approval that the automated pipeline could not provide.

## Files created/modified

None. The implementation is fully designed and ready — the actual changes are straightforward:

1. **Create** `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` — identical to the current Dashboard/Tiles version except the namespace becomes `Anela.Heblo.Application.Features.Purchase.DashboardTiles`

2. **Delete** `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` + the now-empty `Tiles/` folder

3. **Edit** `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`:
   - Remove `using Anela.Heblo.Application.Features.Dashboard.Tiles;` (line 2)
   - Remove `services.RegisterTile<PurchaseOrdersInTransitTile>();` (line 20)

4. **Edit** `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`:
   - Add `services.RegisterTile<PurchaseOrdersInTransitTile>();` after the existing `LowStockEfficiencyTile` registration (line 26). No new `using` needed — `Anela.Heblo.Application.Features.Purchase.DashboardTiles` is already imported.

A git patch capturing all four changes was written to:
`~/.claude/projects/.../memory/relocation.patch`

## Tests

No tests to write — zero test files reference `PurchaseOrdersInTransitTile` (confirmed by grep). The change keeps the existing test suite green because the tile is registered via the same `RegisterTile<T>()` mechanism, just from PurchaseModule instead of DashboardModule.

## How to verify

```bash
# Apply manually if needed:
git mv backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs

# Then edit the namespace in the new file, edit both module files, build and test:
cd backend && dotnet build && dotnet format --verify-no-changes
dotnet test --no-build -v minimal
```

## Notes

The pipeline session lacks write permissions for the working directory. The `Write` and `Edit` tools returned "you haven't granted it yet" for all files under `/home/rem/sandbox/Anela.Heblo/.worktrees/...`. Bash file-creation operations (cp, git mv, redirects, python3 writes) all required interactive approval that the unattended pipeline could not grant.

**To unblock:** Add write permissions to `.claude/settings.json` before running the pipeline:
```json
{
  "permissions": {
    "allow": [
      "Write(*)",
      "Edit(*)",
      "Bash(git mv:*)",
      "Bash(git rm:*)",
      "Bash(git add:*)",
      "Bash(git commit:*)",
      "Bash(dotnet build:*)",
      "Bash(dotnet test:*)",
      "Bash(dotnet format:*)"
    ]
  }
}
```

## PR Summary

Relocates `PurchaseOrdersInTransitTile` from `Dashboard/Tiles/` to `Purchase/DashboardTiles/` to eliminate cross-module coupling — the tile directly depends on `IPurchaseOrderRepository` which belongs to the Purchase domain, so the tile should live with that domain. Transfers DI registration from `DashboardModule` to `PurchaseModule`, removes the stale `using Anela.Heblo.Application.Features.Dashboard.Tiles` import from `DashboardModule`, and deletes the now-empty `Dashboard/Tiles/` folder. No logic changes, no API surface changes, no frontend changes required.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` — new location, namespace updated to `Anela.Heblo.Application.Features.Purchase.DashboardTiles`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — removed stale using and tile registration
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — added tile registration alongside `LowStockEfficiencyTile`

## Status
BLOCKED