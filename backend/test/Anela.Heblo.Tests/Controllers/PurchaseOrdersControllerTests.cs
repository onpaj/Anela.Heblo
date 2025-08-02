using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.API;
using Anela.Heblo.Application.Features.Purchase.Model;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class PurchaseOrdersControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PurchaseOrdersControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
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
        var request = new CreatePurchaseOrderRequest(
            "Test Supplier",
            DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd"),
            "Integration test order",
            new List<CreatePurchaseOrderLineRequest>
            {
                new("MAT001", 10, 25.50m, "Test line")
            });

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var content = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.Id.Should().NotBeEmpty();
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
        var request = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Empty order",
            new List<CreatePurchaseOrderLineRequest>());

        var response = await _client.PostAsJsonAsync("/api/purchase-orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var content = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        content.Should().NotBeNull();
        content!.Lines.Should().BeEmpty();
        content.TotalAmount.Should().Be(0);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithInvalidQuantity_ShouldThrowArgumentException()
    {
        var request = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Order with invalid line",
            new List<CreatePurchaseOrderLineRequest>
            {
                new(Guid.NewGuid(), -5, 25.00m, "Invalid negative quantity")
            });

        var postAction = async () => await _client.PostAsJsonAsync("/api/purchase-orders", request);

        await postAction.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Quantity must be greater than zero*");
    }

    [Fact]
    public async Task GetPurchaseOrderById_WithExistingOrder_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Test order for retrieval",
            new List<CreatePurchaseOrderLineRequest>
            {
                new(Guid.NewGuid(), 5, 10.00m, "Retrieval test line")
            });

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
        var nonExistentId = Guid.NewGuid();
        
        var response = await _client.GetAsync($"/api/purchase-orders/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePurchaseOrder_WithValidData_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Order to update",
            new List<CreatePurchaseOrderLineRequest>
            {
                new(Guid.NewGuid(), 5, 20.00m, "Original line")
            });

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var updateRequest = new UpdatePurchaseOrderRequest(
            orderId,
            DateTime.UtcNow.Date.AddDays(21),
            "Updated notes",
            new List<UpdatePurchaseOrderLineRequest>
            {
                new(createdOrder.Lines.First().Id, Guid.NewGuid(), 10, 30.00m, "Updated line")
            });

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
        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdatePurchaseOrderRequest(
            nonExistentId,
            DateTime.UtcNow.Date.AddDays(21),
            "Updated notes",
            new List<UpdatePurchaseOrderLineRequest>());

        var response = await _client.PutAsJsonAsync($"/api/purchase-orders/{nonExistentId}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePurchaseOrder_WithMismatchedId_ShouldReturnBadRequest()
    {
        var orderId = Guid.NewGuid();
        var differentId = Guid.NewGuid();
        var updateRequest = new UpdatePurchaseOrderRequest(
            differentId,
            DateTime.UtcNow.Date.AddDays(21),
            "Updated notes",
            new List<UpdatePurchaseOrderLineRequest>());

        var response = await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePurchaseOrderStatus_WithValidTransition_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Order for status update",
            new List<CreatePurchaseOrderLineRequest>
            {
                new(Guid.NewGuid(), 1, 10.00m, "Status test line")
            });

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
        var createRequest = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Order for invalid status update",
            new List<CreatePurchaseOrderLineRequest>
            {
                new(Guid.NewGuid(), 1, 10.00m, "Invalid status test line")
            });

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
        var nonExistentId = Guid.NewGuid();
        var statusRequest = new UpdatePurchaseOrderStatusRequest(nonExistentId, "InTransit");
        
        var response = await _client.PutAsJsonAsync($"/api/purchase-orders/{nonExistentId}/status", statusRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPurchaseOrderHistory_WithExistingOrder_ShouldReturnOk()
    {
        var createRequest = new CreatePurchaseOrderRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Order for history test",
            new List<CreatePurchaseOrderLineRequest>
            {
                new(Guid.NewGuid(), 1, 15.00m, "History test line")
            });

        var createResponse = await _client.PostAsJsonAsync("/api/purchase-orders", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        var orderId = createdOrder!.Id;

        var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "InTransit");
        await _client.PutAsJsonAsync($"/api/purchase-orders/{orderId}/status", statusRequest);

        var historyResponse = await _client.GetAsync($"/api/purchase-orders/{orderId}/history");

        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await historyResponse.Content.ReadFromJsonAsync<List<PurchaseOrderHistoryDto>>();
        content.Should().NotBeNull();
        content!.Should().HaveCount(2);
        content.Should().Contain(h => h.Action.Contains("Order created"));
        content.Should().Contain(h => h.Action.Contains("Status changed"));
    }

    [Fact]
    public async Task GetPurchaseOrderHistory_WithNonExistentOrder_ShouldReturnNotFound()
    {
        var nonExistentId = Guid.NewGuid();
        
        var response = await _client.GetAsync($"/api/purchase-orders/{nonExistentId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}