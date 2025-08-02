using Anela.Heblo.Application.Features.Purchase.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text;
using Xunit;
using FluentAssertions;
using Anela.Heblo.API;

namespace Anela.Heblo.Tests.Features.Purchase;

public class PurchaseOrderLinesDebugTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PurchaseOrderLinesDebugTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Ensure test environment is set to enable mock authentication
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"UseMockAuth", "true"}
                });
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithLines_ShouldPersistLines()
    {
        // Arrange
        var request = new CreatePurchaseOrderRequest(
            SupplierName: "Test Supplier Debug",
            OrderDate: "2024-08-02",
            ExpectedDeliveryDate: "2024-08-15",
            Notes: "Debug test order",
            Lines: new List<CreatePurchaseOrderLineRequest>
            {
                new("MAT001", 10m, 25.5m, "First test line"),
                new("MAT002", 5m, 15.0m, "Second test line")
            }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act - Create purchase order
        var createResponse = await _client.PostAsync("/api/purchase-orders", content);

        // Assert - Creation successful
        createResponse.Should().BeSuccessful();
        var createResult = await createResponse.Content.ReadAsStringAsync();
        var createdOrder = JsonSerializer.Deserialize<CreatePurchaseOrderResponse>(createResult, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        createdOrder.Should().NotBeNull();
        createdOrder!.Lines.Should().HaveCount(2, "Two lines were added to the order");
        
        Console.WriteLine($"Created order {createdOrder.OrderNumber} with {createdOrder.Lines.Count} lines");

        // Act - Get the created purchase order
        var getResponse = await _client.GetAsync($"/api/purchase-orders/{createdOrder.Id}");

        // Assert - Lines are persisted
        getResponse.Should().BeSuccessful();
        var getResult = await getResponse.Content.ReadAsStringAsync();
        var retrievedOrder = JsonSerializer.Deserialize<GetPurchaseOrderByIdResponse>(getResult,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        retrievedOrder.Should().NotBeNull();
        retrievedOrder!.Lines.Should().HaveCount(2, "Lines should be persisted in database");
        
        // Debug output
        Console.WriteLine($"Retrieved order {retrievedOrder.OrderNumber}:");
        Console.WriteLine($"- Supplier: {retrievedOrder.SupplierName}");
        Console.WriteLine($"- Lines count: {retrievedOrder.Lines.Count}");
        
        foreach (var line in retrievedOrder.Lines)
        {
            Console.WriteLine($"  - Line {line.Id}: {line.MaterialName} x {line.Quantity} @ {line.UnitPrice}");
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