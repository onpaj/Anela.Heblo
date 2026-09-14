### task: add-daily-consumption-job-failure-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs`

This task depends on `fix-process-daily-consumption-handler` only in narrative order (it demonstrates the end-to-end fix); the test itself mocks `IMediator` directly and does not require the handler change to compile or run, but should be done second so the plan reads in the same order the underlying bug is fixed.

- [ ] **Step 1: Write the new test file (there is no existing test file for this job)**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials.Infrastructure.Jobs;

public class DailyConsumptionJobTests
{
    private const string JobName = "daily-consumption-calculation";

    private static (
        DailyConsumptionJob Sut,
        Mock<IMediator> Mediator,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true)
    {
        var mediator = new Mock<IMediator>();
        var logger = new Mock<ILogger<DailyConsumptionJob>>();
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(JobName, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);

        var sut = new DailyConsumptionJob(mediator.Object, logger.Object, statusChecker.Object);
        return (sut, mediator, statusChecker);
    }

    [Fact]
    public async Task ExecuteAsync_Rethrows_WhenMediatorSendThrows()
    {
        // Arrange
        var thrown = new InvalidOperationException("database unreachable");
        var (sut, mediator, _) = MakeSut();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert — Hangfire's retry contract depends on this: if the handler's exception
        // doesn't reach here and get rethrown, Hangfire marks the run Succeeded and never retries.
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(thrown);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenJobDisabled()
    {
        // Arrange
        var (sut, mediator, _) = MakeSut(jobEnabled: false);

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        mediator.Verify(
            m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenMediatorReturnsSuccess()
    {
        // Arrange
        var (sut, mediator, _) = MakeSut();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                MaterialsProcessed = 3,
                Message = "ok"
            });

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenMediatorReturnsFailureWithoutThrowing()
    {
        // Arrange — the legitimate "already processed" outcome: Success=false but no exception.
        // The job must log a warning and return normally, not throw.
        var (sut, mediator, _) = MakeSut();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResponse
            {
                Success = false,
                ProcessedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                MaterialsProcessed = 0,
                Message = "already processed"
            });

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run the new test file**

Run: `dotnet test --filter "FullyQualifiedName~DailyConsumptionJobTests"`
Expected: PASS — all 4 tests pass. (No handler code is exercised by these tests — `IMediator` is mocked directly — so this passes independent of whether Task 1 has been applied yet; it documents and locks in `DailyConsumptionJob`'s already-correct behavior.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs
git commit -m "test(packing-materials): cover DailyConsumptionJob's rethrow-on-handler-exception contract"
```

---

