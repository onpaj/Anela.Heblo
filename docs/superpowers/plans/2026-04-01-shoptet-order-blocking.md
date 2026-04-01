# Shoptet Order Blocking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose `PATCH /api/shoptet-orders/{code}/block` that validates source state, changes the Shoptet order status to a configured "blocked" ID, and writes an internal note.

**Architecture:** `IShoptetOrderClient` (Domain) ← `ShoptetOrderClient` (Adapter) wired via DI → `BlockOrderProcessingHandler` (Application) → `ShoptetOrdersController` (API). Settings (`ShoptetOrdersSettings`) bind from `"ShoptetOrders"` config section and control allowed source state IDs + target blocked status ID.

**Tech Stack:** .NET 8, MediatR, `IOptions<ShoptetOrdersSettings>`, typed HTTP client via `AddHttpClient<IShoptetOrderClient, ShoptetOrderClient>`, Moq + FluentAssertions for tests.

---

## File Map

| Action | File | Purpose |
|---|---|---|
| Create | `backend/src/Anela.Heblo.Domain/Features/ShoptetOrders/IShoptetOrderClient.cs` | Domain interface for adapter |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs` | Request model for PATCH /notes |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` | Add `SetInternalNoteAsync` + implement `IShoptetOrderClient` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj` | Add Domain project reference |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs` | Register typed client as `IShoptetOrderClient` |
| Modify | `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Add 21XX ShoptetOrders error codes |
| Create | `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` | Config class |
| Create | `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` | DI module registration |
| Create | `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingRequest.cs` | MediatR request |
| Create | `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingResponse.cs` | MediatR response |
| Create | `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs` | Handler with state validation logic |
| Modify | `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | Register `AddShoptetOrdersModule` |
| Modify | `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` | Add ShoptetApi adapter project reference |
| Modify | `backend/src/Anela.Heblo.API/Program.cs` | Call `AddShoptetApiAdapter` |
| Create | `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` | REST endpoint |
| Modify | `backend/src/Anela.Heblo.API/appsettings.json` | Add `ShoptetOrders` section with placeholder values |
| Modify | `backend/src/Anela.Heblo.API/appsettings.Development.json` | Add `ShoptetOrders` section with placeholder values |
| Create | `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` | Unit tests for handler |

---

## Task 1: Domain interface + adapter notes model + adapter wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/ShoptetOrders/IShoptetOrderClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`

- [ ] **Step 1.1: Create `IShoptetOrderClient` in Domain**

Create `backend/src/Anela.Heblo.Domain/Features/ShoptetOrders/IShoptetOrderClient.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ShoptetOrders;

public interface IShoptetOrderClient
{
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default);
}
```

- [ ] **Step 1.2: Create `UpdateNotesRequest` model**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class UpdateNotesRequest
{
    [JsonPropertyName("data")]
    public UpdateNotesData Data { get; set; } = new();
}

public class UpdateNotesData
{
    // Field name "internalNote" — verify against Shoptet OpenAPI spec at
    // https://api.docs.shoptet.com/shoptet-api/openapi before going to production.
    [JsonPropertyName("internalNote")]
    public string InternalNote { get; set; } = string.Empty;
}
```

- [ ] **Step 1.3: Add Domain reference to ShoptetApi adapter csproj**

In `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`, add inside the existing `<ItemGroup>` (or a new one):

```xml
  <ItemGroup>
    <ProjectReference Include="../../Anela.Heblo.Domain/Anela.Heblo.Domain.csproj" />
  </ItemGroup>
