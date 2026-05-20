# Smartsupp Contact Phase 2 — Shoptet Customer Cross-Link

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Given a Smartsupp conversation, resolve the linked Shoptet customer (via conversation variables or email), return their profile + recent orders, and render a `ShoptetCustomerCard` inside `ContactDetailsPanel`.

**Architecture:** New read-only use case `GetSmartsuppContactShoptetInfoQuery` in the Smartsupp feature calls two existing adapter clients — a new `IShoptetCustomerClient` (customer profile via Shoptet `/api/customers/{guid}`) and the existing `IEshopOrderClient` (extended with email-based order lookup). No schema changes to Smartsupp entities. Modules stay decoupled: handler takes both clients as injected abstractions.

**Tech Stack:** .NET 8, MediatR, xUnit, FluentAssertions, Moq; React 18, TanStack Query, Tailwind CSS, Vitest/Testing Library.

---

## Pre-work spike results (already investigated — answers embedded in tasks)

- No Shoptet customer entity in local DB; customer data lives in the Shoptet REST API.
- `shoptet_user_guid` and `shoptet_guid` are in `SmartsuppConversation.VariablesJson` (already exposed via `ConversationDto.Variables` after Phase 1).
- `OrderSummary` model already exists in the adapter but is missing `CustomerGuid`, `Price` (total), and `AdminUrl` — all returned by the list endpoint.
- `IEshopOrderClient` needs two new methods: `GetRecentOrdersByEmailAsync` and `GetOrderStatusNamesAsync`.
- `ShoptetApiAdapterServiceCollectionExtensions` is where all Shoptet HTTP clients are registered.
- Frontend types come from NSwag-generated `api-client.ts`; run `npm run generate-client` after adding the endpoint.

---

## File Map

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs` — eshop statuses API model
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs` — richer order info DTO
- `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/IShoptetCustomerClient.cs` — customer client interface
- `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/ShoptetCustomerInfoDto.cs` — customer profile DTO
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/Model/ShoptetCustomerResponse.cs` — Shoptet API customer response model
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/ShoptetCustomerClient.cs` — implementation
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs` — response DTOs
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs`
- `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`
- `frontend/src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx`

**Modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs` — add `CustomerGuid`, `Price`, `AdminUrl` to `OrderSummary`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — add two new methods
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — implement new methods
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — register new customer client
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `SmartsuppShoptetCustomerNotFound = 2704`
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` — add GET endpoint
- `frontend/src/api/hooks/useSmartsupp.ts` — add types + hook
- `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx` — render `ShoptetCustomerCard`
- `docs/features/smartsupp.md` — document new endpoint
- `docs/integrations/shoptet-api.md` — document Shoptet customer API findings

---

## Task 1: Extend OrderSummary model + add EshopOrderInfo DTO

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs`

- [ ] **Step 1.1: Add missing fields to OrderSummary**

In `OrderListResponse.cs`, add these three properties to the `OrderSummary` class after the existing `PaymentMethod` property:

```csharp
[JsonPropertyName("customerGuid")]
public string? CustomerGuid { get; set; }

[JsonPropertyName("adminUrl")]
public string? AdminUrl { get; set; }

