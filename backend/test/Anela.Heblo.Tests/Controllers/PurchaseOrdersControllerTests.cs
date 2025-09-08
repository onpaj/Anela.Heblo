using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrders;
using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Anela.Heblo.Tests.Controllers;

public class PurchaseOrdersControllerTests : IClassFixture<PurchaseOrdersTestFactory>
{
    private readonly PurchaseOrdersTestFactory _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public PurchaseOrdersControllerTests(PurchaseOrdersTestFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _output = output;
    }

    [Fact]
    public async Task GetPurchaseOrders_WithDefaultParameters_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/purchase-orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetPurchaseOrdersResponse>();
        content.Should().NotBeNull();
        content!.Orders.Should().NotBeNull();
        content.TotalCount.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task GetPurchaseOrders_WithSearchTerm_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/purchase-orders?searchTerm=test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPurchaseOrders_WithStatusFilter_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/purchase-orders?status=Draft");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPurchaseOrders_WithDateRange_ShouldReturnOk()
    {
        var fromDate = DateTime.UtcNow.Date.AddDays(-30).ToString("yyyy-MM-dd");
        var toDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        var response = await _client.GetAsync($"/api/purchase-orders?fromDate={fromDate}&toDate={toDate}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPurchaseOrders_WithPagination_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/purchase-orders?pageNumber=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPurchaseOrders_WithSorting_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/purchase-orders?sortBy=OrderNumber&sortDescending=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithValidData_ShouldReturnCreated()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Integration test order",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 10, UnitPrice = 25.50m, Notes = "Test line" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.Id.Should().NotBe(0);
        content.OrderNumber.Should().NotBeNullOrEmpty();
        content.Status.Should().Be("Draft");
        content.TotalAmount.Should().Be(255.00m);
        content.Lines.Should().HaveCount(1);

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain($"/api/purchase-orders/{content.Id}");
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithEmptyLines_ShouldReturnCreated()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Empty order",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>()
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.Lines.Should().BeEmpty();
        content.TotalAmount.Should().Be(0);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithInvalidQuantity_ShouldReturnBadRequest()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Order with invalid line",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = -5, UnitPrice = 25.00m, Notes = "Invalid negative quantity" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPurchaseOrderById_WithExistingOrder_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Test order for retrieval",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 5, UnitPrice = 10.00m, Notes = "Retrieval test line" }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var getResponse = await _client.GetAsync($"/api/purchase-orders/{orderId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await getResponse.Content.ReadFromJsonAsync<GetPurchaseOrderByIdResponse>();
        content.Should().NotBeNull();
        content!.Id.Should().Be(orderId);
        content.OrderNumber.Should().Be(createdOrder.OrderNumber);
        content.Status.Should().Be("Draft");
        content.Lines.Should().HaveCount(1);
        content.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPurchaseOrderById_WithNonExistentOrder_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;

        var response = await _client.GetAsync($"/api/purchase-orders/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePurchaseOrder_WithValidData_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Order to update",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 5, UnitPrice = 20.00m, Notes = "Original line" }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var updateRequest = new UpdatePurchaseOrderRequest
        {
            Id = orderId,
            SupplierId = 1,
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21),
            Notes = "Updated notes",
            Lines = new List<UpdatePurchaseOrderLineRequest>
            {
                new() { Id = createdOrder.Lines.First().Id, MaterialId = "MAT002", Name = "Updated Material", Quantity = 10, UnitPrice = 30.00m, Notes = "Updated line" }
            },
            OrderNumber = null // OrderNumber - keep existing
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await updateResponse.Content.ReadFromJsonAsync<UpdatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.Notes.Should().Be("Updated notes");
        content.Lines.Should().HaveCount(1);
        content.Lines.First().Quantity.Should().Be(10);
        content.Lines.First().UnitPrice.Should().Be(30.00m);
    }

    [Fact]
    public async Task UpdatePurchaseOrder_WithNonExistentOrder_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;
        var updateRequest = new UpdatePurchaseOrderRequest
        {
            Id = nonExistentId,
            SupplierId = 1,
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21),
            Notes = "Updated notes",
            Lines = new List<UpdatePurchaseOrderLineRequest>(),
            OrderNumber = null // OrderNumber - keep existing
        };

        var response = await _client.PutAsJsonAsync($"/api/purchase-orders/{nonExistentId}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePurchaseOrder_WithMismatchedId_ShouldReturnBadRequest()
    {
        var orderId = 12345;
        var differentId = 54321;
        var updateRequest = new UpdatePurchaseOrderRequest
        {
            Id = differentId,
            SupplierId = 1,
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21),
            Notes = "Updated notes",
            Lines = new List<UpdatePurchaseOrderLineRequest>(),
            OrderNumber = null // OrderNumber - keep existing
        };

        var response = await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePurchaseOrderStatus_WithValidTransition_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Order for status update",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 10.00m, Notes = "Status test line" }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "InTransit");
        var statusResponse = await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}/status", statusRequest);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await statusResponse.Content.ReadFromJsonAsync<UpdatePurchaseOrderStatusResponse>();
        content.Should().NotBeNull();
        content!.Status.Should().Be("InTransit");
        content.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdatePurchaseOrderStatus_WithInvalidTransition_ShouldReturnBadRequest()
    {
        var createRequest = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Order for invalid status update",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 10.00m, Notes = "Invalid status test line" }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "Completed");
        var statusResponse = await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}/status", statusRequest);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePurchaseOrderStatus_WithNonExistentOrder_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;
        var statusRequest = new UpdatePurchaseOrderStatusRequest(nonExistentId, "InTransit");

