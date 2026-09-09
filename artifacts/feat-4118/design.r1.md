# Design: Enforce CRON expression format validation in the `RecurringJobConfiguration` domain entity

## Component Design

### `RecurringJobConfiguration` (unchanged public shape)
`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`

Responsibility gains one clause: guaranteeing its own `CronExpression` field is always a structurally well-formed CRON string (5 or 6 whitespace-delimited fields), on every write path, not just the ones already exercised by the Application layer.

New private static member:

```csharp
/// <summary>
/// Structural-only check: confirms the value has the field count of a standard
/// (5-field) or Quartz-style (6-field, leading seconds) CRON expression. Does
/// NOT validate per-field value ranges (e.g. "99 99 * * *" passes this check) —
/// that stronger semantic validation is performed by
/// UpdateRecurringJobCronHandler.IsValidCronExpression (NCrontab.Advanced) on
/// the one user-facing write path. This check exists so the entity itself
/// cannot be put into a state with an obviously malformed CronExpression via
/// any caller, not only the MediatR handler.
/// </summary>
private static void ValidateCronFormat(string cronExpression)
{
    var fields = cronExpression.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    if (fields.Length is not (5 or 6))
        throw new ValidationException($"'{cronExpression}' is not a valid CRON expression.");
}
```

Call sites (each immediately after that method's existing `IsNullOrWhiteSpace(cronExpression)` guard, before any field assignment):
- Constructor
- `UpdateConfiguration(...)`
- `UpdateCronExpression(...)`

No new public members, no new types, no change to the `CronExpression` property's type or visibility.

### Unaffected components (verified, no change required)
- `UpdateRecurringJobCronHandler` — its `IsValidCronExpression` (NCrontab-based) gate still runs first on the admin-facing update path, so no valid-today request starts failing.
- `RecurringJobSeeder` — its constructor and `UpdateConfiguration` calls use only already-valid, code-constant CRON strings; behavior unchanged for all current job metadata.
- `RecurringJobConfigurationConfiguration` (EF Core mapping), `RecurringJobDto`, `UpdateJobCronRequestBody`, `HangfireRecurringJobScheduler`, `RecurringJobNextRunCalculator`, `RecurringJobDiscoveryService` — all consume `CronExpression` as `string`; no shape change.

## Data Schemas
No schema change. `CronExpression` remains `string`, `[Required]`, `[MaxLength(50)]`, mapped as-is by `RecurringJobConfigurationConfiguration`. No new migration.

No API request/response shape changes: `UpdateRecurringJobCronRequest`, `UpdateRecurringJobCronResponse`, `RecurringJobDto`, `UpdateJobCronRequestBody` are all unaffected — the new failure mode (a `ValidationException` thrown from inside `UpdateCronExpression`) is already caught by `UpdateRecurringJobCronHandler`'s existing `catch (Exception ex)` block and mapped to the existing `ErrorCodes.RecurringJobUpdateFailed` response shape; this scenario is unreachable in practice on that path today (the handler's own `IsValidCronExpression` already rejects malformed input before calling the entity), so no new response scenario needs to be documented for the API consumer.
