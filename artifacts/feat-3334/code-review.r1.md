# Code Review: feat-3334 (whole-branch)

## Summary

The implementation correctly converts `TimeWindowParser` from a static class to an injectable instance class backed by `System.TimeProvider`, registers it in `AnalyticsModule`, injects it into `GetProductMarginSummaryHandler`, replaces the silent fallback with `ArgumentException`, and updates the test file to use a frozen `FakeTimeProvider`. All five functional requirements from the spec are addressed. All 8 tests pass deterministically. No blocking issues found.

## Review Result: CLEAN

---

### FR-1: Convert TimeWindowParser to injectable instance class
**Status:** PASS

`static class` → `public class`. Constructor `TimeWindowParser(TimeProvider timeProvider)` added; `_timeProvider` stored as `readonly` field. `DateTime.Today` replaced with `_timeProvider.GetLocalNow().Date` (consistent with existing `InvoiceImportStatisticsTile` pattern). All five named time windows remain present and return unchanged date range expressions.

---

### FR-2: Register TimeWindowParser in AnalyticsModule
**Status:** PASS

`services.AddScoped<TimeWindowParser>()` added before the existing `IMarginCalculator` and `IMonthlyBreakdownGenerator` registrations. `TimeProvider` is provided by the .NET 8 host as a singleton, so the dependency chain resolves at startup without `InvalidOperationException`.

---

### FR-3: Inject TimeWindowParser into GetProductMarginSummaryHandler
**Status:** PASS

`private readonly TimeWindowParser _timeWindowParser` field added. Constructor gains `TimeWindowParser timeWindowParser` parameter with correct assignment. Static call `TimeWindowParser.ParseTimeWindow(request.TimeWindow)` replaced with instance call `_timeWindowParser.ParseTimeWindow(request.TimeWindow)`. Change is minimal and surgical.

---

### FR-4: Replace silent fallback with ArgumentException
**Status:** PASS

`_ => throw new ArgumentException($"Unknown time window value: '{timeWindow}'", nameof(timeWindow))` — offending value included in message, parameter name correctly set via `nameof`. All five recognised time window strings continue to return correct date ranges.

---

### FR-5: Update tests to use frozen TimeProvider
**Status:** PASS

- `FrozenNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)` and `FrozenDate = new DateTime(2026, 1, 15)` declared as static fields
- `FakeTimeProvider(FrozenNow)` → `TimeWindowParser` → `_handler` wired in constructor
- Zero occurrences of `DateTime.Today` remain in the test file (verified)
- All four test methods that previously used `DateTime.Today` now reference `FrozenDate` / `FrozenDate.Year`
- `ParseTimeWindow_UnknownValue_ThrowsArgumentException` added and asserts `ArgumentException` with message containing the bad value
- 8 tests pass, 0 fail — deterministically

---

## Advisory / cleanup notes

- **Dead `_` arm in `Handle_DifferentTimeWindows_ParsesCorrectly`**: The local `expectedDates` switch still contains `_ => (new DateTime(FrozenDate.Year, 1, 1), FrozenDate)`. This arm can never be reached because all `[InlineData]` values are known windows and unknown values now throw `ArgumentException` before reaching the assertion. Not blocking — the `[Theory]` tests cover what they claim to cover.

---

## Overall Notes

All functional requirements met. The diff is clean, targeted, and does not disturb adjacent code. Tests are fully deterministic after this change.
