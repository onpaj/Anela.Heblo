## Module
BackgroundJobs

## Finding
`FailedJobsTile` is in the Application layer but takes `Hangfire.JobStorage` — a concrete infrastructure type — as a constructor parameter:

- **File**: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`
- **Line 2**: `using Hangfire;`
- **Line 9**: `private readonly JobStorage _jobStorage;`
- **Line 35**: `_jobStorage.GetMonitoringApi().FailedCount()` — calls Hangfire's monitoring API directly.

## Why it matters
`JobStorage` is a concrete Hangfire class (infrastructure). The Application layer must depend only on abstractions, never on Infrastructure. This creates a hard compile-time coupling from `Anela.Heblo.Application` → Hangfire, mirroring the same violation as `HangfireJobRegistrationHelper` in the same module. It also makes the tile untestable without a real Hangfire storage.

## Suggested fix
Two equally valid options:

1. **Thin interface**: Introduce `IFailedJobCounter` in the Application layer (`Features/BackgroundJobs/Services/`), implement it in the API project using `JobStorage`, and register the binding in `AddHangfireServices`. `FailedJobsTile` injects `IFailedJobCounter` instead.

2. **Move the tile**: Relocate `FailedJobsTile` to `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/` (alongside the other Hangfire infrastructure) where a direct `JobStorage` dependency is appropriate.

Option 1 keeps the tile in Application without breaking layering. Option 2 is simpler if the tile has no domain logic that needs to stay in Application.

---
_Filed by daily arch-review routine on 2026-05-28._