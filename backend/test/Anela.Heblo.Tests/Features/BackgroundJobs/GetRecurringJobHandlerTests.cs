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
        var job = new RecurringJobConfiguration("print-picking-list", "Print", "Desc", "0 13 * * *", "Europe/Prague", true, "User1");
        var dto = new RecurringJobDto { JobName = "print-picking-list", CronExpression = "0 13 * * *", TimeZoneId = "Europe/Prague", IsEnabled = true };
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
        var job = new RecurringJobConfiguration("print-picking-list", "Print", "Desc", "0 13 * * *", "Europe/Prague", false, "User1");
        var dto = new RecurringJobDto { JobName = "print-picking-list", CronExpression = "0 13 * * *", TimeZoneId = "Europe/Prague", IsEnabled = false };
        _repositoryMock.Setup(r => r.GetByJobNameAsync("print-picking-list", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mapperMock.Setup(m => m.Map<RecurringJobDto>(job)).Returns(dto);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Job!.NextRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenJobHasNonDefaultTimeZone_UsesJobTimeZoneForNextRunAt()
    {
        var request = new GetRecurringJobRequest { JobName = "print-picking-list" };
        var job = new RecurringJobConfiguration("print-picking-list", "Print", "Desc", "0 13 * * *", "America/New_York", true, "User1");
        var dto = new RecurringJobDto { JobName = "print-picking-list", CronExpression = "0 13 * * *", TimeZoneId = "America/New_York", IsEnabled = true };
        _repositoryMock.Setup(r => r.GetByJobNameAsync("print-picking-list", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mapperMock.Setup(m => m.Map<RecurringJobDto>(job)).Returns(dto);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Job.Should().NotBeNull();
        // cron "0 13 * * *" → next after 08:00 New York (EDT, UTC-4) is 2026-03-30 13:00 EDT = 17:00 UTC
        result.Job!.NextRunAt.Should().Be(new DateTime(2026, 3, 30, 17, 0, 0, DateTimeKind.Utc));
        // Confirms the calculator used the job's own timezone, not the Europe/Prague default
        result.Job.NextRunAt.Should().NotBe(new DateTime(2026, 3, 31, 11, 0, 0, DateTimeKind.Utc));
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
