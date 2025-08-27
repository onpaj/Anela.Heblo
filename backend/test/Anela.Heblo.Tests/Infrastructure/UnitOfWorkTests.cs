using Anela.Heblo.Persistence;
using Anela.Heblo.Xcc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Tests.Infrastructure;

/// <summary>
/// Unit tests for the UnitOfWork implementation
/// </summary>
public class UnitOfWorkTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;

    public UnitOfWorkTests()
    {
        // Setup in-memory database with warnings suppressed for transaction tests
        var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.InMemory.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // Setup service provider
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString())
                   .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.InMemory.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _context = new ApplicationDbContext(dbContextOptions);
        _unitOfWork = new UnitOfWork(_context, _serviceProvider);
    }

    [Fact]
    public void UnitOfWork_Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        Assert.NotNull(_unitOfWork);
        Assert.IsAssignableFrom<IUnitOfWork>(_unitOfWork);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutTransaction_ShouldReturnChangeCount()
    {
        // Act
        var result = await _unitOfWork.SaveChangesAsync();

        // Assert
        Assert.Equal(0, result); // No changes were made
    }

    [Fact]
    public async Task BeginTransactionAsync_ShouldStartTransaction()
    {
        // Act & Assert - Should not throw exception
        // Note: In-memory database doesn't support real transactions, but the method should still execute
        await _unitOfWork.BeginTransactionAsync();
    }

    [Fact]
    public async Task CommitTransactionAsync_WithoutTransaction_ShouldThrowException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _unitOfWork.CommitTransactionAsync());

        Assert.Equal("No transaction to commit", exception.Message);
    }

    [Fact]
    public async Task RollbackTransactionAsync_WithoutTransaction_ShouldThrowException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _unitOfWork.RollbackTransactionAsync());

        Assert.Equal("No transaction to rollback", exception.Message);
    }

    [Fact]
    public async Task BeginTransactionAsync_WhenTransactionAlreadyExists_ShouldThrowException()
    {
        // Arrange
        await _unitOfWork.BeginTransactionAsync();

        // Act & Assert
        // Note: In-memory database doesn't support real transactions, but our UnitOfWork should still track state
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _unitOfWork.BeginTransactionAsync());

        Assert.Equal("Transaction already started", exception.Message);
    }

    [Fact]
    public async Task TransactionFlow_BeginCommit_ShouldWorkCorrectly()
    {
        // Arrange
        await _unitOfWork.BeginTransactionAsync();

        // Act & Assert - Should not throw
        // Note: In-memory database doesn't support real transactions, but our UnitOfWork should handle the flow
        await _unitOfWork.CommitTransactionAsync();
    }

    [Fact]
    public async Task TransactionFlow_BeginRollback_ShouldWorkCorrectly()
    {
        // Arrange
        await _unitOfWork.BeginTransactionAsync();

        // Act & Assert - Should not throw
        // Note: In-memory database doesn't support real transactions, but our UnitOfWork should handle the flow
        await _unitOfWork.RollbackTransactionAsync();
    }

    [Fact]
    public async Task MultipleBeginCommitCycles_ShouldWorkCorrectly()
    {
        // First cycle
        await _unitOfWork.BeginTransactionAsync();
        await _unitOfWork.CommitTransactionAsync();

        // Second cycle
        await _unitOfWork.BeginTransactionAsync();
        await _unitOfWork.CommitTransactionAsync();

        // Should not throw exceptions
        // Note: In-memory database doesn't support real transactions, but our UnitOfWork should handle multiple cycles
    }

    [Fact]
    public void Repository_ShouldReturnSameInstanceForSameEntityType()
    {
        // Act
        var repo1 = _unitOfWork.Repository<Anela.Heblo.Domain.Features.Purchase.PurchaseOrder, int>();
        var repo2 = _unitOfWork.Repository<Anela.Heblo.Domain.Features.Purchase.PurchaseOrder, int>();

        // Assert
        Assert.Same(repo1, repo2);
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _context.Dispose();
        _serviceProvider.Dispose();
    }
}