# Double-Shot V → S Order Swap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every Shoptet order line for a double-shot `V` variant (`SA015030V`) is replaced inside Shoptet by its `S` mirror product set (`SA015030S`), so stock, picking, packing and invoicing behave as for a normal set.

**Architecture:** A pure planner decides, per order, which `V` lines to swap (or finish swapping). A sweep service finds candidate orders through the Shoptet `productCode` filter, applies the plan through new order-item client operations (POST the set, then DELETE the variant), and leaves a note. A 5-minute recurring job runs the sweep (dry run until a feature flag is on), and the picking-list print runs one sweep first through an ExpeditionList-owned contract.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, `System.Net.Http.Json`, Hangfire `IRecurringJob` auto-discovery, OpenFeature-backed `IFeatureFlagChecker`.

**Spec:** `docs/superpowers/specs/2026-10-12-doubleshot-order-swap-design.md` (read it first). Shoptet behaviour: `docs/integrations/shoptet-api.md` §3.8 and §3.17.

## Global Constraints

- Mapping comes **only** from `DoubleShotCodeRule` (`backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs`, already on this branch from #4479 commit `472d5c319`): `IsVariantCode` = `^SA\d+V$`, `TryGetSetCode("SA015030V")` → `"SA015030S"`. Do not re-implement or modify it.
- Never call `PATCH /api/orders/{code}/item/{id}`. A swap is always `POST /api/orders/{code}/item` (the set) **then** `DELETE /api/orders/{code}/item/{id}` (the variant).
- Swap tag (S line `remark`): exactly `auto-swap {variantCode} #{variantItemId}`, e.g. `auto-swap SA015030V #1356551`.
- Feature flag key: `is-double-shot-order-swap-enabled`, default `false`. Off = dry run (log only, no writes).
- Job name `swap-double-shot-orders`, cron `*/5 * * * *`, category `Warehouse`, `DefaultIsEnabled = true`.
- Eligible statuses default `[-1, 3, 32, -2, 35, 73]`; lookback default `14` days.
- Shoptet numeric fields are JSON strings. Use the formats verified on the test store: `amount` `"0.###"` (e.g. `"2"`), `unitPriceWithVat` `"0.00"` (e.g. `"1770.00"`), `vatRate` `"0.##"` (e.g. `"21"`), `priceRatio` `"0.0000"` (e.g. `"0.9000"`), all `CultureInfo.InvariantCulture`.
- Cancellation filters use `when (!ct.IsCancellationRequested)`, never `when (ex is not OperationCanceledException)` (HTTP timeouts are `TaskCanceledException`).
- Application DTO-ish types are **classes** (project rule); internal planner results may be `sealed record`.
- Note text (Czech, appended to the order's internal remark): `Automaticky prohozeno: SA015030V → SA015030S (2 ks)`, multiple swaps joined by `"; "`.
- Commit messages: conventional commits, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Deviations from the spec (deliberate, small):**
- The four new client methods live in a new partial file `ShoptetOrderClient.Items.cs` (the main file is already 378 lines).
- The default eligible-status list is a code constant applied when the configured array is empty, instead of a non-empty property default. A non-empty array default would be merged index-wise by the config binder (see the config-binder gotcha).
- No frontend flag mirror: the frontend never reads this flag, and `FeatureFlagRegistryFrontendMirrorTests` only requires the mirror to be a subset.

## Review Focus

1. **A `V` code with no `S` set in Shoptet** (Andy forgot one size): the POST returns 422. The order must keep its `V` line, nothing is deleted, an **error** is logged naming order + both codes, and the other lines of the same order still swap. Pinned in Task 3 (`Sweep_KeepsVariant_AndLogsError_WhenAddReturns422`).
2. **DELETE fails after POST succeeded** (crash, timeout, 5xx): the next sweep must delete only the `V` line and must not add a second `S` line. Pinned in Task 2 (`Plan_HealsHalfSwap_WhenTaggedSetLineExists`) and Task 3 (`Sweep_OnlyDeletesVariant_WhenHalfSwapped`).
3. **Two `V` lines with the same code in one order** (e.g. one discounted): both swap independently with their own price ratio and tag. Pinned in Task 2 (`Plan_KeepsTwoLinesOfSameVariantSeparate`).
4. **Order moved to packing between listing and swap**: re-read status wins and the order is untouched. Pinned in Task 2 (`Plan_IsNotEligible_WhenStatusNotInList`) and Task 3 (`Sweep_DoesNotWrite_WhenOrderNotEligible`).
5. **Swap sweep throws during a picking-list print** (Shoptet down): printing must still happen. Pinned in Task 5 (`PrintPickingListAsync_StillPrints_WhenPreparerThrows`).

---

## File Structure

| File | Responsibility |
|---|---|
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderDetail.cs` | `EshopOrderDetail`, `EshopOrderLine` — order lines read from Shoptet |
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/NewEshopOrderItem.cs` | `NewEshopOrderItem` — line to add |
| Modify `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` | 4 new operations |
| Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderItemModels.cs` | Shoptet wire models for detail/items/add/errors |
| Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs` | Partial: the 4 operations + error helper |
| Modify `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs:12` | `public class` → `public partial class` |
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotSwapPlanner.cs` | Pure planning + plan records |
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/IDoubleShotOrderSwapService.cs` | Service contract + summary record |
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotOrderSwapService.cs` | Sweep: find, plan, apply, note |
| Modify `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` | 2 settings |
| Modify `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` | DI registrations |
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs` | Recurring job |
| Modify `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs`, `FeatureFlagRegistry.cs`, `backend/src/Anela.Heblo.API/appsettings.json` | Flag |
| Create `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListOrderPreparer.cs` | Pre-print hook contract |
| Modify `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` | Call the hook |
| Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs` | Hook → sweep |
| Create `docs/processes/job-double-shot-order-swap.md`; modify `flow-picking-list.md`, `module-shoptet-orders.md`, `module-feature-flags.md`, `INDEX.md` (generated) | Process docs |

Tests (all in `backend/test/Anela.Heblo.Tests/`):
`Adapters/ShoptetApi/ShoptetOrderClientItemsTests.cs`, `Application/ShoptetOrders/DoubleShotSwapPlannerTests.cs`, `Application/ShoptetOrders/DoubleShotOrderSwapServiceTests.cs`, `Application/ShoptetOrders/SwapDoubleShotOrdersJobTests.cs`, `Features/ExpeditionList/ExpeditionListServicePreparerTests.cs`; modify `Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs` and `ExpeditionListServicePrintSinkTests.cs` (constructor).

**Running tests.** From the repo root (the `.sln` is there):
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~<ClassName>"
```
If it hangs at 0% CPU (another worktree building concurrently), run `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false` and then the same `dotnet test` with `--no-build`.

---

### Task 1: Order-item operations on the Shoptet order client

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderDetail.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/NewEshopOrderItem.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` (append to interface)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderItemModels.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs:12`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientItemsTests.cs`

**Interfaces:**
- Consumes: existing `ShoptetOrderClient` fields `_http`, `JsonOptions`; existing `OrderListResponse` (`Data.Orders[].Code`, `Data.Paginator.PageCount`); test helper `FakeDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage>)` (internal, `Anela.Heblo.Tests.Adapters.ShoptetApi` namespace).
- Produces (on `IEshopOrderClient`):
  - `Task<IReadOnlyList<string>> ListOrderCodesContainingProductAsync(string productCode, DateTimeOffset createdFrom, CancellationToken ct = default)`
  - `Task<EshopOrderDetail?> GetOrderDetailAsync(string orderCode, CancellationToken ct = default)`
  - `Task AddOrderItemAsync(string orderCode, NewEshopOrderItem item, CancellationToken ct = default)`
  - `Task DeleteOrderItemAsync(string orderCode, long itemId, CancellationToken ct = default)`
  - Failures throw `HttpRequestException` with `StatusCode` set and message `Shoptet {METHOD} {path} returned {code}: {errorCode} – {message}`.
  - `EshopOrderDetail { string Code; int StatusId; IReadOnlyList<EshopOrderLine> Lines }`
  - `EshopOrderLine { long ItemId; string ItemType; string? Code; decimal Amount; decimal UnitPriceWithVat; decimal VatRate; decimal PriceRatio; string? Remark }`
  - `NewEshopOrderItem { string ItemType; string Code; decimal Amount; decimal UnitPriceWithVat; decimal VatRate; decimal PriceRatio; string? Remark }` (all `init`)

- [ ] **Step 1: Create the Application types (no logic, needed for the test to compile)**

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderDetail.cs`:
```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders;

/// <summary>
/// An order's status and lines as returned by GET /api/orders/{code}. Carries only what the
/// double-shot swap needs (docs/integrations/shoptet-api.md §3.17).
/// </summary>
public class EshopOrderDetail
{
    public string Code { get; init; } = string.Empty;
    public int StatusId { get; init; }
    public IReadOnlyList<EshopOrderLine> Lines { get; init; } = [];
}

public class EshopOrderLine
{
    public long ItemId { get; init; }
    public string ItemType { get; init; } = string.Empty;
    public string? Code { get; init; }
    public decimal Amount { get; init; }
    public decimal UnitPriceWithVat { get; init; }
    public decimal VatRate { get; init; }
    public decimal PriceRatio { get; init; } = 1m;
    public string? Remark { get; init; }
}
```

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/NewEshopOrderItem.cs`:
```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders;

/// <summary>
/// A line to add to an existing order via POST /api/orders/{code}/item.
/// </summary>
public class NewEshopOrderItem
{
    public string ItemType { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal UnitPriceWithVat { get; init; }
    public decimal VatRate { get; init; }
    public decimal PriceRatio { get; init; } = 1m;
    public string? Remark { get; init; }
}
```

Append to the `IEshopOrderClient` interface body (before the closing brace):
```csharp

    /// <summary>
    /// Codes of orders created at or after <paramref name="createdFrom"/> that contain a line with
    /// exactly <paramref name="productCode"/> (GET /api/orders?productCode=&amp;creationTimeFrom=, all pages).
    /// The filter matches items[] line codes only, not set components (shoptet-api.md §3.8).
    /// </summary>
    Task<IReadOnlyList<string>> ListOrderCodesContainingProductAsync(
        string productCode, DateTimeOffset createdFrom, CancellationToken ct = default);

    /// <summary>
    /// GET /api/orders/{code} mapped to status + lines. Returns null when the order does not exist (404).
    /// </summary>
    Task<EshopOrderDetail?> GetOrderDetailAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// POST /api/orders/{code}/item. Adding a product-set line makes Shoptet expand its components
    /// and deduct their stock (shoptet-api.md §3.17). Non-2xx throws HttpRequestException with StatusCode.
    /// </summary>
    Task AddOrderItemAsync(string orderCode, NewEshopOrderItem item, CancellationToken ct = default);

    /// <summary>
    /// DELETE /api/orders/{code}/item/{itemId}. Shoptet returns the line's stock (shoptet-api.md §3.17).
    /// Non-2xx throws HttpRequestException with StatusCode.
    /// </summary>
    Task DeleteOrderItemAsync(string orderCode, long itemId, CancellationToken ct = default);
```

- [ ] **Step 2: Write the failing adapter tests**

`backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientItemsTests.cs`:
```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetOrderClientItemsTests
{
    private readonly List<(HttpMethod Method, string PathAndQuery, string? Body)> _requests = new();

    private ShoptetOrderClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeDelegatingHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            _requests.Add((req.Method, req.RequestUri!.PathAndQuery, body));
            return respond(req);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.shoptet.cz") };
        return new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));
    }

    private static HttpResponseMessage Raw(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string OrderListPage(int page, int pageCount, params string[] codes) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                orders = codes.Select(c => new { code = c, status = new { id = -2 } }),
                paginator = new { page, pageCount, totalCount = codes.Length },
            },
        });

    [Fact]
    public async Task ListOrderCodesContainingProduct_SendsEncodedFiltersAndReadsAllPages()
    {
        var client = BuildClient(req => req.RequestUri!.Query.Contains("page=1&")
            ? Raw(HttpStatusCode.OK, OrderListPage(1, 2, "126000001", "126000002"))
            : Raw(HttpStatusCode.OK, OrderListPage(2, 2, "126000003")));

        var codes = await client.ListOrderCodesContainingProductAsync(
            "SA015030V", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)));

        codes.Should().Equal("126000001", "126000002", "126000003");
        _requests.Should().HaveCount(2);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri("https://x" + _requests[0].PathAndQuery).Query);
        query["productCode"].Should().Be("SA015030V");
        query["creationTimeFrom"].Should().Be("2026-10-01T00:00:00+02:00");
        query["itemsPerPage"].Should().Be("50");
        _requests[0].PathAndQuery.Should().Contain("%2B02%3A00", "Shoptet needs the '+' of the offset URL-encoded");
    }

    [Fact]
    public async Task GetOrderDetail_MapsStatusAndLines()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.OK, """
        {"data":{"order":{"code":"126000043","status":{"id":-2,"name":"Vyřizuje se"},
          "items":[
            {"itemId":1356563,"itemType":"product","code":"SA015030V","amount":"2.000","priceRatio":"0.9000","remark":null,
             "unitPrice":{"withVat":"1770.00","withoutVat":"1462.81","vat":"307.19","vatRate":"21.00"}},
            {"itemId":1356566,"itemType":"shipping","code":null,"amount":"1.000","priceRatio":"1.0000","remark":null,
             "unitPrice":{"withVat":"0.00","withoutVat":"0.00","vat":"0.00","vatRate":"21.00"}}]}}}
        """));

        var detail = await client.GetOrderDetailAsync("126000043");

        detail.Should().NotBeNull();
        detail!.Code.Should().Be("126000043");
        detail.StatusId.Should().Be(-2);
        detail.Lines.Should().HaveCount(2);
        var v = detail.Lines[0];
        v.ItemId.Should().Be(1356563);
        v.ItemType.Should().Be("product");
        v.Code.Should().Be("SA015030V");
        v.Amount.Should().Be(2m);
        v.UnitPriceWithVat.Should().Be(1770m);
        v.VatRate.Should().Be(21m);
        v.PriceRatio.Should().Be(0.9m);
        v.Remark.Should().BeNull();
        detail.Lines[1].Code.Should().BeNull();
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000043");
    }

    [Fact]
    public async Task GetOrderDetail_ReturnsNull_On404()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.NotFound, """{"data":null,"errors":[]}"""));

        (await client.GetOrderDetailAsync("126999999")).Should().BeNull();
    }

    [Fact]
    public async Task AddOrderItem_PostsShoptetStringFormattedBody()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.Created, """{"data":{"order":{"code":"126000043"}}}"""));

        await client.AddOrderItemAsync("126000043", new NewEshopOrderItem
        {
            ItemType = "product-set",
            Code = "SA015030S",
            Amount = 2m,
            UnitPriceWithVat = 1770m,
            VatRate = 21m,
            PriceRatio = 0.9m,
            Remark = "auto-swap SA015030V #1356563",
        });

        _requests.Should().ContainSingle();
        _requests[0].Method.Should().Be(HttpMethod.Post);
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000043/item");
        using var doc = JsonDocument.Parse(_requests[0].Body!);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("itemType").GetString().Should().Be("product-set");
        data.GetProperty("code").GetString().Should().Be("SA015030S");
        data.GetProperty("amount").GetString().Should().Be("2");
        data.GetProperty("unitPriceWithVat").GetString().Should().Be("1770.00");
        data.GetProperty("vatRate").GetString().Should().Be("21");
        data.GetProperty("priceRatio").GetString().Should().Be("0.9000");
        data.GetProperty("remark").GetString().Should().Be("auto-swap SA015030V #1356563");
        data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "itemType", "code", "amount", "unitPriceWithVat", "vatRate", "priceRatio", "remark");
    }

    [Fact]
    public async Task AddOrderItem_Throws422WithShoptetErrorInMessage()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.UnprocessableEntity,
            """{"data":null,"errors":[{"errorCode":"invalid-request-data","message":"Product SA015030S not found.","instance":"data.code"}]}"""));

        var act = () => client.AddOrderItemAsync("126000043", new NewEshopOrderItem { ItemType = "product-set", Code = "SA015030S", Amount = 1m });

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ex.Message.Should().Contain("invalid-request-data").And.Contain("Product SA015030S not found.");
    }

    [Fact]
    public async Task DeleteOrderItem_SendsDeleteToItemPath_AndSurfaces409()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.Conflict,
            """{"data":null,"errors":[{"errorCode":"record-already-changed","message":"Order has been changed before update.","instance":"payload"}]}"""));

        var act = () => client.DeleteOrderItemAsync("126000043", 1356563);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _requests[0].Method.Should().Be(HttpMethod.Delete);
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000043/item/1356563");
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrderClientItemsTests"`
Expected: build error `'ShoptetOrderClient' does not implement interface member 'IEshopOrderClient.ListOrderCodesContainingProductAsync(...)'`.

