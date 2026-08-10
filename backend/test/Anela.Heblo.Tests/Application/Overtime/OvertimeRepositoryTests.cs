using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Attendance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public OvertimeRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"OvertimeTestDb_{Guid.NewGuid()}")
            .Options;
        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task EmployeeUpsert_InsertsThenUpdates()
    {
        var repo = new OvertimeEmployeeRepository(_context);
        await repo.UpsertAsync(new OvertimeEmployee
        {
            PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m,
            BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        await repo.UpsertAsync(new OvertimeEmployee
        {
            PersonId = Person, DisplayName = "Pepina H.", BaselineHours = 3.0m,
            BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        var all = await repo.GetAllAsync(CancellationToken.None);
        all.Should().HaveCount(1);
        all[0].DisplayName.Should().Be("Pepina H.");
        all[0].BaselineHours.Should().Be(3.0m);
    }

    [Fact]
    public async Task GetLatestClosed_ReturnsNewestClosedStatement_IgnoringOpen()
    {
        var repo = new OvertimeStatementRepository(_context);
        await repo.AddAsync(Statement(2026, 9, OvertimeStatementStatus.Closed, balanceAfter: 5m), CancellationToken.None);
        await repo.AddAsync(Statement(2026, 10, OvertimeStatementStatus.Closed, balanceAfter: 8m), CancellationToken.None);
        await repo.AddAsync(Statement(2026, 11, OvertimeStatementStatus.Open, balanceAfter: 0m), CancellationToken.None);

        var latest = await repo.GetLatestClosedAsync(Person, CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.Month.Should().Be(10);
        latest.BalanceAfter.Should().Be(8m);
    }

    [Fact]
    public async Task AnyOpenBefore_DetectsOlderOpenMonth()
    {
        var repo = new OvertimeStatementRepository(_context);
        await repo.AddAsync(Statement(2026, 9, OvertimeStatementStatus.Open, 0m), CancellationToken.None);

        (await repo.AnyOpenBeforeAsync(2026, 10, CancellationToken.None)).Should().BeTrue();
        (await repo.AnyOpenBeforeAsync(2026, 9, CancellationToken.None)).Should().BeFalse();
    }

    private static OvertimeMonthlyStatement Statement(int year, int month, OvertimeStatementStatus status, decimal balanceAfter) => new()
    {
        PersonId = Person, Year = year, Month = month, Status = status, BalanceAfter = balanceAfter
    };
}
