# Design: Unit test coverage for RecurringJobNextRunCalculator

## Component Design

### `RecurringJobNextRunCalculatorTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`
- **Responsibility:** Exercise all four branches of the static `RecurringJobNextRunCalculator.Calculate` method plus one DST-boundary case, per `spec.r1.md` FR-1 through FR-5.
- **Collaborators:**
  - `RecurringJobNextRunCalculator.Calculate` (SUT, static, no instantiation).
  - `Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance` — non-generic `ILogger` no-op, used where the test does not assert on logging (FR-1, FR-4, FR-5).
  - `Moq.Mock<ILogger>` — used only where a `LogWarning` call itself is part of the assertion (FR-2, FR-3). Verified via the underlying `ILogger.Log<TState>(...)` method (see arch-review.r1.md, Decision 2), since `LogWarning` is a non-mockable static extension method.
- **No constructor / shared fixture:** each `[Fact]` is self-contained with its own local Arrange block; there is no shared mutable state to warrant a fixture class or `IClassFixture`.
- **Structure convention:** Arrange / Act / Assert comment sections, `FluentAssertions` (`.Should().Be(...)`, `.Should().BeNull()`, `.Should().NotThrow()`), consistent with `RecurringJobStatusCheckerTests.cs` and the rest of `Features/BackgroundJobs/`.

### Test method inventory
| Test method | FR | Logger double | Core assertion |
|---|---|---|---|
| `Calculate_ReturnsNull_WhenJobDisabled` | FR-1 | `Mock<ILogger>` (verify **never** called) | Result is `null`; no `Log` invocation at all |
| `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` | FR-2 | `Mock<ILogger>` (verify `Log(Warning, ...)` once) | Result is `null`; warning logged exactly once, referencing the bad timezone id |
| `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` | FR-3 | `Mock<ILogger>` (verify `Log(Warning, ...)` once) | Result is `null`; warning logged exactly once, referencing the bad CRON string |
| `Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone` | FR-4 | `NullLogger.Instance` | Returned `DateTime` equals the independently-computed expected UTC instant; `.Kind == DateTimeKind.Utc` |
| `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone` | FR-4 | `NullLogger.Instance` | Same, using `timeZoneId = "Europe/Prague"` (matches `RecurringJobMetadata.DefaultTimeZoneId`) with a winter `utcNow` so the CET (UTC+1) offset is unambiguous |
| `Calculate_DoesNotThrow_AroundDstSpringForwardGap` | FR-5 | `NullLogger.Instance` | Invoking `Calculate` with `utcNow`/CRON chosen so `nextLocal` falls in the spring-forward gap does not throw; result is a valid `DateTime?` (concrete value or `null`) |
| `Calculate_DoesNotThrow_AroundDstAutumnAmbiguousHour` (optional, recommended) | FR-5 | `NullLogger.Instance` | Same for the autumn ambiguous-hour case |

## Data Schemas
Not applicable — no persisted data, no API request/response shapes, no event payloads. Inputs and outputs are plain in-memory values matching `Calculate`'s existing signature:

```csharp
public static DateTime? Calculate(
    string cronExpression,
    bool isEnabled,
    string timeZoneId,
    DateTime utcNow,
    ILogger logger,
    string? jobName = null)
```

Representative literal fixtures used across the test methods (illustrative, not prescriptive — exact values are an implementation detail for the developer/planner):
- Disabled job: `isEnabled = false` with otherwise arbitrary valid arguments (e.g. `cronExpression = "0 6 * * *"`, `timeZoneId = "UTC"`).
- Unknown timezone: `timeZoneId = "Not/A/Real/Zone"`.
- Invalid CRON: `cronExpression = "not a cron"`, `timeZoneId = "UTC"`.
- UTC happy path: `utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`, `cronExpression = "0 6 * * *"`, `timeZoneId = "UTC"` → expected `2026-01-01T06:00:00Z`.
- Non-UTC happy path: `timeZoneId = "Europe/Prague"`, a winter `utcNow` chosen strictly before the expected local occurrence to avoid CRON-boundary ambiguity.
- DST spring-forward: `timeZoneId = "Europe/Prague"`, `utcNow`/`cronExpression` chosen so the computed local next-occurrence falls within the last-Sunday-of-March 02:00–03:00 gap.
- DST autumn ambiguity: same idea for the last-Sunday-of-October repeated 02:00–03:00 hour.
