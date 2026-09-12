# [arch-review] BackgroundJobs: Domain entity does not validate CRON expression format — invariant only enforced at handler level

## Module
BackgroundJobs

## Finding
`RecurringJobConfiguration` has two methods that accept a CRON expression:
- Constructor (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`, lines 47–79)
- `UpdateCronExpression` (lines 128–138)

Both validate only that the value is non-null/non-empty. Neither checks that the string is a *valid* CRON expression.

The format validation exists only in `UpdateRecurringJobCronHandler` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`, lines 42–52) via `NCrontab.Advanced.CrontabSchedule.Parse()`. The constructor path has **no** format check at all — `RecurringJobSeeder` calls the constructor directly (line 31 of `RecurringJobSeeder.cs`) with CRON values that come from code constants, which are trusted. However, the domain entity is the invariant boundary, and the domain invariant (a CRON string must be parseable) is not enforced there.

A non-handler caller (a future service, a test, an admin migration script) can call `UpdateCronExpression` or the constructor with `"not-a-cron"` and the entity will accept it, and the repository will persist it. The only protection is handler-level validation, which is an application-layer concern, not a domain-layer guarantee.

## Why it matters
Domain entities are the last line of defence for business invariants. "A CRON expression must be syntactically valid" is a domain invariant of `RecurringJobConfiguration`, not an application-layer input-validation rule. When it lives only in the handler, the invariant is enforced on one entry path and silently absent on all others.

This violates the principle that domain models should be impossible to put into an invalid state.

## Suggested fix
Add a static validation helper to the domain entity (or a value object `CronExpression`) that performs a structural check without requiring an external library. A lightweight option is to count CRON fields (standard 5-field or Quartz 6-field split on whitespace) and reject strings that don't match either form:

```csharp
private static void ValidateCronFormat(string cronExpression)
{
    var fields = cronExpression.Trim().Split(new[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries);
    if (fields.Length is not (5 or 6))
        throw new ArgumentException($"'{cronExpression}' is not a valid CRON expression.", nameof(cronExpression));
}
```

Call this from the constructor and `UpdateCronExpression`. If the team prefers the NCrontab parse as the canonical check, `NCrontab.Advanced` is already a dependency of the Application project — a shared Application-layer value object (`CronExpression`) wrapping the parse could also work, keeping the library out of Domain.

---
_Filed by daily arch-review routine on 2026-09-09._
