Plan saved to `docs/superpowers/plans/2026-06-01-move-hangfire-job-registration-helper.md`.

## Summary

Six-task, TDD-flavored plan for relocating `HangfireJobRegistrationHelper` from the Application layer to the API layer:

1. **Baseline** — verify clean build, green tests, and snapshot the `using Hangfire` count in Application as evidence (7 → 6).
2. **Move source** — `git mv` the helper + change namespace to `Anela.Heblo.API.Infrastructure.Hangfire`; intentionally leaves the test project broken to confirm Task 3 is needed.
3. **Move tests** — `git mv` test file to mirror the production path, update one using directive and the namespace. The `HangfireTestFixture` stays put.
4. **Cleanup** — remove the now-unused `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` from `RecurringJobDiscoveryService.cs`. Deliberately keep it in `HangfireRecurringJobScheduler.cs` (still needed for `IHangfireRecurringJobScheduler`).
5. **Validate** — repo-wide stale-reference grep, full build/test/format, `git log --follow` check.
6. **Commit** — single atomic conventional-commit, explicitly documenting why `Hangfire.Core` stays on `Application.csproj` (six other callers; out of scope per Amendment B).

Every step includes the exact command, expected output, and any code change inline. The self-review table at the end maps each FR/NFR/Decision/Risk from the spec and arch-review to the task that addresses it.