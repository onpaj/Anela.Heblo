### task: add-defaults-helper
Create `Infrastructure/UserDashboardSettingsDefaults.cs` (internal static), extracting the logic currently in `GetUserSettingsHandler`:
- `AutoShowTiles(ITileRegistry)` -> `DefaultEnabled && AutoShow` tiles.
- `BuildDefault(userId, autoShowTiles, now)` -> new `UserDashboardSettings` (IsVisible=true, DisplayOrder=index, timestamps=now, tile `DashboardSettings` back-reference set).
- `BackfillMissing(settings, autoShowTiles, now)` -> appends missing tiles at maxOrder+i+1, sets `settings.LastModified`, returns bool changed.
- `ProjectMissing(settings, autoShowTiles, now)` -> returns the DTO tile list = existing tiles + missing AutoShow tiles (same ordering rule) **without mutating** `settings` or `settings.Tiles` (tracked EF entities).
Tests: `Infrastructure/UserDashboardSettingsDefaultsTests.cs` incl. equivalence test (BackfillMissing result == ProjectMissing result) and a no-mutation assertion.
Verify: `dotnet test --filter UserDashboardSettingsDefaultsTests`.

