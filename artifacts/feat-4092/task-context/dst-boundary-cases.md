### task: dst-boundary-cases

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add two methods after `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone`, at the end of the class)

- [ ] **Step 1: Add the FR-5 DST-boundary tests (autumn ambiguous hour, spring-forward gap)**

```csharp
    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour()
    {
        // Arrange
        // Europe/Prague DST ends 2026-10-25: local clocks fall back from 03:00 CEST to 02:00 CET,
        // so local 02:00-03:00 occurs twice that day. TimeZoneInfo.ConvertTimeToUtc resolves an
        // ambiguous unspecified-kind DateTime using the zone's standard (CET, UTC+1) offset --
        // verified directly against this repo's NCrontab.Advanced/TimeZoneInfo combination.
        var utcNow = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "30 2 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_Throws_AroundDstSpringForwardGap()
    {
        // Arrange
        // Europe/Prague DST starts 2026-03-29: local clocks jump forward from 02:00 CET to 03:00 CEST,
        // so local 02:00-03:00 never occurs that day. TimeZoneInfo.ConvertTimeToUtc throws
        // ArgumentException for any local time inside this gap. RecurringJobNextRunCalculator.Calculate
        // only catches TimeZoneNotFoundException and CrontabException, so this exception is NOT caught
        // and propagates to the caller. This test documents CURRENT, uncaught behavior -- it is not
        // an endorsement of it. Fixing it (e.g. catching ArgumentException and returning null like the
        // other error paths) is a legitimate follow-up but explicitly out of scope for this coverage-only
        // ticket -- see spec.r1.md Out of Scope. Consider filing a separate issue after this PR merges.
        var utcNow = new DateTime(2026, 3, 28, 20, 0, 0, DateTimeKind.Utc);

        // Act
        Action act = () => RecurringJobNextRunCalculator.Calculate(
            cronExpression: "30 2 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
```

- [ ] **Step 2: Run the full test class to verify all tests pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0`

- [ ] **Step 3: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: build succeeds, `dotnet format` reports no changes needed, all tests pass (existing tests untouched, 7 new tests added).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator DST boundary cases"
```
