# StockUp Playwright → Shoptet REST API Migration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Playwright browser automation for stock-up operations with direct Shoptet REST API calls (`PATCH /api/stocks/{stockId}/movements`), eliminating the headless-Chromium reliability problems.

**Architecture:** A new `IShoptetStockClient` interface lives in the Application layer so both the REST adapter and the Playwright adapter can reference it without cross-adapter dependencies. `ShoptetStockClient` in `Anela.Heblo.Adapters.ShoptetApi` implements it. `ShoptetPlaywrightStockDomainService` is modified to inject and delegate `StockUpAsync` to the REST client while retaining Playwright only for `SubmitStockTakingAsync`. `StockUpProcessingService` is simplified to skip the post-verify step (it was a Playwright-only safety net; a REST 200 with empty errors array is a hard guarantee).

**Tech Stack:** .NET 8, `System.Net.Http.Json` (`PatchAsJsonAsync`, `ReadFromJsonAsync`), `Microsoft.Extensions.Options`, xUnit, FluentAssertions, Moq.

---

## Key Context

- **`stockId`** is a warehouse ID, not a product ID. Most Shoptet shops have one stock. `GET /api/stocks` returns `defaultStockId`. Configure via `Shoptet:StockId` (int, default `1`).
- **No `documentNumber` field in REST API.** `PATCH /api/stocks/{stockId}/movements` has `additionalProperties: false` — only `productCode` + `amountChange`. After migration, Shoptet admin "Historie" shows movements with timestamp/amount/API-email but no BOX-/GPM- doc numbers. Traceability moves to Heblo's `StockUpOperation` table.
- **Partial errors on 200:** Shoptet returns `200 OK` even when some products fail; the `errors[]` array will be non-empty. Since we always send one product per call, any non-empty `errors` means failure.
- **`SubmitStockTakingAsync` stays on Playwright** — out of scope for this migration.
- **Pre-verify remains harmless:** `VerifyStockUpExistsAsync` now returns `false` always (REST API has no document-number search). The pre-check in `StockUpProcessingService` evaluates to "not found, proceed with submit", which is correct behavior.

---

## File Map

| Action | File |
|--------|------|
| **Create** | `backend/src/Anela.Heblo.Application/Features/Catalog/Stock/IShoptetStockClient.cs` |
| **Create** | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/Model/UpdateStockRequest.cs` |
| **Create** | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/Model/UpdateStockResponse.cs` |
| **Create** | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` |
| **Create** | `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs` |
| **Create** | `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs` |
| **Modify** | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs` |
| **Modify** | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` |
| **Modify** | `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Playwright/ShoptetPlaywrightStockDomainService.cs` |
| **Modify** | `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/ShoptetAdapterServiceCollectionExtensions.cs` |
| **Modify** | `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs` |
| **Modify** | `backend/src/Anela.Heblo.API/appsettings.json` |
| **Modify** | `docs/integrations/shoptet-api.md` |

---

## Task 1: Add `IShoptetStockClient` Interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Stock/IShoptetStockClient.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Stock;

/// <summary>
/// Abstraction for updating product stock quantities via the Shoptet REST API.
/// Placed in the Application layer so both adapters (REST and Playwright) can reference it
/// without creating a cross-adapter project dependency.
/// </summary>
public interface IShoptetStockClient
{
    /// <summary>
    /// Applies a relative stock quantity change for one product.
    /// Positive <paramref name="amountChange"/> increases stock (stock-up).
    /// Negative <paramref name="amountChange"/> decreases stock (e.g., ingredient consumption).
    /// </summary>
    Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
cd backend && dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Stock/IShoptetStockClient.cs
git commit -m "feat(stock): add IShoptetStockClient interface in Application layer"
```

---

## Task 2: Add DTO Models for REST Request/Response

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/Model/UpdateStockRequest.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/Model/UpdateStockResponse.cs`

These are pure data classes — no tests needed.

- [ ] **Step 1: Create `UpdateStockRequest.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock.Model;

/// <summary>
/// Body for PATCH /api/stocks/{stockId}/movements.
/// Shoptet's schema uses additionalProperties: false — only productCode + one of
/// amountChange/quantity/realStock are accepted. We always use amountChange (relative delta).
/// </summary>
public class UpdateStockRequest
{
    [JsonPropertyName("data")]
    public List<UpdateStockItem> Data { get; set; } = new();
}

