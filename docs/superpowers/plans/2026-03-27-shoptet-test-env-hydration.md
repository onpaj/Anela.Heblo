# Shoptet Test Environment Hydration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create an on-demand integration test that seeds a Shoptet test store with 26 orders across 2 shipping methods and 3 states, with idempotent upsert+reset semantics.

**Architecture:** A new `Anela.Heblo.Adapters.ShoptetApi` class library provides `ShoptetOrderClient` (typed HTTP client). The test project gains a project reference and a new `ShoptetTestEnvironmentHydrationTests` class. Two guards prevent running against production. The seed catalog is a static list of `OrderDefinition` values; the upsert algorithm creates missing orders and resets existing ones to their target state.

**Tech Stack:** .NET 8, `System.Text.Json`, xUnit, FluentAssertions, `Microsoft.Extensions.Http`

---

## File Map

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/CreateOrderRequest.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/CreateOrderResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateStatusRequest.cs`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs`

**Modify:**
- `Anela.Heblo.sln` — add ShoptetApi project
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj` — add `ProjectReference`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` — register `ShoptetOrderClient`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/appsettings.json` — add `Shoptet` section

---

## Task 1: Create `Anela.Heblo.Adapters.ShoptetApi` project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the csproj**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.ShoptetApi</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

</Project>
```

Note: No reference to `Anela.Heblo.Application` — this adapter is self-contained (no domain dependencies).

- [ ] **Step 2: Add to solution**

Run from repo root (`/Users/pajgrtondrej/Work/GitHub/Anela.Heblo`):

```bash
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected output: `Project 'backend/src/Adapters/.../Anela.Heblo.Adapters.ShoptetApi.csproj' added to the solution.`

- [ ] **Step 3: Verify it builds**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj Anela.Heblo.sln
git commit -m "feat: scaffold Anela.Heblo.Adapters.ShoptetApi project"
```

---

## Task 2: `ShoptetApiSettings`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs`

- [ ] **Step 1: Create `ShoptetApiSettings`**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs
namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetApiSettings
{
    public static string ConfigurationKey => "Shoptet";

    public bool IsTestEnvironment { get; set; } = false;
    public string BaseUrl { get; set; } = "https://api.myshoptet.com";
    public string ApiToken { get; set; } = null!;
    public Dictionary<string, string> ShippingGuidMap { get; set; } = new();
    public string PaymentMethodGuid { get; set; } = null!;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
git commit -m "feat: add ShoptetApiSettings"
```

---

## Task 3: Request / Response DTOs

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/CreateOrderRequest.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/CreateOrderResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateStatusRequest.cs`

- [ ] **Step 1: Create `CreateOrderRequest`**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/CreateOrderRequest.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class CreateOrderRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("externalCode")]
    public string ExternalCode { get; set; } = null!;

    [JsonPropertyName("shippingGuid")]
    public string ShippingGuid { get; set; } = null!;

    [JsonPropertyName("paymentMethodGuid")]
    public string PaymentMethodGuid { get; set; } = null!;

    [JsonPropertyName("currency")]
    public OrderCurrency Currency { get; set; } = new();

    [JsonPropertyName("billingAddress")]
    public OrderAddress BillingAddress { get; set; } = new();

    [JsonPropertyName("items")]
    public List<OrderItem> Items { get; set; } = new();

    [JsonPropertyName("suppressEmailSending")]
    public bool SuppressEmailSending { get; set; } = true;

    [JsonPropertyName("suppressStockMovements")]
    public bool SuppressStockMovements { get; set; } = true;

    [JsonPropertyName("suppressDocumentGeneration")]
    public bool SuppressDocumentGeneration { get; set; } = true;

    [JsonPropertyName("suppressProductChecking")]
    public bool SuppressProductChecking { get; set; } = true;
}

public class OrderCurrency
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "CZK";
}

public class OrderAddress
{
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = null!;

    [JsonPropertyName("street")]
    public string Street { get; set; } = null!;

    [JsonPropertyName("city")]
    public string City { get; set; } = null!;

    [JsonPropertyName("zip")]
    public string Zip { get; set; } = null!;
}

