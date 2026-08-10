using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class GetMonthlyStatementsHandlerTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<IOvertimeAdjustmentRepository> _adjustments = new();
    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IContractHoursProvider> _contractHours = new();

    public GetMonthlyStatementsHandlerTests()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 8, 1), IsActive = true }
            });
        _statements.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeMonthlyStatement>());
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeMonthlyStatement?)null);
        _adjustments.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeAdjustment>());
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
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, 2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(6.4m);
    }

    private GetMonthlyStatementsHandler CreateSut()
    {
        var calc = new OvertimeCalculationService(
            _client.Object, _contractHours.Object, Options.Create(new OvertimeOptions()),
            NullLogger<OvertimeCalculationService>.Instance);
        return new GetMonthlyStatementsHandler(_employees.Object, _statements.Object, _adjustments.Object, calc);
    }

    [Fact]
    public async Task OpenMonth_ComputesLive_MaterializesStatement_AndProjectsBalance()
    {
        var result = await CreateSut().Handle(new GetMonthlyStatementsRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsClosed.Should().BeFalse();
        var dto = result.Statements.Single();
        dto.WorkedHours.Should().Be(8.00m);
        dto.RequiredHours.Should().Be(134.40m);   // 21 working days × 6.4
        dto.PreviousBalance.Should().Be(2.5m);
        dto.ProjectedBalance.Should().Be(2.5m + dto.DeltaHours);
        _statements.Verify(r => r.AddAsync(It.Is<OvertimeMonthlyStatement>(
            s => s.PersonId == Person && s.Status == OvertimeStatementStatus.Open && s.WorkedHours == 8.00m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClosedMonth_ReturnsFrozenNumbers_WithoutTouchingLogeto()
    {
        _statements.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeMonthlyStatement>
            {
                new()
                {
                    PersonId = Person, Year = 2026, Month = 8, Status = OvertimeStatementStatus.Closed,
                    RequiredHours = 134.4m, WorkedHours = 130m, DeltaHours = -4.4m, BalanceAfter = -1.9m, IsReviewed = true
                }
            });

        var result = await CreateSut().Handle(new GetMonthlyStatementsRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.IsClosed.Should().BeTrue();
        result.Statements.Single().ProjectedBalance.Should().Be(-1.9m);
        _client.Verify(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogetoFailure_ReturnsErrorResponse_NotException()
    {
        _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Logeto down"));

        var result = await CreateSut().Handle(new GetMonthlyStatementsRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }
}