public class UpdateStockItem
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = string.Empty;

    [JsonPropertyName("amountChange")]
    public double AmountChange { get; set; }
}
```

- [ ] **Step 2: Create `UpdateStockResponse.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock.Model;

/// <summary>
/// Response from PATCH /api/stocks/{stockId}/movements.
/// Shoptet returns 200 OK even for partial failures; check Errors for per-product issues.
/// If all records fail, Shoptet returns 400 and Errors is also populated.
/// </summary>
public class UpdateStockResponse
{
    [JsonPropertyName("errors")]
    public List<UpdateStockError>? Errors { get; set; }
}

public class UpdateStockError
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Instance is the productCode that caused the error.</summary>
    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Build to verify**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/
git commit -m "feat(stock): add UpdateStockRequest/Response DTOs for Shoptet stock movements endpoint"
```

---

## Task 3: Create `ShoptetStockClient` (TDD)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs`
- Create: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs`

The `FakeDelegatingHandler` used in tests is already defined as `internal class` in
`backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs:460`
and is accessible because the new test file shares the same assembly and namespace.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetStockClientTests
{
    private static ShoptetStockClient BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        int stockId = 1)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        var settings = Options.Create(new ShoptetApiSettings { StockId = stockId });
        return new ShoptetStockClient(http, settings);
    }

    private static HttpResponseMessage Json(object obj, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(obj,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task UpdateStockAsync_SuccessWithNoErrors_DoesNotThrow()
    {
        // Arrange — Shoptet returns 200 with null errors (success)
        var client = BuildClient(_ => Json(new { data = (object?)null, errors = (object?)null }));

        // Act & Assert — no exception
        await client.Invoking(c => c.UpdateStockAsync("AKL001", 5))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateStockAsync_SuccessWithEmptyErrorsArray_DoesNotThrow()
    {
        // Arrange
        var client = BuildClient(_ => Json(new { data = (object?)null, errors = Array.Empty<object>() }));

        // Act & Assert
        await client.Invoking(c => c.UpdateStockAsync("AKL001", 5))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateStockAsync_ResponseContainsErrors_ThrowsHttpRequestException()
    {
        // Arrange — Shoptet returns 200 but with error for our product (partial failure)
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "unknown-product", message = "Product \"AKL001\" does not exist. Skipped.", instance = "AKL001" },
            },
        }));

        // Act
        var act = () => client.UpdateStockAsync("AKL001", 5);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*AKL001*unknown-product*");
    }

    [Fact]
    public async Task UpdateStockAsync_Http400_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "stock-change-not-allowed", message = "Stock change not allowed for product set.", instance = "SET001" },
            },
        }, HttpStatusCode.BadRequest));

        // Act
        var act = () => client.UpdateStockAsync("SET001", 10);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*400*");
    }

    [Fact]
    public async Task UpdateStockAsync_UsesCorrectUrlWithStockId()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var client = BuildClient(req =>
        {
            captured = req;
            return Json(new { data = (object?)null, errors = (object?)null });
        }, stockId: 7);

        // Act
        await client.UpdateStockAsync("AKL001", 5);

        // Assert
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Patch);
        captured.RequestUri!.PathAndQuery.Should().Be("/api/stocks/7/movements");
    }

    [Fact]
    public async Task UpdateStockAsync_SerializesRequestBodyCorrectly()
    {
        // Arrange
        string? capturedBody = null;
        var client = BuildClient(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return Json(new { data = (object?)null, errors = (object?)null });
        });

        // Act
        await client.UpdateStockAsync("OCH001030", 3);

        // Assert — body must contain productCode and amountChange
        capturedBody.Should().Contain("\"productCode\"");
        capturedBody.Should().Contain("OCH001030");
        capturedBody.Should().Contain("\"amountChange\"");
        capturedBody.Should().Contain("3");
    }

    [Fact]
    public async Task UpdateStockAsync_NegativeAmount_SerializesCorrectly()
    {
        // Arrange — negative amounts are used for ingredient consumption (GiftPackageManufacture)
        string? capturedBody = null;
        var client = BuildClient(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return Json(new { data = (object?)null, errors = (object?)null });
        });

        // Act
        await client.UpdateStockAsync("OCH001030", -2);

        // Assert
        capturedBody.Should().Contain("-2");
    }
}
```

- [ ] **Step 2: Run tests to confirm they FAIL (class does not exist yet)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetStockClientTests" \
  --no-build 2>&1 | tail -5