[JsonPropertyName("price")]
public OrderPriceSummary? Price { get; set; }
```

And add this new class at the bottom of the file (after `OrderNotes`):

```csharp
public class OrderPriceSummary
{
    [JsonPropertyName("withVat")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? WithVat { get; set; }

    [JsonPropertyName("withoutVat")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? WithoutVat { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }
}
```

Add `using System.Text.Json.Serialization;` at the top if not already present.

- [ ] **Step 1.2: Create ShoptetEshopResponse model**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class ShoptetEshopResponse
{
    [JsonPropertyName("data")]
    public ShoptetEshopData? Data { get; set; }
}

public class ShoptetEshopData
{
    [JsonPropertyName("eshop")]
    public ShoptetEshopDetail? Eshop { get; set; }
}

public class ShoptetEshopDetail
{
    [JsonPropertyName("orderStatuses")]
    public List<ShoptetOrderStatus> OrderStatuses { get; set; } = new();
}

public class ShoptetOrderStatus
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
```

- [ ] **Step 1.3: Create EshopOrderInfo application DTO**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class EshopOrderInfo
{
    public string Code { get; set; } = null!;
    public string? CustomerGuid { get; set; }
    public decimal? TotalWithVat { get; set; }
    public string? CurrencyCode { get; set; }
    public int StatusId { get; set; }
    public string? AdminUrl { get; set; }
    public DateTime? OrderDate { get; set; }
}
```

- [ ] **Step 1.4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs \
        backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs
git commit -m "feat(shoptet): add CustomerGuid/Price/AdminUrl to OrderSummary, add EshopOrderInfo + eshop status model"
```

---

## Task 2: Extend IEshopOrderClient and implement new methods in ShoptetOrderClient

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

- [ ] **Step 2.1: Add two methods to IEshopOrderClient**

In `IEshopOrderClient.cs`, add after the existing `GetRecentOrdersAsync` signature:

```csharp
/// <summary>
/// Gets the first <paramref name="count"/> orders for the given email from the most recent page.
/// Filters in-memory (Shoptet API does not support email filter on list endpoint).
/// </summary>
Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default);

/// <summary>
/// Returns a map of status id → status name from GET /api/eshop?include=orderStatuses.
/// </summary>
Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default);
```

- [ ] **Step 2.2: Implement GetRecentOrdersByEmailAsync in ShoptetOrderClient**

In `ShoptetOrderClient.cs`, add this method (and the private helper `MapToOrderInfo`) before the closing brace:

```csharp
public async Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default)
{
    var response = await _http.GetAsync("/api/orders?page=1&itemsPerPage=50", ct);
    response.EnsureSuccessStatusCode();

    var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
    return (data?.Data.Orders ?? [])
        .Where(o => string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase))
        .Take(count)
        .Select(MapToOrderInfo)
        .ToList();
}

public async Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default)
{
    var response = await _http.GetAsync("/api/eshop?include=orderStatuses", ct);
    response.EnsureSuccessStatusCode();

    var data = await response.Content.ReadFromJsonAsync<ShoptetEshopResponse>(JsonOptions, ct);
    return (data?.Data?.Eshop?.OrderStatuses ?? [])
        .Where(s => !string.IsNullOrWhiteSpace(s.Name))
        .ToDictionary(s => s.Id, s => s.Name!);
}

private static EshopOrderInfo MapToOrderInfo(OrderSummary o) => new()
{
    Code = o.Code,
    CustomerGuid = o.CustomerGuid,
    TotalWithVat = o.Price?.WithVat,
    CurrencyCode = o.Price?.CurrencyCode,
    StatusId = o.Status.Id,
    AdminUrl = o.AdminUrl,
    OrderDate = o.CreationTime is { } t && DateTime.TryParse(t, out var dt) ? dt : null,
};
```

Add `using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;` at the top of the file if not already there.

- [ ] **Step 2.3: Verify the project builds**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi
```

Expected: Build succeeded. 0 error(s).

- [ ] **Step 2.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
git commit -m "feat(shoptet): add GetRecentOrdersByEmailAsync + GetOrderStatusNamesAsync to order client"
```

---

## Task 3: Create IShoptetCustomerClient interface and DTO

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/IShoptetCustomerClient.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/ShoptetCustomerInfoDto.cs`

- [ ] **Step 3.1: Create the DTO**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/ShoptetCustomerInfoDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetCustomers;

public class ShoptetCustomerInfoDto
{
    public string Guid { get; set; } = null!;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CustomerGroup { get; set; }
    public string? PriceList { get; set; }
    public string? DefaultShippingAddress { get; set; }
}
```

- [ ] **Step 3.2: Create the interface**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/IShoptetCustomerClient.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetCustomers;

public interface IShoptetCustomerClient
{
    /// <summary>
    /// Fetches the Shoptet customer by their GUID (shoptet_guid or shoptet_user_guid from conversation variables).
    /// Returns null if the customer does not exist or the API returns 404.
    /// </summary>
    Task<ShoptetCustomerInfoDto?> GetCustomerByGuidAsync(string guid, CancellationToken ct = default);
}
```

- [ ] **Step 3.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/
git commit -m "feat(shoptet-customers): add IShoptetCustomerClient interface and ShoptetCustomerInfoDto"
```

---

## Task 4: Implement ShoptetCustomerClient in adapter

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/Model/ShoptetCustomerResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/ShoptetCustomerClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 4.1: Verify the Shoptet customer API (MANDATORY before writing the model)**

Call the live API to discover the actual response shape. Use the token from your local `secrets.json`:

```bash
curl -s -H "Shoptet-Private-API-Token: <your-token>" \
  "https://api.myshoptet.com/api/customers/<a-known-guid>" | jq .
```

Substitute a real `customerGuid` from any recent Shoptet order (grab one from `GET /api/orders?page=1&itemsPerPage=1`).

**Document findings in `docs/integrations/shoptet-api.md`** under a new "4. Customers API" section before proceeding. Note: field names, nested object shapes, and whether `customerGroup`, `priceList`, `billingAddress` are present in the response.

- [ ] **Step 4.2: Create the API response model**

After verifying the actual response, create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/Model/ShoptetCustomerResponse.cs`. The model below is a reasonable starting point — **adjust field names to match what the live API actually returns**:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Customers.Model;

public class ShoptetCustomerResponse
{
    [JsonPropertyName("data")]
    public ShoptetCustomerData? Data { get; set; }
}

public class ShoptetCustomerData
{
    [JsonPropertyName("customer")]
    public ShoptetCustomerDetail? Customer { get; set; }
}

public class ShoptetCustomerDetail
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = null!;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("customerGroup")]
    public ShoptetNamedItem? CustomerGroup { get; set; }

    [JsonPropertyName("priceList")]
    public ShoptetNamedItem? PriceList { get; set; }

    [JsonPropertyName("billingAddress")]
    public ShoptetCustomerAddress? BillingAddress { get; set; }
}

public class ShoptetNamedItem
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class ShoptetCustomerAddress
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
}
```

- [ ] **Step 4.3: Implement ShoptetCustomerClient**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/ShoptetCustomerClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Customers.Model;
using Anela.Heblo.Application.Features.ShoptetCustomers;

namespace Anela.Heblo.Adapters.ShoptetApi.Customers;

public class ShoptetCustomerClient : IShoptetCustomerClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetCustomerClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ShoptetCustomerInfoDto?> GetCustomerByGuidAsync(string guid, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/customers/{guid}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ShoptetCustomerResponse>(JsonOptions, ct);
        var detail = data?.Data?.Customer;

        if (detail is null)
            return null;

        return new ShoptetCustomerInfoDto
        {
            Guid = detail.Guid,
            FullName = detail.FullName,
            Email = detail.Email,
            CustomerGroup = detail.CustomerGroup?.Name,
            PriceList = detail.PriceList?.Name,
            DefaultShippingAddress = FormatAddress(detail.BillingAddress),
        };
    }

    private static string? FormatAddress(ShoptetCustomerAddress? addr)
    {
        if (addr is null) return null;

        var parts = new[] { addr.CountryCode, addr.City, addr.Zip, addr.Street }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
```

- [ ] **Step 4.4: Register the new client in DI**

In `ShoptetApiAdapterServiceCollectionExtensions.cs`, add after the existing `IShipmentClient` registration block:

```csharp
services.AddHttpClient<IShoptetCustomerClient, ShoptetCustomerClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
});
```

Add at the top of the file:
```csharp
using Anela.Heblo.Adapters.ShoptetApi.Customers;
using Anela.Heblo.Application.Features.ShoptetCustomers;
```

- [ ] **Step 4.5: Verify build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi
```

Expected: Build succeeded. 0 error(s).

- [ ] **Step 4.6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/ \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs \
        docs/integrations/shoptet-api.md
git commit -m "feat(shoptet-customers): implement ShoptetCustomerClient adapter + DI registration"
```

---

## Task 5: Add error code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 5.1: Add error code**

In `ErrorCodes.cs`, after `SmartsuppConversationEmpty = 2703`, add:

```csharp
[HttpStatusCode(HttpStatusCode.NotFound)]
SmartsuppShoptetCustomerNotFound = 2704,
```

- [ ] **Step 5.2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(smartsupp): add SmartsuppShoptetCustomerNotFound error code"
```

---

## Task 6: Create GetSmartsuppContactShoptetInfo use case (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoResponse.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoHandler.cs`

- [ ] **Step 6.1: Create response DTOs**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class ShoptetContactInfoDto
{
    public ShoptetCustomerSnapshotDto Customer { get; set; } = null!;
    public List<ShoptetOrderSnapshotDto> RecentOrders { get; set; } = new();
    public DateTime? CartUpdatedAt { get; set; }
}

public class ShoptetCustomerSnapshotDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CustomerGroup { get; set; }
    public string? PriceList { get; set; }
    public string? DefaultShippingAddress { get; set; }
}

