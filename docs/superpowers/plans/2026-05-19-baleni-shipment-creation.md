# Baleni — Shoptet Shipment Creation + Label Print Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scan a packing order in the Balení kiosk → auto-create a Shoptet shipment on demand → print the returned label; fully hands-free.

**Architecture:** New `POST /api/shipment-labels/create` endpoint wraps the Shoptet Delivery API: duplicate guard → weight computation from catalog → carrier resolution via shipping-options → shipment creation → one-retry label fetch. If a shipment already exists the packer chooses reuse or force-create. If the label is still not ready after the retry the UI shows a "Zkusit znovu" button. All existing `PackingLabelPrinter` / `printLabelPdf` / `useShipmentLabels` code is reused unchanged.

**Tech Stack:** .NET 8 / MediatR / FluentValidation / xUnit / FluentAssertions / Moq (backend); React 18 / TanStack Query / Jest / React Testing Library / Playwright (frontend).

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `docs/integrations/shoptet-api.md` | Modify | Add § 11 probe findings: POST body, shipping-options shape, weight unit, latency |
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Modify | Add 2905–2909 error codes |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsSettings.cs` | Create | Options class: package dimensions, default/min weight |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentCreation.cs` | Create | Application models: ShippingOption, CreateShipmentCommand, ShipmentPackage, CreatedShipment |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs` | Modify | Add GetShippingOptionsAsync + CreateShipmentAsync |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` | Modify | Add WeightGrams to PackingOrderItem |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShippingOptionsResponse.cs` | Create | Adapter DTO for GET shipping-options |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetCreateShipmentRequest.cs` | Create | Adapter DTO for POST /api/shipments body |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetCreateShipmentResponse.cs` | Create | Adapter DTO for POST /api/shipments response |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` | Modify | Implement GetShippingOptionsAsync + CreateShipmentAsync |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` | Modify | Populate WeightGrams per item from catalog (with DefaultItemWeightGrams fallback) |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentRequest.cs` | Create | MediatR request: OrderCode, ForceCreate |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentResponse.cs` | Create | Response: ShipmentGuid, Status, LabelReady, Labels, ExistingShipmentFound |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentHandler.cs` | Create | Handler: duplicate guard → weight → carrier → create → retry label fetch |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Validators/CreateOrderShipmentRequestValidator.cs` | Create | FluentValidation: OrderCode not empty |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs` | Modify | Bind settings; register new validator + pipeline behavior |
| `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | Modify | Pass `configuration` to `AddShipmentLabelsModule` |
| `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs` | Modify | Add POST /api/shipment-labels/create |
| `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/CreateOrderShipmentHandlerTests.cs` | Create | Handler unit tests |
| `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/CreateOrderShipmentRequestValidatorTests.cs` | Create | Validator unit tests |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs` | Modify | Add tests for new client methods |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs` | Modify | Add WeightGrams population test |
| `backend/test/Anela.Heblo.Tests/Controllers/ShipmentLabelsControllerTests.cs` | Create | Integration tests: 200, 409, 401 |
| `frontend/src/api/hooks/useCreateShipment.ts` | Create | useMutation: POST create; treats 2905 as non-throw |
| `frontend/src/components/baleni/PackingShipmentCreator.tsx` | Create | State machine: no shipment / existing / creating / label-ready / not-ready / error |
| `frontend/src/components/baleni/BaleniPacking.tsx` | Modify | Swap `<PackingLabelPrinter>` for `<PackingShipmentCreator>` |
| `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx` | Modify | Update mock + assertion for PackingShipmentCreator |
| `frontend/test/e2e/fixtures/test-data.ts` | Modify | Add NO_SHIPMENT_PACKING_ORDER_CODE + EXISTING_SHIPMENT_ORDER_CODE |
| `frontend/test/e2e/baleni/packing.spec.ts` | Modify | Add create-flow + reuse-choice E2E tests |

---

## Task 1: Live API Probe — Document `POST /api/shipments` and Shipping Options

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

This task must run BEFORE Task 6 (adapter DTOs). The exact `POST /api/shipments` body and
`GET /api/shipments/order/{code}/shipping-options` shape are not in the existing docs.

- [ ] **Step 1: Get the Shoptet API token from user secrets**

```bash
dotnet user-secrets list --project backend/src/Anela.Heblo.API/ | grep "Shoptet:ApiToken"
```

Save the token value as `SHOPTET_TOKEN` in your shell:

```bash
export SHOPTET_TOKEN="<value from above>"
```

- [ ] **Step 2: Probe GET /api/shipments/order/{code}/shipping-options**

Use an order code that is in packing state (status 26). Pick any real order code from the staging environment. If none is available, use a known order from the store and note the response.

```bash
export ORDER_CODE="<real order code in packing state>"
curl -s -H "Shoptet-Private-API-Token: $SHOPTET_TOKEN" \
  "https://api.myshoptet.com/api/shipments/order/${ORDER_CODE}/shipping-options" \
  | python3 -c "import json,sys; print(json.dumps(json.load(sys.stdin), indent=2, ensure_ascii=False))"
```

Note the full response shape: the top-level key inside `data`, the array field name, and the fields on each option (especially the carrier identifier field name — it may be `carrierId`, `carrierCode`, `shippingGuid`, or similar).

- [ ] **Step 3: Probe POST /api/shipments**

Use a real order code. The exact required fields are unknown; start with the minimal shape inferred from the GET response and iterate if 422 is returned.

```bash
curl -s -X POST \
  -H "Shoptet-Private-API-Token: $SHOPTET_TOKEN" \
  -H "Content-Type: application/json" \
  "https://api.myshoptet.com/api/shipments" \
  -d "{
    \"orderCode\": \"${ORDER_CODE}\",
    \"packages\": [{
      \"width\": 300,
      \"height\": 200,
      \"depth\": 150,
      \"weight\": 0.5
    }]
  }" \
  | python3 -c "import json,sys; print(json.dumps(json.load(sys.stdin), indent=2, ensure_ascii=False))"
```

If 422, inspect the error messages to find the missing fields (e.g., carrier code field, orderCode vs orderId).
Probe with variations until 200/201 is returned. Record the successful request body.

- [ ] **Step 4: Check weight unit**

From the successful POST body and the GET shipments response: is `weight` in kg (decimal, e.g. `0.5`) or grams (integer, e.g. `500`)? The GET response example in the docs shows `"weight": 0.5` — confirm this is kg.

- [ ] **Step 5: Observe requested → created latency**

After the POST, poll `GET /api/shipments?orderCode=${ORDER_CODE}` every 2 seconds for up to 30 seconds. Record how long until the package `labelUrl` becomes non-null. This validates the single-retry assumption.

```bash
for i in $(seq 1 15); do
  echo "--- Attempt $i ---";
  curl -s -H "Shoptet-Private-API-Token: $SHOPTET_TOKEN" \
    "https://api.myshoptet.com/api/shipments?orderCode=${ORDER_CODE}" \
    | python3 -c "import json,sys; d=json.load(sys.stdin); pkgs=[p for s in (d.get('data',{}).get('items') or []) for p in (s.get('packages') or [])]; print('labelUrl:', pkgs[0].get('labelUrl') if pkgs else 'no packages')";
  sleep 2;
done
```

- [ ] **Step 6: Update shoptet-api.md**

Add a new section `## 11.8 POST /api/shipments — Create Shipment` documenting:
- Exact request body (all required fields, field names, value types)
- Carrier identifier field name and how to obtain the value from the shipping-options response
- Weight unit (kg or grams)
- Response body shape (guid, status, packages)
- Observed `requested` → `created` latency (validates one-retry assumption)
- Any 422 error codes encountered

Also add `## 11.9 GET /api/shipments/order/{code}/shipping-options` documenting the full response shape.

- [ ] **Step 7: Commit docs update**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs: document POST /api/shipments and shipping-options probe findings"
```

---

## Task 2: Error Codes 2905–2909

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:309-320`

- [ ] **Step 1: Add the five new error codes after `ShipmentLabelPdfNotFound = 2904`**

```csharp
    // ShipmentLabels module errors (2902–29XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShipmentLabelsNoShipmentFound = 2902,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentLabelsNotGenerated = 2903,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShipmentLabelPdfNotFound = 2904,

    [HttpStatusCode(HttpStatusCode.Conflict)]
    ShipmentAlreadyExists = 2905,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentCarrierNotResolved = 2906,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShipmentCreationFailed = 2907,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentLabelNotReady = 2908,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentOrderWeightUnavailable = 2909,
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -q
```

Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(shipment-labels): add error codes 2905-2909 for shipment creation"
```

---

## Task 3: ShipmentLabelsSettings + ShipmentCreation.cs Application Models

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsSettings.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentCreation.cs`

