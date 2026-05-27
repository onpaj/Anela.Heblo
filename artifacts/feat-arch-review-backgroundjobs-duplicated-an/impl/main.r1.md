---

# Implementation: Consolidate Hangfire RecurringJob Registration

## Status: DONE ✅

All 6 tasks completed, both spec and code quality reviews passed for each.

### What was built

Extracted `HangfireJobRegistrationHelper` — a single shared helper that replaces two independent copies of reflection-based `RecurringJob.AddOrUpdate<TJob>` plumbing. Both startup registration (`RecurringJobDiscoveryService`) and runtime CRON updates (`HangfireRecurringJobScheduler`) now delegate to it, and both produce identical Hangfire records via the `RecurringJobOptions` overload.

### Key architectural correction applied

Spec FR-6 proposed placing the helper in the API project — the architecture review correctly flagged this as a circular dependency. Helper lives in `Anela.Heblo.Application/Features/BackgroundJobs/Services/` (public static) where both consumers can reach it via the existing `API → Application` reference.

### Commits

| SHA | Description |
|-----|-------------|
| `525245a6` | `test: add HangfireJobRegistrationHelper unit tests (red)` |
| `7ce1ac77` | `feat(background-jobs): add HangfireJobRegistrationHelper` |
| `607b23e0` | `refactor(background-jobs): route RecurringJobDiscoveryService through HangfireJobRegistrationHelper` |
| `be6add90` | `refactor(background-jobs): route HangfireRecurringJobScheduler through HangfireJobRegistrationHelper` |
| `eb63d094` | `test: add HangfireRecurringJobScheduler parity tests` |

### Validation results

- **88 BackgroundJobs tests** — 0 failures
- **Build** — 0 errors
- **Single `RecurringJob.AddOrUpdate` call site** confirmed in `HangfireJobRegistrationHelper.cs` only
- **No orphaned** `RegisterRecurringJobInternal` or `UpdateJobInternal` references
- **Legacy `TimeZoneInfo` overload** fully eliminated