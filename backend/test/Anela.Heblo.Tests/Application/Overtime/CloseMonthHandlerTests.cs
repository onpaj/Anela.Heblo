using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class CloseMonthHandlerTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid InactivePerson = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<IOvertimeAdjustmentRepository> _adjustments = new();
    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IContractHoursProvider> _contractHours = new();
    private readonly Mock<IOvertimeReportPublisher> _publisher = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly List<OvertimeMonthlyStatement> _monthStatements = new();

    public CloseMonthHandlerTests()
    {
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-123", Name: "Andy", Email: null, IsAuthenticated: true));
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 8, 1), IsActive = true }
            });
        _monthStatements.Add(new OvertimeMonthlyStatement
        {
            PersonId = Person, Year = 2026, Month = 8, Status = OvertimeStatementStatus.Open, IsReviewed = true
        });
        _statements.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(_monthStatements);
        _statements.Setup(r => r.AnyOpenBeforeAsync(2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>())).ReturnsAsync((OvertimeMonthlyStatement?)null);
        _statements.Setup(r => r.GetAllClosedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<OvertimeMonthlyStatement>());
        _adjustments.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeAdjustment>
            {
                new() { PersonId = Person, Year = 2026, Month = 8, Type = OvertimeAdjustmentType.Payout, Hours = -1m, Note = "x" }
            });
        _adjustments.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<OvertimeAdjustment>());
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity> { new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work } });
        _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoTimeEntry>
            {
                new()
                {
                    Guid = Guid.NewGuid(), Person = Person, Date = new DateOnly(2026, 8, 3), Activity = WorkActivity,
                    From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
                    To = new DateTimeOffset(2026, 8, 3, 16, 0, 0, TimeSpan.Zero)
                }
            });
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, 2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(6.4m);
        _publisher.SetupGet(p => p.IsConfigured).Returns(false);
    }

    private CloseMonthHandler CreateSut()
    {
        var calc = new OvertimeCalculationService(
            _client.Object, _contractHours.Object, Options.Create(new OvertimeOptions()),
            NullLogger<OvertimeCalculationService>.Instance);
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        return new CloseMonthHandler(
            _employees.Object, _statements.Object, _adjustments.Object, calc,
            new OvertimeExcelBuilder(), _publisher.Object, _currentUser.Object, timeProvider.Object,
            Options.Create(new OvertimeOptions()),
            NullLogger<CloseMonthHandler>.Instance);
    }

    [Fact]
    public async Task Close_FreezesStatement_ChainsBalance_AndSkipsPublishWhenUnconfigured()
    {
        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ClosedCount.Should().Be(1);
        result.PublishSkipped.Should().BeTrue();
        var statement = _monthStatements.Single();
        statement.Status.Should().Be(OvertimeStatementStatus.Closed);
        statement.ClosedBy.Should().Be("Andy");
        // worked 8, required 21×6.4=134.4 → delta −126.4; balance = 2.5 − 126.4 − 1 (adjustment)
        statement.BalanceAfter.Should().Be(2.5m + statement.DeltaHours - 1m);
    }

    [Fact]
    public async Task Close_Fails_WhenAlreadyClosed()
    {
        _monthStatements[0].Status = OvertimeStatementStatus.Closed;

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeMonthAlreadyClosed);
    }

    [Fact]
    public async Task Close_Fails_WhenOlderMonthOpen()
    {
        _statements.Setup(r => r.AnyOpenBeforeAsync(2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimePreviousMonthOpen);
    }

    [Fact]
    public async Task Close_Fails_WhenUnreviewed_UnlessForced()
    {
        _monthStatements[0].IsReviewed = false;

        var blocked = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);
        blocked.ErrorCode.Should().Be(ErrorCodes.OvertimeMonthNotReviewed);
        blocked.Params!["names"].Should().Contain("Pepina");

        var forced = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8, Force = true }, CancellationToken.None);
        forced.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Close_Fails_WhenContractHoursMissing()
    {
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, 2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeContractHoursMissing);
    }

    [Fact]
    public async Task Close_Succeeds_WithPublishFailedFlag_WhenPublisherThrows()
    {
        _publisher.SetupGet(p => p.IsConfigured).Returns(true);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("graph down"));

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PublishFailed.Should().BeTrue();
        _monthStatements.Single().Status.Should().Be(OvertimeStatementStatus.Closed);
    }

    [Fact]
    public async Task Close_AlsoFreezesOpenStatementsOfInactiveEmployees()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 8, 1), IsActive = true },
                new() { PersonId = InactivePerson, DisplayName = "Karel", BaselineHours = 10m, BaselineDate = new DateOnly(2026, 8, 1), IsActive = false }
            });
        _monthStatements.Add(new OvertimeMonthlyStatement
        {
            PersonId = InactivePerson, Year = 2026, Month = 8, Status = OvertimeStatementStatus.Open,
            IsReviewed = false, DeltaHours = -2m
        });
        _statements.Setup(r => r.GetLatestClosedAsync(InactivePerson, It.IsAny<CancellationToken>())).ReturnsAsync((OvertimeMonthlyStatement?)null);

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ClosedCount.Should().Be(2);
        var inactiveStatement = _monthStatements.Single(s => s.PersonId == InactivePerson);
        inactiveStatement.Status.Should().Be(OvertimeStatementStatus.Closed);
        inactiveStatement.BalanceAfter.Should().Be(10m - 2m);
    }
}