- [ ] **Step 1: Create ShipmentLabelsSettings.cs**

```csharp
namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShipmentLabelsSettings
{
    public const string ConfigurationKey = "ShipmentLabels";

    /// <summary>Default box width in millimetres. Configure per environment in user secrets.</summary>
    public int DefaultPackageWidthMm { get; set; } = 300;

    /// <summary>Default box height in millimetres.</summary>
    public int DefaultPackageHeightMm { get; set; } = 200;

    /// <summary>Default box depth in millimetres.</summary>
    public int DefaultPackageDepthMm { get; set; } = 150;

    /// <summary>
    /// Fallback item weight in grams when the catalog has no GrossWeight or NetWeight.
    /// Logged as a warning each time it is applied.
    /// </summary>
    public int DefaultItemWeightGrams { get; set; } = 500;

    /// <summary>Minimum total package weight in grams (floor applied after summing items).</summary>
    public int MinPackageWeightGrams { get; set; } = 100;
}
```

- [ ] **Step 2: Create ShipmentCreation.cs (application-layer models)**

```csharp
namespace Anela.Heblo.Application.Features.ShipmentLabels;

/// <summary>One carrier option returned by the shipping-options endpoint.</summary>
public class ShippingOption
{
    public string CarrierCode { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Input passed to IShipmentClient.CreateShipmentAsync.</summary>
public class CreateShipmentCommand
{
    public string OrderCode { get; set; } = null!;
    public string CarrierCode { get; set; } = null!;
    public ShipmentPackage Package { get; set; } = null!;
}

public class ShipmentPackage
{
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int DepthMm { get; set; }
    public int WeightGrams { get; set; }
}

/// <summary>Minimal result returned by IShipmentClient.CreateShipmentAsync.</summary>
public class CreatedShipment
{
    public Guid ShipmentGuid { get; set; }
    public string? Status { get; set; }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -q
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsSettings.cs \
        backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentCreation.cs
git commit -m "feat(shipment-labels): add ShipmentLabelsSettings and creation domain models"
```

---

## Task 4: Extend IShipmentClient and Add WeightGrams to PackingOrderItem

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`

- [ ] **Step 1: Extend IShipmentClient with two new methods**

Replace the full file content:

```csharp
namespace Anela.Heblo.Application.Features.ShipmentLabels;

public interface IShipmentClient
{
    Task<IReadOnlyList<ShipmentLabel>> GetLabelsByOrderCodeAsync(string orderCode, CancellationToken ct = default);

    Task<IReadOnlyList<ShippingOption>> GetShippingOptionsAsync(string orderCode, CancellationToken ct = default);

    Task<CreatedShipment> CreateShipmentAsync(CreateShipmentCommand command, CancellationToken ct = default);
}
```

- [ ] **Step 2: Add WeightGrams to PackingOrderItem**

In `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`, add one property to `PackingOrderItem`:

```csharp
/// <summary>
/// Item gross weight in grams (GrossWeight ?? NetWeight from catalog).
/// Falls back to DefaultItemWeightGrams when catalog data is absent — a warning is logged.
/// </summary>
public int WeightGrams { get; set; }
```

- [ ] **Step 3: Build (expects compile errors in ShoptetShipmentClient — not yet implemented)**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore -q 2>&1 | grep -E "error|Error"
```

Expected: errors only in `ShoptetShipmentClient.cs` about missing interface members. No errors elsewhere.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs \
        backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs
git commit -m "feat(shipment-labels): extend IShipmentClient; add WeightGrams to PackingOrderItem"
```

---

## Task 5: Adapter DTOs for Shipment Creation

> ⚠️ These DTOs depend on findings from Task 1. Update field names/types to match the probed API before implementing Task 6.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShippingOptionsResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetCreateShipmentRequest.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetCreateShipmentResponse.cs`

- [ ] **Step 1: Create ShoptetShippingOptionsResponse.cs**

Adjust the field names/structure to match what the probe in Task 1 revealed.

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetShippingOptionsResponse
{
    [JsonPropertyName("data")]
    public ShoptetShippingOptionsData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

internal class ShoptetShippingOptionsData
{
    // ⚠️ Update the JsonPropertyName based on Task 1 probe (may differ from "shippingOptions")
    [JsonPropertyName("shippingOptions")]
    public List<ShoptetShippingOptionDto>? ShippingOptions { get; set; }
}

internal class ShoptetShippingOptionDto
{
    // ⚠️ Update field name from Task 1 probe (may be "carrierId", "carrierCode", "guid", etc.)
    [JsonPropertyName("carrierId")]
    public string? CarrierId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
```

- [ ] **Step 2: Create ShoptetCreateShipmentRequest.cs**

Adjust fields to match the successful POST body found in Task 1.

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetCreateShipmentRequest
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;

    // ⚠️ Update field name from Task 1 probe (may be "carrier", "carrierId", "shippingOptionId")
    [JsonPropertyName("carrierId")]
    public string CarrierId { get; set; } = null!;

    [JsonPropertyName("packages")]
    public List<ShoptetCreatePackageDto> Packages { get; set; } = [];
}

internal class ShoptetCreatePackageDto
{
    // Dimensions in mm; weight in kg (converted from grams). Verify units from Task 1 probe.
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    // ⚠️ Verify weight unit (kg vs grams) from Task 1 probe.
    [JsonPropertyName("weight")]
    public double Weight { get; set; }
}
```

- [ ] **Step 3: Create ShoptetCreateShipmentResponse.cs**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetCreateShipmentResponse
{
    [JsonPropertyName("data")]
    public ShoptetCreateShipmentData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

internal class ShoptetCreateShipmentData
{
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj --no-restore -q
```

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/
git commit -m "feat(shipment-labels): add adapter DTOs for shipping options + shipment creation"
```

---

## Task 6: ShoptetShipmentClient — Implement New Methods (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`

The `FakeDelegatingHandler` lives in `ShoptetApiExpeditionListSourceTests.cs`. Both test files are in the same namespace, so it is accessible without import.

- [ ] **Step 1: Write failing tests for GetShippingOptionsAsync**

Add to `ShoptetShipmentClientTests.cs` (append to the class):

```csharp
[Fact]
public async Task GetShippingOptionsAsync_WithOptions_ReturnsMappedList()
{
    // Arrange — update "carrierId" / "shippingOptions" to match Task 1 probe findings
    var client = BuildClient(_ => Json(new
    {
        data = new
        {
            shippingOptions = new[]
            {
                new { carrierId = "ppl_parcel", name = "PPL" },
                new { carrierId = "zasilkovna", name = "Zásilkovna" },
            }
        },
        errors = Array.Empty<object>(),
    }));

    // Act
    var result = await client.GetShippingOptionsAsync("0001234");

    // Assert
    result.Should().HaveCount(2);
    result[0].CarrierCode.Should().Be("ppl_parcel");
    result[0].Name.Should().Be("PPL");
}

[Fact]
public async Task GetShippingOptionsAsync_WithEmptyOptions_ReturnsEmptyList()
{
    var client = BuildClient(_ => Json(new
    {
        data = new { shippingOptions = Array.Empty<object>() },
        errors = Array.Empty<object>(),
    }));

    var result = await client.GetShippingOptionsAsync("0001234");

    result.Should().BeEmpty();
}

[Fact]
public async Task GetShippingOptionsAsync_WhenShoptetReturnsNonSuccess_ThrowsHttpRequestException()
{
    var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
    {
        Content = new StringContent("Service Unavailable", Encoding.UTF8, "text/plain"),
    });

    var act = () => client.GetShippingOptionsAsync("0001234");

    await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*503*");
}

[Fact]
public async Task CreateShipmentAsync_PostsCorrectBodyAndReturnsMappedResult()
{
    // Arrange
    var shipmentGuid = Guid.NewGuid();
    HttpRequestMessage? capturedRequest = null;

    var client = BuildClient(req =>
    {
        capturedRequest = req;
        return Json(new
        {
            data = new { guid = shipmentGuid, status = "requested" },
            errors = Array.Empty<object>(),
        });
    });

    var command = new Anela.Heblo.Application.Features.ShipmentLabels.CreateShipmentCommand
    {
        OrderCode = "0001234",
        CarrierCode = "ppl_parcel",
        Package = new Anela.Heblo.Application.Features.ShipmentLabels.ShipmentPackage
        {
            WidthMm = 300,
            HeightMm = 200,
            DepthMm = 150,
            WeightGrams = 500,
        }
    };

    // Act
    var result = await client.CreateShipmentAsync(command);

    // Assert
    result.ShipmentGuid.Should().Be(shipmentGuid);
    result.Status.Should().Be("requested");
    capturedRequest!.Method.Should().Be(HttpMethod.Post);
    capturedRequest.RequestUri!.PathAndQuery.Should().Be("/api/shipments");

    var body = await capturedRequest.Content!.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<JsonElement>(body);
    json.GetProperty("orderCode").GetString().Should().Be("0001234");
    // Verify weight is sent as kg (0.5 kg for 500g) — adjust if probe found grams
    json.GetProperty("packages")[0].GetProperty("weight").GetDouble().Should().BeApproximately(0.5, 0.001);
}

[Fact]
public async Task CreateShipmentAsync_WhenShoptetReturnsNonSuccess_ThrowsHttpRequestException()
{
    var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    });

    var command = new Anela.Heblo.Application.Features.ShipmentLabels.CreateShipmentCommand
    {
        OrderCode = "0001234",
        CarrierCode = "ppl_parcel",
        Package = new Anela.Heblo.Application.Features.ShipmentLabels.ShipmentPackage
        { WidthMm = 300, HeightMm = 200, DepthMm = 150, WeightGrams = 500 }
    };

    var act = () => client.CreateShipmentAsync(command);

    await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*422*");
}
```

- [ ] **Step 2: Run tests to confirm they fail (methods not implemented yet)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetShipmentClientTests" --no-build -q 2>&1 | tail -10
```

Expected: test build fails (interface members unimplemented) or tests fail.

- [ ] **Step 3: Implement GetShippingOptionsAsync and CreateShipmentAsync in ShoptetShipmentClient.cs**

Append to the class (after `GetLabelsByOrderCodeAsync`):

```csharp
public async Task<IReadOnlyList<ShippingOption>> GetShippingOptionsAsync(
    string orderCode,
    CancellationToken ct = default)
{
    var encodedOrderCode = Uri.EscapeDataString(orderCode);
    var response = await _http.GetAsync(
        $"/api/shipments/order/{encodedOrderCode}/shipping-options", ct);

    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"GET shipping-options for order {orderCode} returned {(int)response.StatusCode}: {body}");
    }

    var data = await response.Content.ReadFromJsonAsync<ShoptetShippingOptionsResponse>(JsonOptions, ct);

    if (data?.Errors is { Count: > 0 })
    {
        var errorMsg = string.Join("; ", data.Errors.Select(e => e.Message));
        throw new HttpRequestException($"Shoptet Delivery API error for order {orderCode}: {errorMsg}");
    }

    // ⚠️ Update "ShippingOptions" property accessor to match the probed field name from Task 1
    var options = data?.Data?.ShippingOptions ?? [];

    return options
        .Where(o => o.CarrierId is not null)
        .Select(o => new ShippingOption
        {
            CarrierCode = o.CarrierId!,
            Name = o.Name ?? string.Empty,
        })
        .ToList();
}

public async Task<CreatedShipment> CreateShipmentAsync(
    CreateShipmentCommand command,
    CancellationToken ct = default)
{
    // ⚠️ Verify weight unit from Task 1 probe. Currently assumes kg (divide grams by 1000).
    var requestBody = new ShoptetCreateShipmentRequest
    {
        OrderCode = command.OrderCode,
        CarrierId = command.CarrierCode,
        Packages =
        [
            new ShoptetCreatePackageDto
            {
                Width = command.Package.WidthMm,
                Height = command.Package.HeightMm,
                Depth = command.Package.DepthMm,
                Weight = command.Package.WeightGrams / 1000.0,
            }
        ],
    };

    var response = await _http.PostAsJsonAsync("/api/shipments", requestBody, JsonOptions, ct);

    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"POST /api/shipments for order {command.OrderCode} returned {(int)response.StatusCode}: {body}");
    }

    var data = await response.Content.ReadFromJsonAsync<ShoptetCreateShipmentResponse>(JsonOptions, ct);

    if (data?.Errors is { Count: > 0 })
    {
        var errorMsg = string.Join("; ", data.Errors.Select(e => e.Message));
        throw new HttpRequestException(
            $"Shoptet Delivery API error creating shipment for order {command.OrderCode}: {errorMsg}");
    }

    return new CreatedShipment
    {
        ShipmentGuid = data?.Data?.Guid ?? Guid.Empty,
        Status = data?.Data?.Status,
    };
}
```

Also add the missing `using` directive at the top of `ShoptetShipmentClient.cs`:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;
using Anela.Heblo.Application.Features.ShipmentLabels;
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetShipmentClientTests" --no-build -q 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs
git commit -m "feat(shipment-labels): implement GetShippingOptionsAsync + CreateShipmentAsync in ShoptetShipmentClient"
```

