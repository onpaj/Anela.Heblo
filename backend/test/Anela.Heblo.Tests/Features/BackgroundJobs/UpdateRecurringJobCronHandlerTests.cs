using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class UpdateRecurringJobCronHandlerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IHangfireRecurringJobScheduler> _schedulerMock;
    private readonly UpdateRecurringJobCronHandler _handler;

    public UpdateRecurringJobCronHandlerTests()
    {
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _schedulerMock = new Mock<IHangfireRecurringJobScheduler>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: "test-user", Email: null, IsAuthenticated: true));

        _handler = new UpdateRecurringJobCronHandler(
            Mock.Of<Microsoft.Extensions.Logging.ILogger<UpdateRecurringJobCronHandler>>(),
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _schedulerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenJobNotFound_ReturnsNotFoundError()
    {
        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("missing-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);

        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "missing-job",
            CronExpression = "0 3 * * *"
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCronExpressionInvalid_ReturnsBadRequest()
    {
        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "my-job",
            CronExpression = "not-a-cron"
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidCronExpression);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WhenCronExpressionEmpty_ReturnsBadRequest(string emptyCron)
    {
        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "my-job",
            CronExpression = emptyCron
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidCronExpression);
    }

    [Fact]
    public async Task Handle_WhenValidCron_UpdatesDbAndHangfire()
    {
        const string newCron = "0 3 * * *";
        var job = CreateTestJob("my-job", "0 2 * * *");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("my-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "my-job",
            CronExpression = newCron
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.JobName.Should().Be("my-job");
        result.CronExpression.Should().Be(newCron);
        result.LastModifiedBy.Should().Be("test-user");

        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<RecurringJobConfiguration>(c => c.CronExpression == newCron),
            It.IsAny<CancellationToken>()), Times.Once);

        _schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron), Times.Once);
    }

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 3 * * *")]
    [InlineData("0 0 1 * *")]
    [InlineData("*/5 * * * *")]
    public async Task Handle_AcceptsValidCronFormats(string validCron)
    {
        var job = CreateTestJob("my-job", "0 2 * * *");
        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("my-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new UpdateRecurringJobCronRequest { JobName = "my-job", CronExpression = validCron };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    private static RecurringJobConfiguration CreateTestJob(string jobName, string cronExpression)
    {
        return new RecurringJobConfiguration(
            jobName: jobName,
            displayName: "Test Job",
            description: "A test job",
            cronExpression: cronExpression,
            isEnabled: true,
            lastModifiedBy: "seed");
    }
}
