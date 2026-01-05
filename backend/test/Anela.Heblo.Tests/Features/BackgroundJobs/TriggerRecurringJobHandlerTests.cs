using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class TriggerRecurringJobHandlerTests
{
    private readonly Mock<IRecurringJobTriggerService> _mockTriggerService;
    private readonly TriggerRecurringJobHandler _handler;

    public TriggerRecurringJobHandlerTests()
    {
        _mockTriggerService = new Mock<IRecurringJobTriggerService>();
        _handler = new TriggerRecurringJobHandler(_mockTriggerService.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccess_WhenJobTriggeredSuccessfully()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("test-job", false))
            .ReturnsAsync("job-id-123");

        var request = new TriggerRecurringJobRequest
        {
            JobName = "test-job",
            ForceDisabled = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("job-id-123", result.JobId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenJobNotFound()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("nonexistent-job", false))
            .ReturnsAsync((string?)null);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "nonexistent-job",
            ForceDisabled = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.JobId);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenJobDisabledAndNotForced()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("disabled-job", false))
            .ReturnsAsync((string?)null);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "disabled-job",
            ForceDisabled = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.JobId);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccess_WhenJobDisabledButForced()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("disabled-job", true))
            .ReturnsAsync("job-id-456");

        var request = new TriggerRecurringJobRequest
        {
            JobName = "disabled-job",
            ForceDisabled = true
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("job-id-456", result.JobId);
    }
}