---

## Task 7: Populate WeightGrams in ShoptetApiPackingOrderClient (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`

- [ ] **Step 1: Write a failing test that asserts WeightGrams is populated**

Read `ShoptetApiPackingOrderClientTests.cs` to find the existing test helper that builds a client (uses `BuildOrderClient` + `FakeDelegatingHandler`). Append these tests:

```csharp
[Fact]
public async Task GetPackingOrderAsync_PopulatesWeightGramsFromCatalogGrossWeight()
{
    // Arrange
    var catalogMock = new Mock<ICatalogRepository>();
    var coolingMock = new Mock<ICarrierCoolingRepository>();

    var catalog = new Dictionary<string, CatalogAggregate>
    {
        ["PROD001"] = new CatalogAggregate { GrossWeight = 350.0, NetWeight = 300.0 },
    };
    catalogMock.Setup(c => c.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(catalog.AsReadOnly() as IReadOnlyDictionary<string, CatalogAggregate>
            ?? new Dictionary<string, CatalogAggregate>(catalog));

    coolingMock.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<Anela.Heblo.Domain.Features.Logistics.CarrierCoolingSetting>());

    // Build an order client with a fake HTTP handler that returns a minimal order detail
    var orderClient = BuildOrderClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.Contains("/status"))
            return Json(new { data = new { order = new { status = new { id = 26 } } } });
        return Json(new
        {
            data = new
            {
                order = new
                {
                    code = "0001234",
                    fullName = "Test User",
                    shipping = new { guid = PplDoRukyGuid, name = "PPL" },
                    items = new[] { new { productCode = "PROD001", name = "Produkt", amount = 2.0 } },
                }
            }
        });
    });

    var client = new ShoptetApiPackingOrderClient(orderClient, catalogMock.Object, coolingMock.Object);

    // Act
    var result = await client.GetPackingOrderAsync("0001234");

    // Assert
    result.Should().NotBeNull();
    result!.Items.Should().HaveCount(1);
    result.Items[0].WeightGrams.Should().Be(350); // GrossWeight (350g) preferred over NetWeight
}

[Fact]
public async Task GetPackingOrderAsync_FallsBackToNetWeightWhenGrossWeightIsNull()
{
    // Arrange
    var catalogMock = new Mock<ICatalogRepository>();
    var coolingMock = new Mock<ICarrierCoolingRepository>();
    var catalog = new Dictionary<string, CatalogAggregate>
    {
        ["PROD001"] = new CatalogAggregate { GrossWeight = null, NetWeight = 250.0 },
    };
    catalogMock.Setup(c => c.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(catalog as IReadOnlyDictionary<string, CatalogAggregate>
            ?? new Dictionary<string, CatalogAggregate>(catalog));
    coolingMock.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<Anela.Heblo.Domain.Features.Logistics.CarrierCoolingSetting>());

    var orderClient = BuildOrderClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.Contains("/status"))
            return Json(new { data = new { order = new { status = new { id = 26 } } } });
        return Json(new { data = new { order = new { code = "0001234", fullName = "Test", shipping = new { guid = PplDoRukyGuid, name = "PPL" }, items = new[] { new { productCode = "PROD001", name = "P", amount = 1.0 } } } } });
    });

    var client = new ShoptetApiPackingOrderClient(orderClient, catalogMock.Object, coolingMock.Object);

    var result = await client.GetPackingOrderAsync("0001234");

    result!.Items[0].WeightGrams.Should().Be(250);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiPackingOrderClientTests" --no-build -q 2>&1 | tail -10
```

Expected: tests fail because `WeightGrams` is always 0.

- [ ] **Step 3: Add a logger field and WeightGrams population to ShoptetApiPackingOrderClient**

The constructor currently takes `IEshopOrderClient`, `ICatalogRepository`, `ICarrierCoolingRepository`. Add `ILogger<ShoptetApiPackingOrderClient>` as a fourth parameter and inject it.

In `GetPackingOrderAsync`, extend the section that builds `coolingByCode` to also build a `weightByCode` map:

