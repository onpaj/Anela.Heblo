## Module
BackgroundJobs

## Finding
`RecurringJobStatusChecker` — the Application-layer implementation of the Domain-owned `IRecurringJobStatusChecker` — is placed at:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs
```

with namespace `Anela.Heblo.Application.Features.BackgroundJobs`.

Every other service implementation in the same module lives in the `Services/` subfolder:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IJobEnqueuer.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
```

`RecurringJobStatusChecker` follows the same service pattern as `RecurringJobSeeder` but is orphaned at the module root.

## Why it matters
The module's own conventions (and `docs/architecture/filesystem.md`) place service implementations under `Services/`. A developer adding a new job or browsing the module for status-checking logic will look in `Services/` first and miss the class. It also pollutes the module root (which otherwise contains only mapping profiles, the module registration, and the next-run calculator utility).

## Suggested fix
Move the file:
- From: `BackgroundJobs/RecurringJobStatusChecker.cs`
- To: `BackgroundJobs/Services/RecurringJobStatusChecker.cs`

Update namespace from `Anela.Heblo.Application.Features.BackgroundJobs` to `Anela.Heblo.Application.Features.BackgroundJobs.Services`.

Update the `using` in `BackgroundJobsModule.cs` if needed. No logic changes required.

---
_Filed by daily arch-review routine on 2026-09-03._
