# Spec: Materialize GetAvailableTilesHandler result (#4473)
## Problem
`GetAvailableTilesHandler.Handle` assigns a lazy `Select` to `GetAvailableTilesResponse.Tiles` (`IEnumerable<DashboardTileDto>`); each enumeration re-queries the registry and reallocates DTOs. `GetTileDataHandler` already materializes with `.ToArray()`.
## Requirements
- R1: `Tiles` holds a materialized collection (`.ToArray()` on the projection).
- R2: No change to DTO shape, API contract, or ordering.
## Acceptance
- Enumerating `response.Tiles` twice calls `ITileRegistry.GetAvailableTiles()` once and yields the same instances.
- Existing `GetAvailableTilesHandlerTests` pass.
## Out of scope
Changing `Tiles` type, other handlers.