```csharp
// Add logger field at class level:
private readonly ILogger<ShoptetApiPackingOrderClient> _logger;

// Add to constructor parameters and body:
ILogger<ShoptetApiPackingOrderClient> logger
// ...
_logger = logger;

// In GetPackingOrderAsync, after building coolingByCode:
var weightByCode = catalogItems.ToDictionary(
    kv => kv.Key,
    kv => (int?)(kv.Value.GrossWeight ?? kv.Value.NetWeight));

// When building PackingOrderItem, replace:
//   ImageUrl = catalogItems.TryGetValue(i.ProductCode, out var c) ? c.Image : null,
// with:
var catalogItem = catalogItems.TryGetValue(i.ProductCode, out var c) ? c : null;
var weightGrams = weightByCode.TryGetValue(i.ProductCode, out var w) ? w : null;
if (weightGrams is null)
{
    _logger.LogWarning(
        "Product {ProductCode} has no weight in catalog; using DefaultItemWeightGrams",
        i.ProductCode);
}

// In PackingOrderItem initializer:
ImageUrl = catalogItem?.Image,
WeightGrams = (int)(weightGrams ?? DefaultItemWeightGrams),
```

Since `DefaultItemWeightGrams` is in `ShipmentLabelsSettings`, the adapter needs to inject `IOptions<ShipmentLabelsSettings>`. However, this creates a cross-layer dependency (adapter depends on application settings). The cleaner approach: extract the default as a constructor parameter — but that changes the DI registration.

**Simpler approach (avoid cross-layer coupling):** Add a `DefaultItemWeightGrams` property with a hardcoded default directly on `ShoptetApiPackingOrderClient` and make it configurable via `ShoptetApiSettings`:

Add to `ShoptetApiSettings.cs`:
```csharp
/// <summary>
/// Fallback weight in grams for catalog items without GrossWeight or NetWeight.
/// Used by the packing order client for shipment weight calculation.
/// </summary>
public int DefaultItemWeightGrams { get; set; } = 500;
```

Inject `IOptions<ShoptetApiSettings>` into `ShoptetApiPackingOrderClient`:
```csharp
private readonly int _defaultItemWeightGrams;

// In constructor, add IOptions<ShoptetApiSettings> settings parameter:
_defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
```

Then use `_defaultItemWeightGrams` in the weight fallback.

Full updated `ShoptetApiPackingOrderClient.cs`:

```csharp
using System.Net;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetApiPackingOrderClient : IPackingOrderClient
{
    private readonly ShoptetOrderClient _orderClient;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;
    private readonly ILogger<ShoptetApiPackingOrderClient> _logger;
    private readonly int _defaultItemWeightGrams;

    public ShoptetApiPackingOrderClient(
        IEshopOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings)
    {
        _orderClient = orderClient as ShoptetOrderClient
            ?? throw new InvalidOperationException(
                $"{nameof(IEshopOrderClient)} must be {nameof(ShoptetOrderClient)} " +
                $"but got {orderClient.GetType().Name}.");
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
    }

    public async Task<PackingOrder?> GetPackingOrderAsync(string code, CancellationToken ct = default)
    {
        ExpeditionOrderDetail detail;
        try
        {
            detail = await _orderClient.GetExpeditionOrderDetailAsync(code, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var statusId = await _orderClient.GetOrderStatusIdAsync(code, ct);
        var order = ShoptetApiExpeditionListSource.MapToExpeditionOrder(detail);

        var settings2 = await _carrierCooling.GetAllAsync(ct);
        var matrix = settings2.ToDictionary(s => (s.Carrier, s.DeliveryHandling), s => s.Cooling);
        order.CarrierCooling = ShoptetApiExpeditionListSource.ResolveCarrierCooling(
            detail.Shipping?.Guid ?? string.Empty, matrix);

        var productCodes = order.Items.Select(i => i.ProductCode).Distinct().ToList();
        var catalogItems = await _catalog.GetByIdsAsync(productCodes, ct);
        var coolingByCode = catalogItems.ToDictionary(kv => kv.Key, kv => kv.Value.Properties.Cooling);
        var weightByCode = catalogItems.ToDictionary(
            kv => kv.Key,
            kv => (int?)(kv.Value.GrossWeight.HasValue ? (int)kv.Value.GrossWeight.Value
                       : kv.Value.NetWeight.HasValue ? (int)kv.Value.NetWeight.Value
                       : (int?)null));

        ShoptetApiExpeditionListSource.ApplyEnrichment(
            order.Items,
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            coolingByCode);

        var items = order.Items.Select(i =>
        {
            var catalogItem = catalogItems.TryGetValue(i.ProductCode, out var c) ? c : null;
            if (!weightByCode.TryGetValue(i.ProductCode, out var w) || w is null)
            {
                _logger.LogWarning(
                    "Product {ProductCode} has no weight in catalog; using default {Default}g",
                    i.ProductCode, _defaultItemWeightGrams);
            }

            return new PackingOrderItem
            {
                Name = i.Name,
                Quantity = i.Quantity,
                ImageUrl = catalogItem?.Image,
                SetName = i.IsFromSet ? i.SetName : null,
                WeightGrams = w ?? _defaultItemWeightGrams,
            };
        }).ToList();

        return new PackingOrder
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = detail.Shipping?.Name ?? string.Empty,
            Cooling = order.CarrierCooling,
            IsCooled = order.IsCooled,
            StatusId = statusId,
            CustomerNote = string.IsNullOrWhiteSpace(order.CustomerRemark) ? null : order.CustomerRemark,
            EshopNote = string.IsNullOrWhiteSpace(order.EshopRemark) ? null : order.EshopRemark,
            Items = items,
        };
    }
}
```

- [ ] **Step 4: Update DI registration for ShoptetApiPackingOrderClient**

Find where `ShoptetApiPackingOrderClient` is registered (search `Adapters/Anela.Heblo.Adapters.ShoptetApi/` for its registration). The DI container will automatically inject the new parameters — no manual change needed as long as `IOptions<ShoptetApiSettings>` and `ILogger<ShoptetApiPackingOrderClient>` are already registered (they are: Options and Logging are registered globally).

- [ ] **Step 5: Run tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiPackingOrderClientTests" --no-build -q 2>&1 | tail -10
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
git commit -m "feat(shipment-labels): populate WeightGrams in ShoptetApiPackingOrderClient"
```

---

## Task 8: CreateOrderShipment Use Case — Validator + Handler (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Validators/CreateOrderShipmentRequestValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/CreateOrderShipmentRequestValidatorTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/CreateOrderShipmentHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentHandler.cs`

- [ ] **Step 1: Create CreateOrderShipmentRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;

public class CreateOrderShipmentRequest : IRequest<CreateOrderShipmentResponse>
{
    public string OrderCode { get; set; } = null!;
    public bool ForceCreate { get; set; }
}
```

- [ ] **Step 2: Create CreateOrderShipmentResponse.cs**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;

public class CreateOrderShipmentResponse : BaseResponse
{
    public Guid? ShipmentGuid { get; set; }
    public string? Status { get; set; }
    public bool LabelReady { get; set; }
    public IReadOnlyList<ShipmentLabelDto> Labels { get; set; } = [];
    public bool ExistingShipmentFound { get; set; }

    public CreateOrderShipmentResponse(
        Guid shipmentGuid,
        string? status,
        bool labelReady,
        IReadOnlyList<ShipmentLabelDto> labels)
    {
        ShipmentGuid = shipmentGuid;
        Status = status;
        LabelReady = labelReady;
        Labels = labels;
    }

    public CreateOrderShipmentResponse(
        ErrorCodes errorCode,
        IReadOnlyList<ShipmentLabelDto>? existingLabels = null,
        bool existingShipmentFound = false)
        : base(errorCode)
    {
        Labels = existingLabels ?? [];
        ExistingShipmentFound = existingShipmentFound;
    }
}
```

- [ ] **Step 3: Create CreateOrderShipmentRequestValidator.cs**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using FluentValidation;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Validators;

