# Code Review: add-datetime-regression-tests

## Summary
All four acceptance criteria are met: each test class has the required `[Fact]`, calls `Map()` directly, inputs a `Kind=Unspecified` `DateTime`, and asserts `Offset == TimeSpan.Zero` on the `DateTimeOffset?` result. The `expected` value in Ledger/Contact is computed with `TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local)`, which matches exactly what `Map()` does (`dto.LastUpdate.Value.DateTime` yields `Kind=Unspecified`, then the same `ConvertTimeToUtc` call is applied) — the assertion is tight and not accidentally vacuous.

## Review Result: PASS

### task: add-datetime-regression-tests
**Status:** PASS

## Overall Notes
- The choice to use `TimeZoneInfo.Local` rather than a hardcoded offset means the test is environment-sensitive (will produce a different UTC offset on a CI runner set to `TZ=UTC` vs. a Prague developer machine), but both paths exercise the correct code and the test remains a valid regression guard in both environments, as documented in the impl notes.
- Ledger/Contact DTOs take `DateTimeOffset?` for `LastUpdate`; the test wraps the `Kind=Unspecified` `DateTime` in `new DateTimeOffset(unspecified, TimeSpan.Zero)`. The `.DateTime` property of that `DateTimeOffset` returns `Kind=Unspecified`, which is exactly the crash path — this indirection is correct and well-commented.
- Department/AccountingTemplate DTOs take non-nullable `DateTime`; the test passes `Kind=Unspecified` directly. No wrapping needed and none is done — correct.
- No pre-existing tests were modified. The two failing integration tests noted in the impl are a pre-existing Testcontainer/PostgreSQL dependency unrelated to this change.
