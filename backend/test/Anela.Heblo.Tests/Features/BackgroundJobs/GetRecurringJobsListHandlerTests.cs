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
    private readonly GetRecurringJobsListHandler _handler;

    public GetRecurringJobsListHandlerTests()
    {
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<GetRecurringJobsListHandler>>();
        _handler = new GetRecurringJobsListHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_Should_Return_All_Jobs_From_Repository()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1"),
            new RecurringJobConfiguration("Job2", "Display 2", "Description 2", "0 1 * * *", false, "User2")
        };

        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto
            {
                JobName = "Job1",
                DisplayName = "Display 1",
                Description = "Description 1",
                CronExpression = "0 0 * * *",
                IsEnabled = true,
                LastModifiedBy = "User1"
            },
            new RecurringJobDto
            {
                JobName = "Job2",
                DisplayName = "Display 2",
                Description = "Description 2",
                CronExpression = "0 1 * * *",
                IsEnabled = false,
                LastModifiedBy = "User2"
            }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Jobs.Should().HaveCount(2);
        result.Jobs[0].JobName.Should().Be("Job1");
        result.Jobs[1].JobName.Should().Be("Job2");
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_List_When_No_Jobs_Exist()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>();
        var jobDtos = new List<RecurringJobDto>();

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Call_Repository_GetAllAsync()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>();
        var jobDtos = new List<RecurringJobDto>();

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Map_Entities_To_Dtos()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>();

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mapperMock.Verify(
            m => m.Map<List<RecurringJobDto>>(jobs),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Log_Information_Messages()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1" }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Getting recurring jobs list")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrieved") && v.ToString()!.Contains("recurring jobs")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