public class CreateOrderShipmentRequestValidator : AbstractValidator<CreateOrderShipmentRequest>
{
    public CreateOrderShipmentRequestValidator()
    {
        RuleFor(x => x.OrderCode)
            .NotEmpty()
            .WithMessage("OrderCode is required.");
    }
}
```

- [ ] **Step 4: Write failing validator tests**

Create `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/CreateOrderShipmentRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class CreateOrderShipmentRequestValidatorTests
{
    private readonly CreateOrderShipmentRequestValidator _validator = new();

    [Fact]
    public async Task Validate_WithValidOrderCode_IsValid()
    {
        var result = await _validator.ValidateAsync(
            new CreateOrderShipmentRequest { OrderCode = "0001234" });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Validate_WithEmptyOrNullOrderCode_IsInvalid(string? orderCode)
    {
        var result = await _validator.ValidateAsync(
            new CreateOrderShipmentRequest { OrderCode = orderCode! });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrderCode");
    }
}
```

- [ ] **Step 5: Write failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/CreateOrderShipmentHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class CreateOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClientMock = new();
    private readonly Mock<IPackingOrderClient> _orderClientMock = new();
    private static readonly ShipmentLabelsSettings DefaultSettings = new()
    {
        DefaultPackageWidthMm = 300,
        DefaultPackageHeightMm = 200,
        DefaultPackageDepthMm = 150,
        DefaultItemWeightGrams = 500,
        MinPackageWeightGrams = 100,
    };

    private CreateOrderShipmentHandler CreateHandler(ShipmentLabelsSettings? settings = null) =>
        new(
            _shipmentClientMock.Object,
            _orderClientMock.Object,
            Options.Create(settings ?? DefaultSettings),
            NullLogger<CreateOrderShipmentHandler>.Instance);

    private static PackingOrder PackingOrderWith(params (string code, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            Items = items.Select(i => new PackingOrderItem
            {
                Name = i.code,
                Quantity = i.qty,
                WeightGrams = i.weightGrams,
            }).ToList(),
        };

    [Fact]
    public async Task Handle_HappyPath_CreatesShipmentAndReturnsReadyLabel()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        var label = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = "https://example.com/label.pdf",
        };

        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])                          // duplicate guard → no existing shipment
            .ReturnsAsync([label]);                    // label fetch after create → ready

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 2, 300)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "ppl_parcel", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid, Status = "requested" });

        // Act
        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234", ForceCreate = false },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.LabelReady.Should().BeTrue();
        response.Labels.Should().HaveCount(1);
        response.Labels[0].LabelUrl.Should().Be("https://example.com/label.pdf");
        response.ExistingShipmentFound.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LabelNotReadyAfterCreate_RetryAndStillNotReady_ReturnsLabelReadyFalse()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        var labelNotReady = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = null,
            LabelZpl = null,
        };

        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])               // duplicate guard
            .ReturnsAsync([labelNotReady])  // first fetch: not ready
            .ReturnsAsync([labelNotReady]); // retry: still not ready

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 500)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "ppl_parcel", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid, Status = "requested" });

        // Act
        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.LabelReady.Should().BeFalse();
        response.Labels.Should().HaveCount(1);

        // Verify retry happened (3 calls total: 1 guard + 1 first fetch + 1 retry)
        _shipmentClientMock.Verify(
            c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_ExistingShipmentWithoutForceCreate_ReturnsAlreadyExistsWithLabels()
    {
        // Arrange
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = "https://existing.com/label.pdf",
        };

        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        // Act
        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234", ForceCreate = false },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentAlreadyExists);
        response.ExistingShipmentFound.Should().BeTrue();
        response.Labels.Should().HaveCount(1);
        response.Labels[0].LabelUrl.Should().Be("https://existing.com/label.pdf");

        // No create call made
        _shipmentClientMock.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingShipmentWithForceCreate_ProceedsToCreate()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://new.com/label.pdf" }]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 400)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "ppl_parcel", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid, Status = "requested" });

        // Act
        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234", ForceCreate = true },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _shipmentClientMock.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoCarrierOptions_ReturnsCarrierNotResolved()
    {
        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 400)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
    }

    [Fact]
    public async Task Handle_EmptyOrder_ReturnsWeightUnavailable()
    {
        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", Items = [] });

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable);
    }

    [Fact]
    public async Task Handle_WeightAppliesMinPackageFloor()
    {
        // Arrange: item weighs 10g × 1 = 10g, below MinPackageWeightGrams = 100g
        var shipmentGuid = Guid.NewGuid();
        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://x.com" }]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 10)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "ppl_parcel", Name = "PPL" }]);

        CreateShipmentCommand? capturedCommand = null;
        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => capturedCommand = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid, Status = "requested" });

        // Act
        await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        // Assert: weight was floored to MinPackageWeightGrams (100g)
        capturedCommand!.Package.WeightGrams.Should().Be(100);
    }

    [Fact]
    public async Task Handle_CreateShipmentThrows_ReturnsShipmentCreationFailed()
    {
        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 500)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "ppl_parcel", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet API unavailable"));

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
    }
}
```

- [ ] **Step 6: Run tests to confirm they fail (handler not yet created)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateOrderShipmentHandler|FullyQualifiedName~CreateOrderShipmentRequestValidator" \
  --no-build -q 2>&1 | tail -10
```

- [ ] **Step 7: Create CreateOrderShipmentHandler.cs**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;

public class CreateOrderShipmentHandler
    : IRequestHandler<CreateOrderShipmentRequest, CreateOrderShipmentResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly ShipmentLabelsSettings _settings;
    private readonly ILogger<CreateOrderShipmentHandler> _logger;

    public CreateOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IOptions<ShipmentLabelsSettings> settings,
        ILogger<CreateOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CreateOrderShipmentResponse> Handle(
        CreateOrderShipmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Duplicate guard
            var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                request.OrderCode, cancellationToken);

            if (existingLabels.Count > 0 && !request.ForceCreate)
            {
                var existingDtos = MapToDtos(existingLabels);
                return new CreateOrderShipmentResponse(
                    ErrorCodes.ShipmentAlreadyExists,
                    existingDtos,
                    existingShipmentFound: true);
            }

            // 2. Load order and compute weight
            var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, cancellationToken);
            if (order is null || order.Items.Count == 0)
            {
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentOrderWeightUnavailable);
            }

            var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
            if (totalWeightGrams == 0)
            {
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentOrderWeightUnavailable);
            }

            var packageWeightGrams = Math.Max(totalWeightGrams, _settings.MinPackageWeightGrams);

            // 3. Resolve carrier
            var shippingOptions = await _shipmentClient.GetShippingOptionsAsync(
                request.OrderCode, cancellationToken);

            if (shippingOptions.Count == 0)
            {
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentCarrierNotResolved);
            }

            // 4. Create shipment
            var command = new CreateShipmentCommand
            {
                OrderCode = request.OrderCode,
                CarrierCode = shippingOptions[0].CarrierCode,
                Package = new ShipmentPackage
                {
                    WidthMm = _settings.DefaultPackageWidthMm,
                    HeightMm = _settings.DefaultPackageHeightMm,
                    DepthMm = _settings.DefaultPackageDepthMm,
                    WeightGrams = packageWeightGrams,
                }
            };

            CreatedShipment created;
            try
            {
                created = await _shipmentClient.CreateShipmentAsync(command, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Shoptet API failed to create shipment for order {OrderCode}", request.OrderCode);
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentCreationFailed);
            }

            // 5. Fetch label with one retry
            var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                request.OrderCode, cancellationToken);
            var labelReady = labels.Any(l => l.LabelUrl is not null || l.LabelZpl is not null);

            if (!labelReady)
            {
                await Task.Delay(3000, cancellationToken);
                labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                    request.OrderCode, cancellationToken);
                labelReady = labels.Any(l => l.LabelUrl is not null || l.LabelZpl is not null);
            }

            return new CreateOrderShipmentResponse(
                created.ShipmentGuid,
                created.Status,
                labelReady,
                MapToDtos(labels));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error creating shipment for order {OrderCode}", request.OrderCode);
            return new CreateOrderShipmentResponse(ErrorCodes.InternalServerError);
        }
    }

    private static IReadOnlyList<ShipmentLabelDto> MapToDtos(IReadOnlyList<ShipmentLabel> labels) =>
        labels.Select(l => new ShipmentLabelDto
        {
            ShipmentGuid = l.ShipmentGuid,
            PackageName = l.PackageName,
            LabelUrl = l.LabelUrl,
            LabelZpl = l.LabelZpl,
            TrackingNumber = l.TrackingNumber,
            TrackingUrl = l.TrackingUrl,
        }).ToList();
}
```

- [ ] **Step 8: Run all new tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateOrderShipment" --no-build -q 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/CreateOrderShipment/ \
        backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Validators/CreateOrderShipmentRequestValidator.cs \
        backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/
git commit -m "feat(shipment-labels): CreateOrderShipment handler, validator, and unit tests"
```

---

## Task 9: Module + Controller (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`
- Create: `backend/test/Anela.Heblo.Tests/Controllers/ShipmentLabelsControllerTests.cs`

- [ ] **Step 1: Write failing integration tests first**

Create `backend/test/Anela.Heblo.Tests/Controllers/ShipmentLabelsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class ShipmentLabelsControllerTests : IClassFixture<HebloWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;

    public ShipmentLabelsControllerTests(HebloWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateShipment_WithoutAuthentication_Returns401()
    {
        // HebloWebApplicationFactory enables mock auth; use a client with NO auth header
        var unauthClient = new HttpClient { BaseAddress = _client.BaseAddress };
        var response = await unauthClient.PostAsJsonAsync(
            "/api/shipment-labels/create",
            new { orderCode = "0001234", forceCreate = false });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateShipment_WithEmptyOrderCode_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/shipment-labels/create",
            new { orderCode = "", forceCreate = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

Note: The test for a valid creation (200) requires a stubbed `IShipmentClient` and `IPackingOrderClient`. Since the integration test factory uses the real adapter (which hits Shoptet), keep integration tests minimal — validation and auth are sufficient. Full handler behavior is covered by unit tests in Task 8.

- [ ] **Step 2: Run tests to confirm they fail (endpoint not yet added)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShipmentLabelsControllerTests" --no-build -q 2>&1 | tail -10
```

- [ ] **Step 3: Update ShipmentLabelsModule.cs**

Replace the full file:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ShipmentLabels;

public static class ShipmentLabelsModule
{
    public static IServiceCollection AddShipmentLabelsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ShipmentLabelsSettings>(
            configuration.GetSection(ShipmentLabelsSettings.ConfigurationKey));

        services.AddScoped<IValidator<GetOrderShipmentLabelsRequest>, GetOrderShipmentLabelsRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>,
            ValidationBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>>();

        services.AddScoped<IValidator<CreateOrderShipmentRequest>, CreateOrderShipmentRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<CreateOrderShipmentRequest, CreateOrderShipmentResponse>,
            ValidationBehavior<CreateOrderShipmentRequest, CreateOrderShipmentResponse>>();

        return services;
    }
}
```

- [ ] **Step 4: Update ApplicationModule.cs to pass configuration**

Find the line `services.AddShipmentLabelsModule();` and change it to:

```csharp
services.AddShipmentLabelsModule(configuration);
```

- [ ] **Step 5: Add POST endpoint to ShipmentLabelsController.cs**

Append to the controller class (before the closing `}`) and add a new request class after the existing `GetShipmentLabelsRequest` class:

```csharp
    /// <summary>
    /// Creates a Shoptet shipment for the order on demand and returns the label(s).
    /// If a shipment already exists, returns 409 with existing labels attached.
    /// If the carrier has not generated the label yet after one retry, returns labelReady=false.
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<CreateOrderShipmentResponse>> CreateShipment(
        [FromBody] CreateShipmentRequest body,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new CreateOrderShipmentRequest
        {
            OrderCode = body.OrderCode,
            ForceCreate = body.ForceCreate,
        }, cancellationToken);

        return HandleResponse(response);
    }