- [ ] **Step 4: Add the wire models**

`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderItemModels.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

/// <summary>GET /api/orders/{code} — just the status and items[] (shoptet-api.md §3.9, §3.17).</summary>
public class OrderItemsDetailResponse
{
    [JsonPropertyName("data")]
    public OrderItemsDetailData? Data { get; set; }
}

public class OrderItemsDetailData
{
    [JsonPropertyName("order")]
    public OrderItemsDetail? Order { get; set; }
}

public class OrderItemsDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public OrderItemsStatus Status { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ShoptetOrderItem> Items { get; set; } = new();
}

public class OrderItemsStatus
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

public class ShoptetOrderItem
{
    [JsonPropertyName("itemId")]
    public long ItemId { get; set; }

    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("priceRatio")]
    public string? PriceRatio { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("unitPrice")]
    public ShoptetItemUnitPrice? UnitPrice { get; set; }
}

public class ShoptetItemUnitPrice
{
    [JsonPropertyName("withVat")]
    public string? WithVat { get; set; }

    [JsonPropertyName("vatRate")]
    public string? VatRate { get; set; }
}

/// <summary>POST /api/orders/{code}/item body. All numbers are strings (shoptet-api.md §3.17).</summary>
public class AddOrderItemRequest
{
    [JsonPropertyName("data")]
    public AddOrderItemData Data { get; set; } = new();
}

public class AddOrderItemData
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("unitPriceWithVat")]
    public string UnitPriceWithVat { get; set; } = string.Empty;

    [JsonPropertyName("vatRate")]
    public string VatRate { get; set; } = string.Empty;

    [JsonPropertyName("priceRatio")]
    public string PriceRatio { get; set; } = string.Empty;

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}

/// <summary>Shoptet error envelope: {"data":null,"errors":[{"errorCode","message","instance"}]}.</summary>
public class ShoptetErrorResponse
{
    [JsonPropertyName("errors")]
    public List<ShoptetError>? Errors { get; set; }
}

public class ShoptetError
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Make the client partial and implement the operations**

In `ShoptetOrderClient.cs` line 12 change `public class ShoptetOrderClient : ...` to `public partial class ShoptetOrderClient : ...` (nothing else in that file changes).

`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs`:
```csharp
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Order-item operations (add / delete / read lines). Behaviour verified on the test store and
/// documented in docs/integrations/shoptet-api.md §3.17. PATCH of an item is deliberately absent:
/// changing a line's code only relabels it and corrupts stock.
/// </summary>
public partial class ShoptetOrderClient
{
    private const int OrderListPageSize = 50;
    private const string CreationTimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<IReadOnlyList<string>> ListOrderCodesContainingProductAsync(
        string productCode, DateTimeOffset createdFrom, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);

