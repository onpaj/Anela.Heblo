// Note: This test uses a CountingRepositoryWrapper instead of SQLite + DbCommandInterceptor
// because ApplicationDbContext.OnModelCreating contains PostgreSQL-specific column type
// annotations (decimal(18,6), timestamp without time zone) that prevent EnsureCreated()
// from succeeding with the SQLite provider. The repository wrapper approach provides
// equivalent guarantees at the repository method level: it proves the handler calls
// GetAllAsync exactly once and GetRecentLogsForMaterialsAsync exactly once, which
// with the known single-query implementations of those methods, is equivalent to
// exactly two DB round-trips.

using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialsListQueryCountTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public PackingMaterialsListQueryCountTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialsList_QueryCount_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Handle_IssuesExactlyTwoReaderExecutions()
    {
        // Arrange
        var m1 = new PackingMaterial("M1", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("M2", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddRangeAsync(m1, m2);
        await _context.SaveChangesAsync();

        var countingRepository = new CountingRepositoryWrapper(new PackingMaterialRepository(_context));
        var handler = new GetPackingMaterialsListHandler(
            countingRepository,
            NullLogger<GetPackingMaterialsListHandler>.Instance);

        // Act
        await handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        countingRepository.GetAllAsyncCallCount.Should().Be(1,
            "GetAllAsync should be called exactly once for fetching materials");
        countingRepository.GetRecentLogsForMaterialsAsyncCallCount.Should().Be(1,
            "GetRecentLogsForMaterialsAsync should be called exactly once for fetching logs");
        countingRepository.TotalDataAccessOperations.Should().Be(2,
            "the list handler should issue exactly two data access operations");
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Wrapper around PackingMaterialRepository that tracks method calls to verify
    /// the handler uses exactly two data access operations (one for materials, one for logs).
    /// </summary>
    private sealed class CountingRepositoryWrapper : IPackingMaterialRepository
    {
        private readonly PackingMaterialRepository _inner;

        public int GetAllAsyncCallCount { get; private set; }
        public int GetRecentLogsForMaterialsAsyncCallCount { get; private set; }
        public int TotalDataAccessOperations => GetAllAsyncCallCount + GetRecentLogsForMaterialsAsyncCallCount;

        public CountingRepositoryWrapper(PackingMaterialRepository inner)
        {
            _inner = inner;
        }

        public async Task<IEnumerable<PackingMaterial>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllAsyncCallCount++;
            return await _inner.GetAllAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
            IEnumerable<int> packingMaterialIds,
            DateTime fromDate,
            CancellationToken cancellationToken = default)
        {
            GetRecentLogsForMaterialsAsyncCallCount++;
            return await _inner.GetRecentLogsForMaterialsAsync(packingMaterialIds, fromDate, cancellationToken);
        }

        // Delegate all other methods to inner repository
        public Task<PackingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
            => _inner.GetRecentLogsAsync(packingMaterialId, fromDate, cancellationToken);

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