```

Add the using directive and the request class at the bottom of the file:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
```

```csharp
public class CreateShipmentRequest
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;

    [JsonPropertyName("forceCreate")]
    public bool ForceCreate { get; set; }
}
```

- [ ] **Step 6: Run integration tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShipmentLabelsControllerTests" --no-build -q 2>&1 | tail -10
```

- [ ] **Step 7: Full BE build + format + test**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore -q
dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | head -20
dotnet test backend/Anela.Heblo.sln --no-build -q 2>&1 | tail -20
```

Expected: build succeeds, no format issues, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs \
        backend/test/Anela.Heblo.Tests/Controllers/ShipmentLabelsControllerTests.cs
git commit -m "feat(shipment-labels): add POST /api/shipment-labels/create endpoint and wire module"
```

---

## Task 10: Frontend — useCreateShipment Hook (TDD)

**Prerequisite:** The BE must be built first so the OpenAPI TypeScript client is regenerated.
Run `npm run build` in `frontend/` before this task to regenerate `src/api/generated/api-client.ts`
with `CreateOrderShipmentResponse`, `ErrorCodes.ShipmentAlreadyExists`, etc.

**Files:**
- Create: `frontend/src/api/hooks/useCreateShipment.ts`
- Create: `frontend/src/api/hooks/__tests__/useCreateShipment.test.ts`

- [ ] **Step 1: Regenerate the TypeScript API client**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Confirm `shipmentLabels_CreateShipment` appears in `src/api/generated/api-client.ts`.

```bash
grep -c "shipmentLabels_CreateShipment\|ShipmentAlreadyExists" frontend/src/api/generated/api-client.ts
```

Expected: count > 0.

- [ ] **Step 2: Write failing tests for useCreateShipment**

Create `frontend/src/api/hooks/__tests__/useCreateShipment.test.ts`:

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { useCreateShipment } from '../useCreateShipment';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: { fetch: mockFetch },
};

(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);

function json(body: object, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('useCreateShipment', () => {
  beforeEach(() => {
    mockFetch.mockReset();
  });

  it('returns label data on success', async () => {
    mockFetch.mockResolvedValueOnce(
      json({
        success: true,
        shipmentGuid: 'abc-guid',
        labelReady: true,
        labels: [{ shipmentGuid: 'abc-guid', packageName: 'P1', labelUrl: 'https://x.com/label.pdf' }],
        existingShipmentFound: false,
      })
    );

    const { result } = renderHook(() => useCreateShipment());
    result.current.mutate({ orderCode: '0001234', forceCreate: false });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.labelReady).toBe(true);
    expect(result.current.data?.labels).toHaveLength(1);
    expect(result.current.data?.existingShipmentFound).toBe(false);
  });

  it('returns existingShipmentFound=true for ShipmentAlreadyExists (no throw)', async () => {
    mockFetch.mockResolvedValueOnce(
      json({
        success: false,
        errorCode: 'ShipmentAlreadyExists',
        labels: [{ shipmentGuid: 'old-guid', packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      }, 409)
    );

    const { result } = renderHook(() => useCreateShipment());
    result.current.mutate({ orderCode: '0001234', forceCreate: false });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.existingShipmentFound).toBe(true);
    expect(result.current.data?.labels).toHaveLength(1);
  });

  it('throws an error for ShipmentCarrierNotResolved', async () => {
    mockFetch.mockResolvedValueOnce(
      json({ success: false, errorCode: 'ShipmentCarrierNotResolved' }, 422)
    );

    const { result } = renderHook(() => useCreateShipment());
    result.current.mutate({ orderCode: '0001234', forceCreate: false });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect((result.current.error as Error).message).toBe(
      'Dopravce se nepodařilo určit pro tuto objednávku'
    );
  });

  it('throws a generic error for unknown error codes', async () => {
    mockFetch.mockResolvedValueOnce(
      json({ success: false, errorCode: 'SomeUnknownCode' }, 500)
    );

    const { result } = renderHook(() => useCreateShipment());
    result.current.mutate({ orderCode: '0001234', forceCreate: false });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect((result.current.error as Error).message).toBe('Zásilku se nepodařilo vytvořit');
  });
});
```

- [ ] **Step 3: Run tests to confirm they fail**

```bash
cd frontend && npx jest src/api/hooks/__tests__/useCreateShipment.test.ts --no-coverage 2>&1 | tail -15
```

- [ ] **Step 4: Create useCreateShipment.ts**

```typescript
import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { ErrorCodes, ShipmentLabelDto } from '../generated/api-client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

interface CreateShipmentInput {
  orderCode: string;
  forceCreate: boolean;
}

export interface CreateShipmentResult {
  shipmentGuid?: string;
  labelReady: boolean;
  labels: ShipmentLabelDto[];
  existingShipmentFound: boolean;
}

const MESSAGES: Partial<Record<string, string>> = {
  [ErrorCodes.ShipmentAlreadyExists]: 'Zásilka pro tuto objednávku již existuje',
  [ErrorCodes.ShipmentCarrierNotResolved]: 'Dopravce se nepodařilo určit pro tuto objednávku',
  [ErrorCodes.ShipmentCreationFailed]: 'Shoptet nemohl vytvořit zásilku — zkuste znovu',
  [ErrorCodes.ShipmentOrderWeightUnavailable]: 'Nelze zjistit hmotnost objednávky',
};

const GENERIC_ERROR = 'Zásilku se nepodařilo vytvořit';

const createShipment = async (input: CreateShipmentInput): Promise<CreateShipmentResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/shipment-labels/create`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ orderCode: input.orderCode, forceCreate: input.forceCreate }),
    }
  );

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (data.errorCode === ErrorCodes.ShipmentAlreadyExists) {
    return {
      labelReady: (data.labels?.length ?? 0) > 0,
      labels: data.labels ?? [],
      existingShipmentFound: true,
    };
  }

  if (!data.success) {
    const message = (data.errorCode && MESSAGES[data.errorCode as string]) ?? GENERIC_ERROR;
    throw new Error(message);
  }

  return {
    shipmentGuid: data.shipmentGuid as string | undefined,
    labelReady: (data.labelReady as boolean) ?? false,
    labels: (data.labels as ShipmentLabelDto[]) ?? [],
    existingShipmentFound: false,
  };
};

