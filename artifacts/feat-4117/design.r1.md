# Design: Remove framework coupling from `RecurringJobConfiguration`

## Component Design

No new or restructured components. Edits are confined to the existing Domain entity and its direct unit test file; no interface, DI, or call-site changes.

- **`RecurringJobConfiguration`** (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`)
  - Responsibility unchanged: the Domain entity encapsulating a recurring background job's configuration, with guard-clause validation on construction and mutation.
  - Public surface unchanged: constructor and methods `UpdateConfiguration`, `Enable`, `Disable`, `UpdateCronExpression` keep identical signatures.
  - Internal change only: remove `[Required]`/`[MaxLength(n)]` attributes from `JobName`, `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `LastModifiedBy`; replace all 14 `throw new ValidationException("...")` guard-clause sites with `throw new ArgumentException("...")`, same message text; remove `using System.ComponentModel.DataAnnotations;`.

- **`RecurringJobConfigurationConfiguration`** (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`)
  - Untouched. Remains the sole source of truth for `HasMaxLength(...)`/`IsRequired()` DB-level constraints (already matches the entity's former attribute values).

- **`RecurringJobConfigurationTests`** (`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`)
  - The 9 `Assert.Throws<ValidationException>` call sites become `Assert.Throws<ArgumentException>`.
  - Remove the now-unused `using System.ComponentModel.DataAnnotations;`.
  - Rename the 6 test methods that say `ShouldThrowValidationException` to `ShouldThrowArgumentException` so names match what they assert.

- **Downstream consumers** (`UpdateRecurringJobStatusHandler`, `UpdateRecurringJobCronHandler`, `RecurringJobSeeder`)
  - No code changes. All catch guard-clause failures via generic `catch (Exception ex)` and surface `ex.Message`; behavior is unaffected by the exception-type swap.

## Data Schemas

No schema, API, or event-payload changes.

- **Database**: no migration. `RecurringJobConfigurationConfiguration`'s Fluent API already governs column length/required constraints for the `RecurringJobConfigurations` table; EF Core ignores the entity's (now-removed) `DataAnnotations` since Fluent API takes precedence. Verify no pending model changes after the edit (e.g. `dotnet ef migrations has-pending-model-changes`).
- **API/contracts**: none — this entity has no direct API surface; only its exception type changes internally, and no handler pattern-matches on `ValidationException` for this entity today.
