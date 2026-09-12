### task: invalid-cron-path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add a method after `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown`)

- [ ] **Step 1: Add the FR-3 (invalid CRON) test**

```csharp
    [Fact]
    public void Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "not a cron",
            isEnabled: true,
            timeZoneId: "UTC",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

(Verified: `CrontabSchedule.Parse("not a cron")` throws `NCrontab.Advanced.Exceptions.CrontabException: The provided cron string <not a cron> has too few parameters`, which `Calculate` catches and logs as a warning before returning `null`.)

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator invalid-cron path"
```

---
