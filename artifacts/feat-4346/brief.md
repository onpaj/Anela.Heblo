# Build broken on main: CS1503 in RecurringJobSeeder.HasSeededFieldsChanged call

`RecurringJobSeeder.cs` line 51 calls `HasSeededFieldsChanged(existing, config)` where `existing` is the full `List<RecurringJobConfiguration>` from `_repository.GetAllAsync(...)`. The method expects a single `RecurringJobConfiguration`; the per-item variable is `existingConfig` (from `existingByName.TryGetValue`). CS1503 breaks `dotnet build`/`dotnet test` for the whole solution on main. Introduced by PR #4324 (closes #4318).

Fix: `HasSeededFieldsChanged(existing, config)` -> `HasSeededFieldsChanged(existingConfig, config)`.

Source: GitHub issue #4346.
