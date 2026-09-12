## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs:60` and `:88` — the `Mock<ILogger>` warning-verification lambda (`It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(...) && ...)`) is duplicated verbatim across the timezone and CRON tests. Could be extracted into a small private helper (e.g. `VerifyWarningLogged(logger, params string[] expectedSubstrings)`) to avoid drift if the pattern needs to change later. Non-blocking; matches existing per-test style in sibling files in this folder.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs:154` (`Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone`) and the non-UTC counterpart use `NullLogger.Instance`, so FR-4's "no log call is made on the happy path" acceptance criterion isn't actually asserted (NullLogger can't be verified). This mirrors the spec's own NFR-2 guidance, so it's not a defect, just an unverified acceptance criterion worth noting.

### Verification notes (not findings, recorded for confidence)
- Read `RecurringJobNextRunCalculator.cs` in full; all four branches (disabled / unknown timezone / invalid CRON / happy path) and the uncaught spring-forward `ArgumentException` path match the spec and the tests exactly.
- Independently recomputed every expected value rather than trusting test comments:
  - UTC case: `utcNow` 2026-01-01T00:00Z, cron `0 6 * * *` → next occurrence 2026-01-01T06:00Z. Matches.
  - Europe/Prague (winter, CET = UTC+1): `utcNow` 2026-01-01T04:59Z → local 05:59, next occurrence local 06:00 → UTC 05:00. Matches, and this genuinely discriminates a naive "return local time stamped as UTC" bug (would wrongly assert 06:00Z).
  - Confirmed 2026-10-25 is the last Sunday of October (DST end) and 2026-03-29 is the last Sunday of March (DST start) via manual day-of-week calculation from a known Jan 1, 2026 = Thursday anchor.
  - Autumn ambiguous-hour case: `utcNow` 2026-10-24T20:00Z is still CEST (UTC+2) → local 22:00 Oct 24 → next occurrence local 2026-10-25T02:30 (ambiguous hour). .NET's documented behavior resolves ambiguous unspecified `DateTime` at the zone's standard (non-DST) offset (CET, UTC+1) → UTC 01:30. Matches the asserted `2026-10-25T01:30:00Z`.
  - Spring-forward case: `utcNow` 2026-03-28T20:00Z is CET (UTC+1) → local 21:00 Mar 28 → next occurrence local 2026-03-29T02:30, which falls inside the skipped 02:00–03:00 gap → `ConvertTimeToUtc` throws `ArgumentException`, uncaught by `Calculate`'s catch clauses (only `TimeZoneNotFoundException`/`CrontabException` are caught). Matches the test's expected throw.
- Disabled-job test asserts `Times.Never` on any log call (not just warnings) — correctly rules out a regression that logs on the disabled path.
- Confirmed the `Mock<ILogger>`/`It.IsAnyType` verification idiom is the same one already used in sibling files (`GetRecurringJobsListHandlerTests.cs`, `UpdateRecurringJobStatusHandlerTests.cs`) in the same test folder, so it's consistent with existing conventions, not a novel/risky pattern.
- No production code was touched; diff is exactly the one new test file as expected for this coverage-only ticket.