```

- [ ] **Step 1.4: Update `ShoptetOrderClient` to implement `IShoptetOrderClient` and add `SetInternalNoteAsync`**

In `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`, change the class declaration and add the new method. Full updated file:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Domain.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetOrderClient : IShoptetOrderClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetOrderClient(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public async Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)
    {
        var detail = await GetOrderDetailAsync(orderCode, ct);
        return detail.Status.Id;
    }

    /// <summary>
    /// List all orders whose externalCode starts with the given prefix.
    /// Because externalCode is not returned by the list endpoint, this method fetches
    /// the single-order detail for each candidate. Pass emailFilter to narrow candidates
    /// before issuing detail requests (the list endpoint does include email).
    /// Paginates automatically (API max is 50 items per page).
    /// </summary>
    public async Task<List<OrderSummary>> ListByExternalCodePrefixAsync(
        string prefix,
        string? emailFilter = null,
        CancellationToken ct = default)
    {
        var result = new List<OrderSummary>();
        var page = 1;

        while (true)
        {
            var response = await _http.GetAsync($"/api/orders?page={page}&itemsPerPage=50", ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            IEnumerable<OrderSummary> candidates = data.Data.Orders;

            if (emailFilter != null)
                candidates = candidates.Where(o => string.Equals(o.Email, emailFilter, StringComparison.OrdinalIgnoreCase));

            foreach (var summary in candidates)
            {
                var detail = await GetOrderDetailAsync(summary.Code, ct);
                if (detail.ExternalCode?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    result.Add(detail);
            }

            if (page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }

    /// <summary>
    /// Get a single order by its Shoptet order code. Returns the full order summary
    /// including externalCode, which is not available in the list endpoint.
    /// </summary>
    public async Task<OrderSummary> GetOrderDetailAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders/{code}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return data!.Data.Order;
    }

    /// <summary>
    /// Create a new order. Returns the created order code.
    /// </summary>
    public async Task<string> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        // Shoptet REST API requires the body wrapped in {"data": {...}}
        var envelope = new { data = request };
        var response = await _http.PostAsJsonAsync("/api/orders", envelope, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return result!.Data.Order.Code;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default)
    {
        var body = new UpdateStatusRequest
        {
            Data = new UpdateStatusData { StatusId = statusId },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/status", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/status returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    /// <inheritdoc />
    public async Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default)
    {
        var body = new UpdateNotesRequest
        {
            Data = new UpdateNotesData { InternalNote = note },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/notes", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/notes returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    /// <summary>
    /// Delete an order by its code.
    /// </summary>
    public async Task DeleteOrderAsync(string orderCode, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/orders/{orderCode}", ct);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 1.5: Update DI registration to use `IShoptetOrderClient`**

Replace the existing `AddHttpClient<ShoptetOrderClient>` call in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs`:

Old:
```csharp
services.AddHttpClient<ShoptetOrderClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
});
```

New:
```csharp
services.AddHttpClient<IShoptetOrderClient, ShoptetOrderClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
});
```

- [ ] **Step 1.6: Add ShoptetApi adapter reference to API project**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add inside the `<ItemGroup>` that lists other adapter `ProjectReference` entries:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.ShoptetApi\Anela.Heblo.Adapters.ShoptetApi.csproj" />
```

- [ ] **Step 1.7: Register ShoptetApi adapter in Program.cs**

In `backend/src/Anela.Heblo.API/Program.cs`, add after the existing `builder.Services.AddShoptetAdapter(builder.Configuration);` line:

```csharp
builder.Services.AddShoptetApiAdapter(builder.Configuration);
```

Also add the using at the top of the file if not already present:
```csharp
using Anela.Heblo.Adapters.ShoptetApi;
```

- [ ] **Step 1.8: Verify build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 1.9: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/ShoptetOrders/IShoptetOrderClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs \
        backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/Program.cs
git commit -m "feat(shoptet-orders): add IShoptetOrderClient interface, SetInternalNoteAsync, wire DI"
```

---

## Task 2: Error codes + Application settings + module

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 2.1: Add ShoptetOrders error codes**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, add after the KnowledgeBase block (after line with `KnowledgeBaseChunkNotFound = 2003,`):

```csharp
    // ShoptetOrders module errors (21XX)
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShoptetOrderInvalidSourceState = 2101,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShoptetOrderNotFound = 2102,
```

- [ ] **Step 2.2: Create `ShoptetOrdersSettings`**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class ShoptetOrdersSettings
{
    public const string ConfigurationKey = "ShoptetOrders";

    /// <summary>
    /// Shoptet order status IDs that are valid source states for the blocking operation.
    /// Orders in any other state will be rejected with ShoptetOrderInvalidSourceState.
    /// Configure actual values in user secrets / Azure App Config per environment.
    /// </summary>
    public int[] AllowedBlockSourceStateIds { get; set; } = [];

    /// <summary>
    /// Shoptet order status ID to assign when blocking an order.
    /// Configure actual value in user secrets / Azure App Config per environment.
    /// </summary>
    public int BlockedStatusId { get; set; }
}
```

- [ ] **Step 2.3: Create `ShoptetOrdersModule`**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ShoptetOrders;

public static class ShoptetOrdersModule
{
    public static IServiceCollection AddShoptetOrdersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ShoptetOrdersSettings>(
            configuration.GetSection(ShoptetOrdersSettings.ConfigurationKey));

        return services;
    }
}
```