```

Expected: compilation error — `ShoptetStockClient` not found.

- [ ] **Step 3: Create `ShoptetStockClient.cs`**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Application.Features.Catalog.Stock;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClient : IShoptetStockClient
{
    private readonly HttpClient _http;
    private readonly IOptions<Orders.ShoptetApiSettings> _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetStockClient(HttpClient http, IOptions<Orders.ShoptetApiSettings> settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default)
    {
        var stockId = _settings.Value.StockId;

        var body = new UpdateStockRequest
        {
            Data = [new UpdateStockItem { ProductCode = productCode, AmountChange = amountChange }],
        };

        var response = await _http.PatchAsJsonAsync($"/api/stocks/{stockId}/movements", body, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/stocks/{stockId}/movements returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<UpdateStockResponse>(JsonOptions, ct);
        if (result?.Errors is { Count: > 0 })
        {
            var error = result.Errors[0];
            throw new HttpRequestException(
                $"Shoptet stock update failed for {productCode}: [{error.ErrorCode}] {error.Message}");
        }
    }
}
```

- [ ] **Step 4: Run tests — all must PASS**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetStockClientTests" -v normal
```

Expected: `6 passed, 0 failed`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs
git commit -m "feat(stock): add ShoptetStockClient implementing IShoptetStockClient via REST API"
```

---

## Task 4: Add `StockId` to `ShoptetApiSettings`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs`

Current content of the file (lines 1-12):
```csharp
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

- [ ] **Step 1: Add `StockId` property**

Add one property after `PaymentMethodGuid`:

```csharp
namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetApiSettings
{
    public static string ConfigurationKey => "Shoptet";

    public bool IsTestEnvironment { get; set; } = false;
    public string BaseUrl { get; set; } = "https://api.myshoptet.com";
    public string ApiToken { get; set; } = null!;
    public Dictionary<string, string> ShippingGuidMap { get; set; } = new();
    public string PaymentMethodGuid { get; set; } = null!;

    /// <summary>
    /// Shoptet warehouse ID used for stock movements. Discover via GET /api/stocks (returns defaultStockId).
    /// Most single-warehouse stores use id 1. Configure per environment in user secrets: Shoptet:StockId
    /// </summary>
    public int StockId { get; set; } = 1;
}
```

- [ ] **Step 2: Add config entry to `appsettings.json`**

File: `backend/src/Anela.Heblo.API/appsettings.json`

Find the existing `"Shoptet"` section (around line 66). Add `StockId` with a placeholder:

```json
"Shoptet": {
  "Token": "xxxxxxxx",
  "StockId": 1
},
```

> **Note:** The actual live `StockId` value (production and staging) must be set in user secrets / Azure App Service environment variable `Shoptet__StockId`. To discover the value, call `GET /api/stocks` with your production API token and use the returned `defaultStockId`.

- [ ] **Step 3: Add to test project appsettings**

File: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/appsettings.json`

In the `"Shoptet"` section, add:
```json
"StockId": 1
```
(The real test store StockId goes in user secrets under `Shoptet:StockId`.)

- [ ] **Step 4: Build**

```bash
cd backend && dotnet build --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/appsettings.json
git commit -m "feat(stock): add Shoptet:StockId config key to ShoptetApiSettings"
```

---

## Task 5: Register `ShoptetStockClient` in DI

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`

Current file (lines 1-34):
```csharp
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        services.AddOptions<ShoptetApiSettings>()
            .Bind(configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

        services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.AddTransient<IPickingListSource, ShoptetApiExpeditionListSource>();

        return services;
    }
}
```

- [ ] **Step 1: Add `ShoptetStockClient` registration**

Replace the full file with:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using Anela.Heblo.Application.Features.Catalog.Stock;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        services.AddOptions<ShoptetApiSettings>()
            .Bind(configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

        services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.AddHttpClient<IShoptetStockClient, ShoptetStockClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.AddTransient<IPickingListSource, ShoptetApiExpeditionListSource>();

        return services;
    }
}
```

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
git commit -m "feat(stock): register ShoptetStockClient as IShoptetStockClient in DI"
```

---

## Task 6: Migrate `ShoptetPlaywrightStockDomainService` to Use REST

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Playwright/ShoptetPlaywrightStockDomainService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/ShoptetAdapterServiceCollectionExtensions.cs`

`StockUpScenario` and `VerifyStockUpScenario` become unused by this service — remove them from the constructor and DI registration to avoid loading unused Playwright browsers.

- [ ] **Step 1: Replace `ShoptetPlaywrightStockDomainService.cs`**

