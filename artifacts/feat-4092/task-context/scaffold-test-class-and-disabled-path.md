### task: scaffold-test-class-and-disabled-path

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`

- [ ] **Step 1: Create the test file with usings, class declaration, and the FR-1 (disabled job) test**

```csharp
using System;
using Anela.Heblo.Application.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobNextRunCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsNull_WhenJobDisabled()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: false,
            timeZoneId: "UTC",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator disabled-job path"
```

---
