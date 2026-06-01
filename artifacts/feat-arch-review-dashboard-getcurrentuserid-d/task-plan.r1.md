Plan saved to `docs/superpowers/plans/2026-06-01-consolidate-getcurrentuserid.md`.

## Summary

The plan decomposes the refactor into 10 bite-sized tasks following TDD with frequent commits:

1. **Task 1** — Add `protected string GetCurrentUserId()` to `BaseApiController` (with `System.Security.Claims` using).
2. **Task 2** — Cover the new method with 6 unit tests on a new `BaseApiControllerTests` fixture (uses a test-only `TestController : BaseApiController`).
3. **Tasks 3–5** — Remove the duplicate private methods from `DashboardController`, `CarrierCoolingController`, `GiftSettingsController` (each as its own commit, including the unused `using` removal and the codebase-wide sweep at the end of Task 5).
4. **Task 6** — Verification sweep covering Architecture Review amendments #1 (also catch `InvalidOperationException`) and #5 (no other `UnauthorizedAccessException` thrower).
5. **Task 7** — New `UnauthorizedAccessExceptionHandler : IExceptionHandler` returning `401 ProblemDetails` with no `detail` field, exception logged server-side.
6. **Task 8** — 3 unit tests pinning the 401 status, body shape, and the no-message-leak property.
7. **Task 9** — `services.AddExceptionHandler<...>()` + `AddProblemDetails()` registration and `app.UseExceptionHandler()` wiring before `UseRouting()`.
8. **Task 10** — Final validation (`dotnet build`, `dotnet format --verify-no-changes`, full test suite, sweep checks).

Each task lists exact file paths and line ranges, full code blocks, exact commands with expected output, and a final `git commit`. The plan also embeds the architecture review's six amendments and explicitly maps every spec requirement to a task in the self-review notes at the bottom.