```csharp
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Application.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightStockDomainService : IEshopStockDomainService
{
    private readonly StockTakingScenario _inventoryAlignScenario;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IShoptetStockClient _stockClient;

    public ShoptetPlaywrightStockDomainService(
        StockTakingScenario inventoryAlignScenario,
        IStockTakingRepository stockTakingRepository,
        ICurrentUserService currentUser,
        TimeProvider timeProvider,
        IShoptetStockClient stockClient)
    {
        _inventoryAlignScenario = inventoryAlignScenario;
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _stockClient = stockClient;
    }

    public async Task StockUpAsync(StockUpRequest stockUpOrder)
    {
        foreach (var product in stockUpOrder.Products)
        {
            await _stockClient.UpdateStockAsync(product.ProductCode, product.Amount);
        }
    }

    /// <summary>
    /// The Shoptet REST API does not support searching movements by document number,
    /// so this check is not possible. Returns false to allow the caller to proceed with submit.
    /// Traceability is maintained in Heblo's StockUpOperation table via DocumentNumber.
    /// </summary>
    public Task<bool> VerifyStockUpExistsAsync(string documentNumber)
        => Task.FromResult(false);

    public async Task<StockTakingRecord> SubmitStockTakingAsync(EshopStockTakingRequest order)
    {
        try
        {
            StockTakingRecord result;
            if (!order.SoftStockTaking)
            {
                result = await _inventoryAlignScenario.RunAsync(order);
            }
            else
            {
                result = new StockTakingRecord()
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.TargetAmount,
                    AmountOld = (double)order.TargetAmount,
                };
            }
            result.User = _currentUser.GetCurrentUser().Name;
            result.Date = _timeProvider.GetUtcNow().DateTime;
            await _stockTakingRepository.AddAsync(result);
            await _stockTakingRepository.SaveChangesAsync();
            return result;
        }
        catch (Exception e)
        {
            return new StockTakingRecord
            {
                Date = _timeProvider.GetUtcNow().DateTime,
                Code = order.ProductCode,
                AmountNew = (double)order.TargetAmount,
                AmountOld = (double)order.TargetAmount,
                Error = e.Message,
            };
        }
    }
}
```

- [ ] **Step 2: Remove `StockUpScenario` and `VerifyStockUpScenario` from DI**

In `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/ShoptetAdapterServiceCollectionExtensions.cs`, remove lines 49–50:

```csharp
// REMOVE these two lines:
services.AddSingleton<StockUpScenario>();
services.AddSingleton<VerifyStockUpScenario>();
```

The file after removal (lines 48–53 area should look like):
```csharp
        services.AddScoped<IEshopStockDomainService, ShoptetPlaywrightStockDomainService>();
        services.AddSingleton<StockTakingScenario>();

        services.AddSingleton<ICashRegisterOrdersSource, ShoptetPlaywrightCashRegisterOrdersSource>();
```

- [ ] **Step 3: Build the full solution**

```bash
cd backend && dotnet build --no-restore -q
```

Expected: `Build succeeded.`

If you see "unused variable" warnings for `_stockUpScenario` or `_verifyStockUpScenario` — those fields were removed in Step 1, so this should be clean.

- [ ] **Step 4: Run all non-integration tests**

```bash
cd backend && dotnet test --filter "Category!=Playwright&Category!=Integration" --no-build -q
```

Expected: all pass (the existing `StockUpOperationTests.cs` domain tests should still pass).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Playwright/ShoptetPlaywrightStockDomainService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/ShoptetAdapterServiceCollectionExtensions.cs
git commit -m "feat(stock): migrate StockUpAsync to REST API, retire Playwright stock-up/verify scenarios"
```

---

## Task 7: Simplify `StockUpProcessingService` — Remove Post-Verify (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs`

The post-verify step (lines 112–135 in `StockUpProcessingService.cs`) called `VerifyStockUpExistsAsync` after submit to confirm the change landed. With REST, a successful `StockUpAsync` (no exception) is a hard guarantee. The verify would now always return `false` → always mark `Failed`. We replace it with a direct `MarkAsCompleted`.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

public class StockUpProcessingServiceTests
{
    private readonly Mock<IStockUpOperationRepository> _repo = new();
    private readonly Mock<IEshopStockDomainService> _eshop = new();

    private StockUpProcessingService CreateService() =>
        new(_repo.Object, _eshop.Object, NullLogger<StockUpProcessingService>.Instance);

