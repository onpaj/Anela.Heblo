### task: unknown-timezone-path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add a method inside the existing class, after `Calculate_ReturnsNull_WhenJobDisabled`)

- [ ] **Step 1: Add the FR-2 (unknown timezone) test**

```csharp
    [Fact]
    public void Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "Not/A/Real/Zone",
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

(This test was verified against the real `TimeZoneInfo.FindSystemTimeZoneById` — `"Not/A/Real/Zone"` throws `System.TimeZoneNotFoundException: The time zone ID 'Not/A/Real/Zone' was not found on the local computer.`, which `Calculate` catches and logs as a warning before returning `null`.)

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator unknown-timezone path"
```

---