public class ShoptetOrderSnapshotDto
{
    public string Code { get; set; } = null!;
    public string? StatusName { get; set; }
    public decimal? TotalWithVat { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime? OrderDate { get; set; }
    public string? AdminUrl { get; set; }
}
```

- [ ] **Step 6.2: Create request and response classes**

Create `GetSmartsuppContactShoptetInfoRequest.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoRequest : IRequest<GetSmartsuppContactShoptetInfoResponse>
{
    public string ConversationId { get; set; } = null!;
}
```

Create `GetSmartsuppContactShoptetInfoResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoResponse : BaseResponse
{
    public ShoptetContactInfoDto? ContactInfo { get; set; }

    public GetSmartsuppContactShoptetInfoResponse() { }
    public GetSmartsuppContactShoptetInfoResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 6.3: Write failing tests (RED)**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetSmartsuppContactShoptetInfoHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<IShoptetCustomerClient> _customerClient = new();
    private readonly Mock<IEshopOrderClient> _orderClient = new();

    private GetSmartsuppContactShoptetInfoHandler CreateHandler() =>
        new(_repo.Object, _customerClient.Object, _orderClient.Object);

    private static ShoptetCustomerInfoDto MakeCustomer(string guid = "cust-1") =>
        new() { Guid = guid, FullName = "Jana Nováková", Email = "jana@test.cz", CustomerGroup = "VIP", PriceList = "Retail" };

    private static List<EshopOrderInfo> MakeOrders() =>
    [
        new() { Code = "2024001", CustomerGuid = "cust-1", TotalWithVat = 1250m, CurrencyCode = "CZK", StatusId = 26, AdminUrl = "https://anela.myshoptet.com/admin/orders/2024001", OrderDate = new DateTime(2026, 4, 1) },
    ];

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenConversationMissing()
    {
        _repo.Setup(r => r.GetConversationAsync("missing", default)).ReturnsAsync((SmartsuppConversation?)null);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "missing" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_ResolvesViaUserGuid_WhenShoptetUserGuidPresent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_user_guid":"user-guid-1","shoptet_guid":"guid-2"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("user-guid-1", default)).ReturnsAsync(MakeCustomer("user-guid-1"));
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-2", default)).ReturnsAsync(MakeCustomer("guid-2"));
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, default)).ReturnsAsync(MakeOrders());
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(default)).ReturnsAsync(new Dictionary<int, string> { { 26, "Balí se" } });

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        // Must use user-guid-1, not guid-2
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("user-guid-1", default), Times.Once);
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-2", default), Times.Never);
    }

    [Fact]
    public async Task Handle_ResolvesViaShoptetGuid_WhenUserGuidAbsent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"guid-abc"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-abc", default)).ReturnsAsync(MakeCustomer("guid-abc"));
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, default)).ReturnsAsync(MakeOrders());
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(default)).ReturnsAsync(new Dictionary<int, string>());

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-abc", default), Times.Once);
    }

    [Fact]
    public async Task Handle_ResolvesViaEmail_WhenNoGuidsPresent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            ContactEmail = "jana@test.cz",
            VariablesJson = null,
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, default)).ReturnsAsync(MakeOrders());
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("cust-1", default)).ReturnsAsync(MakeCustomer("cust-1"));
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(default)).ReturnsAsync(new Dictionary<int, string> { { 26, "Balí se" } });

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.Customer.FullName.Should().Be("Jana Nováková");
        result.ContactInfo.RecentOrders.Should().HaveCount(1);
        result.ContactInfo.RecentOrders[0].StatusName.Should().Be("Balí se");
        result.ContactInfo.RecentOrders[0].TotalWithVat.Should().Be(1250m);
    }

    [Fact]
    public async Task Handle_ReturnsCustomerNotFound_WhenNoGuidAndNoMatchingOrders()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            ContactEmail = "unknown@test.cz",
            VariablesJson = null,
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("unknown@test.cz", 5, default)).ReturnsAsync([]);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppShoptetCustomerNotFound);
    }

    [Fact]
    public async Task Handle_ParsesCartUpdatedAt_FromVariables()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"guid-1","shoptet_cart_updated_at":"2026-04-15T12:00:00"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-1", default)).ReturnsAsync(MakeCustomer("guid-1"));
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, default)).ReturnsAsync([]);
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(default)).ReturnsAsync(new Dictionary<int, string>());

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.CartUpdatedAt.Should().Be(new DateTime(2026, 4, 15, 12, 0, 0));
    }
}
```

- [ ] **Step 6.4: Run tests to verify they fail (RED)**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "GetSmartsuppContactShoptetInfoHandlerTests" --no-build 2>&1 | tail -20
```