    private static StockUpOperation PendingOperation(string docNumber = "BOX-000001-AKL001") =>
        new(docNumber, "AKL001", 5, StockUpSourceType.TransportBox, 1);

    [Fact]
    public async Task ProcessPendingOperations_SuccessfulSubmit_MarksCompleted()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()))
              .ReturnsAsync(false);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert — operation should be Completed after a successful REST call
        operation.State.Should().Be(StockUpOperationState.Completed);
    }

    [Fact]
    public async Task ProcessPendingOperations_SuccessfulSubmit_DoesNotCallVerifyAfterSubmit()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()))
              .ReturnsAsync(false);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert — VerifyStockUpExistsAsync called exactly once (pre-check), NOT twice
        _eshop.Verify(e => e.VerifyStockUpExistsAsync(operation.DocumentNumber), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingOperations_StockUpAsyncThrows_MarksAsFailed()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()))
              .ReturnsAsync(false);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .ThrowsAsync(new HttpRequestException("Shoptet stock update failed for AKL001: [unknown-product] Product does not exist."));

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Failed);
        operation.ErrorMessage.Should().Contain("unknown-product");
    }

    [Fact]
    public async Task ProcessPendingOperations_PreCheckReturnsTrueAlreadyInShoptet_MarksCompletedWithoutSubmit()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.VerifyStockUpExistsAsync(operation.DocumentNumber))
              .ReturnsAsync(true); // already submitted (e.g. Playwright legacy record)

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert — completes without calling StockUpAsync
        operation.State.Should().Be(StockUpOperationState.Completed);
        _eshop.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingOperations_PreCheckThrows_SubmitProceedsAndCompletes()
    {
        // Arrange — pre-check failure should NOT block the submit
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()))
              .ThrowsAsync(new Exception("network timeout"));
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert — submit proceeded and completed
        operation.State.Should().Be(StockUpOperationState.Completed);
        _eshop.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Once);
    }
}
```

- [ ] **Step 2: Add missing using to the test file**

Add at the top of the test file (after the last using):
```csharp
using FluentAssertions;
```

The full using block:
```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
```

- [ ] **Step 3: Run tests — they should FAIL**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~StockUpProcessingServiceTests" -v normal 2>&1 | tail -20
```

Expected: failures. Specifically:
- `DoesNotCallVerifyAfterSubmit` should fail because current code calls `VerifyStockUpExistsAsync` twice
- `SuccessfulSubmit_MarksCompleted` should fail because current code marks Failed (verify returns false)

- [ ] **Step 4: Update `StockUpProcessingService.ProcessOperationAsync`**

Replace the `private async Task ProcessOperationAsync(...)` method in
`backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs`
(currently lines 66–147) with:

```csharp
private async Task ProcessOperationAsync(StockUpOperation operation, CancellationToken ct)
{
    _logger.LogDebug(
        "Processing operation {OperationId} for document {DocumentNumber}",
        operation.Id, operation.DocumentNumber);

    try
    {
        // Pre-check: skip submit if the record somehow already exists.
        // With the REST adapter, VerifyStockUpExistsAsync always returns false
        // (the REST API has no document-number search). The check is retained so
        // that manually accepted legacy Playwright records are still detected.
        try
        {
            var existsInShoptet = await _eshopService.VerifyStockUpExistsAsync(operation.DocumentNumber);
            if (existsInShoptet)
            {
                _logger.LogWarning(
                    "Document {DocumentNumber} already exists in Shoptet, marking as completed",
                    operation.DocumentNumber);
                operation.MarkAsCompleted(DateTime.UtcNow);
                await _repository.SaveChangesAsync(ct);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Pre-check verification failed for {DocumentNumber}, continuing with submit",
                operation.DocumentNumber);
        }

        operation.MarkAsSubmitted(DateTime.UtcNow);
        await _repository.SaveChangesAsync(ct);
        _logger.LogDebug("Operation {DocumentNumber} marked as Submitted", operation.DocumentNumber);

        var request = new StockUpRequest(operation.ProductCode, operation.Amount, operation.DocumentNumber);
        await _eshopService.StockUpAsync(request);

        // REST API guarantees: a 200 response with no errors means the stock change was applied.
        // No post-verify needed — that was a Playwright-only safety net.
        operation.MarkAsCompleted(DateTime.UtcNow);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Operation {DocumentNumber} completed successfully",
            operation.DocumentNumber);
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Failed to process operation {OperationId} for document {DocumentNumber}",
            operation.Id, operation.DocumentNumber);

        operation.MarkAsFailed(DateTime.UtcNow, $"Processing failed: {ex.Message}");
        await _repository.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run tests — all must PASS**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~StockUpProcessingServiceTests" -v normal
```

