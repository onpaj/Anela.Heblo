using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.DataQuality;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DqtRunRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly DqtRunRepository _repository;

    public DqtRunRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new DqtRunRepository(_context);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_ReturnsRunCoveringDate()
    {
        // Arrange
        var yesterday = new DateOnly(2026, 5, 5);
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        run.Complete(totalChecked: 100, totalMismatches: 0);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, yesterday);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(run.Id);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_ReturnsNullWhenNoRunCoversDate()
    {
        // Arrange
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 3),
            DqtTriggerType.Scheduled);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act — yesterday is outside the run's range
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 5, 5));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_FiltersByTestType()
    {
        // Arrange
        var yesterday = new DateOnly(2026, 5, 5);
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act
        var sameType = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, yesterday);

        // Assert
        sameType.Should().NotBeNull();
        sameType!.TestType.Should().Be(DqtTestType.IssuedInvoiceComparison);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_ReturnsMostRecentByStartedAt()
    {
        // Arrange — two runs both cover yesterday; the later StartedAt wins.
        var yesterday = new DateOnly(2026, 5, 5);
        var earlier = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        typeof(DqtRun).GetProperty(nameof(DqtRun.StartedAt))!
            .SetValue(earlier, new DateTime(2026, 5, 6, 6, 0, 0, DateTimeKind.Utc));

        var later = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Manual);
        typeof(DqtRun).GetProperty(nameof(DqtRun.StartedAt))!
            .SetValue(later, new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));

        _context.Set<DqtRun>().AddRange(earlier, later);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, yesterday);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(later.Id);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_MatchesWideRangeCoveringDate()
    {
        // Arrange — run with a multi-day range that includes yesterday.
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 7),
            DqtTriggerType.Manual);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 5, 5));

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(run.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
