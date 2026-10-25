# add-defaults-helper r1
Added internal static `UserDashboardSettingsDefaults` (AutoShowTiles, BuildDefault, BackfillMissing, ProjectMissing) in Dashboard/Infrastructure, with 6 unit tests incl. equivalence and no-mutation. `dotnet test --filter UserDashboardSettingsDefaultsTests`: 6/6 pass. Handler not yet changed (later tasks).
