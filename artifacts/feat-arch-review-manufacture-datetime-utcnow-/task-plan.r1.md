Plan saved to `docs/superpowers/plans/2026-06-12-manufacture-timeprovider-consistency.md`.

The plan breaks the work into 4 tasks executing in TDD red-green order:

- **Task 0** — Baseline build + targeted test run to catch preexisting breakage.
- **Task 1** — Duplicate handler: assertion already has `Mock<TimeProvider>` infrastructure in place; just adds two `Should().Be(FixedNow.UtcDateTime)` assertions, replaces 2 `DateTime.UtcNow` calls, commits.
- **Task 2** — Update handler: migrates the test class from `TimeProvider.System` → `Mock<TimeProvider>`, tightens the existing `BeCloseTo(DateTime.UtcNow, ...)` assertion at line 223 to exact frozen-time equality, replaces 1 `DateTime.UtcNow` call, commits.
- **Task 3** — Create handler: migrates the test class from `TimeProvider.System` → `Mock<TimeProvider>`, adds two `CreatedDate`/`StateChangedAt` assertions to the existing `Handle_ShouldCreateOrderWithCorrectBasicProperties` test, replaces 2 `DateTime.UtcNow` calls, commits.
- **Task 4** — Final sweep: grep-confirm no `DateTime.UtcNow` left, full build, `dotnet format --verify-no-changes`, and full `Anela.Heblo.Tests.Features.Manufacture` namespace test run.

Per pipeline instructions, skipping the execution-choice handoff prompt.