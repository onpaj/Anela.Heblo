# Design: Fix CS1503 build break in RecurringJobSeeder

## Component Design
Only `RecurringJobSeeder.SeedDefaultConfigurationsAsync` is touched. In the `else if` branch, the condition becomes `HasSeededFieldsChanged(existingConfig, config)`. `existingConfig` is the `RecurringJobConfiguration` resolved via `existingByName.TryGetValue`; the same variable is already used inside the branch body (`existingConfig.UpdateConfiguration(...)`, `UpdateAsync(existingConfig, ...)`). No new components or interfaces.

## Data Schemas
None. No database, API or event payload changes.