        var code = Uri.EscapeDataString(productCode);
        var from = Uri.EscapeDataString(createdFrom.ToString(CreationTimeFormat, Invariant));
        var result = new List<string>();

        for (var page = 1; ; page++)
        {
            var path = $"/api/orders?productCode={code}&creationTimeFrom={from}&page={page}&itemsPerPage={OrderListPageSize}";
            var response = await _http.GetAsync(path, ct);
            await EnsureShoptetSuccessAsync(response, "GET", path, ct);

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            result.AddRange(data.Data.Orders.Select(o => o.Code));
            if (page >= data.Data.Paginator.PageCount)
                break;
        }

        return result;
    }

    public async Task<EshopOrderDetail?> GetOrderDetailAsync(string orderCode, CancellationToken ct = default)
    {
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}";
        var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureShoptetSuccessAsync(response, "GET", path, ct);

        var data = await response.Content.ReadFromJsonAsync<OrderItemsDetailResponse>(JsonOptions, ct);
        var order = data?.Data?.Order;
        if (order == null)
            return null;

        return new EshopOrderDetail
        {
            Code = order.Code,
            StatusId = order.Status.Id,
            Lines = order.Items.Select(MapLine).ToList(),
        };
    }

    public async Task AddOrderItemAsync(string orderCode, NewEshopOrderItem item, CancellationToken ct = default)
    {
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}/item";
        var body = new AddOrderItemRequest
        {
            Data = new AddOrderItemData
            {
                ItemType = item.ItemType,
                Code = item.Code,
                Amount = item.Amount.ToString("0.###", Invariant),
                UnitPriceWithVat = item.UnitPriceWithVat.ToString("0.00", Invariant),
                VatRate = item.VatRate.ToString("0.##", Invariant),
                PriceRatio = item.PriceRatio.ToString("0.0000", Invariant),
                Remark = item.Remark,
            },
        };

        var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureShoptetSuccessAsync(response, "POST", path, ct);
    }

    public async Task DeleteOrderItemAsync(string orderCode, long itemId, CancellationToken ct = default)
    {
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}/item/{itemId.ToString(Invariant)}";
        var response = await _http.DeleteAsync(path, ct);
        await EnsureShoptetSuccessAsync(response, "DELETE", path, ct);
    }

    private static EshopOrderLine MapLine(ShoptetOrderItem item) =>
        new()
        {
            ItemId = item.ItemId,
            ItemType = item.ItemType,
            Code = item.Code,
            Amount = ParseDecimal(item.Amount, 0m),
            UnitPriceWithVat = ParseDecimal(item.UnitPrice?.WithVat, 0m),
            VatRate = ParseDecimal(item.UnitPrice?.VatRate, 0m),
            PriceRatio = ParseDecimal(item.PriceRatio, 1m),
            Remark = item.Remark,
        };

    private static decimal ParseDecimal(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Number, Invariant, out var parsed) ? parsed : fallback;

    /// <summary>
    /// Like EnsureSuccessStatusCode, but keeps Shoptet's errors[] (errorCode + message) in the
    /// exception text so a 422 explains itself in the logs. StatusCode stays set for callers.
    /// </summary>
    private static async Task EnsureShoptetSuccessAsync(
        HttpResponseMessage response, string method, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Shoptet {method} {path} returned {(int)response.StatusCode}: {DescribeShoptetErrors(body)}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private static string DescribeShoptetErrors(string body)
    {
        try
        {
            var errors = JsonSerializer.Deserialize<ShoptetErrorResponse>(body, JsonOptions)?.Errors;
            if (errors is { Count: > 0 })
                return string.Join("; ", errors.Select(e => $"{e.ErrorCode} – {e.Message}"));
        }
        catch (JsonException)
        {
            // Not Shoptet's JSON envelope (e.g. a gateway HTML page) — fall through to the raw body.
        }

        return body;
    }
}
```

If the test project does not reference `System.Web.HttpUtility` (it is part of the shared framework; no package needed), keep the import as `System.Web.HttpUtility` fully qualified as written.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrderClientItemsTests|FullyQualifiedName~ShoptetOrderClientTests"`
Expected: all PASS (the existing `ShoptetOrderClientTests` must still pass after the `partial` change).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderDetail.cs \
  backend/src/Anela.Heblo.Application/Features/ShoptetOrders/NewEshopOrderItem.cs \
  backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs \
  backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderItemModels.cs \
  backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs \
  backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs \
  backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientItemsTests.cs
