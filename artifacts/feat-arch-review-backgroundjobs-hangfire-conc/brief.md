## Module
BackgroundJobs

## Finding
Two concrete Hangfire adapter classes reside in `Anela.Heblo.Application` and import the `Hangfire` namespace directly:

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs` — `using Hangfire;`, depends on `IBackgroundJobClient`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` — `using Hangfire;`, calls `RecurringJob.AddOrUpdate<TJob>(...)`

As a result `Anela.Heblo.Application.csproj` carries a hard `PackageReference` on `Hangfire.Core` (visible in the project file). The Application layer only references the Domain project, so this is currently the only reason the Application project needs Hangfire.

## Why it matters
Clean Architecture requires the Application layer to depend only on Domain, with Infrastructure concerns (Hangfire, EF Core, HTTP clients, etc.) confined to Infrastructure/API projects. Having Hangfire types in the Application layer:

- Breaks the dependency rule: Application → Infrastructure
- Couples scheduling mechanics to business handlers and makes them harder to unit-test without a real Hangfire `IBackgroundJobClient`
- Prevents swapping the job scheduler without touching Application code

The interfaces (`IHangfireJobEnqueuer`, `IHangfireRecurringJobScheduler`) are correctly defined in Application — only the concrete implementations are misplaced.

## Suggested fix
Move the concrete classes to the API infrastructure layer where Hangfire is already fully configured:

```
backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs
backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
```

Register them in `BackgroundJobsModule.AddBackgroundJobsModule` or, if they depend on API-level types, in `Program.cs`/an API extension. Remove `Hangfire.Core` from `Anela.Heblo.Application.csproj` once the move is done.

---
_Filed by daily arch-review routine on 2026-05-27._