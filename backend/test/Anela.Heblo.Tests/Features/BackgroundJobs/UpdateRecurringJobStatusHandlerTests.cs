using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class UpdateRecurringJobStatusHandlerTests
{
    private readonly Mock<ILogger<UpdateRecurringJobStatusHandler>> _loggerMock;
    private readonly Mock<IRecurringJobConfigurationRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly UpdateRecurringJobStatusHandler _handler;

    private const string ValidJobName = "TestJob";
    private const string ValidDisplayName = "Test Job";
    private const string ValidDescription = "Test Description";
    private const string ValidCronExpression = "0 0 * * *";

    public UpdateRecurringJobStatusHandlerTests()
    {
        _loggerMock = new Mock<ILogger<UpdateRecurringJobStatusHandler>>();
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdateRecurringJobStatusHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task Handle_Should_Enable_Job_When_IsEnabled_Is_True()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = ValidJobName,
            IsEnabled = true
        };

        var job = new RecurringJobConfiguration(
            ValidJobName,
            ValidDisplayName,
            ValidDescription,
            ValidCronExpression,
            false,
            "System");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.JobName.Should().Be(ValidJobName);
        result.IsEnabled.Should().BeTrue();
        result.LastModifiedBy.Should().Be("Test User");
        result.LastModifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        job.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Disable_Job_When_IsEnabled_Is_False()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = ValidJobName,
            IsEnabled = false
        };

        var job = new RecurringJobConfiguration(
            ValidJobName,
            ValidDisplayName,
            ValidDescription,
            ValidCronExpression,
            true,
            "System");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.JobName.Should().Be(ValidJobName);
        result.IsEnabled.Should().BeFalse();
        result.LastModifiedBy.Should().Be("Test User");

        job.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_Return_Error_When_Job_Not_Found()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = "NonExistentJob",
            IsEnabled = true
        };

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("NonExistentJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);
        result.Params.Should().ContainKey("JobName");
        result.Params!["JobName"].Should().Be("NonExistentJob");
    }

    [Fact]
    public async Task Handle_Should_Call_Repository_UpdateAsync()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = ValidJobName,
            IsEnabled = true
        };

        var job = new RecurringJobConfiguration(
            ValidJobName,
            ValidDisplayName,
            ValidDescription,
            ValidCronExpression,
            false,
            "System");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            r => r.UpdateAsync(job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Use_Current_User_For_ModifiedBy()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = ValidJobName,
            IsEnabled = true
        };

        var job = new RecurringJobConfiguration(
            ValidJobName,
            ValidDisplayName,
            ValidDescription,
            ValidCronExpression,
            false,
            "System");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.LastModifiedBy.Should().Be("Test User");
        job.LastModifiedBy.Should().Be("Test User");
    }

    [Fact]
    public async Task Handle_Should_Log_Information_Messages()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = ValidJobName,
            IsEnabled = true
        };

        var job = new RecurringJobConfiguration(
            ValidJobName,
            ValidDisplayName,
            ValidDescription,
            ValidCronExpression,
            false,
            "System");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Updating recurring job status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status updated to")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Log_Warning_When_Job_Not_Found()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = "NonExistentJob",
            IsEnabled = true
        };

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("NonExistentJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Recurring job not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Return_Error_When_Exception_Occurs()
    {
        // Arrange
        var request = new UpdateRecurringJobStatusRequest
        {
            JobName = ValidJobName,
            IsEnabled = true
        };

        var job = new RecurringJobConfiguration(
            ValidJobName,
            ValidDisplayName,
            ValidDescription,
            ValidCronExpression,
            false,
            "System");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync(ValidJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RecurringJobUpdateFailed);
        result.Params.Should().ContainKey("JobName");
        result.Params!["JobName"].Should().Be(ValidJobName);
        result.Params.Should().ContainKey("Message");
    }
}
