# Cooled Order Shoptet additionalField — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the expedition list PDF is generated for a cooled order, PATCH Shoptet `additionalField[index=1]` to `"CHLAZENE"` so downstream warehouse workflows have a programmatic signal.

**Architecture:** Add `SetAdditionalFieldAsync` directly to `ShoptetOrderClient` (following the existing pattern for expedition-specific methods not exposed on `IEshopOrderClient`). Inject `ILogger<ShoptetApiExpeditionListSource>` into the expedition source, then add a per-order try/catch PATCH loop inside `FlushBatchAsync`. Add the field-registry table and Postman verification note to the Shoptet docs.

**Tech Stack:** .NET 8 C#, xUnit, FluentAssertions, Moq (`Moq.Protected`), `System.Net.Http`, QuestPDF.

---

## File Map

**New:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateAdditionalFieldRequest.cs` — PATCH body DTO for `additionalFields`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetOrderClient_SetAdditionalFieldTests.cs` — unit tests for the new client method
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — component tests for the FlushBatchAsync hook

**Modified:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — add `SetAdditionalFieldAsync`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — add logger, constants, and cooling PATCH loop in `FlushBatchAsync`
- `docs/integrations/shoptet-api.md` — §3.6 verification note + new §3.7 field registry

---

## Task 1: Create the DTO

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateAdditionalFieldRequest.cs`

DTOs are pure data carriers; no dedicated test is needed. The PATCH body serialization is verified implicitly in Task 2.

- [ ] **Step 1: Create the file**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

/// <summary>
/// Body for PATCH /api/orders/{code}/notes when only additionalFields need updating.
/// Sending only this slice leaves customerRemark, eshopRemark, and trackingNumber untouched.
/// </summary>
public class UpdateAdditionalFieldRequest
{
    [JsonPropertyName("data")]
    public required UpdateAdditionalFieldData Data { get; init; }
}

public class UpdateAdditionalFieldData
{
    [JsonPropertyName("additionalFields")]
    public required IReadOnlyList<AdditionalFieldEntry> AdditionalFields { get; init; }
}

public class AdditionalFieldEntry
{
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateAdditionalFieldRequest.cs
git commit -m "feat: add UpdateAdditionalFieldRequest DTO for Shoptet PATCH /notes additionalFields"
```

---

## Task 2: TDD — `SetAdditionalFieldAsync` on `ShoptetOrderClient`

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetOrderClient_SetAdditionalFieldTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