public class OrderItem
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = "product";

    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("vatRate")]
    public decimal VatRate { get; set; }

    [JsonPropertyName("itemPriceWithVat")]
    public decimal ItemPriceWithVat { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
```

- [ ] **Step 2: Create `OrderListResponse`**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class OrderListResponse
{
    [JsonPropertyName("data")]
    public OrderListData Data { get; set; } = new();
}

public class OrderListData
{
    [JsonPropertyName("orders")]
    public List<OrderSummary> Orders { get; set; } = new();

    [JsonPropertyName("paginator")]
    public Paginator Paginator { get; set; } = new();
}

public class OrderSummary
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("externalCode")]
    public string? ExternalCode { get; set; }

    [JsonPropertyName("status")]
    public OrderStatusSummary Status { get; set; } = new();
}

public class OrderStatusSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

public class Paginator
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
```

- [ ] **Step 3: Create `CreateOrderResponse`**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/CreateOrderResponse.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class CreateOrderResponse
{
    [JsonPropertyName("data")]
    public CreateOrderData Data { get; set; } = new();
}

public class CreateOrderData
{
    [JsonPropertyName("order")]
    public OrderSummary Order { get; set; } = new();
}
```

- [ ] **Step 4: Create `UpdateStatusRequest`**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateStatusRequest.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class UpdateStatusRequest
{
    [JsonPropertyName("data")]
    public UpdateStatusData Data { get; set; } = new();
}

public class UpdateStatusData
{
    [JsonPropertyName("status")]
    public UpdateStatusValue Status { get; set; } = new();
}

public class UpdateStatusValue
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
```

- [ ] **Step 5: Build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
git commit -m "feat: add ShoptetApi order DTOs"
```

---

## Task 4: `ShoptetOrderClient`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

- [ ] **Step 1: Create `ShoptetOrderClient`**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetOrderClient
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

    /// <summary>
    /// Find a single order by its exact externalCode.
    /// Returns null when not found.
    /// </summary>
    public async Task<OrderSummary?> FindByExternalCodeAsync(string externalCode, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"/api/orders?externalCode={Uri.EscapeDataString(externalCode)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
        return result?.Data.Orders.FirstOrDefault(o => o.ExternalCode == externalCode);
    }

    /// <summary>
    /// List all orders whose externalCode starts with the given prefix.
    /// Paginates automatically.
    /// </summary>
    public async Task<List<OrderSummary>> ListByExternalCodePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var result = new List<OrderSummary>();
        var page = 1;

        while (true)
        {
            var response = await _http.GetAsync($"/api/orders?page={page}&itemsPerPage=100", ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null) break;

            var matching = data.Data.Orders
                .Where(o => o.ExternalCode?.StartsWith(prefix, StringComparison.Ordinal) == true)
                .ToList();

            result.AddRange(matching);

            if (page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }

    /// <summary>
    /// Create a new order. Returns the created order code.
    /// </summary>
    public async Task<string> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/orders", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return result!.Data.Order.Code;
    }

    /// <summary>
    /// Update the status of an existing order.
    /// </summary>
    public async Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default)
    {
        var body = new UpdateStatusRequest
        {
            Data = new UpdateStatusData
            {
                Status = new UpdateStatusValue { Id = statusId }
            }
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/status", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
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

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
git commit -m "feat: add ShoptetOrderClient"
```

---

## Task 5: DI Registration

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs`

- [ ] **Step 1: Create extension**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiServiceCollectionExtensions.cs
using System.Net.Http.Headers;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ShoptetApiSettings>()
            .Bind(configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

        services.AddHttpClient<ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiToken);
        });

        return services;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
git commit -m "feat: add ShoptetApi DI registration"
```

---

## Task 6: Wire into test infrastructure

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/appsettings.json`

- [ ] **Step 1: Add project reference to test csproj**

In `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`, add inside the existing `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.ShoptetApi\Anela.Heblo.Adapters.ShoptetApi.csproj" />
```

- [ ] **Step 2: Register `ShoptetOrderClient` in fixture**

Replace the contents of `ShoptetIntegrationTestFixture.cs`:

```csharp
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application.Features.Users;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;

public class ShoptetIntegrationTestFixture
{
    public IServiceProvider ServiceProvider { get; private set; }
    public IConfiguration Configuration { get; private set; }

    public ShoptetIntegrationTestFixture()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<ShoptetIntegrationTestFixture>()
            .AddEnvironmentVariables();

        Configuration = configBuilder.Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole());

        services.AddShoptetAdapter(Configuration);
        services.AddShoptetApiAdapter(Configuration);
        services.AddCrossCuttingServices();
        services.AddHttpClient();

        ServiceProvider = services.BuildServiceProvider();
    }
}
```

- [ ] **Step 3: Update `appsettings.json` with Shoptet section**

Replace the contents of `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "ShoptetPlaywright": {
    "ShopEntryUrl": "https://your-shoptet-test-instance.myshoptet.com",
    "Login": "test-username",
    "Password": "test-password",
    "Headless": true,
    "Timeout": 30000,
    "DryRun": true
  },
  "ShoptetStockClient": {
    "Url": "https://your-shoptet-test-instance.myshoptet.com/action/ExportManager/export/stock"
  },
  "ProductPriceOptions": {
    "ProductExportUrl": "https://your-shoptet-test-instance.myshoptet.com/action/ExportManager/export/products"
  },
  "Shoptet": {
    "IsTestEnvironment": false,
    "BaseUrl": "https://api.myshoptet.com",
    "ApiToken": "FILL_IN_FROM_TEST_STORE",
    "ShippingGuidMap": {
      "21": "FILL_IN_FROM_TEST_STORE",
      "6": "FILL_IN_FROM_TEST_STORE"
    },
    "PaymentMethodGuid": "FILL_IN_FROM_TEST_STORE"
  }
}
```

- [ ] **Step 4: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/
git commit -m "feat: wire ShoptetOrderClient into test fixture"
```