Expected: Multiple failures — `GetSmartsuppContactShoptetInfoHandler` does not exist yet.

- [ ] **Step 6.5: Implement the handler (GREEN)**

Create `GetSmartsuppContactShoptetInfoHandler.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoHandler
    : IRequestHandler<GetSmartsuppContactShoptetInfoRequest, GetSmartsuppContactShoptetInfoResponse>
{
    private readonly ISmartsuppRepository _repo;
    private readonly IShoptetCustomerClient _customerClient;
    private readonly IEshopOrderClient _orderClient;

    public GetSmartsuppContactShoptetInfoHandler(
        ISmartsuppRepository repo,
        IShoptetCustomerClient customerClient,
        IEshopOrderClient orderClient)
    {
        _repo = repo;
        _customerClient = customerClient;
        _orderClient = orderClient;
    }

    public async Task<GetSmartsuppContactShoptetInfoResponse> Handle(
        GetSmartsuppContactShoptetInfoRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repo.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GetSmartsuppContactShoptetInfoResponse(ErrorCodes.SmartsuppConversationNotFound);

        var variables = ParseVariables(conversation.VariablesJson);
        variables.TryGetValue("shoptet_user_guid", out var userGuid);
        variables.TryGetValue("shoptet_guid", out var shoptetGuid);
        variables.TryGetValue("shoptet_cart_updated_at", out var cartStr);

        // Resolution order: shoptet_user_guid → shoptet_guid → email (first match wins)
        ShoptetCustomerInfoDto? customer = null;
        List<EshopOrderInfo>? preloadedOrders = null;

        if (!string.IsNullOrWhiteSpace(userGuid))
        {
            customer = await _customerClient.GetCustomerByGuidAsync(userGuid, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(shoptetGuid))
        {
            customer = await _customerClient.GetCustomerByGuidAsync(shoptetGuid, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(conversation.ContactEmail))
        {
            preloadedOrders = await _orderClient.GetRecentOrdersByEmailAsync(
                conversation.ContactEmail, 5, cancellationToken);
            var firstGuid = preloadedOrders.FirstOrDefault()?.CustomerGuid;
            if (!string.IsNullOrWhiteSpace(firstGuid))
                customer = await _customerClient.GetCustomerByGuidAsync(firstGuid, cancellationToken);
        }

        if (customer is null)
            return new GetSmartsuppContactShoptetInfoResponse(ErrorCodes.SmartsuppShoptetCustomerNotFound);

        var lookupEmail = customer.Email ?? conversation.ContactEmail ?? string.Empty;
        var orders = preloadedOrders ?? await _orderClient.GetRecentOrdersByEmailAsync(lookupEmail, 5, cancellationToken);
        var statusNames = await _orderClient.GetOrderStatusNamesAsync(cancellationToken);

        DateTime? cartUpdatedAt = null;
        if (!string.IsNullOrWhiteSpace(cartStr) && DateTime.TryParse(cartStr, out var parsedCart))
            cartUpdatedAt = parsedCart;

        return new GetSmartsuppContactShoptetInfoResponse
        {
            ContactInfo = new ShoptetContactInfoDto
            {
                Customer = new ShoptetCustomerSnapshotDto
                {
                    FullName = customer.FullName,
                    Email = customer.Email,
                    CustomerGroup = customer.CustomerGroup,
                    PriceList = customer.PriceList,
                    DefaultShippingAddress = customer.DefaultShippingAddress,
                },
                RecentOrders = orders.Select(o => new ShoptetOrderSnapshotDto
                {
                    Code = o.Code,
                    StatusName = statusNames.TryGetValue(o.StatusId, out var name) ? name : o.StatusId.ToString(),
                    TotalWithVat = o.TotalWithVat,
                    CurrencyCode = o.CurrencyCode,
                    OrderDate = o.OrderDate,
                    AdminUrl = o.AdminUrl,
                }).ToList(),
                CartUpdatedAt = cartUpdatedAt,
            },
        };
    }

    private static Dictionary<string, string> ParseVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
```

