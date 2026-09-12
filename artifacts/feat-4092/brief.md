## Module / File
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)
No test file exists for this class.

## What's not tested
The static `Calculate` method has four distinct branches, none of which are exercised:
1. **Job disabled** — returns `null` immediately when `isEnabled` is false.
2. **Timezone not found** — catches `TimeZoneNotFoundException`, logs a warning, returns `null` instead of propagating.
3. **Invalid CRON expression** — catches `CrontabException`, logs a warning, returns `null`.
4. **Happy path** — converts `utcNow` to the job's local timezone, computes the next CRON occurrence, converts back to UTC and returns it.

The DST-aware timezone conversion (`ConvertTimeFromUtc` → `ConvertTimeToUtc`) is the most subtle path: an ambiguous or skipped local time around a DST boundary could produce a wrong result or throw, and neither case is caught.

## Why it matters
`RecurringJobNextRunCalculator.Calculate` determines the `NextRunAt` timestamp displayed to operators and used for scheduling decisions. A bad timezone id (e.g. a Windows id deployed on Linux) silently returns `null` today — but if the suppression behavior changes or the logging contract is broken, the scheduler could start propagating the exception. More concretely, if an invalid CRON expression reaches this method without a prior validation gate, jobs appear healthy while never firing.

## Suggested approach
Unit tests (no infrastructure needed — purely static math):
- `isEnabled = false` → returns `null`, no other work done.
- Unknown timezone id → returns `null`, warning logged (mock `ILogger`).
- Malformed CRON string → returns `null`, warning logged.
- Valid inputs → returned UTC datetime matches the expected next occurrence for a simple schedule (e.g. `"0 6 * * *"` from a fixed `utcNow`).
- DST edge case → a spring-forward gap or autumn ambiguity produces a sensible result (not a throw).

Effort: small — one test class, no mocks beyond `ILogger`. `FakeTimeProvider` is not needed since `utcNow` is a parameter.

---
_Filed by weekly coverage-gap routine on 2026-09-07. Based on CI run #33791274852 (a21f134808e61a338c3261f8523316d2752ebea3)._
