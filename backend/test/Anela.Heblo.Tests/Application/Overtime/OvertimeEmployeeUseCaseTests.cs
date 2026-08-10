using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeEmployeeUseCaseTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<ILogetoClient> _client = new();

    [Fact]
    public async Task GetEmployees_ReturnsBalanceFromLatestClosedStatement_AndUntrackedPeople()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, IsActive = true }
            });
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeMonthlyStatement { PersonId = Person, Year = 2026, Month = 9, BalanceAfter = 7.5m, Status = OvertimeStatementStatus.Closed });
        var untracked = Guid.NewGuid();
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Person, FirstName = "Pepina", LastName = "H." },
                new() { Guid = untracked, FirstName = "Bára", LastName = "Petrová" },
                new() { Guid = Guid.NewGuid(), FirstName = "Ex", LastName = "Worker", Inactive = true }
            });

        var handler = new GetOvertimeEmployeesHandler(_employees.Object, _statements.Object, _client.Object);
        var result = await handler.Handle(new GetOvertimeEmployeesRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Employees.Single().CurrentBalance.Should().Be(7.5m);
        result.AvailablePeople.Should().ContainSingle(p => p.PersonId == untracked && p.FullName == "Bára Petrová");
    }

    [Fact]
    public async Task GetEmployees_FallsBackToBaseline_WhenNoClosedStatement()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, IsActive = true }
            });
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeMonthlyStatement?)null);
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>());

        var handler = new GetOvertimeEmployeesHandler(_employees.Object, _statements.Object, _client.Object);
        var result = await handler.Handle(new GetOvertimeEmployeesRequest(), CancellationToken.None);

        result.Employees.Single().CurrentBalance.Should().Be(2.5m);
    }

    [Fact]
    public async Task Upsert_RejectsBaselineChange_WhenClosedStatementExists()
    {
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeEmployee { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 9, 1) });
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeMonthlyStatement { PersonId = Person, Status = OvertimeStatementStatus.Closed });

        var handler = new UpsertOvertimeEmployeeHandler(_employees.Object, _statements.Object);
        var result = await handler.Handle(new UpsertOvertimeEmployeeRequest
        {
            PersonId = Person, DisplayName = "Pepina", BaselineHours = 99m, BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _employees.Verify(r => r.UpsertAsync(It.IsAny<OvertimeEmployee>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upsert_SavesEmployee_WhenNoClosedStatement()
    {
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeEmployee?)null);
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeMonthlyStatement?)null);

        var handler = new UpsertOvertimeEmployeeHandler(_employees.Object, _statements.Object);
        var result = await handler.Handle(new UpsertOvertimeEmployeeRequest
        {
            PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _employees.Verify(r => r.UpsertAsync(It.Is<OvertimeEmployee>(e => e.PersonId == Person && e.BaselineHours == 2.5m), It.IsAny<CancellationToken>()), Times.Once);
    }
}
