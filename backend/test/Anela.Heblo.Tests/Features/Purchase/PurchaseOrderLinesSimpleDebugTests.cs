using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Purchase;

public class PurchaseOrderLinesSimpleDebugTests
{
    [Fact]
    public async Task CreatePurchaseOrder_WithLines_ShouldPersistLinesInMemory()
    {
        // Arrange
        var logger = new Mock<ILogger<CreatePurchaseOrderHandler>>();
        var repository = new InMemoryPurchaseOrderRepository();
        var numberGenerator = new InMemoryPurchaseOrderNumberGenerator();
        var catalogRepository = new Mock<ICatalogRepository>();
        
        var handler = new CreatePurchaseOrderHandler(
            logger.Object, 
            repository, 
            numberGenerator, 
            catalogRepository.Object);

        var request = new CreatePurchaseOrderRequest(
            SupplierName: "Test Supplier Debug",
            OrderDate: "2024-08-02",
            ExpectedDeliveryDate: "2024-08-15",
            Notes: "Debug test order",
            Lines: new List<CreatePurchaseOrderLineRequest>
            {
                new("MAT001", "CODE001", "Test Material 1", 10m, 25.5m, "First test line"),
                new("MAT002", "CODE002", "Test Material 2", 5m, 15.0m, "Second test line")
            }
        );

        // Act - Create purchase order
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert - Creation successful and has lines
        response.Should().NotBeNull();
        response.Lines.Should().HaveCount(2, "Two lines were added to the order");
        
        Console.WriteLine($"Created order {response.OrderNumber} with {response.Lines.Count} lines");

        // Act - Get the created purchase order directly from repository
        var retrievedOrder = await repository.GetByIdWithDetailsAsync(response.Id);

        // Assert - Lines are persisted in repository
        retrievedOrder.Should().NotBeNull();
        retrievedOrder!.Lines.Should().HaveCount(2, "Lines should be persisted in repository");
        
        // Debug output
        Console.WriteLine($"Retrieved order {retrievedOrder.OrderNumber}:");
        Console.WriteLine($"- Supplier: {retrievedOrder.SupplierName}");
        Console.WriteLine($"- Lines count: {retrievedOrder.Lines.Count}");
        
        foreach (var line in retrievedOrder.Lines)
        {
            Console.WriteLine($"  - Line {line.Id}: MaterialId={line.MaterialId} x {line.Quantity} @ {line.UnitPrice}");
        }

        // Detailed assertions
        var firstLine = retrievedOrder.Lines.First();
        firstLine.Quantity.Should().Be(10m);
        firstLine.UnitPrice.Should().Be(25.5m);
        firstLine.LineTotal.Should().Be(255.0m);
        firstLine.Notes.Should().Be("First test line");

        var secondLine = retrievedOrder.Lines.Skip(1).First();
        secondLine.Quantity.Should().Be(5m);
        secondLine.UnitPrice.Should().Be(15.0m);
        secondLine.LineTotal.Should().Be(75.0m);
        secondLine.Notes.Should().Be("Second test line");

        // Check total amount
        retrievedOrder.TotalAmount.Should().Be(330.0m, "Total should be 255 + 75 = 330");
    }
}