---

## Task 7: Guard helper

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs` (partial — guards only)

- [ ] **Step 1: Write failing guard unit tests first**

Create the file with guard tests only (hydration body comes in Task 8):

```csharp
// backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetTestEnvironmentHydrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly ShoptetOrderClient _client;

    public ShoptetTestEnvironmentHydrationTests(ShoptetIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<ShoptetOrderClient>();
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void AssertTestEnvironment_WhenFlagFalse_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shoptet:IsTestEnvironment"] = "false",
                ["Shoptet:BaseUrl"] = "https://api.test-store.com",
            })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IsTestEnvironment*");
    }

    [Fact]
    public void AssertTestEnvironment_WhenUrlContainsAnela_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shoptet:IsTestEnvironment"] = "true",
                ["Shoptet:BaseUrl"] = "https://api.anela.myshoptet.com",
            })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*anela*");
    }

    [Fact]
    public void AssertTestEnvironment_WhenValidTestConfig_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shoptet:IsTestEnvironment"] = "true",
                ["Shoptet:BaseUrl"] = "https://api.myshoptet.com",
            })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().NotThrow();
    }

    // ── Guard helper ─────────────────────────────────────────────────────────

    private static void AssertTestEnvironment(IConfiguration config)
    {
        var isTest = config.GetValue<bool>("Shoptet:IsTestEnvironment");
        if (!isTest)
            throw new InvalidOperationException(
                "Hydration must not run against live environment. " +
                "Set Shoptet:IsTestEnvironment=true in test appsettings.json");

        var baseUrl = config["Shoptet:BaseUrl"] ?? string.Empty;
        if (baseUrl.Contains("anela", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Hydration refused: base URL contains 'anela' — this looks like the production store.");
    }

    // ── Placeholder for hydration (Task 8) ───────────────────────────────────

    [Fact(Skip = "Not yet implemented")]
    public Task HydrateTestEnvironment() => Task.CompletedTask;

    [Fact(Skip = "Not yet implemented")]
    public Task PurgeTestOrders() => Task.CompletedTask;
}
```

- [ ] **Step 2: Run the guard tests**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetTestEnvironmentHydrationTests" \
  --logger "console;verbosity=normal"
```

Expected: 3 tests pass (`AssertTestEnvironment_*`), 2 skipped (`HydrateTestEnvironment`, `PurgeTestOrders`).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs
git commit -m "feat: add hydration guard helper with unit tests"
```

---

## Task 8: `HydrateTestEnvironment`

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs`

- [ ] **Step 1: Define the seed catalog and replace the placeholder**

Replace the `HydrateTestEnvironment` placeholder with the full implementation. The file now looks like:

```csharp
// backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetTestEnvironmentHydrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly ShoptetOrderClient _client;
    private readonly ITestOutputHelper _output;

    // Shipping IDs match ShoptetPlaywrightExpeditionListSource constants.
    // The GUIDs for these IDs must be set in appsettings / user secrets
    // under Shoptet:ShippingGuidMap:21 and Shoptet:ShippingGuidMap:6.
    private static readonly IReadOnlyList<OrderDefinition> SeedCatalog = BuildSeedCatalog();

    private record OrderDefinition(string ExternalCode, int ShippingId, int TargetState);

    public ShoptetTestEnvironmentHydrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<ShoptetOrderClient>();
        _output = output;
    }

    // ── Guard tests ───────────────────────────────────────────────────────────

    [Fact]
    public void AssertTestEnvironment_WhenFlagFalse_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shoptet:IsTestEnvironment"] = "false",
                ["Shoptet:BaseUrl"] = "https://api.test-store.com",
            })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IsTestEnvironment*");
    }

    [Fact]
    public void AssertTestEnvironment_WhenUrlContainsAnela_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shoptet:IsTestEnvironment"] = "true",
                ["Shoptet:BaseUrl"] = "https://api.anela.myshoptet.com",
            })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*anela*");
    }

    [Fact]
    public void AssertTestEnvironment_WhenValidTestConfig_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shoptet:IsTestEnvironment"] = "true",
                ["Shoptet:BaseUrl"] = "https://api.myshoptet.com",
            })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().NotThrow();
    }

    // ── Hydration ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HydrateTestEnvironment()
    {
        AssertTestEnvironment(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var paymentGuid = _configuration["Shoptet:PaymentMethodGuid"]!;

        int created = 0, reset = 0, skipped = 0;

        foreach (var definition in SeedCatalog)
        {
            var shippingGuid = _configuration[$"Shoptet:ShippingGuidMap:{definition.ShippingId}"]
                ?? throw new InvalidOperationException(
                    $"Missing ShippingGuidMap entry for shippingId={definition.ShippingId}. " +
                    "Add it to user secrets under Shoptet:ShippingGuidMap:{id}.");

            var existing = await _client.FindByExternalCodeAsync(definition.ExternalCode, ct);

            if (existing is null)
            {
                var request = new CreateOrderRequest
                {
                    Email = "test-seed@heblo.test",
                    ExternalCode = definition.ExternalCode,
                    ShippingGuid = shippingGuid,
                    PaymentMethodGuid = paymentGuid,
                    Currency = new OrderCurrency { Code = "CZK" },
                    BillingAddress = new OrderAddress
                    {
                        FullName = "Test Heblo",
                        Street = "Testovací 1",
                        City = "Praha",
                        Zip = "10000",
                    },
                    Items = new List<OrderItem>
                    {
                        new OrderItem
                        {
                            ItemType = "product",
                            Code = "TEST-ITEM",
                            Name = "Test product",
                            VatRate = 21,
                            ItemPriceWithVat = 1.00m,
                            Quantity = 1,
                        }
                    },
                };

                var code = await _client.CreateOrderAsync(request, ct);

                // Newly created orders may land in a default status — reset to target
                await _client.UpdateStatusAsync(code, definition.TargetState, ct);

                _output.WriteLine($"CREATED  {definition.ExternalCode} → status {definition.TargetState}");
                created++;
            }
            else if (existing.Status.Id != definition.TargetState)
            {
                await _client.UpdateStatusAsync(existing.Code, definition.TargetState, ct);
                _output.WriteLine(
                    $"RESET    {definition.ExternalCode}: {existing.Status.Id} → {definition.TargetState}");
                reset++;
            }
            else
            {
                _output.WriteLine($"OK       {definition.ExternalCode} already in state {definition.TargetState}");
                skipped++;
            }
        }

        _output.WriteLine($"\nDone — created={created} reset={reset} skipped={skipped}");
        (created + reset + skipped).Should().Be(SeedCatalog.Count);
    }

    // ── Purge ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeTestOrders()
    {
        AssertTestEnvironment(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var orders = await _client.ListByExternalCodePrefixAsync("TEST-", ct);

        foreach (var order in orders)
        {
            await _client.DeleteOrderAsync(order.Code, ct);
            _output.WriteLine($"DELETED  {order.Code} ({order.ExternalCode})");
        }

        _output.WriteLine($"\nDeleted {orders.Count} test orders.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertTestEnvironment(IConfiguration config)
    {
        var isTest = config.GetValue<bool>("Shoptet:IsTestEnvironment");
        if (!isTest)
            throw new InvalidOperationException(
                "Hydration must not run against live environment. " +
                "Set Shoptet:IsTestEnvironment=true in test appsettings.json");

        var baseUrl = config["Shoptet:BaseUrl"] ?? string.Empty;
        if (baseUrl.Contains("anela", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Hydration refused: base URL contains 'anela' — this looks like the production store.");
    }

    private static IReadOnlyList<OrderDefinition> BuildSeedCatalog()
    {
        var catalog = new List<OrderDefinition>();

        // Shipping 21 — ZASILKOVNA_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-ZAK-21-INIT-{i:D2}", 21, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-ZAK-21-EXP-{i:D2}", 21, 55));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-ZAK-21-PACK-{i:D2}", 21, 26));

        // Shipping 6 — PPL_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-PPL-6-INIT-{i:D2}", 6, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-EXP-{i:D2}", 6, 55));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-PACK-{i:D2}", 6, 26));

        return catalog;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Run guard unit tests (must still pass)**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~AssertTestEnvironment" \
  --logger "console;verbosity=normal"
```

Expected: 3 tests pass.

- [ ] **Step 4: Run format check**

```bash
dotnet format backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --verify-no-changes
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj --verify-no-changes
```

Fix any formatting issues, then re-run to confirm clean.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs
git commit -m "feat(#hydration): add HydrateTestEnvironment and PurgeTestOrders integration tests"
```

---

## Task 9: Verify end-to-end (manual, with real test credentials)

This step requires real test store credentials in user secrets. Skip in CI.

- [ ] **Step 1: Set user secrets**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/test/Anela.Heblo.Adapters.Shoptet.Tests

dotnet user-secrets set "Shoptet:IsTestEnvironment" "true"
dotnet user-secrets set "Shoptet:BaseUrl" "https://api.myshoptet.com"
dotnet user-secrets set "Shoptet:ApiToken" "<test-store-token>"
dotnet user-secrets set "Shoptet:PaymentMethodGuid" "<guid-from-test-store>"
dotnet user-secrets set "Shoptet:ShippingGuidMap:21" "<guid-for-shipping-21>"
dotnet user-secrets set "Shoptet:ShippingGuidMap:6" "<guid-for-shipping-6>"
```

To discover shipping GUIDs, call the test store: `GET /api/eshop?include=shippingMethods` with the API token and match by shipping method name.

- [ ] **Step 2: Run HydrateTestEnvironment**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~HydrateTestEnvironment" \
  --logger "console;verbosity=normal"
```

Expected: 1 test passes. Output shows 26 lines: CREATED / RESET / OK per order.

- [ ] **Step 3: Re-run to verify idempotency**

Run the same command again. Expected: all 26 lines show `OK` (no CREATED or RESET).

- [ ] **Step 4: Run PurgeTestOrders**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~PurgeTestOrders" \
  --logger "console;verbosity=normal"
```

Expected: 1 test passes. Output shows 26 deleted orders.

- [ ] **Step 5: Run HydrateTestEnvironment again (post-purge)**

Expected: all 26 lines show `CREATED` (store is empty again).

---

## Notes

- **PATCH /status body shape:** The `UpdateStatusRequest` wraps in `data.status.id`. If the Shoptet API rejects this, try the flat form `{"statusId": 55}` — adjust the DTO accordingly.
- **externalCode filter:** `FindByExternalCodeAsync` uses `?externalCode=...` query param. Verify this filter is supported by calling the API manually. If not supported, change the implementation to use `ListByExternalCodePrefixAsync` and find by exact match.
- **New order default status:** When `POST /api/orders` creates an order, it may land in a status other than `-2`. The hydration always calls `UpdateStatusAsync` after create to ensure the correct state.
- **Shipping GUID discovery:** `GET /api/eshop?include=shippingMethods` returns shipping methods. Match by name or browse the admin panel to find numeric ID → GUID mapping.