export const useCreateShipment = () =>
  useMutation<CreateShipmentResult, Error, CreateShipmentInput>({
    mutationFn: createShipment,
  });
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
cd frontend && npx jest src/api/hooks/__tests__/useCreateShipment.test.ts --no-coverage 2>&1 | tail -15
```

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useCreateShipment.ts \
        frontend/src/api/hooks/__tests__/useCreateShipment.test.ts
git commit -m "feat(shipment-labels): add useCreateShipment hook with 2905 non-throw handling"
```

---

## Task 11: Frontend — PackingShipmentCreator Component (TDD)

**Files:**
- Create: `frontend/src/components/baleni/PackingShipmentCreator.tsx`
- Create: `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`

- [ ] **Step 1: Write failing component tests**

Create `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import PackingShipmentCreator from '../PackingShipmentCreator';
import { useCreateShipment } from '../../../api/hooks/useCreateShipment';
import { useShipmentLabels } from '../../../api/hooks/useShipmentLabels';

jest.mock('../../../api/hooks/useCreateShipment', () => ({
  useCreateShipment: jest.fn(),
}));
jest.mock('../../../api/hooks/useShipmentLabels', () => ({
  useShipmentLabels: jest.fn(),
}));
jest.mock('../PackingLabelPrinter', () => ({
  __esModule: true,
  default: ({ orderCode }: { orderCode: string }) => (
    <div data-testid="packing-label-printer" data-order-code={orderCode} />
  ),
}));

const mockUseCreateShipment = useCreateShipment as jest.Mock;
const mockUseShipmentLabels = useShipmentLabels as jest.Mock;

const idleMutation = {
  mutate: jest.fn(),
  isPending: false,
  isSuccess: false,
  isError: false,
  data: undefined,
  error: null,
  reset: jest.fn(),
};

const idleLabels = { data: undefined, isLoading: false, isError: false, refetch: jest.fn() };

beforeEach(() => {
  jest.clearAllMocks();
  mockUseCreateShipment.mockReturnValue({ ...idleMutation });
  mockUseShipmentLabels.mockReturnValue({ ...idleLabels });
});

describe('PackingShipmentCreator', () => {
  it('shows Vytvořit zásilku button in idle state', () => {
    render(<PackingShipmentCreator orderCode="0001234" />);
    expect(screen.getByRole('button', { name: /Vytvořit zásilku/i })).toBeInTheDocument();
  });

  it('clicking Vytvořit zásilku calls mutate with forceCreate=false', () => {
    const mutate = jest.fn();
    mockUseCreateShipment.mockReturnValue({ ...idleMutation, mutate });
    render(<PackingShipmentCreator orderCode="0001234" />);

    fireEvent.click(screen.getByRole('button', { name: /Vytvořit zásilku/i }));

    expect(mutate).toHaveBeenCalledWith({ orderCode: '0001234', forceCreate: false });
  });

  it('shows spinner while creating', () => {
    mockUseCreateShipment.mockReturnValue({ ...idleMutation, isPending: true });
    render(<PackingShipmentCreator orderCode="0001234" />);
    expect(screen.getByTestId('shipment-creating-spinner')).toBeInTheDocument();
  });

  it('shows PackingLabelPrinter when label is ready', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ packageName: 'P1', labelUrl: 'https://x.com' }],
        existingShipmentFound: false,
      },
    });
    render(<PackingShipmentCreator orderCode="0001234" />);
    expect(screen.getByTestId('packing-label-printer')).toBeInTheDocument();
  });

  it('shows Zkusit znovu button when labelReady is false', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: { labelReady: false, labels: [], existingShipmentFound: false },
    });
    render(<PackingShipmentCreator orderCode="0001234" />);
    expect(screen.getByRole('button', { name: /Zkusit znovu/i })).toBeInTheDocument();
  });

  it('Zkusit znovu calls refetch on useShipmentLabels', () => {
    const refetch = jest.fn();
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: { labelReady: false, labels: [], existingShipmentFound: false },
    });
    mockUseShipmentLabels.mockReturnValue({ ...idleLabels, refetch });
    render(<PackingShipmentCreator orderCode="0001234" />);

    fireEvent.click(screen.getByRole('button', { name: /Zkusit znovu/i }));
    expect(refetch).toHaveBeenCalled();
  });

  it('shows existing shipment warning and reuse / create-new buttons', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      },
    });
    render(<PackingShipmentCreator orderCode="0001234" />);
    expect(screen.getByText(/Zásilka již existuje/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Použít existující/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Vytvořit novou/i })).toBeInTheDocument();
  });

  it('clicking Použít existující renders label printer with existing labels', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ shipmentGuid: 'g1', packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      },
    });
    render(<PackingShipmentCreator orderCode="0001234" />);
    fireEvent.click(screen.getByRole('button', { name: /Použít existující/i }));
    expect(screen.getByTestId('packing-label-printer')).toBeInTheDocument();
  });

  it('clicking Vytvořit novou calls mutate with forceCreate=true', () => {
    const mutate = jest.fn();
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        labelReady: true,
        labels: [{ packageName: 'P1', labelUrl: 'https://x.com/old.pdf' }],
        existingShipmentFound: true,
      },
      mutate,
    });
    render(<PackingShipmentCreator orderCode="0001234" />);
    fireEvent.click(screen.getByRole('button', { name: /Vytvořit novou/i }));
    expect(mutate).toHaveBeenCalledWith({ orderCode: '0001234', forceCreate: true });
  });

  it('shows error banner on mutation error', () => {
    mockUseCreateShipment.mockReturnValue({
      ...idleMutation,
      isError: true,
      error: new Error('Shoptet nemohl vytvořit zásilku — zkuste znovu'),
    });
    render(<PackingShipmentCreator orderCode="0001234" />);
    expect(screen.getByTestId('shipment-error-banner')).toBeInTheDocument();
    expect(screen.getByText(/Shoptet nemohl vytvořit zásilku/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd frontend && npx jest src/components/baleni/__tests__/PackingShipmentCreator.test.tsx --no-coverage 2>&1 | tail -15
```

- [ ] **Step 3: Create PackingShipmentCreator.tsx**

```tsx
import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useCreateShipment, CreateShipmentResult } from '../../api/hooks/useCreateShipment';
import { useShipmentLabels } from '../../api/hooks/useShipmentLabels';
import PackingLabelPrinter from './PackingLabelPrinter';
import type { ShipmentLabelDto } from '../../api/generated/api-client';

interface PackingShipmentCreatorProps {
  orderCode: string;
}

function PackingShipmentCreator({ orderCode }: PackingShipmentCreatorProps) {
  const mutation = useCreateShipment();
  const [reuseExisting, setReuseExisting] = useState(false);
  const [labelsForPrint, setLabelsForPrint] = useState<ShipmentLabelDto[] | null>(null);

  const labelsQuery = useShipmentLabels(
    reuseExisting || mutation.data?.labelReady === false ? orderCode : null,
    reuseExisting || mutation.data?.labelReady === false
  );

  const handleCreate = (forceCreate: boolean) => {
    setReuseExisting(false);
    setLabelsForPrint(null);
    mutation.mutate({ orderCode, forceCreate });
  };

  const handleUseExisting = (existingLabels: ShipmentLabelDto[]) => {
    setLabelsForPrint(existingLabels);
    setReuseExisting(true);
  };

  const handleRetry = () => {
    void labelsQuery.refetch();
  };

  // Show label printer when explicitly reusing existing labels
  if (reuseExisting && labelsForPrint) {
    return <PackingLabelPrinter orderCode={orderCode} />;
  }

  // Creating spinner
  if (mutation.isPending) {
    return (
      <div data-testid="shipment-creating-spinner" className="flex items-center gap-2 text-neutral-gray">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span>Vytvářím zásilku…</span>
      </div>
    );
  }

  // Error
  if (mutation.isError) {
    return (
      <div
        data-testid="shipment-error-banner"
        className="rounded border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700"
      >
        {mutation.error?.message ?? 'Zásilku se nepodařilo vytvořit'}
        <button
          className="ml-4 underline"
          onClick={() => mutation.reset()}
        >
          Zpět
        </button>
      </div>
    );
  }

  const result: CreateShipmentResult | undefined = mutation.data;

  // Existing shipment — ask packer to choose
  if (result?.existingShipmentFound) {
    return (
      <div className="flex flex-col gap-3">
        <p className="text-sm font-semibold text-amber-700">
          Zásilka již existuje pro tuto objednávku.
        </p>
        <div className="flex gap-3">
          <button
            className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
            onClick={() => handleUseExisting(result.labels)}
          >
            Použít existující
          </button>
          <button
            className="rounded-lg bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow active:scale-95"
            onClick={() => handleCreate(true)}
          >
            Vytvořit novou
          </button>
        </div>
      </div>
    );
  }

  // Label ready — render printer
  if (result?.labelReady && result.labels.length > 0) {
    return <PackingLabelPrinter orderCode={orderCode} />;
  }

  // Label not ready — retry button
  if (result && !result.labelReady) {
    return (
      <button
        className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
        onClick={handleRetry}
      >
        Zkusit znovu
      </button>
    );
  }

  // Idle — create button
  return (
    <button
      className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
      onClick={() => handleCreate(false)}
    >
      Vytvořit zásilku
    </button>
  );
}

export default PackingShipmentCreator;
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd frontend && npx jest src/components/baleni/__tests__/PackingShipmentCreator.test.tsx --no-coverage 2>&1 | tail -15
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/PackingShipmentCreator.tsx \
        frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx
git commit -m "feat(shipment-labels): add PackingShipmentCreator state-machine component"
```

