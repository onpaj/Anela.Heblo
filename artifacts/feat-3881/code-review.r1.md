## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`, and two new regression test files) against `spec.r1.md`.

- `services.AddSingleton(_ => JobStorage.Current);` was removed from `DashboardModule.AddDashboardModule()` along with the now-unused `using Hangfire;` directive and its explanatory comment. `DashboardModule.cs` retains its other three registrations (`IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `IUserDashboardSettingsMutator`) unchanged — matches FR-1 and FR-2 exactly.
- The equivalent registration was added to `AddHangfireServices` in `ServiceCollectionExtensions.cs`, placed after `AddHangfireServer(...)`/`HangfireDashboardTokenAuthorizationFilter` registration (i.e. after Hangfire storage is configured via `AddHangfire(...).UseMemoryStorage()`/`UsePostgreSqlStorage(...)`) and before the `IBackgroundWorker`, `IJobEnqueuer`, `IFailedJobCounter`, `ICronScheduler` adapter registrations that depend on it — satisfies the ordering requirement in FR-1's acceptance criteria and NFR-2 discoverability.
- Verified directly: `HangfireBackgroundWorker` and `HangfireFailedJobCounter` (in `API/Infrastructure/Hangfire/`) are the only consumers of the `JobStorage` service type in `backend/src`, and both are registered in the same `AddHangfireServices` method as the relocated singleton — no other file needs to change.
- `Program.cs` call order (`AddApplicationServices` before `AddHangfireServices`) is unaffected; since DI registration is lazy, this doesn't matter for correctness, consistent with the spec's NFR discussion.
- Two new regression tests (`DashboardModuleTests.AddDashboardModule_DoesNotRegisterJobStorage`, `HangfireServicesTests.AddHangfireServices_RegistersJobStorage`) directly assert the new ownership boundary via `ServiceCollection` inspection — sound and minimal.
- Confirmed independently: `dotnet build Anela.Heblo.sln` succeeds with 0 errors (pre-existing nullable-reference warnings elsewhere in the test suite are unrelated to this change).
- This is a pure DI-registration relocation with no behavior change, matching spec scope exactly. No correctness issues found.