- [ ] **Step 2.4: Register module in `ApplicationModule.cs`**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, add the using and the call.

Add using at top:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
```

Add registration call inside `AddApplicationServices`, after `services.AddExpeditionListArchiveModule();`:
```csharp
services.AddShoptetOrdersModule(configuration);
```

- [ ] **Step 2.5: Verify build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs \
        backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat(shoptet-orders): add error codes, settings, and module registration"
```

---

## Task 3: Write failing handler tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`

- [ ] **Step 3.1: Create test file with failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class BlockOrderProcessingHandlerTests
{
    private readonly Mock<IShoptetOrderClient> _clientMock;
    private readonly Mock<ILogger<BlockOrderProcessingHandler>> _loggerMock;
    private readonly ShoptetOrdersSettings _settings;
    private BlockOrderProcessingHandler CreateHandler() =>
        new BlockOrderProcessingHandler(
            _clientMock.Object,
            Options.Create(_settings),
            _loggerMock.Object);

    public BlockOrderProcessingHandlerTests()
    {
        _clientMock = new Mock<IShoptetOrderClient>();
        _loggerMock = new Mock<ILogger<BlockOrderProcessingHandler>>();
        _settings = new ShoptetOrdersSettings
        {
            AllowedBlockSourceStateIds = [26, -2],
            BlockedStatusId = 99,
        };
    }

    [Fact]
    public async Task Handle_OrderInAllowedState_ChangesStatusAndSetsNote()
    {
        // Arrange
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.UpdateStatusAsync("0001234", 99, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientMock.Setup(x => x.SetInternalNoteAsync("0001234", "Blocked for review", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "Blocked for review" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _clientMock.Verify(x => x.UpdateStatusAsync("0001234", 99, It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(x => x.SetInternalNoteAsync("0001234", "Blocked for review", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OrderInSecondAllowedState_Succeeds()
    {
        // -2 is also in AllowedBlockSourceStateIds
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0005678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        _clientMock.Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientMock.Setup(x => x.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0005678", Note = "note" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OrderInDisallowedState_ReturnsInvalidSourceStateError_WithoutCallingShoptet()
    {
        // Arrange
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70); // Not in AllowedBlockSourceStateIds

        // Act
        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "note" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderInvalidSourceState);
        result.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("0001234");
        result.Params.Should().ContainKey("currentStatusId").WhoseValue.Should().Be("70");
        _clientMock.Verify(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _clientMock.Verify(x => x.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShoptetApiThrowsOnStatusFetch_ReturnsInternalServerError()
    {
        // Arrange
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        // Act
        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "note" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError()
    {
        // Arrange
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.UpdateStatusAsync("0001234", 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Status update failed"));

        // Act
        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "note" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        _clientMock.Verify(x => x.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3.2: Run tests — expect compilation failure (handler not yet created)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BlockOrderProcessing" 2>&1 | tail -20
```

Expected: Build error — `BlockOrderProcessingHandler`, `BlockOrderProcessingRequest` not found.

---

## Task 4: Implement handler (make tests green)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`

- [ ] **Step 4.1: Create `BlockOrderProcessingRequest`**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingRequest : IRequest<BlockOrderProcessingResponse>
{
    public string OrderCode { get; set; } = null!;
    public string Note { get; set; } = null!;
}
```

- [ ] **Step 4.2: Create `BlockOrderProcessingResponse`**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingResponse : BaseResponse
{
    public BlockOrderProcessingResponse()
    {
    }

    public BlockOrderProcessingResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params)
    {
    }
}
```

- [ ] **Step 4.3: Create `BlockOrderProcessingHandler`**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ShoptetOrders;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingHandler : IRequestHandler<BlockOrderProcessingRequest, BlockOrderProcessingResponse>
{
    private readonly IShoptetOrderClient _shoptetOrderClient;
    private readonly IOptions<ShoptetOrdersSettings> _settings;
    private readonly ILogger<BlockOrderProcessingHandler> _logger;

    public BlockOrderProcessingHandler(
        IShoptetOrderClient shoptetOrderClient,
        IOptions<ShoptetOrdersSettings> settings,
        ILogger<BlockOrderProcessingHandler> logger)
    {
        _shoptetOrderClient = shoptetOrderClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<BlockOrderProcessingResponse> Handle(
        BlockOrderProcessingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentStatusId = await _shoptetOrderClient.GetOrderStatusIdAsync(
                request.OrderCode, cancellationToken);

            if (!_settings.Value.AllowedBlockSourceStateIds.Contains(currentStatusId))
            {
                return new BlockOrderProcessingResponse(
                    ErrorCodes.ShoptetOrderInvalidSourceState,
                    new Dictionary<string, string>
                    {
                        { "orderCode", request.OrderCode },
                        { "currentStatusId", currentStatusId.ToString() },
                    });
            }

            await _shoptetOrderClient.UpdateStatusAsync(
                request.OrderCode, _settings.Value.BlockedStatusId, cancellationToken);

            await _shoptetOrderClient.SetInternalNoteAsync(
                request.OrderCode, request.Note, cancellationToken);

            return new BlockOrderProcessingResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to block order {OrderCode}", request.OrderCode);
            return new BlockOrderProcessingResponse(ErrorCodes.InternalServerError);
        }
    }
}
```

- [ ] **Step 4.4: Run tests — expect all green**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BlockOrderProcessing" -v normal 2>&1 | tail -20
```

Expected:
```
Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

- [ ] **Step 4.5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/ \
        backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs
git commit -m "feat(shoptet-orders): implement BlockOrderProcessingHandler with state validation"
```

---

## Task 5: Controller + config + build

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Development.json`

- [ ] **Step 5.1: Create `ShoptetOrdersController`**

Create `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs`:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/shoptet-orders")]
public class ShoptetOrdersController : BaseApiController
{
    private readonly IMediator _mediator;

    public ShoptetOrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Block an order from processing: validates source state, changes status to the configured
    /// blocked state, and writes an internal note. Requires the order to be in one of the
    /// allowed source states configured in ShoptetOrders:AllowedBlockSourceStateIds.
    /// </summary>
    [HttpPatch("{code}/block")]
    public async Task<ActionResult<BlockOrderProcessingResponse>> BlockOrder(
        string code,
        [FromBody] BlockOrderRequest body)
    {
        var response = await _mediator.Send(new BlockOrderProcessingRequest
        {
            OrderCode = code,
            Note = body.Note,
        });

        if (!response.Success)
            return HandleResponse(response);

        return NoContent();
    }
}

public class BlockOrderRequest
{
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
```

- [ ] **Step 5.2: Add `ShoptetOrders` placeholder config to `appsettings.json`**

In `backend/src/Anela.Heblo.API/appsettings.json`, add the following JSON object at the top-level (alongside existing keys like `"Logging"`, `"AllowedHosts"`, etc.):

```json
"ShoptetOrders": {
  "AllowedBlockSourceStateIds": [],
  "BlockedStatusId": 0
}
```

(Real values go in user secrets per environment. These are safe placeholder defaults that disable the feature until configured.)

- [ ] **Step 5.3: Add same placeholder to `appsettings.Development.json`**

Add the same block to `backend/src/Anela.Heblo.API/appsettings.Development.json`:

```json
"ShoptetOrders": {
  "AllowedBlockSourceStateIds": [],
  "BlockedStatusId": 0
}
```

- [ ] **Step 5.4: Build and format**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

If `dotnet format` reports changes, run `dotnet format` without `--verify-no-changes` to apply them, then re-run with `--verify-no-changes` to confirm clean.

- [ ] **Step 5.5: Run full test suite**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
```

Expected: All existing tests still pass, no regressions.

- [ ] **Step 5.6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/src/Anela.Heblo.API/appsettings.Development.json
git commit -m "feat(shoptet-orders): add ShoptetOrdersController and config placeholders"
```

---

## Post-implementation checklist

- [ ] Verify `internalNote` is the correct Shoptet field name at https://api.docs.shoptet.com/shoptet-api/openapi (`PATCH /api/orders/{code}/notes`)
- [ ] Set real `AllowedBlockSourceStateIds` and `BlockedStatusId` values in user secrets for each environment
- [ ] Test the endpoint manually against the staging Shoptet store with a known order code
