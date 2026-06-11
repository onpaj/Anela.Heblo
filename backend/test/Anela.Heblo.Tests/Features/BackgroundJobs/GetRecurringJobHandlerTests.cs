using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJob;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class GetRecurringJobHandlerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<GetRecurringJobHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetRecurringJobHandler _handler;

    // 2026-03-30 12:00 UTC = 14:00 CEST (DST in effect)
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);

    public GetRecurringJobHandlerTests()
    {
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<GetRecurringJobHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(FixedUtcNow);
        _handler = new GetRecurringJobHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _loggerMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_WhenJobExistsAndEnabled_ReturnsJobWithNextRunAt()
    {
        var request = new GetRecurringJobRequest { JobName = "print-picking-list" };
        var job = new RecurringJobConfiguration("print-picking-list", "Print", "Desc", "0 13 * * *", true, "User1");
        var dto = new RecurringJobDto { JobName = "print-picking-list", CronExpression = "0 13 * * *", IsEnabled = true };
        _repositoryMock.Setup(r => r.GetByJobNameAsync("print-picking-list", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mapperMock.Setup(m => m.Map<RecurringJobDto>(job)).Returns(dto);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Job.Should().NotBeNull();
        result.Job!.JobName.Should().Be("print-picking-list");
        // cron "0 13 * * *" → next after 14:00 Prague is 2026-03-31 13:00 CEST = 11:00 UTC
        result.Job.NextRunAt.Should().Be(new DateTime(2026, 3, 31, 11, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Handle_WhenJobIsDisabled_ReturnsJobWithNullNextRunAt()
    {
        var request = new GetRecurringJobRequest { JobName = "print-picking-list" };
        var job = new RecurringJobConfiguration("print-picking-list", "Print", "Desc", "0 13 * * *", false, "User1");
        var dto = new RecurringJobDto { JobName = "print-picking-list", CronExpression = "0 13 * * *", IsEnabled = false };
        _repositoryMock.Setup(r => r.GetByJobNameAsync("print-picking-list", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mapperMock.Setup(m => m.Map<RecurringJobDto>(job)).Returns(dto);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Job!.NextRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenJobDoesNotExist_ReturnsNotFoundError()
    {
        var request = new GetRecurringJobRequest { JobName = "missing-job" };
        _repositoryMock.Setup(r => r.GetByJobNameAsync("missing-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);
        result.Job.Should().BeNull();
    }
}
