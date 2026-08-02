using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Repositories;

public class MarketingActionRepositoryGetPagedTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MarketingActionRepository _repository;

    public MarketingActionRepositoryGetPagedTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new MarketingActionRepository(_context, NullLogger<MarketingActionRepository>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetPagedAsync_IncludeDeletedTrue_ReturnsBothRows()
    {
        await SeedActionAsync(deleted: false);
        await SeedActionAsync(deleted: true);

        var result = await _repository.GetPagedAsync(
            new MarketingActionQueryCriteria { IncludeDeleted = true });

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_IncludeDeletedFalse_ReturnsOnlyNonDeleted()
    {
        await SeedActionAsync(deleted: false);
        await SeedActionAsync(deleted: true);

        var result = await _repository.GetPagedAsync(
            new MarketingActionQueryCriteria { IncludeDeleted = false });

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => !x.IsDeleted);
    }

    private async Task<MarketingAction> SeedActionAsync(bool deleted)
    {
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: DateTime.UtcNow,
            endDate: null,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);

        if (deleted)
        {
            action.SoftDelete("seed-user", "Seeder", DateTime.UtcNow);
        }

        _context.Set<MarketingAction>().Add(action);
        await _context.SaveChangesAsync();
        return action;
    }
}
