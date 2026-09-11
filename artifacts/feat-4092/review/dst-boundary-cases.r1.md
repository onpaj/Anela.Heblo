# Code Review: RecurringJobNextRunCalculator DST Boundary Cases

## Summary
The implementation adds two focused DST boundary tests to `RecurringJobNextRunCalculatorTests`, covering the autumn ambiguous-hour and spring-forward-gap cases exactly as specified. Both tests are non-vacuous with concrete UTC assertions and exception types. No production code was modified. All 7 tests (5 pre-existing + 2 new) pass, and the full test-project regression confirms no regressions.

## Review Result: PASS

### task: dst-boundary-cases
**Status:** PASS

#### Spec Compliance
- **FR-5 Autumn ambiguous hour**: Test `Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour` drives the implementation with `utcNow = 2026-10-24T20:00:00Z`, cron `"30 2 * * *"`, and timezone `Europe/Prague`. The next local occurrence lands on 2026-10-25 02:30 (ambiguous, occurs twice that day: once as CEST/UTC+2 at 00:30Z, once as CET/UTC+1 at 01:30Z). Test asserts the result is exactly `2026-10-25T01:30:00Z` with `DateTimeKind.Utc` — verifying the implementation resolves via the zone's standard offset (CET, UTC+1). ✓

- **FR-5 Spring forward gap**: Test `Calculate_Throws_AroundDstSpringForwardGap` drives the implementation with `utcNow = 2026-03-28T20:00:00Z`, cron `"30 2 * * *"`, and timezone `Europe/Prague`. The next local occurrence lands on 2026-03-29 02:30 (invalid; clocks jump from 02:00 CET to 03:00 CEST that day). Test asserts `TimeZoneInfo.ConvertTimeToUtc` throws `ArgumentException`, which is not caught by `Calculate` (only catches `TimeZoneNotFoundException` and `CrontabException`) and propagates uncaught to the caller. This documents current, uncaught behavior as specified. ✓

#### Non-Vacuous Assertions
- **Autumn test** (line 156): Assertion pins an exact UTC instant `2026-10-25T01:30:00Z`. If the implementation incorrectly resolved the ambiguous local hour using the daylight offset (CEST, UTC+2) instead of standard (CET, UTC+1), the result would be `2026-10-25T00:30:00Z`, and this test would fail. ✓

- **Spring test** (line 184): Assertion pins an exact exception type `ArgumentException`. Verifies the specific exception that propagates when the computed local time falls inside the spring-forward gap. ✓

#### Completeness
- Exactly 7 tests in class: 5 pre-existing (FR-1 through FR-4) + 2 new (FR-5 autumn and spring). ✓
- All acceptance criteria for FR-5 met:
  - Autumn ambiguous hour: returns expected UTC instant resolved at standard offset ✓
  - Spring forward gap: asserts current `ArgumentException` propagation behavior ✓
- Tests follow existing conventions: xUnit `[Fact]`, FluentAssertions `.Should()`, `NullLogger.Instance`, comment-based Arrange-Act-Assert structure. ✓

#### No Production Code Changes
- Git commit `8cbc2bad4` touched only:
  - `artifacts/feat-4092/impl/dst-boundary-cases.r1.md` (implementation doc)
  - `artifacts/feat-4092/state.json` (state tracking)
  - `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (51 lines added)
- No changes to `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs` — verified with `git show --stat`. ✓

#### Test Verification
- Orchestrator-verified test run: 7/7 pass (5 pre-existing + 2 new). ✓
- Full regression (Category!=Integration): 6806 passed, 0 failed. ✓
- Build: 0 errors. ✓
- Cross-checked DST calculations against IANA timezone database; Europe/Prague transitions and UTC offsets confirmed accurate. ✓

## Overall Notes
This is a clean, targeted coverage task. The test assertions are concrete and would fail if the DST boundary behavior changed in the future. The second test (spring forward gap) documents a legitimate latent bug (uncaught `ArgumentException` while other error paths return `null`), but per spec's out-of-scope section, fixing it is deferred as a separate issue. The implementation correctly notes this in a comment, making it a regression guard for future maintainers.
