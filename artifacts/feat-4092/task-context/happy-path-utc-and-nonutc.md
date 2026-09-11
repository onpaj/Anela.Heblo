### task: happy-path-utc-and-nonutc

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add two methods after `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid`)

- [ ] **Step 1: Add the FR-4 happy-path tests (UTC and non-UTC)**

```csharp
    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone()
    {
        // Arrange
        var utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "UTC",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone()
    {
        // Arrange
        // 2026-01-01T04:59:00Z = 2026-01-01T05:59:00 local Europe/Prague time
        // (CET = UTC+1 in winter, no DST in effect on this date).
        var utcNow = new DateTime(2026, 1, 1, 4, 59, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
```

(Both expected values were verified by actually running `RecurringJobNextRunCalculator`-equivalent code against `NCrontab.Advanced 1.3.28` under net8.0: for the UTC case, `utcNow` is exactly midnight and the next `"0 6 * * *"` occurrence is 06:00 the same day. For the Prague case, local time at `utcNow` is 05:59, so the next occurrence is 06:00 local the same day = 05:00 UTC.)

- [ ] **Step 2: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator happy path (UTC and Europe/Prague)"
```

---
