using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Anela.Heblo.Tests.Controllers;

public class ManufactureOrderControllerTests : IClassFixture<ManufactureOrderTestFactory>
{
    private readonly ManufactureOrderTestFactory _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly JsonSerializerOptions _jsonOptions;

    public ManufactureOrderControllerTests(ManufactureOrderTestFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _output = output;

        // Configure JSON options to handle string enums (matching API behavior)
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    [Fact]
    public async Task GetOrders_WithDefaultParameters_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/ManufactureOrder");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetManufactureOrdersResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Orders.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrders_WithStateFilter_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/ManufactureOrder?state=Draft");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrders_WithDateRange_ShouldReturnOk()
    {
        var dateFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd");
        var dateTo = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

        var response = await _client.GetAsync($"/api/ManufactureOrder?dateFrom={dateFrom}&dateTo={dateTo}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrders_WithResponsiblePersonFilter_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/ManufactureOrder?responsiblePerson=John%20Doe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrder_WithValidData_ShouldReturnCreated()
    {
        var request = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI001",
            ProductName = "Test Semi Product",
            OriginalBatchSize = 1000.0,
            NewBatchSize = 1500.0,
            ScaleFactor = 1.5,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Integration Test User",
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD001",
                    ProductName = "Final Product 1",
                    PlannedQuantity = 100.0
                },
                new()
                {
                    ProductCode = "PROD002",
                    ProductName = "Final Product 2",
                    PlannedQuantity = 150.0
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/ManufactureOrder", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CreateManufactureOrderResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Id.Should().NotBe(0);
        content.OrderNumber.Should().NotBeNullOrEmpty();

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain($"/api/ManufactureOrder/{content.Id}");
    }

    [Fact]
    public async Task CreateOrder_WithEmptyProducts_ShouldReturnCreated()
    {
        var request = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI002",
            ProductName = "Test Semi Product 2",
            OriginalBatchSize = 500.0,
            NewBatchSize = 750.0,
            ScaleFactor = 1.5,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Integration Test User",
            Products = new List<CreateManufactureOrderProductRequest>()
        };

        var response = await _client.PostAsJsonAsync("/api/ManufactureOrder", request);


        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CreateManufactureOrderResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Id.Should().NotBe(0);
        content.OrderNumber.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateOrder_WithInvalidData_ShouldReturnBadRequest()
    {
        var request = new CreateManufactureOrderRequest
        {
            ProductCode = "", // Invalid: empty
            ProductName = "Test Product",
            OriginalBatchSize = 1000.0,
            NewBatchSize = 1500.0,
            ScaleFactor = 1.5,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Test User",
            Products = new List<CreateManufactureOrderProductRequest>()
        };

        var response = await _client.PostAsJsonAsync("/api/ManufactureOrder", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrder_WithExistingId_ShouldReturnOk()
    {
        // First create an order
        var createRequest = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI003",
            ProductName = "Test Semi Product 3",
            OriginalBatchSize = 1000.0,
            NewBatchSize = 1200.0,
            ScaleFactor = 1.2,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Get Test User",
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD003",
                    ProductName = "Final Product 3",
                    PlannedQuantity = 80.0
                }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/ManufactureOrder", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreateManufactureOrderResponse>(_jsonOptions);
        var orderId = createdOrder!.Id;

        // Then get the order
        var getResponse = await _client.GetAsync($"/api/ManufactureOrder/{orderId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await getResponse.Content.ReadFromJsonAsync<GetManufactureOrderResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Order.Should().NotBeNull();
        content.Order!.Id.Should().Be(orderId);
        content.Order.OrderNumber.Should().Be(createdOrder.OrderNumber);
        content.Order.ResponsiblePerson.Should().Be("Get Test User");
    }

    [Fact]
    public async Task GetOrder_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;

        var response = await _client.GetAsync($"/api/ManufactureOrder/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateOrder_WithValidData_ShouldReturnOk()
    {
        // First create an order
        var createRequest = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI004",
            ProductName = "Test Semi Product 4",
            OriginalBatchSize = 1000.0,
            NewBatchSize = 1000.0,
            ScaleFactor = 1.0,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Original User",
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD004",
                    ProductName = "Final Product 4",
                    PlannedQuantity = 50.0
                }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/ManufactureOrder", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreateManufactureOrderResponse>(_jsonOptions);
        var orderId = createdOrder!.Id;

        // Then update the order
        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            ResponsiblePerson = "Updated User",
            SemiProduct = new UpdateManufactureOrderSemiProductRequest
            {
                LotNumber = "LOT-2024-001",
                ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
            },
            Products = new List<UpdateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD004-UPDATED",
                    ProductName = "Updated Final Product 4",
                    PlannedQuantity = 75.0
                },
                new()
                {
                    ProductCode = "PROD005",
                    ProductName = "Additional Product 5",
                    PlannedQuantity = 25.0
                }
            },
            NewNote = "Updated order with new products"
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/ManufactureOrder/{orderId}", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await updateResponse.Content.ReadFromJsonAsync<UpdateManufactureOrderResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Order.Should().NotBeNull();
        content.Order!.Id.Should().Be(orderId);
        content.Order.ResponsiblePerson.Should().Be("Updated User");
    }

    [Fact]
    public async Task UpdateOrder_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;
        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = nonExistentId,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            ResponsiblePerson = "Test User",
            Products = new List<UpdateManufactureOrderProductRequest>()
        };

        var response = await _client.PutAsJsonAsync($"/api/ManufactureOrder/{nonExistentId}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateOrder_WithMismatchedId_ShouldReturnBadRequest()
    {
        var orderId = 123;
        var differentId = 456;
        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = differentId,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            ResponsiblePerson = "Test User",
            Products = new List<UpdateManufactureOrderProductRequest>()
        };

        var response = await _client.PutAsJsonAsync($"/api/ManufactureOrder/{orderId}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithValidTransition_ShouldReturnOk()
    {
        // First create an order
        var createRequest = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI005",
            ProductName = "Test Semi Product 5",
            OriginalBatchSize = 1000.0,
            NewBatchSize = 1000.0,
            ScaleFactor = 1.0,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Status Test User",
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD005",
                    ProductName = "Final Product 5",
                    PlannedQuantity = 60.0
                }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/ManufactureOrder", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreateManufactureOrderResponse>(_jsonOptions);
        var orderId = createdOrder!.Id;

        // Then update the status
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = ManufactureOrderState.Planned,
            ChangeReason = "Moving to next phase of production"
        };

        var statusResponse = await _client.PatchAsJsonAsync($"/api/ManufactureOrder/{orderId}/status", statusRequest);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await statusResponse.Content.ReadFromJsonAsync<UpdateManufactureOrderStatusResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.OldState.Should().Be("Draft");
        content.NewState.Should().Be("Planned");
        content.StateChangedAt.Should().NotBe(default(DateTime));
        content.StateChangedByUser.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateOrderStatus_WithInvalidTransition_ShouldReturnBadRequest()
    {
        // First create an order
        var createRequest = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI006",
            ProductName = "Test Semi Product 6",
            OriginalBatchSize = 1000.0,
            NewBatchSize = 1000.0,
            ScaleFactor = 1.0,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = "Invalid Status User",
            Products = new List<CreateManufactureOrderProductRequest>()
        };

        var createResponse = await _client.PostAsJsonAsync("/api/ManufactureOrder", createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<CreateManufactureOrderResponse>(_jsonOptions);
        var orderId = createdOrder!.Id;

        // Try to make an invalid transition (Draft -> Completed, skipping intermediate states)
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = ManufactureOrderState.Completed,
            ChangeReason = "Invalid direct completion"
        };

        var statusResponse = await _client.PatchAsJsonAsync($"/api/ManufactureOrder/{orderId}/status", statusRequest);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = 999999;
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = nonExistentId,
            NewState = ManufactureOrderState.Planned,
            ChangeReason = "Test reason"
        };

        var response = await _client.PatchAsJsonAsync($"/api/ManufactureOrder/{nonExistentId}/status", statusRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithMismatchedId_ShouldReturnBadRequest()
    {
        var orderId = 123;
        var differentId = 456;
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = differentId,
            NewState = ManufactureOrderState.Planned,
            ChangeReason = "Test reason"
        };

        var response = await _client.PatchAsJsonAsync($"/api/ManufactureOrder/{orderId}/status", statusRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class ManufactureOrderTestFactory : HebloWebApplicationFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // The ManufactureModule is already registered via ApplicationModule
        // Tests use the EF Core in-memory database from the base factory
        // This ensures data persists properly within each test
    }
}