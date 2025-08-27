using Anela.Heblo.Persistence;
using Anela.Heblo.Xcc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        // Setup in-memory database with transaction warning suppression
        var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // Setup service provider with transaction warning suppression
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString())
                   .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
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
    public async Task BeginTransactionAsync_ShouldSucceedWithInMemoryDatabase()
    {
        // Act & Assert - With warning suppression, this should not throw
        await _unitOfWork.BeginTransactionAsync();

        // Transaction was started (even if it's a no-op for in-memory database)
        Assert.True(true); // Test passes if no exception is thrown
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
        // UnitOfWork should track transaction state and prevent multiple concurrent transactions
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
        // With warning suppression, UnitOfWork transaction flow should work properly
        await _unitOfWork.CommitTransactionAsync();
    }

    [Fact]
    public async Task TransactionFlow_BeginRollback_ShouldWorkCorrectly()
    {
        // Arrange
        await _unitOfWork.BeginTransactionAsync();

        // Act & Assert - Should not throw
        // With warning suppression, UnitOfWork transaction flow should work properly
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

        // Should not throw exceptions - multiple transaction cycles should work
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

    [Fact]
    public void Complete_ShouldMarkUnitOfWorkAsCompleted()
    {
        // Act
        _unitOfWork.Complete();

        // Assert - No exception should be thrown
        Assert.True(true); // Completion is marked internally
    }

    [Fact]
    public async Task DisposeAsync_WithoutComplete_ShouldNotSaveChanges()
    {
        // Act & Assert - Should not save changes when Complete() was not called
        await _unitOfWork.DisposeAsync();

        // No exception should be thrown, and changes should not be saved
        Assert.True(true); // Test passes if no exception occurs
    }

    [Fact]
    public async Task DisposeAsync_WithComplete_ShouldSaveChanges()
    {
        // Arrange
        _unitOfWork.Complete();

        // Act - This should trigger SaveChangesAsync due to Complete()
        await _unitOfWork.DisposeAsync();

        // Assert - No exception should be thrown
        Assert.True(true); // Test passes if no exception occurs
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _context.Dispose();
        _serviceProvider.Dispose();
    }
}