        var response = await _client.PutAsJsonAsync($"/api/purchase-orders/{nonExistentId}/status", statusRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPurchaseOrderHistory_WithExistingOrder_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Order for history test",
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 15.00m, Notes = "History test line" }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "InTransit");
        await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}/status", statusRequest);

        var historyResponse = await _client.GetAsync($"/api/purchase-orders/{orderId}/history");

        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await historyResponse.Content.ReadFromJsonAsync<ListResponse<PurchaseOrderHistoryDto>>();
        content.Should().NotBeNull();
        content!.Items.Should().HaveCount(2);
        content.Items.Should().Contain(h => h.Action.Contains("Order created"));
        content.Items.Should().Contain(h => h.Action.Contains("Status changed"));
    }

    [Fact]
    public async Task GetPurchaseOrderHistory_WithNonExistentOrder_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;

        var response = await _client.GetAsync($"/api/purchase-orders/{nonExistentId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithCustomOrderNumber_ShouldUseCustomNumber()
    {
        var customOrderNumber = "CUSTOM-ORDER-123";
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Custom order number test",
            OrderNumber = customOrderNumber, // Custom order number
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 10.00m, Notes = "Custom number test line" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.OrderNumber.Should().Be(customOrderNumber);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithNullOrderNumber_ShouldGenerateDefault()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Auto-generated order number test",
            OrderNumber = null, // Let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 10.00m, Notes = "Auto-gen test line" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.OrderNumber.Should().NotBeNullOrEmpty();
        content.OrderNumber.Should().StartWith("PO");
        content.OrderNumber.Should().MatchRegex(@"^PO\d{8}-\d{4}$"); // POyyyyMMdd-HHmm format
    }

    [Fact]
    public async Task UpdatePurchaseOrder_WithCustomOrderNumber_ShouldUpdateOrderNumber()
    {
        // Create order first
        var createRequest = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            Notes = "Order to update order number",
            OrderNumber = null, // Let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 20.00m, Notes = "Original line" }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        // Update with custom order number
        var customOrderNumber = "UPDATED-ORDER-456";
        var updateRequest = new UpdatePurchaseOrderRequest
        {
            Id = orderId,
            SupplierId = 1,
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21),
            Notes = "Updated notes",
            Lines = new List<UpdatePurchaseOrderLineRequest>
            {
                new() { Id = createdOrder.Lines.First().Id, MaterialId = "MAT001", Name = "Test Material", Quantity = 1, UnitPrice = 20.00m, Notes = "Original line" }
            },
            OrderNumber = customOrderNumber // Custom order number
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await updateResponse.Content.ReadFromJsonAsync<UpdatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.OrderNumber.Should().Be(customOrderNumber);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithRecalculateAll_ShouldReturnOk()
    {
        var request = new RecalculatePurchasePriceRequest
        {
            RecalculateAll = true,
            ForceReload = false
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders/recalculate-purchase-price", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<RecalculatePurchasePriceResponse>();
        content.Should().NotBeNull();
        content!.TotalCount.Should().BeGreaterOrEqualTo(0);
        content.SuccessCount.Should().BeGreaterOrEqualTo(0);
        content.FailedCount.Should().BeGreaterOrEqualTo(0);
        content.ProcessedProducts.Should().NotBeNull();
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithSpecificProduct_ShouldReturnNotFound()
    {
        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = "NONEXISTENT_PRODUCT",
            RecalculateAll = false
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders/recalculate-purchase-price", request);

        // After refactoring to result-based error handling, non-existent product returns 404 Not Found
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Test based on raw JSON structure
        var rawContent = await response.Content.ReadAsStringAsync();
        rawContent.Should().Contain("\"success\":false");
        rawContent.Should().Contain("\"errorCode\":\"CatalogItemNotFound\"");
        rawContent.Should().Contain("\"ProductCode\":\"NONEXISTENT_PRODUCT\"");
        rawContent.Should().Contain("\"message\":\"No products found to recalculate\"");
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithInvalidRequest_ShouldReturnBadRequest()
    {
        var request = new RecalculatePurchasePriceRequest
        {
            ProductCode = null,
            RecalculateAll = false
        };

        var response = await _client.PostAsJsonAsync("/api/purchase-orders/recalculate-purchase-price", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class PurchaseOrdersTestFactory : HebloWebApplicationFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // The PurchaseModule now only registers default implementations
        // Tests use the EF Core in-memory database from the base factory
        // This ensures data persists properly within each test

        // Override the default number generator to use in-memory implementation
        var numberGeneratorDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IPurchaseOrderNumberGenerator));
        if (numberGeneratorDescriptor != null)
        {
            services.Remove(numberGeneratorDescriptor);
        }

        // Use in-memory number generator for testing
        services.AddScoped<IPurchaseOrderNumberGenerator, InMemoryPurchaseOrderNumberGenerator>();

        // Add mock supplier repository
        services.AddScoped<ISupplierRepository, MockSupplierRepository>();
    }
}