Expected: `5 passed, 0 failed`

- [ ] **Step 6: Run full test suite**

```bash
cd backend && dotnet test --filter "Category!=Playwright&Category!=Integration" --no-build -q
```

Expected: all pass, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs
git commit -m "refactor(stock): simplify ProcessOperationAsync — mark completed directly after REST success, remove post-verify"
```

---

## Task 8: Update Documentation

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 1: Add a new section for Stock endpoints**

Append the following section to `docs/integrations/shoptet-api.md` before the last section (or at the end):

```markdown
## 8. Stock Endpoints

Stock movements are used to update product quantities in Shoptet (stock-up and stock-down).

### 8.1 Authentication
Same `Shoptet-Private-API-Token` header as all other endpoints. No separate token.

### 8.2 List Stocks
```
GET /api/stocks
```
Returns all warehouses. Response includes `defaultStockId` (integer). Most single-warehouse
Shoptet stores have exactly one stock. Use this endpoint once to discover the `StockId` value
to configure in `Shoptet:StockId`.

### 8.3 Update Stock Quantity
```
PATCH /api/stocks/{stockId}/movements
Content-Type: application/json

{
  "data": [
    { "productCode": "AKL001", "amountChange": 5 }
  ]
}
```

- `stockId` — warehouse ID (configure via `Shoptet:StockId`, discover via `GET /api/stocks`)
- `productCode` — variant-level SKU (same as stored in `StockUpOperation.ProductCode`)
- `amountChange` — relative delta; positive = stock-up, negative = stock-down (ingredient consumption)
- Up to 300 products per call (this project sends one product per call)

**Partial failure semantics:** Shoptet returns `200 OK` even when one or more products fail;
the `errors[]` array will be non-empty. If all products fail, Shoptet returns `400 Bad Request`.
Always check `errors[]` even on 200. `ShoptetStockClient` throws `HttpRequestException` for
either case.

**No document number field.** `additionalProperties: false` on the request body — no `documentNumber`,
`note`, or `reference` fields are accepted. Movements appear in Shoptet admin with
`changedBy = "api.service-{id}@{domain}"`. Traceability is maintained in Heblo's
`StockUpOperation` table via `DocumentNumber` (BOX-/GPM-/GPD- prefix).

### 8.4 Configuration Keys

| Key | Type | Where to set |
|-----|------|-------------|
| `Shoptet:StockId` | `int` | User secrets / Azure App Service env var |

Default value is `1`. To find the correct value per environment:
```
GET /api/stocks
Authorization: see section 2
```
The response `data.defaultStockId` is the value to configure.

### 8.5 Known Constraints
- Cannot update quantities for product sets (dynamically calculated). Returns `stock-change-not-allowed` error.
- No idempotency key — duplicate PATCHes create duplicate movements. Guard in application layer via
  `StockUpOperation` state machine (unique `DocumentNumber` per operation, Submitted → Completed transition).
- `VerifyStockUpExistsAsync` is not implementable via REST (no document-number filter on `GET /api/stocks/{id}/movements`). The pre-check in `StockUpProcessingService` always returns false and is effectively a no-op with the REST adapter.
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs(shoptet): document stock movement REST endpoints and StockId configuration"
```

---

## Verification Checklist

After all tasks are complete:

- [ ] `dotnet build --no-restore` — no errors, no warnings
- [ ] `dotnet format --verify-no-changes` — passes (no formatting violations)
- [ ] `dotnet test --filter "Category!=Playwright&Category!=Integration"` — all pass
- [ ] Confirm `Shoptet:StockId` is set in Azure App Service environment variables for production and staging (do this before deploying)
- [ ] Deploy to **staging**, trigger a transport box receive in the UI
- [ ] Confirm `StockUpOperation` transitions from `Pending → Submitted → Completed` in Heblo UI (StockUpOperations page)
- [ ] Confirm stock quantity updated in Shoptet staging admin → Zboží → Sklad → Pohyby
- [ ] Confirm a GiftPackage manufacture (negative amount) also transitions to `Completed` and stock decreases
- [ ] Confirm the `StockUpOperations` summary endpoint (`GET /api/StockUpOperations/summary`) shows 0 Failed
