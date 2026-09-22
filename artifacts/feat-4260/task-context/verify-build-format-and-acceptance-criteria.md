### task: verify-build-format-and-acceptance-criteria


**Files:**
- None modified — this task runs verification commands against the five files changed by the four prior tasks, and only edits anything if `dotnet format` reports a violation.

- [ ] **Step 1: Confirm no two-argument call site survives anywhere in `backend/`**

Run: `grep -rn "UpdateCronSchedule(" backend/src backend/test`

Expected: every match is either a test **method name** (`UpdateCronSchedule_...`) or a declaration/call with **three** arguments. Specifically the declarations must read `UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)` (in `ICronScheduler.cs` and `HangfireRecurringJobScheduler.cs`), and every call must have three comma-separated arguments. There must be no match of the form `UpdateCronSchedule(x, y)`.

- [ ] **Step 2: Confirm the Application layer gained no Hangfire coupling (NFR-3, as amended by A-1)**

Run: `grep -rn "^using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/`

Expected output: no matches (exit code 1). (Note: `Anela.Heblo.Application.csproj:12` already carries a `Hangfire.Core` package reference for other features — that is pre-existing and out of scope. The checkable criterion is that no file under `Features/BackgroundJobs/` in the Application project gains a Hangfire `using`.)

Run: `grep -c "^using" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`

Expected output: `0`

- [ ] **Step 3: Confirm the DI registration and lifetime are unchanged**

Run: `grep -n "ICronScheduler" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

Expected output: exactly one line — `services.AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>();` (around line 376), unmodified.

- [ ] **Step 4: Confirm the handler's blast radius is one line (FR-2)**

Run: `git diff HEAD~5 -- backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`

Expected: exactly one removed line and one added line, both the `_scheduler.UpdateCronSchedule(...)` statement. No change to the validation, the not-found branch, the `try`/`catch`, the response construction, the logging, or the ordering relative to `await _repository.UpdateAsync(job, cancellationToken);`.

- [ ] **Step 5: Confirm the change touched only the expected files (NFR-2, NFR-4, FR-6)**

Run: `git diff --name-only HEAD~5`

Expected output: exactly these six paths and nothing else —

```
backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs
```

In particular there must be **no** controller file, no `*Request.cs`/`*Response.cs` DTO, no EF migration under `backend/src/Anela.Heblo.Persistence/Migrations/`, no file under `frontend/`, and no file under `docs/`. `docs/superpowers/plans/2026-03-29-db-driven-cron-config.md` and `docs/superpowers/plans/2026-05-27-consolidate-hangfire-recurringjob-registration.md` show the old two-parameter signature and must be left alone — they are point-in-time records of completed work, not living specs.

- [ ] **Step 6: Full solution build**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors. The warning count must not increase versus the pre-change baseline.

- [ ] **Step 7: Format check**

Run: `cd backend && dotnet format --verify-no-changes`

Expected: exits 0 with no output. If it reports violations in any of the touched files, run `cd backend && dotnet format` (without `--verify-no-changes`), then re-stage the affected file and `git commit --amend` onto whichever of the prior tasks' commits introduced it — do not add a separate "fix formatting" commit for a change this small.

- [ ] **Step 8: Run the full BackgroundJobs + Hangfire test surface**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs|FullyQualifiedName~Hangfire"`

Expected: `Passed!` with 0 failed. This must include, all green:
- `UpdateRecurringJobCronHandlerTests` — all 5 tests, with the happy path asserting `UpdateCronSchedule("my-job", newCron, "Europe/Prague")` exactly once.
- `HangfireRecurringJobSchedulerTests` — the 3 pre-existing tests plus `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone`, the 3 rows of `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException`, and `UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged`.
- `RecurringJobSeederTests` — the 5 pre-existing tests plus `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`.
- `RecurringJobDiscoveryServiceTests`, `HangfireJobEnqueuerTests`, `HangfireFailedJobCounterTests` — untouched, must still pass.

- [ ] **Step 9: Run the full backend test suite**

Run: `cd backend && dotnet test`

Expected: all tests pass, 0 failed. No frontend build and no E2E run are required for this change — there is no HTTP contract change, so the generated TypeScript client produces an empty diff.

- [ ] **Step 10: Final gate — working tree clean**

Run: `git status`

Expected: clean working tree (nothing to commit), unless Step 7 required a formatting fix that was amended into a prior commit — in which case re-run `git status` after the amend and confirm it is clean then.

---