- [ ] **Step 6.6: Run tests (GREEN)**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "GetSmartsuppContactShoptetInfoHandlerTests"
```

Expected: All 6 tests pass.

- [ ] **Step 6.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/ \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs
git commit -m "feat(smartsupp): add GetSmartsuppContactShoptetInfoQuery handler with guid/email resolution"
```

---

## Task 7: Add controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`

- [ ] **Step 7.1: Add the endpoint**

In `SmartsuppController.cs`, add this new action after `GenerateDraftReply` (before the closing brace of the class). Also add the using:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;
```

Action:

```csharp
[HttpGet("conversations/{id}/shoptet-info")]
[ProducesResponseType(typeof(GetSmartsuppContactShoptetInfoResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<GetSmartsuppContactShoptetInfoResponse>> GetShoptetInfo(
    string id,
    CancellationToken cancellationToken = default)
{
    var result = await _mediator.Send(
        new GetSmartsuppContactShoptetInfoRequest { ConversationId = id },
        cancellationToken);
    return HandleResponse(result);
}
```

- [ ] **Step 7.2: Build and format**

```bash
dotnet build backend/src/Anela.Heblo.API
dotnet format backend/src/Anela.Heblo.API
```

Expected: Build succeeded. 0 error(s).

- [ ] **Step 7.3: Run full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests
```

Expected: All tests pass.

- [ ] **Step 7.4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs
git commit -m "feat(smartsupp): expose GET /api/smartsupp/conversations/{id}/shoptet-info"
```

---

## Task 8: Regenerate TypeScript client

- [ ] **Step 8.1: Regenerate**

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Expected: Build succeeded. New types visible in `frontend/src/api/generated/api-client.ts` — search for `ShoptetContactInfoDto` or `shoptetInfo`.

- [ ] **Step 8.2: Commit generated file**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate TypeScript API client with shoptet-info endpoint"
```

---

## Task 9: Add React Query hook and TypeScript types

**Files:**
- Modify: `frontend/src/api/hooks/useSmartsupp.ts`

- [ ] **Step 9.1: Add types and hook**

Add at the end of `useSmartsupp.ts`:

```typescript
export interface ShoptetCustomerSnapshotDto {
  fullName?: string | null;
  email?: string | null;
  customerGroup?: string | null;
  priceList?: string | null;
  defaultShippingAddress?: string | null;
}

export interface ShoptetOrderSnapshotDto {
  code: string;
  statusName?: string | null;
  totalWithVat?: number | null;
  currencyCode?: string | null;
  orderDate?: string | null;
  adminUrl?: string | null;
}

export interface ShoptetContactInfoDto {
  customer: ShoptetCustomerSnapshotDto;
  recentOrders: ShoptetOrderSnapshotDto[];
  cartUpdatedAt?: string | null;
}

export interface GetSmartsuppShoptetInfoResponse {
  success: boolean;
  contactInfo?: ShoptetContactInfoDto | null;
}
```

Add to `SMARTSUPP_QUERY_KEYS`:

```typescript
shoptetInfo: (id: string) => ["smartsupp", "shoptet-info", id] as const,
```

Add the hook:

```typescript
export function useSmartsuppShoptetInfo(conversationId: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.shoptetInfo(conversationId ?? ""),
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await (apiClient as any).http.fetch(
        `${baseUrl}/api/smartsupp/conversations/${conversationId}/shoptet-info`,
        { method: "GET" }
      );
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`Shoptet info error: ${response.status}`);
      return response.json() as Promise<GetSmartsuppShoptetInfoResponse>;
    },
    enabled: !!conversationId,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
}
```

- [ ] **Step 9.2: Commit**

```bash
git add frontend/src/api/hooks/useSmartsupp.ts
git commit -m "feat(smartsupp): add useSmartsuppShoptetInfo hook + response types"
```

---

## Task 10: Create ShoptetCustomerCard component (TDD)

**Files:**
- Test: `frontend/src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx`
- Create: `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`

- [ ] **Step 10.1: Write failing tests (RED)**

Create `__tests__/ShoptetCustomerCard.test.tsx`:

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ShoptetCustomerCard from "../ShoptetCustomerCard";
import * as hooks from "../../../../api/hooks/useSmartsupp";

jest.mock("../../../../api/hooks/useSmartsupp", () => ({
  ...jest.requireActual("../../../../api/hooks/useSmartsupp"),
  useSmartsuppShoptetInfo: jest.fn(),
}));

const mockedHook = hooks.useSmartsuppShoptetInfo as jest.Mock;

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const fullResponse: hooks.GetSmartsuppShoptetInfoResponse = {
  success: true,
  contactInfo: {
    customer: {
      fullName: "Jana Nováková",
      email: "jana@test.cz",
      customerGroup: "VIP",
      priceList: "Retail",
      defaultShippingAddress: "CZ, Praha, 12000, Ulice 5",
    },
    recentOrders: [
      {
        code: "2024001",
        statusName: "Balí se",
        totalWithVat: 1250,
        currencyCode: "CZK",
        orderDate: "2026-04-01T00:00:00",
        adminUrl: "https://anela.myshoptet.com/admin/orders/2024001",
      },
    ],
    cartUpdatedAt: null,
  },
};

describe("ShoptetCustomerCard", () => {
  it("renders nothing when hook returns null (404)", () => {
    mockedHook.mockReturnValue({ data: null, isLoading: false });
    const { container } = render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(container.firstChild).toBeNull();
  });

  it("renders nothing while loading", () => {
    mockedHook.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(container.firstChild).toBeNull();
  });

  it("renders customer name and group", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.getByText("Jana Nováková")).toBeInTheDocument();
    expect(screen.getByText("VIP")).toBeInTheDocument();
    expect(screen.getByText("Retail")).toBeInTheDocument();
  });

  it("renders recent order with code, status, and total", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.getByText("2024001")).toBeInTheDocument();
    expect(screen.getByText("Balí se")).toBeInTheDocument();
    expect(screen.getByText(/1 250/)).toBeInTheDocument();
  });

  it("renders admin link for each order", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    const link = screen.getByRole("link", { name: /zobrazit v shoptet/i });
    expect(link).toHaveAttribute("href", "https://anela.myshoptet.com/admin/orders/2024001");
  });

  it("renders shipping address when present", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.getByText("CZ, Praha, 12000, Ulice 5")).toBeInTheDocument();
  });
});
```

- [ ] **Step 10.2: Run tests to verify they fail (RED)**

```bash
cd frontend && npx vitest run src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx 2>&1 | tail -20
```

Expected: Tests fail — component does not exist.

- [ ] **Step 10.3: Implement ShoptetCustomerCard (GREEN)**

Create `ShoptetCustomerCard.tsx`:

```tsx
import React from "react";
import { useSmartsuppShoptetInfo } from "../../../api/hooks/useSmartsupp";

