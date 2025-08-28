using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Xcc.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Tests;

/// <summary>
/// Tests to verify the UnitOfWork dispose pattern works correctly:
/// - Changes are automatically saved on dispose (without calling Complete())
/// - Changes are not saved when Abort() is called
/// - No need to manually call SaveChangesAsync
/// </summary>
public class UnitOfWorkDisposePatternTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;

    public UnitOfWorkDisposePatternTests()
    {
        var services = new ServiceCollection();
        
        // Use in-memory database for testing
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        // Register repositories that might be used
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // Ensure database is created
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UnitOfWork_ShouldAutoSaveChanges_OnDispose()
    {
        // Arrange
        var testOrderNumber = "TEST-001";
        var testSupplier = "Test Supplier";
        var testDate = DateTime.UtcNow.Date;

        // Act - Use UnitOfWork with dispose pattern
        await using (var unitOfWork = CreateUnitOfWork())
        {
            var repository = unitOfWork.Repository<PurchaseOrder, int>();
            
            var purchaseOrder = new PurchaseOrder(
                testOrderNumber,
                testSupplier,
                testDate,
                null,
                "Test notes",
                "Test User");

            await repository.AddAsync(purchaseOrder, CancellationToken.None);
            
            // Important: NOT calling unitOfWork.SaveChangesAsync() manually
            // Changes should be saved automatically on dispose
        }

        // Assert - Verify changes were saved automatically
        var savedOrder = await _context.PurchaseOrders
            .FirstOrDefaultAsync(po => po.OrderNumber == testOrderNumber);

        savedOrder.Should().NotBeNull("changes should be automatically saved on UnitOfWork dispose");
        savedOrder!.SupplierName.Should().Be(testSupplier);
        savedOrder.OrderDate.Should().Be(testDate);
        savedOrder.Notes.Should().Be("Test notes");
    }

    [Fact]
    public async Task UnitOfWork_ShouldNotSaveChanges_WhenAborted()
    {
        // Arrange
        var testOrderNumber = "TEST-ABORT-001";
        var testSupplier = "Test Supplier Abort";
        var testDate = DateTime.UtcNow.Date;

        // Act - Use UnitOfWork with Abort()
        await using (var unitOfWork = CreateUnitOfWork())
        {
            var repository = unitOfWork.Repository<PurchaseOrder, int>();
            
            var purchaseOrder = new PurchaseOrder(
                testOrderNumber,
                testSupplier,
                testDate,
                null,
                "Test notes abort",
                "Test User");

            await repository.AddAsync(purchaseOrder, CancellationToken.None);
            
            // Abort the unit of work - changes should NOT be saved
            unitOfWork.Abort();
        }

        // Assert - Verify changes were NOT saved
        var savedOrder = await _context.PurchaseOrders
            .FirstOrDefaultAsync(po => po.OrderNumber == testOrderNumber);

        savedOrder.Should().BeNull("changes should not be saved when UnitOfWork is aborted");
    }

    [Fact]
    public async Task UnitOfWork_WithExplicitSaveChangesAsync_ShouldWork()
    {
        // Arrange
        var testOrderNumber = "TEST-EXPLICIT-001";
        var testSupplier = "Test Supplier Explicit";
        var testDate = DateTime.UtcNow.Date;

        // Act - Use UnitOfWork with explicit SaveChangesAsync
        await using (var unitOfWork = CreateUnitOfWork())
        {
            var repository = unitOfWork.Repository<PurchaseOrder, int>();
            
            var purchaseOrder = new PurchaseOrder(
                testOrderNumber,
                testSupplier,
                testDate,
                null,
                "Test notes explicit",
                "Test User");

            await repository.AddAsync(purchaseOrder, CancellationToken.None);
            
            // Explicitly call SaveChangesAsync (this should also work)
            await unitOfWork.SaveChangesAsync();
        }

        // Assert - Verify changes were saved
        var savedOrder = await _context.PurchaseOrders
            .FirstOrDefaultAsync(po => po.OrderNumber == testOrderNumber);

        savedOrder.Should().NotBeNull("explicit SaveChangesAsync should work");
        savedOrder!.SupplierName.Should().Be(testSupplier);
    }

    [Fact]
    public async Task UnitOfWork_WithExplicitSaveAndThenAbort_ShouldHaveChangesSaved()
    {
        // Arrange
        var testOrderNumber = "TEST-SAVE-THEN-ABORT-001";
        var testSupplier = "Test Supplier Save Then Abort";
        var testDate = DateTime.UtcNow.Date;

        // Act - Save explicitly, then abort
        await using (var unitOfWork = CreateUnitOfWork())
        {
            var repository = unitOfWork.Repository<PurchaseOrder, int>();
            
            var purchaseOrder = new PurchaseOrder(
                testOrderNumber,
                testSupplier,
                testDate,
                null,
                "Test notes save then abort",
                "Test User");

            await repository.AddAsync(purchaseOrder, CancellationToken.None);
            
            // Save changes explicitly first
            await unitOfWork.SaveChangesAsync();
            
            // Then abort (should not affect already saved changes)
            unitOfWork.Abort();
        }

        // Assert - Verify changes were saved (because SaveChangesAsync was called before Abort)
        var savedOrder = await _context.PurchaseOrders
            .FirstOrDefaultAsync(po => po.OrderNumber == testOrderNumber);

        savedOrder.Should().NotBeNull("changes should be saved when SaveChangesAsync is called explicitly, even if later aborted");
        savedOrder!.SupplierName.Should().Be(testSupplier);
    }

    [Fact]
    public async Task UnitOfWork_MultipleChanges_ShouldAutoSave()
    {
        // Arrange
        var testOrderNumber1 = "TEST-MULTI-001";
        var testOrderNumber2 = "TEST-MULTI-002";
        var testSupplier = "Test Multi Supplier";
        var testDate = DateTime.UtcNow.Date;

        // Act - Make multiple changes in one UnitOfWork
        await using (var unitOfWork = CreateUnitOfWork())
        {
            var repository = unitOfWork.Repository<PurchaseOrder, int>();
            
            var purchaseOrder1 = new PurchaseOrder(
                testOrderNumber1,
                testSupplier,
                testDate,
                null,
                "First order",
                "Test User");

            var purchaseOrder2 = new PurchaseOrder(
                testOrderNumber2,
                testSupplier,
                testDate.AddDays(1),
                null,
                "Second order",
                "Test User");

            await repository.AddAsync(purchaseOrder1, CancellationToken.None);
            await repository.AddAsync(purchaseOrder2, CancellationToken.None);
            
            // No explicit SaveChangesAsync - should auto-save on dispose
        }

        // Assert - Verify both changes were saved
        var savedOrders = await _context.PurchaseOrders
            .Where(po => po.OrderNumber == testOrderNumber1 || po.OrderNumber == testOrderNumber2)
            .ToListAsync();

        savedOrders.Should().HaveCount(2, "both orders should be automatically saved");
        savedOrders.Should().Contain(po => po.OrderNumber == testOrderNumber1);
        savedOrders.Should().Contain(po => po.OrderNumber == testOrderNumber2);
    }

    private IUnitOfWork CreateUnitOfWork()
    {
        var context = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Func<Type, object?> repositoryFactory = type => _scope.ServiceProvider.GetService(type);
        return new UnitOfWork(context, repositoryFactory);
    }

    public void Dispose()
    {
        _scope.Dispose();
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}