The method is not on `IEshopOrderClient` — it follows the existing pattern of expedition-specific methods on the concrete class only.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetOrderClient_SetAdditionalFieldTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetOrderClient_SetAdditionalFieldTests
{
    private static HttpResponseMessage OkNullData() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":null,"errors":null}""", Encoding.UTF8, "application/json"),
        };

    private static (ShoptetOrderClient client, Mock<HttpMessageHandler> handler) BuildClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        return (new ShoptetOrderClient(http), handler);
    }

    // ── Guard clauses ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAdditionalFieldAsync_NullOrderCode_Throws()
    {
        var (client, _) = BuildClient();
        var act = () => client.SetAdditionalFieldAsync(null!, 1, "X", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_EmptyOrderCode_Throws()
    {
        var (client, _) = BuildClient();
        var act = () => client.SetAdditionalFieldAsync("", 1, "X", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task SetAdditionalFieldAsync_IndexOutOfRange_Throws(int index)
    {
        var (client, _) = BuildClient();
        var act = () => client.SetAdditionalFieldAsync("ABC001", index, "X", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SetAdditionalFieldAsync_TextExceeds255OnLowIndex_Throws(int index)
    {
        var (client, _) = BuildClient();
        var longText = new string('A', 256);
        var act = () => client.SetAdditionalFieldAsync("ABC001", index, longText, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task SetAdditionalFieldAsync_TextExceeds255OnHighIndex_DoesNotThrow(int index)
    {
        var (client, handler) = BuildClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkNullData());

        var longText = new string('A', 256);
        var act = () => client.SetAdditionalFieldAsync("ABC001", index, longText, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAdditionalFieldAsync_ValidArgs_PatchesCorrectUrl()
    {
        // Arrange
        var (client, handler) = BuildClient();
        HttpRequestMessage? captured = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(OkNullData());

        // Act
        await client.SetAdditionalFieldAsync("0012345678", 1, "CHLAZENE", CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Patch);
        captured.RequestUri!.PathAndQuery.Should().Be("/api/orders/0012345678/notes");
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_ValidArgs_SendsCorrectBody()
    {
        // Arrange
        var (client, handler) = BuildClient();
        string? capturedBody = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkNullData());

        // Act
        await client.SetAdditionalFieldAsync("0012345678", 1, "CHLAZENE", CancellationToken.None);

        // Assert
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var fields = doc.RootElement
            .GetProperty("data")
            .GetProperty("additionalFields");
        fields.GetArrayLength().Should().Be(1);
        fields[0].GetProperty("index").GetInt32().Should().Be(1);
        fields[0].GetProperty("text").GetString().Should().Be("CHLAZENE");
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_NullText_SendsNullInBody()
    {
        // Arrange
        var (client, handler) = BuildClient();
        string? capturedBody = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkNullData());

        // Act
        await client.SetAdditionalFieldAsync("0012345678", 1, null, CancellationToken.None);

        // Assert
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var textElem = doc.RootElement
            .GetProperty("data")
            .GetProperty("additionalFields")[0]
            .GetProperty("text");
        textElem.ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Error path ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SetAdditionalFieldAsync_NonSuccessResponse_ThrowsHttpRequestException(HttpStatusCode status)
    {
        // Arrange
        var (client, handler) = BuildClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"errors":[{"errorCode":"INVALID"}]}"""),
            });

        // Act
        var act = () => client.SetAdditionalFieldAsync("0012345678", 1, "CHLAZENE", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~SetAdditionalField" \
  --no-build 2>&1 | tail -20
```

Expected: build error — `ShoptetOrderClient` has no `SetAdditionalFieldAsync` method.

- [ ] **Step 3: Build so tests can run**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj 2>&1 | tail -20
```

Expected: compile error on the new test file referencing `SetAdditionalFieldAsync`.

- [ ] **Step 4: Implement `SetAdditionalFieldAsync` on `ShoptetOrderClient`**

Add the following method to `ShoptetOrderClient.cs` in the `// ── Expedition methods ─` section (after `GetExpeditionOrderDetailAsync`, before `// ── Private helpers ─`):

```csharp
public async Task SetAdditionalFieldAsync(
    string orderCode,
    int index,
    string? text,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(orderCode))
        throw new ArgumentException("Order code must not be null or empty.", nameof(orderCode));
    if (index is < 1 or > 6)
        throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be between 1 and 6 inclusive.");
    if (text != null && index <= 3 && text.Length > 255)
        throw new ArgumentException($"Text for additionalField index {index} must not exceed 255 characters.", nameof(text));

    var body = new UpdateAdditionalFieldRequest
    {
        Data = new UpdateAdditionalFieldData
        {
            AdditionalFields = [new AdditionalFieldEntry { Index = index, Text = text }],
        },
    };

    var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/notes", body, JsonOptions, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"PATCH /api/orders/{orderCode}/notes returned {(int)response.StatusCode}: {errorBody}");
    }
}
```

Add the `using` for the new DTO at the top of `ShoptetOrderClient.cs` — the existing `using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;` already covers the namespace, so no new using is needed.

- [ ] **Step 5: Run tests — verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~SetAdditionalField" 2>&1 | tail -20
```

Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetOrderClient_SetAdditionalFieldTests.cs
git commit -m "feat: add SetAdditionalFieldAsync to ShoptetOrderClient"
```

---

## Task 3: TDD — Expedition cooling hook in `FlushBatchAsync`

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`

`FlushBatchAsync` is a local function inside `CreatePickingList`. Tests drive it by calling `CreatePickingList` with a fully mocked HTTP handler and domain repositories.

- [ ] **Step 1: Create the test directory if it doesn't exist**

```bash
mkdir -p backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition
```

- [ ] **Step 2: Write failing tests**

Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ShoptetApiExpeditionListSource_CoolingMarkerTests
{
    // ZASILKOVNA_DO_RUKY GUID — maps to Carrier.Zasilkovna, DeliveryHandling.NaRuky
    private const string ZasilkovnaDoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e";
    private const string CooledOrderCode = "TEST-COOLED";
    private const string NormalOrderCode = "TEST-NORMAL";
    private const string CooledProductCode = "PROD-COOL";
    private const string NormalProductCode = "PROD-NORMAL";

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static readonly string OrderListJson = $$"""
        {
          "data": {
            "orders": [
              {
                "code": "{{CooledOrderCode}}",
                "status": { "id": -2 },
                "shipping": { "guid": "{{ZasilkovnaDoRukyGuid}}", "name": "Zásilkovna (do ruky)" },
                "price": { "withVat": "500.00", "currencyCode": "CZK" }
              },
              {
                "code": "{{NormalOrderCode}}",
                "status": { "id": -2 },
                "shipping": { "guid": "{{ZasilkovnaDoRukyGuid}}", "name": "Zásilkovna (do ruky)" },
                "price": { "withVat": "300.00", "currencyCode": "CZK" }
              }
            ],
            "paginator": { "totalCount": 2, "page": 1, "pageCount": 1 }
          }
        }
        """;

    private static readonly string CooledOrderDetailJson = $$"""
        {
          "data": {
            "order": {
              "code": "{{CooledOrderCode}}",
              "fullName": "Cooled Customer",
              "phone": "+420111222333",
              "billingAddress": {
                "fullName": "Cooled Customer",
                "street": "Chladna",
                "houseNumber": "1",
                "city": "Praha",
                "zip": "10000"
              },
              "items": [
                {
                  "itemType": "product",
                  "itemId": 1,
                  "code": "{{CooledProductCode}}",
                  "name": "Cooled Product",
                  "amount": 1.000,
                  "unit": "ks",
                  "itemPriceWithVat": "100.00"
                }
              ],
              "completion": []
            }
          }
        }
        """;

    private static readonly string NormalOrderDetailJson = $$"""
        {
          "data": {
            "order": {
              "code": "{{NormalOrderCode}}",
              "fullName": "Normal Customer",
              "phone": "+420444555666",
              "billingAddress": {
                "fullName": "Normal Customer",
                "street": "Normalni",
                "houseNumber": "2",
                "city": "Brno",
                "zip": "60200"
              },
              "items": [
                {
                  "itemType": "product",
                  "itemId": 2,
                  "code": "{{NormalProductCode}}",
                  "name": "Normal Product",
                  "amount": 1.000,
                  "unit": "ks",
                  "itemPriceWithVat": "80.00"
                }
              ],
              "completion": []
            }
          }
        }
        """;

    private Mock<HttpMessageHandler> BuildHandler(bool patchShouldThrow = false)
    {
        var handler = new Mock<HttpMessageHandler>();

        // GET /api/orders?statusId=... — returns the two-order list
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == "/api/orders" &&
                    r.RequestUri.Query.Contains("statusId")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(OrderListJson));

        // GET /api/orders/TEST-COOLED?include=...
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(CooledOrderDetailJson));

        // GET /api/orders/TEST-NORMAL?include=...
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == $"/api/orders/{NormalOrderCode}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(NormalOrderDetailJson));

        // PATCH /api/orders/TEST-COOLED/notes
        if (patchShouldThrow)
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r =>
                        r.Method == HttpMethod.Patch &&
                        r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Simulated Shoptet PATCH failure"));
        }
        else
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r =>
                        r.Method == HttpMethod.Patch &&
                        r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(OkJson("""{"data":null,"errors":null}"""));
        }

        return handler;
    }

    private ShoptetApiExpeditionListSource BuildSource(Mock<HttpMessageHandler> handler, ILogger<ShoptetApiExpeditionListSource>? logger = null)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        var client = new ShoptetOrderClient(http);

        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(CooledProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = CooledProductCode,
                ProductName = "Cooled Product",
                Properties = new CatalogProperties { Cooling = Cooling.L1 },
            });
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Properties = new CatalogProperties { Cooling = Cooling.None },
            });

        var carrierCooling = new Mock<ICarrierCoolingRepository>();
        carrierCooling.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new() { Carrier = Carriers.Zasilkovna, DeliveryHandling = DeliveryHandling.NaRuky, Cooling = Cooling.L1 },
            });

        var giftSettings = new Mock<IGiftSettingRepository>();
        giftSettings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        return new ShoptetApiExpeditionListSource(
            client,
            TimeProvider.System,
            catalog.Object,
            carrierCooling.Object,
            giftSettings.Object,
            logger ?? Mock.Of<ILogger<ShoptetApiExpeditionListSource>>(),
            _ => [0x25, 0x50, 0x44, 0x46]); // minimal stub PDF bytes
    }

    private static PrintPickingListRequest BuildRequest() => new()
    {
        SourceStateId = -2,
        DesiredStateId = 26,
        ChangeOrderState = false,
        Carriers = [],
    };

    [Fact]
    public async Task CreatePickingList_CooledOrder_PatchesShoptetAdditionalField()
    {
        // Arrange
        var handler = BuildHandler();
        var source = BuildSource(handler);

        // Act
        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        // Assert — PATCH /notes was called exactly once for the cooled order
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CreatePickingList_NonCooledOrder_DoesNotPatchAdditionalField()
    {
        // Arrange
        var handler = BuildHandler();
        var source = BuildSource(handler);

        // Act
        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        // Assert — PATCH /notes was NOT called for the normal order
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{NormalOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CreatePickingList_PatchFails_PdfStillCompletes()
    {
        // Arrange
        var logger = new Mock<ILogger<ShoptetApiExpeditionListSource>>();
        var handler = BuildHandler(patchShouldThrow: true);
        var source = BuildSource(handler, logger.Object);

        // Act — must not throw
        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        // Assert — PDF was still produced (result has files), and a warning was logged
        result.TotalCount.Should().Be(2);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(CooledOrderCode)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

- [ ] **Step 3: Run tests — verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~CoolingMarker" 2>&1 | tail -20
```

Expected: compile error — `ShoptetApiExpeditionListSource` constructor does not accept a logger.

- [ ] **Step 4: Add logger to `ShoptetApiExpeditionListSource` constructor**

In `ShoptetApiExpeditionListSource.cs`, add `ILogger<ShoptetApiExpeditionListSource>` as a new constructor parameter and store it:

```csharp
// Add this using at the top of the file:
using Microsoft.Extensions.Logging;

// Change the class declaration to add the field:
private readonly ILogger<ShoptetApiExpeditionListSource> _logger;

// Update the constructor to:
public ShoptetApiExpeditionListSource(
    IEshopOrderClient client,
    TimeProvider timeProvider,
    ICatalogRepository catalog,
    ICarrierCoolingRepository carrierCooling,
    IGiftSettingRepository giftSettings,
    ILogger<ShoptetApiExpeditionListSource> logger,
    Func<ExpeditionProtocolData, byte[]>? generateDocument = null)
{
    _client = (ShoptetOrderClient)client;
    _timeProvider = timeProvider;
    _catalog = catalog;
    _carrierCooling = carrierCooling;
    _giftSettings = giftSettings;
    _logger = logger;
    _generateDocument = generateDocument ?? ExpeditionProtocolDocument.Generate;
}
```

The full updated constructor signature (the `_logger` field line is inserted after `_giftSettings` field, and `logger` parameter is inserted before the optional `generateDocument`).

The DI registration in `ShoptetApiAdapterServiceCollectionExtensions.cs` does NOT need updating — `ILogger<T>` is resolved automatically by the .NET DI container when logging is configured (as it is in the app startup and in the integration test fixture).

- [ ] **Step 5: Add constants and cooling marker loop inside `FlushBatchAsync`**

In `ShoptetApiExpeditionListSource.cs`, add two private constants after the field declarations (before the constructor):

```csharp
private const string CoolingMarkerValue = "CHLAZENE";
private const int CoolingAdditionalFieldIndex = 1;
```

Inside `FlushBatchAsync` (the local function), add the per-order cooling PATCH loop **after** `var pdfBytes = _generateDocument(data);` and **after** `await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);` but **before** the `onBatchFilesReady` call. The final block in `FlushBatchAsync` should look like:

```csharp
var pdfBytes = _generateDocument(data);
var filePath = Path.Combine(Path.GetTempPath(), fileName);
await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);
exportedFiles.Add(filePath);

foreach (var order in batch)
{
    if (!order.IsCooled)
        continue;

    try
    {
        await _client.SetAdditionalFieldAsync(
            order.Code,
            CoolingAdditionalFieldIndex,
            CoolingMarkerValue,
            cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Failed to set Shoptet additionalField[{Index}]={Value} for order {OrderCode}; PDF print continues.",
            CoolingAdditionalFieldIndex,
            CoolingMarkerValue,
            order.Code);
    }
}

if (onBatchFilesReady != null)
    await onBatchFilesReady(new List<string> { filePath });
```

- [ ] **Step 6: Run tests — verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~CoolingMarker" 2>&1 | tail -20
```

Expected: all 3 tests PASS.

- [ ] **Step 7: Run the full unit test suite to check for regressions**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "Category!=Integration" 2>&1 | tail -20
```

Expected: all unit tests PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs
git commit -m "feat: stamp CHLAZENE additionalField on cooled orders during expedition list print"
```

---

## Task 4: Update documentation

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 1: Append verification note to §3.6**

At the end of section **3.6 PATCH /api/orders/{code}/notes**, append:

```markdown
**✅ Verified 2026-05-25 against test store (Shoptet token prefix 780175):**
- PATCH `additionalFields: [{ "index": 1, "text": "CHLAZENE-TEST" }]` → 200 `{"data":null,"errors":null}`
- GET `/api/orders/{code}?include=notes` → `data.order.notes.additionalFields[0].index == 1`, `data.order.notes.additionalFields[0].text == "CHLAZENE-TEST"`
- Round-trip confirmed: field persists and is readable back via the notes include.
- Example request:
  ```json
  {
    "data": {
      "additionalFields": [{ "index": 1, "text": "CHLAZENE" }]
    }
  }
  ```
```

- [ ] **Step 2: Add §3.7 additional-field registry**

After section 3.6 and before the `---` divider before section 4, insert:

```markdown
### 3.7 Heblo reservations for the 6 per-order additional fields

| Index | Reserved by | Value contract | Written when | Cleared when | Reader(s) | Limits |
|---|---|---|---|---|---|---|
| 1 | Expedition cooling marker | Literal string `"CHLAZENE"` for cooled orders; no other value ever written | Original expedition list print, if `ExpeditionOrder.IsCooled == true` | Never (write-only) | External / Shoptet operators (informational; nothing in Heblo reads it back) | ≤ 255 chars (we use 8) |
| 2 | — unassigned — | | | | | ≤ 255 chars |
| 3 | — unassigned — | | | | | ≤ 255 chars |
| 4 | — unassigned — | | | | | length undocumented |
| 5 | — unassigned — | | | | | length undocumented |
| 6 | — unassigned — | | | | | length undocumented |

**Before using an additional field in a new feature, claim it by updating this table in the same PR.** The fields are a finite shared resource (6 total) and the Shoptet API gives no per-field semantic protection — two callers writing to the same index will silently overwrite each other. The Heblo expectation is: one logical owner per index, documented here.

The Heblo client (`ShoptetOrderClient.SetAdditionalFieldAsync`) accepts any 1..6 index; there is no runtime guard tying an index to a feature. The guard is this table and code review.

Length limits: indices 1–3 are capped at 255 chars by the Shoptet API. Indices 4–6 are believed to support longer text but the exact cap has not been verified — measure before assuming.
```

- [ ] **Step 3: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs: verify §3.6 additionalFields round-trip; add §3.7 field registry for cooled-order marker"
```

---

## Task 5: Final verification

- [ ] **Step 1: Full build**

```bash
dotnet build backend/ 2>&1 | tail -20
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Format check**

```bash
dotnet format backend/ --verify-no-changes 2>&1 | tail -20
```

If formatting issues are found, run `dotnet format backend/` to fix them, then re-verify.

- [ ] **Step 3: Run targeted unit tests**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~SetAdditionalField|FullyQualifiedName~CoolingMarker" 2>&1 | tail -20
```

Expected: all tests PASS.

- [ ] **Step 4: Run full unit suite (exclude integration)**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "Category!=Integration" 2>&1 | tail -30
```

Expected: all tests PASS, no regressions.

---

## Self-Review Checklist

| Spec requirement | Covered by |
|---|---|
| PATCH additionalField[1] = "CHLAZENE" for cooled orders | Task 2 (impl) + Task 3 (hook) |
| Non-cooled orders: do nothing | Task 3, `CreatePickingList_NonCooledOrder_DoesNotPatchAdditionalField` |
| Fire-and-forget: failure doesn't stop PDF print | Task 3, `CreatePickingList_PatchFails_PdfStillCompletes` |
| Original print only (not reprints) | Architecture decision — `FlushBatchAsync` is only in `CreatePickingList`, not `ReprintExpeditionListHandler` |
| Guard clauses on index and text length | Task 2, guard tests |
| Shoptet docs §3.6 verification + §3.7 registry | Task 4 |
| DTOs are classes, not records | Verified — `UpdateAdditionalFieldRequest`, `UpdateAdditionalFieldData`, `AdditionalFieldEntry` all use `class` |