interface ShoptetCustomerCardProps {
  conversationId: string | null;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="px-4 py-3 border-b border-gray-100">
      <div className="text-[11px] uppercase tracking-wide text-gray-400 font-medium mb-1.5">{title}</div>
      {children}
    </div>
  );
}

function ShoptetCustomerCard({ conversationId }: ShoptetCustomerCardProps) {
  const { data, isLoading } = useSmartsuppShoptetInfo(conversationId);

  if (isLoading || !data?.contactInfo) return null;

  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  return (
    <>
      <Section title="Shoptet Zákazník">
        <div className="space-y-1">
          {customer.fullName && (
            <div className="text-sm font-semibold text-gray-900">{customer.fullName}</div>
          )}
          {customer.email && (
            <div className="text-xs text-gray-500">{customer.email}</div>
          )}
          {customer.customerGroup && (
            <div className="text-xs text-gray-700">
              <span className="text-gray-400">Skupina: </span>{customer.customerGroup}
            </div>
          )}
          {customer.priceList && (
            <div className="text-xs text-gray-700">
              <span className="text-gray-400">Ceník: </span>{customer.priceList}
            </div>
          )}
          {customer.defaultShippingAddress && (
            <div className="text-xs text-gray-600 mt-1">{customer.defaultShippingAddress}</div>
          )}
        </div>
      </Section>

      {cartUpdatedAt && (
        <Section title="Shoptet Košík">
          <div className="text-xs text-gray-500">
            Aktualizován: {new Date(cartUpdatedAt).toLocaleDateString("cs-CZ")}
          </div>
        </Section>
      )}

      {recentOrders.length > 0 && (
        <Section title="Poslední objednávky">
          <div className="space-y-2">
            {recentOrders.map((order) => (
              <div key={order.code} className="border-b border-gray-50 pb-1.5 last:border-0">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-gray-800">{order.code}</span>
                  {order.totalWithVat != null && (
                    <span className="text-xs text-gray-700">
                      {order.totalWithVat.toLocaleString("cs-CZ")} {order.currencyCode ?? "Kč"}
                    </span>
                  )}
                </div>
                <div className="flex items-center justify-between mt-0.5">
                  {order.statusName && (
                    <span className="text-[11px] text-gray-500">{order.statusName}</span>
                  )}
                  {order.orderDate && (
                    <span className="text-[11px] text-gray-400">
                      {new Date(order.orderDate).toLocaleDateString("cs-CZ")}
                    </span>
                  )}
                </div>
                {order.adminUrl && (
                  <a
                    href={order.adminUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-[11px] text-blue-600 hover:underline"
                  >
                    Zobrazit v Shoptet
                  </a>
                )}
              </div>
            ))}
          </div>
        </Section>
      )}
    </>
  );
}

export default ShoptetCustomerCard;
```

- [ ] **Step 10.4: Run tests (GREEN)**

```bash
cd frontend && npx vitest run src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx 2>&1 | tail -20
```

Expected: All 5 tests pass.

- [ ] **Step 10.5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx
git commit -m "feat(smartsupp): add ShoptetCustomerCard component"
```

---

## Task 11: Wire ShoptetCustomerCard into ContactDetailsPanel

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx`

- [ ] **Step 11.1: Import and render the card**

In `ContactDetailsPanel.tsx`, add the import at the top:

```tsx
import ShoptetCustomerCard from "./ShoptetCustomerCard";
```

Then add `<ShoptetCustomerCard conversationId={conversation.id} />` inside the `<aside>` element, just before the `{/* Informace o kontaktu */}` section:

```tsx
      {/* Shoptet Zákazník — rendered when resolved */}
      <ShoptetCustomerCard conversationId={conversation.id} />

      {/* Informace o kontaktu — merged variables + contactProperties, Shoptet keys first */}
```

- [ ] **Step 11.2: Run existing frontend tests**

```bash
cd frontend && npx vitest run src/components/customer-support/smartsupp/__tests__/ContactDetailsPanel.test.tsx 2>&1 | tail -20
```

Expected: All existing tests pass. The ContactDetailsPanel tests don't mock `useSmartsuppShoptetInfo` — add a jest mock at the top of that test file if they fail due to the new hook:

```tsx
jest.mock("../../../../api/hooks/useSmartsupp", () => ({
  ...jest.requireActual("../../../../api/hooks/useSmartsupp"),
  useSmartsuppShoptetInfo: () => ({ data: null, isLoading: false }),
}));
```

- [ ] **Step 11.3: Build frontend**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: Build succeeded with no errors.

- [ ] **Step 11.4: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx
git commit -m "feat(smartsupp): render ShoptetCustomerCard inside ContactDetailsPanel"
```

---

## Task 12: Update documentation

**Files:**
- Modify: `docs/features/smartsupp.md`
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 12.1: Document new endpoint in smartsupp.md**

In `docs/features/smartsupp.md`, under the "API endpointy" section, add a new row to the authenticated endpoints table:

```markdown
| `GET` | `/api/smartsupp/conversations/{id}/shoptet-info` | Vrátí profil Shoptet zákazníka a poslední objednávky pro danou konverzaci. Vrátí 404 pokud nelze zákazníka identifikovat. |
```

- [ ] **Step 12.2: Document Shoptet customer API findings**

In `docs/integrations/shoptet-api.md`, add a new section "4. Customers API" with the findings from Task 4 Step 4.1. Include: endpoint URL, response shape, fields used, and any quirks discovered during integration.

- [ ] **Step 12.3: Commit**

```bash
git add docs/features/smartsupp.md docs/integrations/shoptet-api.md
git commit -m "docs: document shoptet-info endpoint and Shoptet customer API findings"
```

---

## Task 13: Final verification

- [ ] **Step 13.1: Full backend build + format + test**

```bash
dotnet build backend/src/Anela.Heblo.API
dotnet format backend
dotnet test backend/test/Anela.Heblo.Tests
```

Expected: 0 errors, all tests pass.

- [ ] **Step 13.2: Full frontend build + lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: 0 errors, 0 lint warnings.

- [ ] **Step 13.3: Manual smoke test on staging**

1. Open a conversation whose contact is known to have Shoptet orders.
2. Verify `ShoptetCustomerCard` renders with correct name, group, and orders.
3. Click "Zobrazit v Shoptet" — verify it opens the correct Shoptet admin order page.
4. Open a conversation with no Shoptet link — verify the card is absent.

---

## Self-review checklist

- **Spec coverage:** ✓ Kontakt, Poznámka, resolution order (user_guid → guid → email), customer profile fields, recent orders with code/status/total/date/link, cart section, hide when null.
- **No placeholders:** All code is complete.
- **Type consistency:** `ShoptetContactInfoDto` / `ShoptetCustomerSnapshotDto` / `ShoptetOrderSnapshotDto` used consistently from handler → response → frontend types.
- **DTOs are classes** (not records) as required by OpenAPI generator.
- **Absolute URLs** used in the hook (baseUrl pattern).
- **No mutation:** all mapping creates new objects.