git commit -m "feat(orders): Shoptet order-item add/delete/read client operations

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Swap planner (pure logic)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotSwapPlanner.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/DoubleShotSwapPlannerTests.cs`

**Interfaces:**
- Consumes: `EshopOrderDetail`, `EshopOrderLine`, `NewEshopOrderItem` (Task 1); `DoubleShotCodeRule.IsVariantCode`, `DoubleShotCodeRule.TryGetSetCode` (on branch).
- Produces (namespace `Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap`):
  - `sealed record DoubleShotSwapAction(long VariantItemId, string VariantCode, string SetCode, decimal Amount, NewEshopOrderItem? SetItemToAdd)` with `bool IsHeal` (true when `SetItemToAdd is null`)
  - `sealed record DoubleShotSwapPlan(string OrderCode, bool IsEligible, IReadOnlyList<DoubleShotSwapAction> Actions)`
  - `static class DoubleShotSwapPlanner` with `const string ProductItemType = "product"`, `const string ProductSetItemType = "product-set"`, `static string TagFor(string variantCode, long variantItemId)`, `static DoubleShotSwapPlan Plan(EshopOrderDetail order, IReadOnlyCollection<int> eligibleStatusIds)`

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/DoubleShotSwapPlannerTests.cs`:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class DoubleShotSwapPlannerTests
{
    private static readonly int[] Eligible = [-1, 3, 32, -2, 35, 73];

    private static EshopOrderLine Line(long id, string type, string? code, decimal amount = 1m,
        decimal price = 100m, decimal ratio = 1m, string? remark = null) =>
        new()
        {
            ItemId = id, ItemType = type, Code = code, Amount = amount,
            UnitPriceWithVat = price, VatRate = 21m, PriceRatio = ratio, Remark = remark,
        };

    private static EshopOrderDetail Order(int status, params EshopOrderLine[] lines) =>
        new() { Code = "126000043", StatusId = status, Lines = lines };

    [Fact]
    public void TagFor_FormatsVariantCodeAndItemId()
    {
        DoubleShotSwapPlanner.TagFor("SA015030V", 1356551).Should().Be("auto-swap SA015030V #1356551");
    }

    [Fact]
    public void Plan_SwapsOnlyVariantLines_InMixedOrder()
    {
        var order = Order(-2,
            Line(1, "product", "SA015030V", amount: 2m, price: 1770m, ratio: 0.9m),
            Line(2, "product", "SER002030"),
            Line(3, "product-set", "SA014030S"),
            Line(4, "shipping", null),
            Line(5, "billing", null));

        var plan = DoubleShotSwapPlanner.Plan(order, Eligible);

        plan.IsEligible.Should().BeTrue();
        plan.OrderCode.Should().Be("126000043");
        var action = plan.Actions.Should().ContainSingle().Subject;
        action.VariantItemId.Should().Be(1);
        action.VariantCode.Should().Be("SA015030V");
        action.SetCode.Should().Be("SA015030S");
        action.Amount.Should().Be(2m);
        action.IsHeal.Should().BeFalse();
        action.SetItemToAdd.Should().BeEquivalentTo(new NewEshopOrderItem
        {
            ItemType = "product-set",
            Code = "SA015030S",
            Amount = 2m,
            UnitPriceWithVat = 1770m,
            VatRate = 21m,
            PriceRatio = 0.9m,
            Remark = "auto-swap SA015030V #1",
        });
    }

    [Fact]
    public void Plan_KeepsTwoLinesOfSameVariantSeparate()
    {
        var order = Order(-1,
            Line(10, "product", "SA015030V", amount: 1m, ratio: 1m),
            Line(11, "product", "SA015030V", amount: 3m, ratio: 0.5m));

        var plan = DoubleShotSwapPlanner.Plan(order, Eligible);

        plan.Actions.Should().HaveCount(2);
        plan.Actions[0].SetItemToAdd!.Remark.Should().Be("auto-swap SA015030V #10");
        plan.Actions[0].SetItemToAdd!.PriceRatio.Should().Be(1m);
        plan.Actions[1].SetItemToAdd!.Remark.Should().Be("auto-swap SA015030V #11");
        plan.Actions[1].SetItemToAdd!.Amount.Should().Be(3m);
        plan.Actions[1].SetItemToAdd!.PriceRatio.Should().Be(0.5m);
    }

    [Fact]
    public void Plan_HealsHalfSwap_WhenTaggedSetLineExists()
    {
        var order = Order(-2,
            Line(1, "product", "SA015030V", amount: 2m),
            Line(7, "product-set", "SA015030S", amount: 2m, remark: "auto-swap SA015030V #1"));

        var action = DoubleShotSwapPlanner.Plan(order, Eligible).Actions.Should().ContainSingle().Subject;

        action.IsHeal.Should().BeTrue();
        action.SetItemToAdd.Should().BeNull();
        action.VariantItemId.Should().Be(1);
    }

    [Fact]
    public void Plan_DoesNotHeal_WhenSetLineTagBelongsToAnotherVariantLine()
    {
        var order = Order(-2,
            Line(2, "product", "SA015030V"),
            Line(7, "product-set", "SA015030S", remark: "auto-swap SA015030V #1"));

        DoubleShotSwapPlanner.Plan(order, Eligible).Actions.Single().IsHeal.Should().BeFalse();
    }

    [Fact]
    public void Plan_IsNotEligible_WhenStatusNotInList()
    {
        var plan = DoubleShotSwapPlanner.Plan(Order(26, Line(1, "product", "SA015030V")), Eligible);

        plan.IsEligible.Should().BeFalse();
        plan.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Plan_IsEmpty_WhenNoVariantLines()
    {
        var plan = DoubleShotSwapPlanner.Plan(Order(-2, Line(1, "product", "SA015030S"), Line(2, "product", "BAL001V")), Eligible);

        plan.IsEligible.Should().BeTrue();
        plan.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Plan_IgnoresVariantCodeOnNonProductLine()
    {
        var plan = DoubleShotSwapPlanner.Plan(Order(-2, Line(1, "gift", "SA015030V")), Eligible);

        plan.Actions.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DoubleShotSwapPlannerTests"`
Expected: build error — `DoubleShotSwapPlanner` not found.

- [ ] **Step 3: Implement**

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotSwapPlanner.cs`:
```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

/// <summary>One V line to replace. <see cref="SetItemToAdd"/> is null when a previous run already
/// added the tagged S line and only the V line still needs deleting (a "heal").</summary>
public sealed record DoubleShotSwapAction(
    long VariantItemId,
    string VariantCode,
    string SetCode,
    decimal Amount,
    NewEshopOrderItem? SetItemToAdd)
{
    public bool IsHeal => SetItemToAdd is null;
}

public sealed record DoubleShotSwapPlan(string OrderCode, bool IsEligible, IReadOnlyList<DoubleShotSwapAction> Actions);

/// <summary>
/// Decides which double-shot V lines of an order must become their S mirror set. Pure: no I/O.
/// The S line is tagged with the V line's id so a swap interrupted between POST and DELETE is
/// finished, never duplicated, by the next run.
/// </summary>
public static class DoubleShotSwapPlanner
{
    public const string ProductItemType = "product";
    public const string ProductSetItemType = "product-set";

    public static string TagFor(string variantCode, long variantItemId) =>
        $"auto-swap {variantCode} #{variantItemId}";

    public static DoubleShotSwapPlan Plan(EshopOrderDetail order, IReadOnlyCollection<int> eligibleStatusIds)
    {
        if (!eligibleStatusIds.Contains(order.StatusId))
            return new DoubleShotSwapPlan(order.Code, IsEligible: false, Actions: []);

        var existingTags = order.Lines
            .Where(l => l.ItemType == ProductSetItemType && !string.IsNullOrEmpty(l.Remark))
            .Select(l => l.Remark!)
            .ToHashSet(StringComparer.Ordinal);

        var actions = order.Lines
            .Where(l => l.ItemType == ProductItemType && DoubleShotCodeRule.IsVariantCode(l.Code))
            .Select(l => BuildAction(l, existingTags))
            .ToList();

        return new DoubleShotSwapPlan(order.Code, IsEligible: true, Actions: actions);
    }

    private static DoubleShotSwapAction BuildAction(EshopOrderLine line, IReadOnlySet<string> existingTags)
    {
        var variantCode = line.Code!;
        DoubleShotCodeRule.TryGetSetCode(variantCode, out var setCode);
        var tag = TagFor(variantCode, line.ItemId);

        var setItem = existingTags.Contains(tag)
            ? null
            : new NewEshopOrderItem
            {
                ItemType = ProductSetItemType,
                Code = setCode,
                Amount = line.Amount,
                UnitPriceWithVat = line.UnitPriceWithVat,
                VatRate = line.VatRate,
                PriceRatio = line.PriceRatio,
                Remark = tag,
            };

        return new DoubleShotSwapAction(line.ItemId, variantCode, setCode, line.Amount, setItem);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DoubleShotSwapPlannerTests"`
Expected: 8 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotSwapPlanner.cs \
  backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/DoubleShotSwapPlannerTests.cs
git commit -m "feat(orders): plan double-shot V -> S line swaps with self-healing tag

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Sweep service + settings

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/IDoubleShotOrderSwapService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotOrderSwapService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` (append 2 properties)
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` (register service)
- Test: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/DoubleShotOrderSwapServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 client methods; Task 2 `DoubleShotSwapPlanner.Plan`, `DoubleShotSwapAction`; `IEshopStockClient.ListAsync(CancellationToken)` → `List<EshopStock>` (`EshopStock.Code`), namespace `Anela.Heblo.Domain.Features.Catalog.Stock`; existing `IEshopOrderClient.AppendEshopRemarkAsync(string, string, CancellationToken)`; `TimeProvider` (registered as singleton in the API).
- Produces:
  - `interface IDoubleShotOrderSwapService { Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct = default); }`
  - `sealed record DoubleShotSwapSummary(int OrdersScanned, int LinesSwapped, int LinesHealed, int LinesFailed, int OrdersFailed, bool SkippedBusy = false)`
  - `ShoptetOrdersSettings.DoubleShotSwapEligibleStatusIds` (`int[]`, default `[]` → code default), `ShoptetOrdersSettings.DoubleShotSwapLookbackDays` (`int`, default `14`)

- [ ] **Step 1: Add settings, contract and an empty service (so tests compile)**

Append to `ShoptetOrdersSettings` (before the closing brace):
```csharp

    /// <summary>
    /// Shoptet order states in which the double-shot swap may rewrite order lines (pre-picking only).
    /// Empty = use DoubleShotOrderSwapService.DefaultEligibleStatusIds
    /// (-1 Nová, 3 Čeká se na platbu, 32 Zaplaceno, -2 Vyřizuje se, 35 Poznámka, 73 Oprava-robot).
    /// Kept empty by default on purpose: the config binder merges arrays index-wise, so a non-empty
    /// default would leak into a shorter override.
    /// </summary>
    public int[] DoubleShotSwapEligibleStatusIds { get; set; } = [];

    /// <summary>
    /// How far back (by creation time) the double-shot swap looks for orders containing a V variant.
    /// </summary>
    public int DoubleShotSwapLookbackDays { get; set; } = 14;
```

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/IDoubleShotOrderSwapService.cs`:
```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

public interface IDoubleShotOrderSwapService
{
    /// <summary>
    /// Finds recent orders still containing a double-shot V line and replaces each V line with its
    /// S set (POST S, then DELETE V). With <paramref name="applyChanges"/> false it only logs.
    /// </summary>
    Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct = default);
}

public sealed record DoubleShotSwapSummary(
    int OrdersScanned,
    int LinesSwapped,
    int LinesHealed,
    int LinesFailed,
    int OrdersFailed,
    bool SkippedBusy = false)
{
    public static DoubleShotSwapSummary Busy { get; } = new(0, 0, 0, 0, 0, SkippedBusy: true);
}
```

Create `DoubleShotOrderSwapService.cs` with the constructor and `SweepAsync` throwing `NotImplementedException` for now (it is replaced in Step 4):
```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

public sealed class DoubleShotOrderSwapService : IDoubleShotOrderSwapService
{
    private readonly IEshopOrderClient _orderClient;
    private readonly IEshopStockClient _stockClient;
    private readonly ShoptetOrdersSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DoubleShotOrderSwapService> _logger;

    public DoubleShotOrderSwapService(
        IEshopOrderClient orderClient,
        IEshopStockClient stockClient,
        IOptions<ShoptetOrdersSettings> settings,
        TimeProvider clock,
        ILogger<DoubleShotOrderSwapService> logger)
    {
        _orderClient = orderClient;
        _stockClient = stockClient;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
```

- [ ] **Step 2: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/DoubleShotOrderSwapServiceTests.cs`:
```csharp
using System.Net;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class DoubleShotOrderSwapServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IEshopOrderClient> _orders = new(MockBehavior.Strict);
    private readonly Mock<IEshopStockClient> _stock = new();
    private readonly Mock<ILogger<DoubleShotOrderSwapService>> _logger = new();

    private DoubleShotOrderSwapService CreateSut(int[]? eligible = null)
    {
        _stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<EshopStock>
        {
            new() { Code = "SA015030V" },
            new() { Code = "SA015030S" },
            new() { Code = "SER002030" },
        });
        var settings = Options.Create(new ShoptetOrdersSettings
        {
            DoubleShotSwapEligibleStatusIds = eligible ?? [],
            DoubleShotSwapLookbackDays = 14,
        });
        return new DoubleShotOrderSwapService(_orders.Object, _stock.Object, settings,
            new FakeTimeProvider(Now), _logger.Object);
    }

    private void OrdersContaining(string variantCode, params string[] orderCodes) =>
        _orders.Setup(o => o.ListOrderCodesContainingProductAsync(variantCode, Now.AddDays(-14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderCodes);

    private void Detail(string code, int status, params EshopOrderLine[] lines) =>
        _orders.Setup(o => o.GetOrderDetailAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EshopOrderDetail { Code = code, StatusId = status, Lines = lines });

    private static EshopOrderLine V(long id, decimal amount = 2m, string code = "SA015030V") =>
        new() { ItemId = id, ItemType = "product", Code = code, Amount = amount, UnitPriceWithVat = 1770m, VatRate = 21m, PriceRatio = 1m };

    private void AcceptWrites()
    {
        _orders.Setup(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orders.Setup(o => o.DeleteOrderItemAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orders.Setup(o => o.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void VerifyLogged(LogLevel level, string fragment) =>
        _logger.Verify(l => l.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(fragment)),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

    [Fact]
    public async Task Sweep_QueriesOnlyVariantCodesFromStock()
    {
        OrdersContaining("SA015030V");
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.OrdersScanned.Should().Be(0);
        _orders.Verify(o => o.ListOrderCodesContainingProductAsync("SA015030V", Now.AddDays(-14), It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.ListOrderCodesContainingProductAsync("SA015030S", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_DryRun_MakesNoWrites()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: false);

        summary.LinesSwapped.Should().Be(0);
        _orders.Verify(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyLogged(LogLevel.Information, "DRY RUN");
    }

    [Fact]
    public async Task Sweep_AddsSetBeforeDeletingVariant_AndAppendsOneNote()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1, amount: 2m));
        var calls = new List<string>();
        _orders.Setup(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .Callback<string, NewEshopOrderItem, CancellationToken>((_, i, _) => calls.Add($"add {i.Code} {i.ItemType} {i.Remark}"))
            .Returns(Task.CompletedTask);
        _orders.Setup(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("delete 1")).Returns(Task.CompletedTask);
        _orders.Setup(o => o.AppendEshopRemarkAsync("126000043", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, t, _) => calls.Add($"note {t}")).Returns(Task.CompletedTask);
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        calls.Should().Equal(
            "add SA015030S product-set auto-swap SA015030V #1",
            "delete 1",
            "note Automaticky prohozeno: SA015030V → SA015030S (2 ks)");
        summary.Should().Be(new DoubleShotSwapSummary(1, 1, 0, 0, 0));
    }

    [Fact]
    public async Task Sweep_OnlyDeletesVariant_WhenHalfSwapped()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1),
            new EshopOrderLine { ItemId = 9, ItemType = "product-set", Code = "SA015030S", Amount = 2m, Remark = "auto-swap SA015030V #1" });
        AcceptWrites();
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()), Times.Once);
        summary.LinesHealed.Should().Be(1);
        summary.LinesSwapped.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_KeepsVariant_AndLogsError_WhenAddReturns422()
    {
        OrdersContaining("SA015030V", "126000043");
        OrdersContaining("SA016030V");
        _stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock> { new() { Code = "SA015030V" }, new() { Code = "SA016030V" } });
        Detail("126000043", -2, V(1), V(2, code: "SA016030V"));
        AcceptWrites();
        _orders.Setup(o => o.AddOrderItemAsync("126000043", It.Is<NewEshopOrderItem>(i => i.Code == "SA015030S"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet POST ... returned 422: invalid-request-data", null, HttpStatusCode.UnprocessableEntity));
        var sut = new DoubleShotOrderSwapService(_orders.Object, _stock.Object,
            Options.Create(new ShoptetOrdersSettings()), new FakeTimeProvider(Now), _logger.Object);

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 2, It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.AppendEshopRemarkAsync("126000043",
            "Automaticky prohozeno: SA016030V → SA016030S (2 ks)", It.IsAny<CancellationToken>()), Times.Once);
        summary.LinesFailed.Should().Be(1);
        summary.LinesSwapped.Should().Be(1);
        VerifyLogged(LogLevel.Error, "SA015030S");
    }

    [Fact]
    public async Task Sweep_AbandonsOrderOn409_AndContinuesWithNextOrder()
    {
        OrdersContaining("SA015030V", "126000043", "126000044");
        Detail("126000043", -2, V(1), V(2));
        Detail("126000044", -2, V(5));
        AcceptWrites();
        _orders.Setup(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("409", null, HttpStatusCode.Conflict));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Once,
            "the second V line of an abandoned order must wait for the next sweep");
        _orders.Verify(o => o.DeleteOrderItemAsync("126000044", 5, It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.AppendEshopRemarkAsync("126000043", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        summary.LinesSwapped.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_CountsOrderFailed_WhenDetailThrows_AndContinues()
    {
        OrdersContaining("SA015030V", "126000043", "126000044");
        _orders.Setup(o => o.GetOrderDetailAsync("126000043", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        Detail("126000044", -2, V(5));
        AcceptWrites();
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.OrdersFailed.Should().Be(1);
        summary.LinesSwapped.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_DoesNotWrite_WhenOrderNotEligible()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", 26, V(1));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.Should().Be(new DoubleShotSwapSummary(1, 0, 0, 0, 0));
        // Strict mock: any Add/Delete/Append call would have thrown.
    }

    [Fact]
    public async Task Sweep_UsesConfiguredEligibleStatuses_WhenSet()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -1, V(1));
        var sut = CreateSut(eligible: [-2]);

        await sut.SweepAsync(applyChanges: true);
        // Strict mock: status -1 is not in [-2], so no write happens.
    }

    [Fact]
    public async Task Sweep_SkipsOrderThatDisappeared()
    {
        OrdersContaining("SA015030V", "126000043");
        _orders.Setup(o => o.GetOrderDetailAsync("126000043", It.IsAny<CancellationToken>())).ReturnsAsync((EshopOrderDetail?)null);
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.OrdersFailed.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_ContinuesWithOtherVariants_WhenListingOneFails()
    {
        _stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock> { new() { Code = "SA015030V" }, new() { Code = "SA016030V" } });
        _orders.Setup(o => o.ListOrderCodesContainingProductAsync("SA015030V", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        OrdersContaining("SA016030V", "126000044");
        Detail("126000044", -2, V(5, code: "SA016030V"));
        AcceptWrites();
        var sut = new DoubleShotOrderSwapService(_orders.Object, _stock.Object,
            Options.Create(new ShoptetOrdersSettings()), new FakeTimeProvider(Now), _logger.Object);

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.LinesSwapped.Should().Be(1);
    }
}
```

Note: `Sweep_KeepsVariant...` and `Sweep_ContinuesWithOtherVariants...` construct the SUT directly instead of `CreateSut()` because `CreateSut` overrides the stock setup; Moq uses the latest matching setup, so calling `CreateSut()` after the custom `_stock.Setup` would replace it.

`FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing`. Check the test csproj: `grep -n "TimeProvider.Testing" backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. If it is missing, add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.10.0" />` next to the other `Microsoft.Extensions.*` references and run `dotnet restore backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DoubleShotOrderSwapServiceTests"`
Expected: FAIL with `NotImplementedException`.

- [ ] **Step 4: Implement the service**

Replace the body of `DoubleShotOrderSwapService.cs` with:
```csharp
using System.Globalization;
using System.Net;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

/// <summary>
/// Replaces double-shot V order lines with their S mirror set inside Shoptet
/// (docs/processes/job-double-shot-order-swap.md). Shoptet has no atomic item swap, so each line is
/// POST S (tagged) then DELETE V; DoubleShotSwapPlanner finishes an interrupted swap next run.
/// </summary>
public sealed class DoubleShotOrderSwapService : IDoubleShotOrderSwapService
{
    internal static readonly int[] DefaultEligibleStatusIds = [-1, 3, 32, -2, 35, 73];
    private static readonly SemaphoreSlim SweepLock = new(1, 1);
    private static readonly TimeSpan SweepLockWait = TimeSpan.FromMinutes(2);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const string NotePrefix = "Automaticky prohozeno: ";

    private enum ActionResult { Swapped, Healed, Failed, Abandoned }

    private sealed record OrderOutcome(int Swapped, int Healed, int FailedLines, bool OrderFailed)
    {
        public static OrderOutcome None { get; } = new(0, 0, 0, false);
        public static OrderOutcome Failure { get; } = new(0, 0, 0, true);
    }

    private readonly IEshopOrderClient _orderClient;
    private readonly IEshopStockClient _stockClient;
    private readonly ShoptetOrdersSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DoubleShotOrderSwapService> _logger;

    public DoubleShotOrderSwapService(
        IEshopOrderClient orderClient,
        IEshopStockClient stockClient,
        IOptions<ShoptetOrdersSettings> settings,
        TimeProvider clock,
        ILogger<DoubleShotOrderSwapService> logger)
    {
        _orderClient = orderClient;
        _stockClient = stockClient;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    private IReadOnlyCollection<int> EligibleStatusIds =>
        _settings.DoubleShotSwapEligibleStatusIds.Length == 0
            ? DefaultEligibleStatusIds
            : _settings.DoubleShotSwapEligibleStatusIds.Distinct().ToArray();

    public async Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct = default)
    {
        if (!await SweepLock.WaitAsync(SweepLockWait, ct))
        {
            _logger.LogWarning("DoubleShotSwap: another sweep is still running after {Wait}; skipping this one.", SweepLockWait);
            return DoubleShotSwapSummary.Busy;
        }

        try
        {
            return await SweepLockedAsync(applyChanges, ct);
        }
        finally
        {
            SweepLock.Release();
        }
    }

    private async Task<DoubleShotSwapSummary> SweepLockedAsync(bool applyChanges, CancellationToken ct)
    {
        var orderCodes = await FindCandidateOrderCodesAsync(ct);
        var outcomes = new List<OrderOutcome>(orderCodes.Count);
        foreach (var orderCode in orderCodes)
            outcomes.Add(await ProcessOrderSafelyAsync(orderCode, applyChanges, ct));

        var summary = new DoubleShotSwapSummary(
            OrdersScanned: orderCodes.Count,
            LinesSwapped: outcomes.Sum(o => o.Swapped),
            LinesHealed: outcomes.Sum(o => o.Healed),
            LinesFailed: outcomes.Sum(o => o.FailedLines),
            OrdersFailed: outcomes.Count(o => o.OrderFailed));

        _logger.LogInformation(
            "DoubleShotSwap: scanned {Orders} order(s), swapped {Swapped}, healed {Healed}, failed lines {FailedLines}, failed orders {FailedOrders}{Mode}.",
            summary.OrdersScanned, summary.LinesSwapped, summary.LinesHealed, summary.LinesFailed, summary.OrdersFailed,
            applyChanges ? string.Empty : " [DRY RUN — no changes applied]");
        return summary;
    }

    private async Task<IReadOnlyList<string>> FindCandidateOrderCodesAsync(CancellationToken ct)
    {
        var stock = await _stockClient.ListAsync(ct);
        var variantCodes = stock
            .Select(s => s.Code)
            .Where(DoubleShotCodeRule.IsVariantCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var createdFrom = _clock.GetUtcNow().AddDays(-_settings.DoubleShotSwapLookbackDays);

        var perVariant = new List<IReadOnlyList<string>>(variantCodes.Count);
        foreach (var variantCode in variantCodes)
            perVariant.Add(await ListOrdersSafelyAsync(variantCode, createdFrom, ct));

        return perVariant.SelectMany(codes => codes).Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyList<string>> ListOrdersSafelyAsync(string variantCode, DateTimeOffset createdFrom, CancellationToken ct)
    {
        try
        {
            return await _orderClient.ListOrderCodesContainingProductAsync(variantCode, createdFrom, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "DoubleShotSwap: listing orders containing {VariantCode} failed; skipping it this run.", variantCode);
            return [];
        }
    }

    private async Task<OrderOutcome> ProcessOrderSafelyAsync(string orderCode, bool applyChanges, CancellationToken ct)
    {
        try
        {
            return await ProcessOrderAsync(orderCode, applyChanges, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DoubleShotSwap: order {OrderCode} failed; it will be retried next run.", orderCode);
            return OrderOutcome.Failure;
        }
    }

    private async Task<OrderOutcome> ProcessOrderAsync(string orderCode, bool applyChanges, CancellationToken ct)
    {
        var order = await _orderClient.GetOrderDetailAsync(orderCode, ct);
        if (order == null)
            return OrderOutcome.None;

        var plan = DoubleShotSwapPlanner.Plan(order, EligibleStatusIds);
        if (!plan.IsEligible)
        {
            _logger.LogInformation("DoubleShotSwap: order {OrderCode} is in state {StatusId}, not eligible; left untouched.", orderCode, order.StatusId);
            return OrderOutcome.None;
        }

        if (!applyChanges)
        {
            foreach (var action in plan.Actions)
                _logger.LogInformation(
                    "DoubleShotSwap [DRY RUN]: order {OrderCode} line {ItemId}: would replace {VariantCode} with {SetCode} ({Amount} ks){Heal}.",
                    orderCode, action.VariantItemId, action.VariantCode, action.SetCode, action.Amount, action.IsHeal ? " — finish half swap" : string.Empty);
            return OrderOutcome.None;
        }

        var results = new List<(DoubleShotSwapAction Action, ActionResult Result)>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            var result = await ExecuteActionAsync(orderCode, action, ct);
            results.Add((action, result));
            if (result == ActionResult.Abandoned)
                break;
        }

        var done = results.Where(r => r.Result is ActionResult.Swapped or ActionResult.Healed).Select(r => r.Action).ToList();
        if (done.Count > 0)
            await AppendNoteAsync(orderCode, done, ct);

        return new OrderOutcome(
            Swapped: results.Count(r => r.Result == ActionResult.Swapped),
            Healed: results.Count(r => r.Result == ActionResult.Healed),
            FailedLines: results.Count(r => r.Result is ActionResult.Failed or ActionResult.Abandoned),
            OrderFailed: false);
    }

    private async Task<ActionResult> ExecuteActionAsync(string orderCode, DoubleShotSwapAction action, CancellationToken ct)
    {
        try
        {
            if (action.SetItemToAdd != null)
                await _orderClient.AddOrderItemAsync(orderCode, action.SetItemToAdd, ct);
            await _orderClient.DeleteOrderItemAsync(orderCode, action.VariantItemId, ct);

            _logger.LogInformation(
                "DoubleShotSwap: order {OrderCode} line {ItemId}: replaced {VariantCode} with {SetCode} ({Amount} ks){Heal}.",
                orderCode, action.VariantItemId, action.VariantCode, action.SetCode, action.Amount, action.IsHeal ? " — finished half swap" : string.Empty);
            return action.IsHeal ? ActionResult.Healed : ActionResult.Swapped;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogError(ex,
                "DoubleShotSwap: order {OrderCode} line {ItemId}: Shoptet rejected replacing {VariantCode} with {SetCode} (does the set exist?). The V line stays.",
                orderCode, action.VariantItemId, action.VariantCode, action.SetCode);
            return ActionResult.Failed;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "DoubleShotSwap: order {OrderCode} line {ItemId}: swap interrupted ({Status}); the rest of the order waits for the next run.",
                orderCode, action.VariantItemId, (ex as HttpRequestException)?.StatusCode);
            return ActionResult.Abandoned;
        }
    }

    private async Task AppendNoteAsync(string orderCode, IReadOnlyList<DoubleShotSwapAction> done, CancellationToken ct)
    {
        var note = NotePrefix + string.Join("; ", done.Select(a =>
            $"{a.VariantCode} → {a.SetCode} ({a.Amount.ToString("0.###", Invariant)} ks)"));
        try
        {
            await _orderClient.AppendEshopRemarkAsync(orderCode, note, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DoubleShotSwap: order {OrderCode} was swapped but the note could not be appended.", orderCode);
        }
    }
}
```

Register in `ShoptetOrdersModule.AddShoptetOrdersModule` (before `return services;`), adding `using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;`:
```csharp

        // Double-shot V -> S order swap (job + picking-list pre-step share one sweep).
        services.AddTransient<IDoubleShotOrderSwapService, DoubleShotOrderSwapService>();
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DoubleShotOrderSwapServiceTests|FullyQualifiedName~DoubleShotSwapPlannerTests"`
Expected: all PASS. If `Sweep_AbandonsOrderOn409...` fails because the strict mock saw a second Add for 126000043, the `break` on `Abandoned` is missing.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/ \
  backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs \
  backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs \
  backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/DoubleShotOrderSwapServiceTests.cs \
  backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "feat(orders): sweep service swapping double-shot V lines to S sets

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Recurring job + feature flag

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs`, `FeatureFlagRegistry.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (`FeatureManagement` section)
- Test: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/SwapDoubleShotOrdersJobTests.cs`

**Interfaces:**
- Consumes: `IDoubleShotOrderSwapService.SweepAsync(bool, CancellationToken)` (Task 3); `IRecurringJobStatusChecker.IsJobEnabledAsync(string, CancellationToken, bool defaultIfMissing = true)`; `IFeatureFlagChecker.IsEnabledAsync(string, CancellationToken)`.
- Produces: `FeatureFlagKeys.DoubleShotOrderSwap = "is-double-shot-order-swap-enabled"`; job `swap-double-shot-orders` (auto-discovered by `ServiceCollectionExtensions` assembly scan — no manual registration).

- [ ] **Step 1: Add the flag**

`FeatureFlagKeys.cs` — add inside the class:
```csharp

    /// <summary>
    /// When on, the double-shot swap job and the picking-list pre-step really replace V order lines
    /// with their S set in Shoptet. When off, they only log what they would do (dry run).
    /// </summary>
    public const string DoubleShotOrderSwap = "is-double-shot-order-swap-enabled";
```
`FeatureFlagRegistry.cs` — add to the `All` list:
```csharp
        new(FeatureFlagKeys.DoubleShotOrderSwap,
            Description: "When on, double-shot V order lines are replaced in Shoptet by their S mirror set (POST S, DELETE V); when off the swap only logs what it would do (dry run).",
            DefaultValue: false),
```
`backend/src/Anela.Heblo.API/appsettings.json` — add to `FeatureManagement` (keep valid JSON, comma after the previous last entry):
```json
    "is-double-shot-order-swap-enabled": false
```

- [ ] **Step 2: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/SwapDoubleShotOrdersJobTests.cs`:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class SwapDoubleShotOrdersJobTests
{
    private readonly Mock<IDoubleShotOrderSwapService> _swap = new();
    private readonly Mock<IRecurringJobStatusChecker> _status = new();
    private readonly Mock<IFeatureFlagChecker> _flags = new();

    private SwapDoubleShotOrdersJob CreateSut(bool jobEnabled, bool flagOn)
    {
        _status.Setup(s => s.IsJobEnabledAsync("swap-double-shot-orders", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(jobEnabled);
        _flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, It.IsAny<CancellationToken>()))
            .ReturnsAsync(flagOn);
        _swap.Setup(s => s.SweepAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DoubleShotSwapSummary(0, 0, 0, 0, 0));
        return new SwapDoubleShotOrdersJob(_swap.Object, _status.Object, _flags.Object, NullLogger<SwapDoubleShotOrdersJob>.Instance);
    }

    [Fact]
    public void Metadata_MatchesSpec()
    {
        var job = CreateSut(true, false);

        job.Metadata.JobName.Should().Be("swap-double-shot-orders");
        job.Metadata.CronExpression.Should().Be("*/5 * * * *");
        job.Metadata.Category.Should().Be(RecurringJobCategory.Warehouse);
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_DoesNothing_WhenJobDisabled()
    {
        await CreateSut(jobEnabled: false, flagOn: true).ExecuteAsync();

        _swap.Verify(s => s.SweepAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_PassesFlagAsApplyChanges(bool flagOn)
    {
        await CreateSut(jobEnabled: true, flagOn: flagOn).ExecuteAsync();

        _swap.Verify(s => s.SweepAsync(flagOn, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SwapDoubleShotOrdersJobTests"`
Expected: build error — `SwapDoubleShotOrdersJob` not found.

- [ ] **Step 4: Implement the job**

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs`:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;

public sealed class SwapDoubleShotOrdersJob : IRecurringJob
{
    private readonly IDoubleShotOrderSwapService _swapService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<SwapDoubleShotOrdersJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "swap-double-shot-orders",
        Category = RecurringJobCategory.Warehouse,
        DisplayName = "Swap Double-Shot Orders",
        Description = "Replaces double-shot V variant lines in open Shoptet orders with their S mirror product set, so components are deducted, picked and invoiced.",
        CronExpression = "*/5 * * * *",
        DefaultIsEnabled = true,
    };

    public SwapDoubleShotOrdersJob(
        IDoubleShotOrderSwapService swapService,
        IRecurringJobStatusChecker statusChecker,
        IFeatureFlagChecker featureFlags,
        ILogger<SwapDoubleShotOrdersJob> logger)
    {
        _swapService = swapService;
        _statusChecker = statusChecker;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var applyChanges = await _featureFlags.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, cancellationToken);
        if (!applyChanges)
        {
            _logger.LogInformation(
                "SwapDoubleShotOrders: running in DRY RUN mode (feature flag '{Flag}' disabled) — swaps are logged, not applied.",
                FeatureFlagKeys.DoubleShotOrderSwap);
        }

        await _swapService.SweepAsync(applyChanges, cancellationToken);
    }
}
```

- [ ] **Step 5: Run job, flag and job-config tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SwapDoubleShotOrdersJobTests|FullyQualifiedName~FeatureFlag|FullyQualifiedName~RecurringJob"`
Expected: all PASS (the flag tests check registry ↔ appsettings consistency; the recurring-job tests validate cron/metadata of every discovered job).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs \
  backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs \
  backend/src/Anela.Heblo.API/appsettings.json \
  backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/SwapDoubleShotOrdersJobTests.cs
git commit -m "feat(orders): 5-minute double-shot swap job behind dry-run flag

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Picking-list pre-print sweep

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListOrderPreparer.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` (constructor + `PrintPickingListAsync`)
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs`, `ExpeditionListServicePrintSinkTests.cs` (constructor call)
- Test: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePreparerTests.cs`

**Interfaces:**
- Consumes: `IDoubleShotOrderSwapService` (Task 3), `FeatureFlagKeys.DoubleShotOrderSwap` (Task 4).
- Produces: `interface IPickingListOrderPreparer { Task PrepareAsync(CancellationToken cancellationToken = default); }` in `Anela.Heblo.Application.Features.ExpeditionList.Contracts`; `ExpeditionListService` constructor gains `IPickingListOrderPreparer orderPreparer` as its **second** parameter (after `pickingSource`).

Module boundary: ExpeditionList owns the contract, ShoptetOrders implements it through an adapter, same as `IOrderStatusReader`. `ModuleBoundariesTests` keeps `ExpeditionListShoptetOrdersAllowlist` empty, so ExpeditionList must not reference any ShoptetOrders type.

- [ ] **Step 1: Add the contract**

`backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListOrderPreparer.cs`:
```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

/// <summary>
/// Runs right before a picking list is built so the source orders are in printable shape
/// (today: the double-shot V → S swap, implemented by the ShoptetOrders module).
/// A failure must never stop printing; ExpeditionListService logs and continues.
/// </summary>
public interface IPickingListOrderPreparer
{
    Task PrepareAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePreparerTests.cs`:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServicePreparerTests
{
    private readonly Mock<IExpeditionPickingSource> _pickingSource = new();
    private readonly Mock<IPickingListOrderPreparer> _preparer = new();

    private ExpeditionListService CreateService() => new(
        _pickingSource.Object,
        _preparer.Object,
        new Mock<IEmailSender>().Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        new Mock<IPrintQueueSink>().Object,
        new Mock<ITemporaryFileAccessor>().Object,
        NullLogger<ExpeditionListService>.Instance);

    private readonly List<string> _calls = new();

    private void TrackSource() =>
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<Func<IList<string>, Task>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("build"))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 0 });

    [Fact]
    public async Task PrintPickingListAsync_PreparesOrdersBeforeBuildingList()
    {
        TrackSource();
        _preparer.Setup(p => p.PrepareAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("prepare")).Returns(Task.CompletedTask);

        await CreateService().PrintPickingListAsync(new ExpeditionPickingRequest { SendToPrinter = false });

        Assert.Equal(new[] { "prepare", "build" }, _calls);
    }

    [Fact]
    public async Task PrintPickingListAsync_StillPrints_WhenPreparerThrows()
    {
        TrackSource();
        _preparer.Setup(p => p.PrepareAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Shoptet down"));

        await CreateService().PrintPickingListAsync(new ExpeditionPickingRequest { SendToPrinter = false });

        Assert.Equal(new[] { "build" }, _calls);
    }
}
```

Also add `_preparer`/`new Mock<IPickingListOrderPreparer>().Object` as the second argument of `new ExpeditionListService(...)` in `ExpeditionListServiceOrderStateTests.CreateService()` and in the equivalent factory of `ExpeditionListServicePrintSinkTests` (find it with `grep -n "new ExpeditionListService" backend/test -r`). Example for `ExpeditionListServiceOrderStateTests`:
```csharp
    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingSource.Object,
        new Mock<IPickingListOrderPreparer>().Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
        _temporaryFileAccessor.Object,
        NullLogger<ExpeditionListService>.Instance);
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListService"`
Expected: build error — no `ExpeditionListService` constructor takes 8 arguments.

- [ ] **Step 4: Implement the hook in ExpeditionListService**

Add the field and constructor parameter (second position):
```csharp
    private readonly IPickingListOrderPreparer _orderPreparer;

    public ExpeditionListService(
        IExpeditionPickingSource pickingSource,
        IPickingListOrderPreparer orderPreparer,
        IEmailSender emailSender,
        TimeProvider clock,
        IOptions<PrintPickingListOptions> options,
        IPrintQueueSink printQueueSink,
        ITemporaryFileAccessor temporaryFileAccessor,
        ILogger<ExpeditionListService> logger)
    {
        _pickingSource = pickingSource;
        _orderPreparer = orderPreparer;
        // ...existing assignments unchanged
    }
```
In `PrintPickingListAsync`, immediately before `var result = await _pickingSource.CreatePickingListAsync(request, batchCallback, cancellationToken);` insert:
```csharp
        await PrepareOrdersAsync(cancellationToken);

```
and add the private method next to `SendEmailCopy`:
```csharp
    private async Task PrepareOrdersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _orderPreparer.PrepareAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Picking-list order preparation failed; printing continues with the orders as they are.");
        }
    }
```

- [ ] **Step 5: Implement the ShoptetOrders adapter and register it**

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs`:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

/// <summary>
/// Runs one double-shot swap sweep before every picking-list build, so a print never shows a V line.
/// Dry run unless the double-shot swap flag is on — same switch as the recurring job.
/// </summary>
internal sealed class ShoptetOrdersDoubleShotPickingPreparerAdapter : IPickingListOrderPreparer
{
    private readonly IDoubleShotOrderSwapService _swapService;
    private readonly IFeatureFlagChecker _featureFlags;

    public ShoptetOrdersDoubleShotPickingPreparerAdapter(IDoubleShotOrderSwapService swapService, IFeatureFlagChecker featureFlags)
    {
        _swapService = swapService;
        _featureFlags = featureFlags;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        var applyChanges = await _featureFlags.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, cancellationToken);
        await _swapService.SweepAsync(applyChanges, cancellationToken);
    }
}
```
In `ShoptetOrdersModule`, next to the `IOrderStatusReader` registration:
```csharp

        // Cross-module contract: ShoptetOrders implements ExpeditionList's IPickingListOrderPreparer
        // (double-shot V -> S swap before every picking-list print). Registered by the provider.
        services.AddTransient<IPickingListOrderPreparer, ShoptetOrdersDoubleShotPickingPreparerAdapter>();
```

Add an adapter test to `SwapDoubleShotOrdersJobTests.cs` (same file keeps the flag-plumbing tests together; `InternalsVisibleTo Anela.Heblo.Tests` is set on the Application project):
```csharp
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickingPreparer_SweepsWithFlagAsApplyChanges(bool flagOn)
    {
        CreateSut(jobEnabled: true, flagOn: flagOn);
        var adapter = new Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure
            .ShoptetOrdersDoubleShotPickingPreparerAdapter(_swap.Object, _flags.Object);

        await adapter.PrepareAsync();

        _swap.Verify(s => s.SweepAsync(flagOn, It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 6: Run to verify pass (including architecture tests)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListService|FullyQualifiedName~SwapDoubleShotOrdersJobTests|FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~ApplicationStartupTests"`
Expected: all PASS. `ApplicationStartupTests` proves DI can build `ExpeditionListService` with the new dependency.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListOrderPreparer.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs \
  backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs \
  backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs \
  backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ \
  backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/SwapDoubleShotOrdersJobTests.cs
git commit -m "feat(expedition): run double-shot swap sweep before every picking-list print

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Process docs, full validation

**Files:**
- Create: `docs/processes/job-double-shot-order-swap.md`
- Modify: `docs/processes/flow-picking-list.md` (Data flow: new step 0), `docs/processes/module-shoptet-orders.md` (list the job), `docs/processes/module-feature-flags.md` (flag table row)
- Regenerate: `docs/processes/INDEX.md`

**Interfaces:**
- Consumes: everything above. No code.

- [ ] **Step 1: Write the process doc**

Follow `docs/processes/_TEMPLATE.md` section by section (read it in full first). Front matter:
```yaml
---
process: job-double-shot-order-swap
kind: job
module: shoptet-orders
summary: Every 5 minutes (and before each picking-list print) replaces double-shot V variant lines in open Shoptet orders with their S mirror product set, so set components are deducted, picked and invoiced.
owns:
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/**
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderDetail.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/NewEshopOrderItem.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderItemModels.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs
verified_at: "<short SHA of HEAD at the time you write it>"
related: [flow-picking-list, feed-issued-invoices]
---
```
Body content (from the spec, in the template's sections):
- **Purpose**: V (e-shop variant, display only) vs S (product set carrying composition); why the swap; who notices (warehouse picking list, Flexi invoices, Shoptet stock).
- **Trigger**: job `swap-double-shot-orders`, `*/5 * * * *` Europe/Prague, category Warehouse, toggle in Recurring Jobs; plus pre-step of every picking-list print (all four entry points of `flow-picking-list`). Flag `is-double-shot-order-swap-enabled` (off = dry run).
- **Data flow**: stock CSV (`IEshopStockClient.ListAsync`) → V codes (`DoubleShotCodeRule`, `^SA\d+V$` → `…S`) → `GET /api/orders?productCode=&creationTimeFrom=` (lookback `ShoptetOrders:DoubleShotSwapLookbackDays`, 14) → `GET /api/orders/{code}` → planner → `POST /api/orders/{code}/item` (product-set, same amount/unit price/VAT/priceRatio, remark tag) → `DELETE /api/orders/{code}/item/{id}` → `PATCH /api/orders/{code}/notes` (eshopRemark note).
- **Logic & formulas**: eligible states (`ShoptetOrders:DoubleShotSwapEligibleStatusIds`, default -1/3/32/-2/35/73; never 26+); tag format; heal rule; 422 → V stays + error; 409/other → rest of order waits; one sweep at a time (static lock, 2 min wait).
- **Runtime facts / Known quirks**: no atomic swap in Shoptet; PATCH relabels without stock (never use); item edits not in order history (hence the note); invoices follow item edits; order codes of deleted orders are reused; test store has no real V/S pair (set `SA014030` has no suffix); prod mirror sets are `SA014–SA017 × 005/030/100` ending in `S`.
- **Rollout**: merged with flag off; check dry-run log once Andy creates V variants; then enable.

- [ ] **Step 2: Update related docs**

- `flow-picking-list.md` → in *Data flow* insert before step 1: `0. **Prepare orders** — ExpeditionListService calls IPickingListOrderPreparer (ShoptetOrders: one double-shot V → S swap sweep, see job-double-shot-order-swap). A failure is logged and printing continues.` Add `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListOrderPreparer.cs` is already covered by its `ExpeditionList/**` owns glob — no owns change. Add `job-double-shot-order-swap` to `related`.
- `module-shoptet-orders.md` → add the job and the swap service to its process/job list and key-files list (match the existing bullet style there).
- `module-feature-flags.md` → add a row to the flag table mirroring the `is-delivered-order-completion-enabled` row: `| is-double-shot-order-swap-enabled | false | false | (inherits false) | SwapDoubleShotOrdersJob + picking-list pre-step (ShoptetOrders module) | On: V lines are replaced by their S set in Shoptet. Off: dry run — only logged. |`

- [ ] **Step 3: Regenerate the index and check**

Run: `python3 scripts/process-docs/check.py index && python3 scripts/process-docs/check.py check`
Expected: index rewritten with the new doc; check reports no orphan job (`swap-double-shot-orders` is owned) and no errors for the touched docs.

- [ ] **Step 4: Full backend validation**

Run (repo root):
```bash
dotnet build
dotnet format --verify-no-changes || dotnet format
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration"
```
Expected: build succeeds with no new warnings in touched files; format clean; all tests pass. Frontend is untouched, so no `npm run build` is needed. If anything fails, fix it before committing; do not skip.

- [ ] **Step 5: Commit**

```bash
git add docs/processes/
git commit -m "docs(processes): double-shot order swap job + picking-list pre-step

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-review notes

- Spec coverage: rule (on branch) · client ops (T1) · planner (T2) · service, settings, lock, 422/409, note (T3) · job + flag (T4) · picking hook (T5) · process docs + validation (T6). The spec's FE flag mirror is dropped deliberately (see Deviations).
- Types: `EshopOrderDetail/EshopOrderLine/NewEshopOrderItem` (T1) are used unchanged in T2–T3; `DoubleShotSwapSummary` ctor order `(OrdersScanned, LinesSwapped, LinesHealed, LinesFailed, OrdersFailed, SkippedBusy)` matches every test; `FeatureFlagKeys.DoubleShotOrderSwap` used in T4 and T5.
- The static `SweepLock` is shared across tests in one process. Every test path releases it in `finally`, so sequential tests are safe. xUnit runs test classes in parallel, but only `DoubleShotOrderSwapServiceTests` touches the real service, and tests within one class run sequentially.