---

## Task 12: Frontend — Wire PackingShipmentCreator into BaleniPacking + E2E Fixtures

**Files:**
- Modify: `frontend/src/components/baleni/BaleniPacking.tsx`
- Modify: `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`
- Modify: `frontend/test/e2e/fixtures/test-data.ts`
- Modify: `frontend/test/e2e/baleni/packing.spec.ts`

- [ ] **Step 1: Write failing BaleniPacking test first**

In `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`, update the mock at the top:

Replace:

```typescript
jest.mock('../PackingLabelPrinter', () => ({
  __esModule: true,
  default: ({ orderCode }: { orderCode: string }) => (
    <div data-testid="packing-label-printer" data-order-code={orderCode} />
  ),
}));
```

With:

```typescript
jest.mock('../PackingShipmentCreator', () => ({
  __esModule: true,
  default: ({ orderCode }: { orderCode: string }) => (
    <div data-testid="packing-shipment-creator" data-order-code={orderCode} />
  ),
}));
```

Update the two tests that reference `packing-label-printer`:

```typescript
it('mounts PackingShipmentCreator when order is in packing state', () => {
  mockHook.mockReturnValue({
    ...baseResult,
    data: {
      code: '250001',
      customerName: 'Jan Novák',
      shippingMethodName: 'PPL',
      cooling: 'None',
      isCooled: false,
      statusId: 26,
      isInPackingState: true,
      customerNote: null,
      eshopNote: null,
      items: [],
    },
  });

  render(<BaleniPacking />);
  expect(screen.getByTestId('packing-shipment-creator')).toBeInTheDocument();
  expect(screen.getByTestId('packing-shipment-creator')).toHaveAttribute(
    'data-order-code',
    '250001'
  );
});

it('does not mount PackingShipmentCreator when order is not in packing state', () => {
  mockHook.mockReturnValue({
    ...baseResult,
    data: {
      code: '250001',
      customerName: 'Jan Novák',
      shippingMethodName: 'PPL',
      cooling: 'None',
      isCooled: false,
      statusId: 5,
      isInPackingState: false,
      customerNote: null,
      eshopNote: null,
      items: [],
    },
  });

  render(<BaleniPacking />);
  expect(screen.queryByTestId('packing-shipment-creator')).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd frontend && npx jest src/components/baleni/__tests__/BaleniPacking.test.tsx --no-coverage 2>&1 | tail -15
```

- [ ] **Step 3: Update BaleniPacking.tsx**

Replace the `PackingLabelPrinter` import with `PackingShipmentCreator`:

```typescript
import PackingShipmentCreator from './PackingShipmentCreator';
```

And change the JSX:

```tsx
{data && data.isInPackingState && (
  <PackingShipmentCreator orderCode={data.code} />
)}
```

- [ ] **Step 4: Run BaleniPacking tests to confirm they pass**

```bash
cd frontend && npx jest src/components/baleni/__tests__/BaleniPacking.test.tsx --no-coverage 2>&1 | tail -15
```

- [ ] **Step 5: Add E2E fixtures to test-data.ts**

In `frontend/test/e2e/fixtures/test-data.ts`, update `TestPackingOrders`:

```typescript
export const TestPackingOrders: Record<string, string | null> = {
  multiPackagePacking: null,

  // An order in packing state (statusId 26) that has NO existing Shoptet shipment.
  // Set a real staging order code here before running E2E tests.
  // Throw (never skip) if missing — see CLAUDE.md rule.
  noShipmentPacking: null,

  // An order in packing state that ALREADY HAS a Shoptet shipment.
  // Used to test the reuse/create-new choice.
  existingShipmentPacking: null,
};
```

- [ ] **Step 6: Extend packing.spec.ts with shipment creation E2E tests**

Append to `frontend/test/e2e/baleni/packing.spec.ts`:

```typescript
test('shows Vytvořit zásilku button for an order with no existing shipment', async ({ page }) => {
  if (!TestPackingOrders.noShipmentPacking) {
    throw new Error(
      'TestPackingOrders.noShipmentPacking fixture missing — set a real order code with no shipment in test-data.ts'
    );
  }

  const input = page.getByRole('textbox');
  await input.fill(TestPackingOrders.noShipmentPacking);
  await input.press('Enter');

  await expect(page.getByRole('button', { name: /Vytvořit zásilku/i })).toBeVisible({ timeout: 15000 });
});

test('shows existing-shipment warning for an order with an existing shipment', async ({ page }) => {
  if (!TestPackingOrders.existingShipmentPacking) {
    throw new Error(
      'TestPackingOrders.existingShipmentPacking fixture missing — set a real order code with an existing shipment in test-data.ts'
    );
  }

  const input = page.getByRole('textbox');
  await input.fill(TestPackingOrders.existingShipmentPacking);
  await input.press('Enter');

  // First: order loads and create button appears; click it
  await page.getByRole('button', { name: /Vytvořit zásilku/i }).click({ timeout: 15000 });

  // Then: existing shipment warning appears with reuse/create-new buttons
  await expect(page.getByText(/Zásilka již existuje/i)).toBeVisible({ timeout: 15000 });
  await expect(page.getByRole('button', { name: /Použít existující/i })).toBeVisible();
  await expect(page.getByRole('button', { name: /Vytvořit novou/i })).toBeVisible();
});
```

- [ ] **Step 7: Full FE build + lint + unit tests**

```bash
cd frontend && npm run build 2>&1 | tail -20
npm run lint 2>&1 | tail -10
npx jest --no-coverage 2>&1 | tail -20
```

Expected: build succeeds, no lint errors, all unit tests pass.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/baleni/BaleniPacking.tsx \
        frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx \
        frontend/test/e2e/fixtures/test-data.ts \
        frontend/test/e2e/baleni/packing.spec.ts
git commit -m "feat(shipment-labels): wire PackingShipmentCreator into BaleniPacking; add E2E fixtures"
```

---

## Verification Checklist

Before declaring this feature complete, run through each check:

- [ ] **BE build clean:** `dotnet build backend/Anela.Heblo.sln -q` — 0 errors, 0 warnings
- [ ] **BE format:** `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
- [ ] **BE tests:** `dotnet test backend/Anela.Heblo.sln -q` — all pass
- [ ] **FE build:** `cd frontend && npm run build` — TypeScript client regenerated, includes `shipmentLabels_CreateShipment`
- [ ] **FE lint:** `npm run lint` — 0 errors
- [ ] **FE tests:** `npx jest --no-coverage` — all pass
- [ ] **E2E:** `./scripts/run-playwright-tests.sh` against staging — set `noShipmentPacking` and `existingShipmentPacking` fixtures first
- [ ] **Manual kiosk:** scan an order with no shipment → Vytvořit zásilku → shipment created → label prints
- [ ] **Manual kiosk (duplicate):** scan an order with an existing shipment → click Vytvořit zásilku → Zásilka již existuje warning → Použít existující → label prints
- [ ] **Probe findings committed** to `docs/integrations/shoptet-api.md`

---

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Exact `POST /api/shipments` body unknown | Task 1 probe must run before Task 5/6; adapter DTOs marked ⚠️ |
| Carrier response field name differs from `carrierId` | Task 1 documents the real field; Task 5 and 6 use the actual name |
| Weight unit is grams (not kg) | Probe verifies; adjust `Weight = command.Package.WeightGrams / 1000.0` to `command.Package.WeightGrams` if needed |
| `requested → created` latency > 6s routinely | Current design: one retry after 3s. If latency is consistently higher, increase `Task.Delay` or add a second retry |
| Catalog weight gaps | `DefaultItemWeightGrams` fallback + `LogWarning` per item; carrier weight may be approximate |
