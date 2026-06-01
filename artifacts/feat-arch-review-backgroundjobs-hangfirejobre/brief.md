## Module
BackgroundJobs

## Finding
`HangfireJobRegistrationHelper` is a public static class in the Application layer that directly calls the Hangfire infrastructure API:

- **File**: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`
- **Lines 4, 52–68, 71–86**: `using Hangfire;` + `RecurringJob.AddOrUpdate<TJob>(...)` and reflection dispatch to `RegisterOrUpdateGeneric<TJob>` which calls the Hangfire static API.

The module's own `BackgroundJobsModule.cs` comment explicitly acknowledges the correct rule: *"Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler) are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices because their implementations live in the API project (Clean Architecture dependency rule)."*

Despite that, this static helper — which does exactly the same kind of infrastructure work — was placed in the Application project.

## Why it matters
Clean Architecture forbids the Application layer from depending on Infrastructure. `Hangfire.RecurringJob` is a concrete infrastructure class. Placing it in Application introduces a hard dependency from Application → Hangfire in the compiled assembly, which defeats the architectural boundary the codebase otherwise respects. It also makes it impossible to test registration logic without a real Hangfire storage.

## Suggested fix
Move `HangfireJobRegistrationHelper` to the API project alongside the other Hangfire infrastructure code, e.g. `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`. The callers (startup discovery and runtime CRON update in the API/Infrastructure layer) already live there and the class has no callers in Application itself.

---
_Filed by daily arch-review routine on 2026-05-28._