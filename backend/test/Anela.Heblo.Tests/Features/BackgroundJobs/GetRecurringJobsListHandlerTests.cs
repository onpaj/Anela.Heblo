using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class GetRecurringJobsListHandlerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<GetRecurringJobsListHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetRecurringJobsListHandler _handler;

    // Fixed reference time used in all tests to avoid flakiness
    private static readonly DateTimeOffset FixedUtcNow = new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);

    public GetRecurringJobsListHandlerTests()
    {
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<GetRecurringJobsListHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(FixedUtcNow);
        _handler = new GetRecurringJobsListHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _loggerMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_Should_Return_All_Jobs_From_Repository()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1"),
            new RecurringJobConfiguration("Job2", "Display 2", "Description 2", "0 1 * * *", false, "User2")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", DisplayName = "Display 1", Description = "Description 1", CronExpression = "0 0 * * *", IsEnabled = true, LastModifiedBy = "User1" },
            new RecurringJobDto { JobName = "Job2", DisplayName = "Display 2", Description = "Description 2", CronExpression = "0 1 * * *", IsEnabled = false, LastModifiedBy = "User2" }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Jobs.Should().HaveCount(2);
        result.Jobs[0].JobName.Should().Be("Job1");
        result.Jobs[1].JobName.Should().Be("Job2");
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_List_When_No_Jobs_Exist()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>();
        var jobDtos = new List<RecurringJobDto>();
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Call_Repository_GetAllAsync()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>();
        var jobDtos = new List<RecurringJobDto>();
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Map_Entities_To_Dtos()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>();
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        await _handler.Handle(request, CancellationToken.None);

        _mapperMock.Verify(m => m.Map<List<RecurringJobDto>>(jobs), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Log_Information_Messages()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "0 0 * * *", IsEnabled = true }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Getting recurring jobs list")),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _loggerMock.Verify(
            x => x.Log(LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrieved") && v.ToString()!.Contains("recurring jobs")),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenJobIsEnabled_SetsNextRunAtToFutureUtcDateTime()
    {
        // Fixed time is 2026-03-30 12:00:00 UTC; cron "0 13 * * *" next fires at 13:00 same day
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Desc", "0 13 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "0 13 * * *", IsEnabled = true }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        var expectedNextRun = new DateTime(2026, 3, 30, 13, 0, 0, DateTimeKind.Utc);
        result.Jobs[0].NextRunAt.Should().Be(expectedNextRun);
    }

    [Fact]
    public async Task Handle_WhenJobIsDisabled_SetsNextRunAtToNull()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job2", "Display 2", "Desc", "0 13 * * *", false, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job2", CronExpression = "0 13 * * *", IsEnabled = false }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Jobs[0].NextRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MixedEnabledDisabled_SetsNextRunAtCorrectly()
    {
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Desc", "0 13 * * *", true, "User1"),
            new RecurringJobConfiguration("Job2", "Display 2", "Desc", "0 3 * * *", false, "User2")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "0 13 * * *", IsEnabled = true },
            new RecurringJobDto { JobName = "Job2", CronExpression = "0 3 * * *", IsEnabled = false }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Jobs[0].NextRunAt.Should().Be(new DateTime(2026, 3, 30, 13, 0, 0, DateTimeKind.Utc));
        result.Jobs[1].NextRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenCronExpressionIsInvalid_SetsNextRunAtToNullAndLogsWarning()
    {
        // Arrange — "NOT_A_CRON" is syntactically invalid and will cause CrontabSchedule.Parse to throw
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Desc", "NOT_A_CRON", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "NOT_A_CRON", IsEnabled = true }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);
        _mapperMock.Setup(m => m.Map<List<RecurringJobDto>>(jobs)).Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — fallback to null, not an exception
        result.Jobs[0].NextRunAt.Should().BeNull();

        // Warning logged with job name and expression
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("NOT_A_CRON") &&
                    v.ToString()!.Contains("Job1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
