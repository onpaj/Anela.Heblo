using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeAdjustmentUseCaseTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<IOvertimeAdjustmentRepository> _adjustments = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public OvertimeAdjustmentUseCaseTests()
    {
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-123", Name: "Andy", Email: null, IsAuthenticated: true));
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeEmployee { PersonId = Person, DisplayName = "Pepina" });
        SetupMonth(OvertimeStatementStatus.Open);
    }

    private void SetupMonth(OvertimeStatementStatus status)
        => _statements.Setup(r => r.GetByMonthAsync(2026, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeMonthlyStatement>
            {
                new() { PersonId = Person, Year = 2026, Month = 9, Status = status }
            });

    [Fact]
    public async Task SetReviewed_TogglesFlag_OnOpenStatement()
    {
        var handler = new SetStatementReviewedHandler(_statements.Object);
        var result = await handler.Handle(new SetStatementReviewedRequest { PersonId = Person, Year = 2026, Month = 9, IsReviewed = true }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _statements.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetReviewed_Fails_WhenMonthClosed()
    {
        SetupMonth(OvertimeStatementStatus.Closed);
        var handler = new SetStatementReviewedHandler(_statements.Object);
        var result = await handler.Handle(new SetStatementReviewedRequest { PersonId = Person, Year = 2026, Month = 9, IsReviewed = true }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.OvertimeMonthAlreadyClosed);
    }

    [Fact]
    public async Task CreateAdjustment_Saves_WithAuditFields()
    {
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));
        var handler = new CreateAdjustmentHandler(_employees.Object, _statements.Object, _adjustments.Object, _currentUser.Object, timeProvider.Object);

        var result = await handler.Handle(new CreateAdjustmentRequest
        {
            PersonId = Person, Year = 2026, Month = 9,
            Type = OvertimeAdjustmentType.Payout, Hours = -40m, Note = "Proplaceno v prémiích"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _adjustments.Verify(r => r.AddAsync(It.Is<OvertimeAdjustment>(a =>
            a.PersonId == Person && a.Hours == -40m && a.CreatedBy == "Andy"
            && a.CreatedAtUtc == new DateTime(2026, 9, 15, 10, 0, 0)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAdjustment_Fails_WhenMonthClosed()
    {
        SetupMonth(OvertimeStatementStatus.Closed);
        var handler = new CreateAdjustmentHandler(_employees.Object, _statements.Object, _adjustments.Object, _currentUser.Object, TimeProvider.System);

        var result = await handler.Handle(new CreateAdjustmentRequest
        {
            PersonId = Person, Year = 2026, Month = 9, Type = OvertimeAdjustmentType.Payout, Hours = -1m, Note = "x"
        }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeAdjustmentMonthClosed);
    }

    [Fact]
    public async Task CreateAdjustment_Fails_ForUnknownEmployee()
    {
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeEmployee?)null);
        var handler = new CreateAdjustmentHandler(_employees.Object, _statements.Object, _adjustments.Object, _currentUser.Object, TimeProvider.System);

        var result = await handler.Handle(new CreateAdjustmentRequest
        {
            PersonId = Person, Year = 2026, Month = 9, Type = OvertimeAdjustmentType.Other, Hours = 1m, Note = "x"
        }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeEmployeeNotFound);
    }

    [Fact]
    public async Task DeleteAdjustment_Fails_WhenMonthClosed()
    {
        SetupMonth(OvertimeStatementStatus.Closed);
        _adjustments.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeAdjustment { Id = 5, PersonId = Person, Year = 2026, Month = 9 });
        var handler = new DeleteAdjustmentHandler(_statements.Object, _adjustments.Object);

        var result = await handler.Handle(new DeleteAdjustmentRequest { Id = 5 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeAdjustmentMonthClosed);
        _adjustments.Verify(r => r.DeleteAsync(It.IsAny<OvertimeAdjustment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAdjustment_Deletes_OnOpenMonth()
    {
        _adjustments.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeAdjustment { Id = 5, PersonId = Person, Year = 2026, Month = 9 });
        var handler = new DeleteAdjustmentHandler(_statements.Object, _adjustments.Object);

        var result = await handler.Handle(new DeleteAdjustmentRequest { Id = 5 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _adjustments.Verify(r => r.DeleteAsync(It.Is<OvertimeAdjustment>(a => a.Id == 5), It.IsAny<CancellationToken>()), Times.Once);
    }
}
