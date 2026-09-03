// Note: Uses a CountingRepositoryWrapper instead of SQLite + DbCommandInterceptor for the
// same reason as PackingMaterialsListQueryCountTests.cs (ApplicationDbContext.OnModelCreating
// has PostgreSQL-specific column type annotations incompatible with SQLite's EnsureCreated()).
// The wrapper proves the handler calls GetAllAsync zero times and GetMaterialNamesByIdsAsync
// exactly once, with the correct page-scoped id set.

using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetConsumptionHistoryQueryCountTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public GetConsumptionHistoryQueryCountTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"GetConsumptionHistory_QueryCount_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Handle_NeverCallsGetAllAsync_AndCallsGetMaterialNamesByIdsAsyncExactlyOnceWithPageScopedIds()
    {
        // Arrange
        var m1 = new PackingMaterial("M1", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("M2", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddRangeAsync(m1, m2);
        await _context.SaveChangesAsync();

        await _context.Set<PackingMaterialConsumption>().AddRangeAsync(
            new PackingMaterialConsumption(m1.Id, new DateOnly(2026, 1, 10), ConsumptionType.PerOrder, 5m, "INV-1"),
            new PackingMaterialConsumption(m2.Id, new DateOnly(2026, 1, 11), ConsumptionType.PerOrder, 3m, "INV-2"));
        await _context.SaveChangesAsync();

        var countingRepository = new CountingRepositoryWrapper(new PackingMaterialRepository(_context));
        var handler = new GetConsumptionHistoryHandler(
            countingRepository,
            NullLogger<GetConsumptionHistoryHandler>.Instance);

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);

        countingRepository.GetAllAsyncCallCount.Should().Be(0,
            "GetConsumptionHistoryHandler must never load the full packing-materials table");
        countingRepository.GetMaterialNamesByIdsAsyncCallCount.Should().Be(1,
            "material names must be resolved with a single targeted lookup per request");

        countingRepository.LastMaterialNamesByIdsAsyncIds.Should().NotBeNull();
        var expectedIds = response.Items.Select(i => i.PackingMaterialId).Distinct().ToHashSet();
        countingRepository.LastMaterialNamesByIdsAsyncIds!.ToHashSet().Should().BeSubsetOf(expectedIds);
        countingRepository.LastMaterialNamesByIdsAsyncIds!.Count.Should().BeLessThanOrEqualTo(expectedIds.Count);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Wrapper around PackingMaterialRepository that tracks calls to GetAllAsync and
    /// GetMaterialNamesByIdsAsync to verify GetConsumptionHistoryHandler's data access pattern.
    /// </summary>
    private sealed class CountingRepositoryWrapper : IPackingMaterialRepository
    {
        private readonly PackingMaterialRepository _inner;

        public int GetAllAsyncCallCount { get; private set; }
        public int GetMaterialNamesByIdsAsyncCallCount { get; private set; }
        public IReadOnlyCollection<int>? LastMaterialNamesByIdsAsyncIds { get; private set; }

        public CountingRepositoryWrapper(PackingMaterialRepository inner)
        {
            _inner = inner;
        }

        public async Task<IEnumerable<PackingMaterial>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllAsyncCallCount++;
            return await _inner.GetAllAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
            IEnumerable<int> packingMaterialIds,
            CancellationToken cancellationToken = default)
        {
            GetMaterialNamesByIdsAsyncCallCount++;
            var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
            LastMaterialNamesByIdsAsyncIds = ids;
            return await _inner.GetMaterialNamesByIdsAsync(ids, cancellationToken);
        }

        // Delegate all other methods to inner repository
        public Task<PackingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
            => _inner.GetRecentLogsAsync(packingMaterialId, fromDate, cancellationToken);

        public Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
            IEnumerable<int> packingMaterialIds,
            DateTime fromDate,
            CancellationToken cancellationToken = default)
            => _inner.GetRecentLogsForMaterialsAsync(packingMaterialIds, fromDate, cancellationToken);

        public Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default)
            => _inner.HasDailyProcessingBeenRunAsync(date, cancellationToken);

        public Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
            => _inner.AddDailyRunAsync(run, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllWithAllocationsAsync(cancellationToken);

        public Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default)
            => _inner.GetByIdWithAllocationsAsync(id, cancellationToken);

        public Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default)
            => _inner.AddConsumptionRowsAsync(rows, cancellationToken);

        public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
            => _inner.GetConsumptionsByDateAsync(date, cancellationToken);

        public Task<PackingMaterial> AddAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
            => _inner.AddAsync(entity, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> AddRangeAsync(IEnumerable<PackingMaterial> entities, CancellationToken cancellationToken = default)
            => _inner.AddRangeAsync(entities, cancellationToken);

        public Task UpdateAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(entity, cancellationToken);

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(id, cancellationToken);

        public Task DeleteRangeAsync(IEnumerable<PackingMaterial> entities, CancellationToken cancellationToken = default)
            => _inner.DeleteRangeAsync(entities, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);

        public Task<PackingMaterial?> SingleOrDefaultAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.SingleOrDefaultAsync(predicate, cancellationToken);

        public Task<bool> AnyAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.AnyAsync(predicate, cancellationToken);

        public Task<int> CountAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>>? predicate = null, CancellationToken cancellationToken = default)
            => _inner.CountAsync(predicate, cancellationToken);

        public Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
            MaterialConsumptionHistoryFilter filter,
            int skip,
            int take,
            bool ascending,
            CancellationToken cancellationToken = default)
            => _inner.GetConsumptionHistoryAsync(filter, skip, take, ascending, cancellationToken);
    }
}
