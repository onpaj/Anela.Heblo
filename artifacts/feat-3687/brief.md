# [arch-review] BackgroundJobs: RecurringJobConfiguration doesn't persist TimeZoneId; NextRunCalculator silently uses default timezone for all jobs

## Module
BackgroundJobs

## Finding
`RecurringJobMetadata` supports a per-job timezone via `TimeZoneId` (defaulting to `"Europe/Prague"`), and `HangfireJobRegistrationHelper.RegisterOrUpdate` correctly passes it to Hangfire so jobs are scheduled in the right timezone:

```csharp
// backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs  line 83
TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
```

However, `RecurringJobConfiguration` (the persisted entity) has no `TimeZoneId` column, and `RecurringJobDto` has no timezone field. As a result, `RecurringJobNextRunCalculator` always derives "next run" using the constant default:

```csharp
// backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs  line 24
tz = TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId);
```

This means that if any job is ever implemented with a non-Prague timezone, Hangfire will correctly schedule it in that timezone, but the UI will display the wrong "next run" time because the calculator uses Prague unconditionally.

The data model already acknowledges timezones as a first-class concern (the `TimeZoneId` property on `RecurringJobMetadata`, the timezone plumbing in `HangfireJobRegistrationHelper`), but the persistence model drops it — an inconsistency in the domain representation.

## Why it matters
This is a latent display bug. It's dormant right now only because all current jobs happen to use the default timezone. The moment a job with a different timezone is added, the "Next run" column in the admin UI will show a wrong time without any error or warning. It will also be hard to diagnose because both the Hangfire dashboard and the API endpoint return contradictory data.

## Suggested fix
Add `TimeZoneId` to `RecurringJobConfiguration` and `RecurringJobDto`, and pass it through to the calculator:

1. **Domain entity** (`RecurringJobConfiguration.cs`): add `public string TimeZoneId { get; private set; }` — set in constructor, included in `UpdateConfiguration`.
2. **DTO** (`RecurringJobDto.cs`): add `public string TimeZoneId { get; set; }`.
3. **Mapping** (`BackgroundJobsMappingProfile.cs`): `CreateMap` already covers same-named properties; no explicit mapping needed if names match.
4. **Calculator** (`RecurringJobNextRunCalculator.cs`): change the signature to `Calculate(string cronExpression, bool isEnabled, string timeZoneId, DateTime utcNow, ...)` and use the passed-in `timeZoneId` instead of the constant.
5. **Callers** (`GetRecurringJobHandler`, `GetRecurringJobsListHandler`): pass `dto.TimeZoneId`.
6. **Seeder** (`RecurringJobSeeder`): pass `job.Metadata.TimeZoneId` to the `RecurringJobConfiguration` constructor (add the parameter).
7. **Migration**: add a `TimeZoneId` column with default `'Europe/Prague'` (no data loss, all existing rows are already using the default).

---
_Filed by daily arch-review routine on 2026-07-18._
