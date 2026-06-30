# Manual "Tisknout zakázku" Single-Order Expedition Print — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the automatic Tisk-Robot background print (PR #3315) with a manual "Tisknout zakázku" button on the "Tisk expedice" screen that prints a single order (entered by code) onto an expedition list and moves it to state 26 ("Balí se"), after validating the order is not in a non-printable state.

**Architecture:** Reuse the existing render/print/state-change pipeline (`ExpeditionListService.PrintPickingListAsync` → `IExpeditionPickingSource` → `ShoptetApiExpeditionListSource` → PDF → print sink). Add a single-order-by-code **selection branch** to the picking source (the only part the current batch-by-state flow can't do), a new MediatR use case that validates state then drives that pipeline, a controller endpoint, and a React button + modal.

**Tech Stack:** .NET 8, MediatR, FluentValidation, xUnit + Moq + FluentAssertions; React + TypeScript, React Query, Tailwind.

---

## Background facts (verified in code)

- **Picking source** `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` selects orders via `FetchAllOrdersAsync(SourceStateId)` then `BuildOrdersByMethod(orders, Carriers)`. An **empty `Carriers` list disables the carrier filter** (`carrierFilter = requestedCarriers.Any() ? ... : null`). The state change happens in the same method: `if (request.ChangeOrderState) foreach (code in processedCodes) UpdateStatusAsync(code, request.DesiredStateId)`.
- **Client** `ShoptetOrderClient` implements both `IEshopOrderClient` and `IShoptetExpeditionOrderSource`. `GetOrderStatusIdAsync(code)` returns the current state id. `GetOrderDetailInternalAsync` proves `GET /api/orders/{code}` deserializes to `CreateOrderResponse` whose `Data.Order` is an `OrderSummary` (carries `Shipping.Guid`, `Price`, `Status`).
- **BaseResponse** (`Shared/BaseResponse.cs`) has ctors `()` and `(ErrorCodes, Dictionary<string,string>?)`. Failing responses are mapped to HTTP status by the `[HttpStatusCode(...)]` attribute on each `ErrorCodes` value, so the endpoint may return 422/404 — the frontend hook must read the JSON body regardless of `response.ok`.
- **Validator registration is manual** per module (no `AddValidatorsFromAssembly`). Pattern: `services.AddScoped<IValidator<TReq>, TValidator>()` + `services.AddScoped<IPipelineBehavior<TReq,TResp>, ValidationBehavior<TReq,TResp>>()`. `ValidationBehavior` lives in `Anela.Heblo.Application.Common.Behaviors`.
- **Test projects:** Application/handler tests → `backend/test/Anela.Heblo.Tests`. Shoptet adapter tests (HTTP-faked, QuestPDF community license) → `backend/test/Anela.Heblo.Adapters.Shoptet.Tests`. The picking-source test pattern with a fake `HttpMessageHandler` already exists in `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`.

---

## File Structure

**Revert (Task 0):** removes `Infrastructure/Jobs/AutoPrintPickingListTask.cs`, its test, the `RegisterRefreshTask` block in `ExpeditionListModule.cs`, the `AutoPrintSourceStateId` field, and `appsettings*.json` entries.

**Create:**
- `backend/.../Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderRequest.cs`
- `.../PrintExpeditionOrder/PrintExpeditionOrderResponse.cs`
- `.../PrintExpeditionOrder/PrintExpeditionOrderRequestValidator.cs`
- `.../PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderRequestValidatorTests.cs`
- `frontend/src/components/modals/PrintOrderModal.tsx`

**Modify:**
- `backend/.../Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` (+`OrderCode`)
- `backend/.../Features/Logistics/Picking/PrintPickingListRequest.cs` (+`OrderCode`)
- `backend/.../Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` (copy `OrderCode`)
- `backend/src/Adapters/.../Orders/IShoptetExpeditionOrderSource.cs` (+`GetOrderByCodeAsync`)
- `backend/src/Adapters/.../Orders/ShoptetOrderClient.cs` (impl `GetOrderByCodeAsync`)
- `backend/src/Adapters/.../Expedition/ShoptetApiExpeditionListSource.cs` (single-order branch)
- `backend/.../Shared/ErrorCodes.cs` (+2 codes)
- `backend/.../Features/ExpeditionList/ExpeditionListModule.cs` (validator + behavior)
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs` (+endpoint)
- `frontend/src/api/hooks/useExpeditionList.ts` (+`usePrintExpeditionOrder`)
- `frontend/src/pages/ExpeditionListArchivePage.tsx` (button + modal wiring)
- `frontend/src/i18n.ts` (error messages)

---

## Task 0: Revert PR #3315

**Files:** removes the 6 files/changes from commit `62ad67171`.

- [ ] **Step 1: Revert the merged PR commit**

```bash
git revert 62ad67171 --no-edit
```

- [ ] **Step 2: Verify the auto-print code is gone**

Run: `grep -rn "AutoPrintPickingListTask\|AutoPrintSourceStateId\|AutoPrintPickingList" backend/ ; echo "exit:$?"`
Expected: no matches (grep prints nothing).

- [ ] **Step 3: Build the baseline**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

> Note: if `git revert` reports conflicts (unlikely — this branch has no later edits to those files), abort with `git revert --abort` and instead manually delete `AutoPrintPickingListTask.cs` + its test, remove the `RegisterRefreshTask(...)` block (lines ~18-29) from `ExpeditionListModule.cs`, delete the `AutoPrintSourceStateId` field from `PrintPickingListOptions.cs`, and revert the `appsettings.json` / `appsettings.Production.json` additions (`BackgroundRefresh:IExpeditionListService:AutoPrintPickingList`).

---

## Task 1: Add `OrderCode` to the request contracts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs`

- [ ] **Step 1: Add a failing test asserting the adapter forwards `OrderCode`**

Open `LogisticsExpeditionPickingAdapterTests.cs` and add this test method inside the existing test class (mirror the existing setup that mocks `IPickingListSource` and captures the `PrintPickingListRequest`):

```csharp
[Fact]
public async Task CreatePickingListAsync_ForwardsOrderCode_ToInnerRequest()
{
    // Arrange
    PrintPickingListRequest? captured = null;
    var inner = new Mock<IPickingListSource>();
    inner.Setup(s => s.CreatePickingList(
            It.IsAny<PrintPickingListRequest>(),
            It.IsAny<Func<IList<string>, Task>?>(),
            It.IsAny<CancellationToken>()))
        .Callback<PrintPickingListRequest, Func<IList<string>, Task>?, CancellationToken>(
            (req, _, _) => captured = req)
        .ReturnsAsync(new PrintPickingListResult { ExportedFiles = new List<string>(), TotalCount = 0 });
    var adapter = new LogisticsExpeditionPickingAdapter(inner.Object);

    // Act
    await adapter.CreatePickingListAsync(
        new ExpeditionPickingRequest { OrderCode = "0001234" }, null, CancellationToken.None);

    // Assert
    captured!.OrderCode.Should().Be("0001234");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests.CreatePickingListAsync_ForwardsOrderCode"`
Expected: FAIL to compile — `ExpeditionPickingRequest` / `PrintPickingListRequest` has no `OrderCode`.

- [ ] **Step 3: Add `OrderCode` to `ExpeditionPickingRequest`**

In `ExpeditionPickingRequest.cs`, add after the `SendToPrinter` property (line 14):

```csharp
    /// <summary>
    /// When set, the picking list is built for this single order code instead of
    /// fetching all orders in <see cref="SourceStateId"/>. Used by the manual
    /// "Tisknout zakázku" action.
    /// </summary>
    public string? OrderCode { get; set; }
```

- [ ] **Step 4: Add `OrderCode` to `PrintPickingListRequest`**

In `PrintPickingListRequest.cs`, add after `SendToPrinter` (line 15):

```csharp
    public string? OrderCode { get; set; }
```

- [ ] **Step 5: Copy `OrderCode` in the adapter**

In `LogisticsExpeditionPickingAdapter.cs`, add to the `innerRequest` initializer (after `SendToPrinter = request.SendToPrinter,`):

```csharp
            OrderCode = request.OrderCode,
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests.CreatePickingListAsync_ForwardsOrderCode"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: add OrderCode to expedition picking request contracts"
```

---

## Task 2: Add `GetOrderByCodeAsync` to the Shoptet expedition client

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetExpeditionOrderSource.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`

- [ ] **Step 1: Add failing tests for the new client method**

Add to `ShoptetApiExpeditionListSourceTests` (it already has `BuildClient`, `Json` helpers):

```csharp
[Fact]
public async Task GetOrderByCodeAsync_ReturnsOrder_WhenFound()
{
    // Arrange
    var client = BuildClient(req => Json(new CreateOrderResponse
    {
        Data = new CreateOrderData
        {
            Order = new OrderSummary { Code = "0001234", Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid } },
        },
    }));

    // Act
    var order = await client.GetOrderByCodeAsync("0001234", CancellationToken.None);

    // Assert
    order.Should().NotBeNull();
    order!.Code.Should().Be("0001234");
    order.Shipping!.Guid.Should().Be(ZasilkovnaDoRukyGuid);
}

[Fact]
public async Task GetOrderByCodeAsync_ReturnsNull_WhenNotFound()
{
    // Arrange
    var client = BuildClient(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));

    // Act
    var order = await client.GetOrderByCodeAsync("nope", CancellationToken.None);

    // Assert
    order.Should().BeNull();
}
```

> If `CreateOrderResponse` / `CreateOrderData` are not already imported, they live in `Anela.Heblo.Adapters.ShoptetApi.Orders.Model` (same namespace already imported in this test file via `using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;`). Verify the exact type names with `grep -rn "class CreateOrderResponse" backend/src/Adapters` and adjust if the inner property differs.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests.GetOrderByCodeAsync"`
Expected: FAIL to compile — `GetOrderByCodeAsync` not defined.

- [ ] **Step 3: Add the interface method**

In `IShoptetExpeditionOrderSource.cs`, add after `GetExpeditionOrderDetailAsync`:

```csharp
    Task<OrderSummary?> GetOrderByCodeAsync(string code, CancellationToken ct = default);
```

- [ ] **Step 4: Implement it in `ShoptetOrderClient`**

Add `using System.Net;` to the top of `ShoptetOrderClient.cs` (after the existing `using` block). Then add this method in the "Expedition methods" region (after `GetExpeditionOrderDetailAsync`, around line 206):

```csharp
    public async Task<OrderSummary?> GetOrderByCodeAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders/{code}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return data?.Data.Order;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests.GetOrderByCodeAsync"`
Expected: PASS (both).

- [ ] **Step 6: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: add GetOrderByCodeAsync to Shoptet expedition order client"
```

---

## Task 3: Single-order selection branch in the picking source

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`

- [ ] **Step 1: Add a failing test for the single-order branch**

Add to `ShoptetApiExpeditionListSourceTests`:

```csharp
[Fact]
public async Task CreatePickingList_WithOrderCode_PrintsSingleOrderAndChangesStateTo26()
{
    // Arrange — fake routes: GET /api/orders/{code} (summary), GET detail, PATCH status
    var statusUpdates = new List<(string code, int statusId)>();
    var client = BuildClient(req =>
    {
        var path = req.RequestUri!.AbsolutePath;
        if (req.Method == HttpMethod.Patch && path.EndsWith("/status"))
        {
            var code = path.Split('/')[3];
            statusUpdates.Add((code, 26));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
        if (path.Contains("include=") || path.EndsWith("/0001234")) // detail (include=stockLocation,notes)
            return Json(DetailFor("0001234"));
        if (path.EndsWith("/0001234")) // summary GET /api/orders/{code}
            return Json(new CreateOrderResponse
            {
                Data = new CreateOrderData { Order = new OrderSummary { Code = "0001234", Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid } } },
            });
        return Json(new CreateOrderResponse { Data = new CreateOrderData { Order = new OrderSummary { Code = "0001234", Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid } } } });
    });
    var source = BuildSource(client, generateDocument: _ => new byte[] { 1, 2, 3 });

    // Act
    var result = await source.CreatePickingList(
        new PrintPickingListRequest
        {
            OrderCode = "0001234",
            Carriers = new List<Carriers>(), // no carrier filter
            ChangeOrderState = true,
            DesiredStateId = 26,
            SendToPrinter = false,
        },
        onBatchFilesReady: null,
        CancellationToken.None);

    // Assert
    result.TotalCount.Should().Be(1);
    statusUpdates.Should().ContainSingle().Which.Should().Be(("0001234", 26));
}
```

> The fake-handler routing above is intentionally explicit. When implementing, confirm the exact request URLs by reading how `GetExpeditionOrderDetailAsync` (`?include=stockLocation,notes`) and `GetOrderByCodeAsync` (`/api/orders/{code}`) are formed, and order the `if` branches so the detail (`include=`) check precedes the plain-summary check. Reuse the existing `DetailFor` and `Json` helpers.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~CreatePickingList_WithOrderCode"`
Expected: FAIL — `OrderCode` is ignored, no order fetched, `TotalCount == 0`.

- [ ] **Step 3: Add the selection branch and helper**

In `ShoptetApiExpeditionListSource.cs`, replace the first line of `CreatePickingList`:

```csharp
        var allOrders = await FetchAllOrdersAsync(request.SourceStateId, cancellationToken);
```

with:

```csharp
        var allOrders = string.IsNullOrEmpty(request.OrderCode)
            ? await FetchAllOrdersAsync(request.SourceStateId, cancellationToken)
            : await FetchSingleOrderAsync(request.OrderCode, cancellationToken);
```

Then add this helper next to `FetchAllOrdersAsync`:

```csharp
    private async Task<List<OrderSummary>> FetchSingleOrderAsync(string code, CancellationToken ct)
    {
        var order = await _client.GetOrderByCodeAsync(code, ct);
        return order is null ? new List<OrderSummary>() : new List<OrderSummary> { order };
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~CreatePickingList_WithOrderCode"`
Expected: PASS.

- [ ] **Step 5: Run the full picking-source test class to confirm no regression**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests"`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: select single order by code in expedition picking source"
```

---

## Task 4: Add error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add two error codes in the ShoptetOrders (21XX) block**

In `ErrorCodes.cs`, immediately after `ShoptetOrderNotFound = 2102,` (line 233), add:

```csharp
    // Manual expedition single-order print
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ExpeditionOrderInvalidState = 2103,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ExpeditionOrderNotPrinted = 2104,
```

- [ ] **Step 2: Build to confirm the enum compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add expedition single-order print error codes"
```

---

## Task 5: PrintExpeditionOrder use case (request, response, validator, handler)

**Files:**
- Create: `backend/.../Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderRequest.cs`
- Create: `.../PrintExpeditionOrderResponse.cs`
- Create: `.../PrintExpeditionOrderRequestValidator.cs`
- Create: `.../PrintExpeditionOrderHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderRequestValidatorTests.cs`

- [ ] **Step 1: Create the request**

`PrintExpeditionOrderRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderRequest : IRequest<PrintExpeditionOrderResponse>
{
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 2: Create the response**

`PrintExpeditionOrderResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderResponse : BaseResponse
{
    public PrintExpeditionOrderResponse() { }

    public PrintExpeditionOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params) { }
}
```

- [ ] **Step 3: Create the validator**

`PrintExpeditionOrderRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderRequestValidator : AbstractValidator<PrintExpeditionOrderRequest>
{
    public PrintExpeditionOrderRequestValidator()
    {
        RuleFor(x => x.OrderCode).NotEmpty();
    }
}
```

- [ ] **Step 4: Write the failing handler tests**

`PrintExpeditionOrderHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class PrintExpeditionOrderHandlerTests
{
    private readonly Mock<IExpeditionListService> _service = new();
    private readonly Mock<IEshopOrderClient> _client = new();

    private PrintExpeditionOrderHandler CreateHandler() => new(
        _service.Object,
        _client.Object,
        Options.Create(new PrintPickingListOptions()),
        new Mock<ILogger<PrintExpeditionOrderHandler>>().Object);

    [Theory]
    [InlineData(-3)]
    [InlineData(26)]
    [InlineData(52)]
    [InlineData(70)]
    public async Task Handle_OrderInNonPrintableState_ReturnsInvalidStateError(int statusId)
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(statusId);

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ExpeditionOrderInvalidState);
        _service.Verify(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidState_PrintsWithOrderCodeAndDesiredState26()
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        ExpeditionPickingRequest? captured = null;
        _service.Setup(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .Callback<ExpeditionPickingRequest, IList<string>?, CancellationToken>((r, _, _) => captured = r)
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 });

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        captured!.OrderCode.Should().Be("0001234");
        captured.DesiredStateId.Should().Be(26);
        captured.ChangeOrderState.Should().BeTrue();
        captured.SendToPrinter.Should().BeTrue();
        captured.Carriers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NothingPrinted_ReturnsNotPrintedError()
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        _service.Setup(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 0 });

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ExpeditionOrderNotPrinted);
    }

    [Fact]
    public async Task Handle_OrderLookupThrows_ReturnsNotFoundError()
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("nope", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404"));

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "nope" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
    }
}
```

- [ ] **Step 5: Run the handler tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"`
Expected: FAIL to compile — `PrintExpeditionOrderHandler` not defined.

- [ ] **Step 6: Implement the handler**

`PrintExpeditionOrderHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderHandler : IRequestHandler<PrintExpeditionOrderRequest, PrintExpeditionOrderResponse>
{
    // -3 = cancelled/blocked; 26 = Balí se; 52 = Zabaleno; 70 = Předáno přepravci.
    // These are already-in-progress / done / cancelled states — printing them would double-print.
    private static readonly int[] NonPrintableStateIds = { -3, 26, 52, 70 };

    private readonly IExpeditionListService _expeditionListService;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly ILogger<PrintExpeditionOrderHandler> _logger;

    public PrintExpeditionOrderHandler(
        IExpeditionListService expeditionListService,
        IEshopOrderClient eshopOrderClient,
        IOptions<PrintPickingListOptions> options,
        ILogger<PrintExpeditionOrderHandler> logger)
    {
        _expeditionListService = expeditionListService;
        _eshopOrderClient = eshopOrderClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PrintExpeditionOrderResponse> Handle(
        PrintExpeditionOrderRequest request,
        CancellationToken cancellationToken)
    {
        int currentStatusId;
        try
        {
            currentStatusId = await _eshopOrderClient.GetOrderStatusIdAsync(request.OrderCode, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Order {OrderCode} status lookup failed", request.OrderCode);
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ShoptetOrderNotFound,
                new Dictionary<string, string> { { "orderCode", request.OrderCode } });
        }

        if (NonPrintableStateIds.Contains(currentStatusId))
        {
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ExpeditionOrderInvalidState,
                new Dictionary<string, string>
                {
                    { "orderCode", request.OrderCode },
                    { "currentStatusId", currentStatusId.ToString() },
                });
        }

        var printRequest = new ExpeditionPickingRequest
        {
            OrderCode = request.OrderCode,
            Carriers = new List<Carriers>(),
            DesiredStateId = _options.Value.DesiredStateId,
            ChangeOrderState = true,
            SendToPrinter = true,
        };

        var result = await _expeditionListService.PrintPickingListAsync(
            printRequest, cancellationToken: cancellationToken);

        if (result.TotalCount == 0)
        {
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ExpeditionOrderNotPrinted,
                new Dictionary<string, string> { { "orderCode", request.OrderCode } });
        }

        return new PrintExpeditionOrderResponse();
    }
}
```

- [ ] **Step 7: Run the handler tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"`
Expected: PASS (all 7 cases).

- [ ] **Step 8: Write and run the validator test**

`PrintExpeditionOrderRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class PrintExpeditionOrderRequestValidatorTests
{
    private readonly PrintExpeditionOrderRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyOrderCode_Fails()
    {
        var result = _validator.Validate(new PrintExpeditionOrderRequest { OrderCode = "" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NonEmptyOrderCode_Passes()
    {
        var result = _validator.Validate(new PrintExpeditionOrderRequest { OrderCode = "0001234" });
        result.IsValid.Should().BeTrue();
    }
}
```

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~PrintExpeditionOrderRequestValidatorTests"`
Expected: PASS (both).

- [ ] **Step 9: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: add PrintExpeditionOrder use case with state validation"
```

---

## Task 6: Register validator + ValidationBehavior in the module

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/ExpeditionListModule.cs`

- [ ] **Step 1: Add the registration**

Add these `using` directives to the top of `ExpeditionListModule.cs`:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;
using FluentValidation;
using MediatR;
```

Inside `AddExpeditionListModule`, after `services.AddScoped<IExpeditionListService, ExpeditionListService>();`, add:

```csharp
        services.AddScoped<IValidator<PrintExpeditionOrderRequest>, PrintExpeditionOrderRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<PrintExpeditionOrderRequest, PrintExpeditionOrderResponse>,
            ValidationBehavior<PrintExpeditionOrderRequest, PrintExpeditionOrderResponse>>();
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/ExpeditionListModule.cs
git commit -m "chore: register PrintExpeditionOrder validator and behavior"
```

---

## Task 7: Controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs`

- [ ] **Step 1: Add the endpoint**

Add `using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;` to the controller's usings, then add this action inside `ExpeditionListController` (after `RunFix`):

```csharp
    [HttpPost("print-order")]
    [FeatureAuthorize(Feature.Warehouse_Expedition, AccessLevel.Write)]
    public async Task<ActionResult<PrintExpeditionOrderResponse>> PrintOrder(
        [FromBody] PrintExpeditionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
```

- [ ] **Step 2: Build the API project**

Run: `cd backend && dotnet build src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs
git commit -m "feat: add POST /api/expedition-list/print-order endpoint"
```

---

## Task 8: Frontend hook `usePrintExpeditionOrder`

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionList.ts`

- [ ] **Step 1: Add the mutation hook**

Replace the contents of `useExpeditionList.ts` with (keeps the existing `useRunExpeditionListPrintFix`, adds the new hook):

```typescript
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { BaseResponse } from "../../types/errors";

export const useRunExpeditionListPrintFix = () => {
  return useMutation({
    mutationFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list/run-fix`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`,
        );
      }

      return await response.json();
    },
  });
};

export const usePrintExpeditionOrder = () => {
  return useMutation<BaseResponse, Error, { orderCode: string }>({
    mutationFn: async ({ orderCode }): Promise<BaseResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list/print-order`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ orderCode }),
      });

      // The handler returns a BaseResponse body even for failures (mapped to
      // 4xx by the ErrorCodes HttpStatusCode attribute), so read the body first.
      const data = await response.json().catch(() => null);
      if (data && typeof data.success === "boolean") {
        return {
          success: data.success,
          errorCode: data.errorCode ?? undefined,
          params: data.params ?? undefined,
        };
      }

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      return { success: true };
    },
  });
};
```

- [ ] **Step 2: Type-check the frontend**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors in `useExpeditionList.ts`. (A full `npm run build` runs in Task 11.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionList.ts
git commit -m "feat: add usePrintExpeditionOrder mutation hook"
```

---

## Task 9: PrintOrderModal component

**Files:**
- Create: `frontend/src/components/modals/PrintOrderModal.tsx`

- [ ] **Step 1: Create the modal**

`PrintOrderModal.tsx`:

```typescript
import { useState, useEffect } from "react";
import { X, Printer, Hash } from "lucide-react";
import { usePrintExpeditionOrder } from "../../api/hooks/useExpeditionList";
import { getErrorMessage } from "../../utils/errorHandler";

interface PrintOrderModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (orderCode: string) => void;
}

function PrintOrderModal({ isOpen, onClose, onSuccess }: PrintOrderModalProps) {
  const [orderCode, setOrderCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const printOrderMutation = usePrintExpeditionOrder();

  useEffect(() => {
    if (isOpen) {
      setOrderCode("");
      setError(null);
    }
  }, [isOpen]);

  const handleClose = () => {
    if (!printOrderMutation.isPending) {
      onClose();
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (printOrderMutation.isPending) return;

    const trimmed = orderCode.trim();
    if (!trimmed) {
      setError("Zadejte číslo zakázky.");
      return;
    }
    setError(null);

    try {
      const result = await printOrderMutation.mutateAsync({ orderCode: trimmed });
      if (result.success) {
        onSuccess(trimmed);
      } else {
        setError(
          result.errorCode
            ? getErrorMessage(result.errorCode, result.params ?? undefined)
            : "Zakázku se nepodařilo vytisknout.",
        );
      }
    } catch {
      setError("Zakázku se nepodařilo vytisknout. Zkuste to znovu.");
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
        <div className="flex items-center justify-between p-6 border-b">
          <div className="flex items-center space-x-2">
            <Printer className="h-5 w-5 text-indigo-600" />
            <h2 className="text-lg font-semibold text-gray-900">Tisknout zakázku</h2>
          </div>
          <button
            onClick={handleClose}
            disabled={printOrderMutation.isPending}
            className="p-2 hover:bg-gray-100 rounded-full transition-colors disabled:opacity-50"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5 text-gray-500" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <p className="text-sm text-gray-600">
            Zadejte číslo zakázky. Zakázka bude vytištěna na expediční list a převedena do stavu „Balí se".
          </p>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <div className="flex items-center space-x-1">
                <Hash className="h-4 w-4" />
                <span>Číslo zakázky</span>
              </div>
            </label>
            <input
              type="text"
              value={orderCode}
              onChange={(e) => setOrderCode(e.target.value)}
              placeholder="např. 0001234"
              autoFocus
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
              disabled={printOrderMutation.isPending}
            />
          </div>

          {error && (
            <div className="p-3 bg-red-100 border border-red-300 text-red-700 rounded-md text-sm">
              {error}
            </div>
          )}

          <div className="flex justify-end space-x-3 pt-2">
            <button
              type="button"
              onClick={handleClose}
              disabled={printOrderMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-md transition-colors disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={printOrderMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md transition-colors disabled:opacity-50 flex items-center space-x-1"
            >
              {printOrderMutation.isPending ? (
                <>
                  <div className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></div>
                  <span>Tisknu...</span>
                </>
              ) : (
                <>
                  <Printer className="h-4 w-4" />
                  <span>Tisknout</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export default PrintOrderModal;
```

- [ ] **Step 2: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors in `PrintOrderModal.tsx`.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/modals/PrintOrderModal.tsx
git commit -m "feat: add PrintOrderModal for single-order expedition print"
```

---

## Task 10: Wire button into the page + add i18n messages

**Files:**
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx`
- Modify: `frontend/src/i18n.ts`

- [ ] **Step 1: Add error messages to i18n**

In `frontend/src/i18n.ts`, after the line `ShoptetOrderNotFound: "Objednávka nebyla nalezena",` (line 214), add:

```typescript
        ExpeditionOrderInvalidState: "Zakázku nelze vytisknout – je ve stavu {currentStatusId}",
        ExpeditionOrderNotPrinted: "Zakázku se nepodařilo vytisknout – zkontrolujte způsob dopravy",
```

- [ ] **Step 2: Import the modal and `Printer` icon usage**

In `ExpeditionListArchivePage.tsx`, the `Printer` icon is already imported (line 2). Add the modal import after the existing hook imports (after line 17):

```typescript
import PrintOrderModal from "../components/modals/PrintOrderModal";
```

- [ ] **Step 3: Add modal open state**

After `const [reprintConfirm, setReprintConfirm] = useState<ExpeditionListItemDto | null>(null);` (line 51), add:

```typescript
  const [isPrintOrderModalOpen, setIsPrintOrderModalOpen] = useState(false);
```

- [ ] **Step 4: Add the success handler**

After `handleRunFix` (after line 133), add:

```typescript
  const handlePrintOrderSuccess = async (orderCode: string) => {
    setIsPrintOrderModalOpen(false);
    showSuccess("Zakázka vytištěna", `Zakázka ${orderCode} byla odeslána na tisk a převedena do stavu „Balí se".`);
    await queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
  };
```

- [ ] **Step 5: Add the toolbar button**

In the toolbar, immediately before the "Spustit tisk oprav" button (before line 196, the `<button onClick={handleRunFix} ...>`), add:

```typescript
          <button
            onClick={() => setIsPrintOrderModalOpen(true)}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 transition-colors"
          >
            <Printer size={14} />
            Tisknout zakázku
          </button>
```

- [ ] **Step 6: Render the modal**

Just before the final closing `</div>` of the component's returned JSX (at the end of the `return`, alongside where `reprintConfirm` modal is rendered ~line 349-380), add:

```typescript
      <PrintOrderModal
        isOpen={isPrintOrderModalOpen}
        onClose={() => setIsPrintOrderModalOpen(false)}
        onSuccess={handlePrintOrderSuccess}
      />
```

- [ ] **Step 7: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/ExpeditionListArchivePage.tsx frontend/src/i18n.ts
git commit -m "feat: add Tisknout zakazku button and modal to expedition page"
```

---

## Task 11: Full verification

- [ ] **Step 1: Backend build + format + tests**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes || dotnet format
dotnet test
```
Expected: Build succeeded; format clean; all tests pass (including the `*Response : BaseResponse` reflection contract test and the new tests).

- [ ] **Step 2: Frontend build + lint**

Run:
```bash
cd frontend
npm run build
npm run lint
```
Expected: build succeeds (generated TS client now includes `ExpeditionListController_PrintOrder` and the two new `ErrorCodes`), lint clean. Note: `npm run build` is stricter than `tsc --noEmit` — generated client enums are strings.

- [ ] **Step 3: Manual / E2E smoke test against staging**

1. Open "Tisk expedice" (`/logistics/expedition-archive`).
2. Click "Tisknout zakázku" → enter a known order code in a printable state (e.g. -2) → confirm: a new expedition list appears in the archive and the order moves to state 26 ("Balí se") in Shoptet.
3. Enter an order in state 26/52/70 or a cancelled order (-3) → confirm an inline error appears, no print, no state change.
4. Enter a non-existent code → confirm "Objednávka nebyla nalezena".

- [ ] **Step 4: Final commit (if format/lint changed anything)**

```bash
git add -A
git commit -m "chore: apply formatting after expedition single-order print feature"
```

---

## Self-review notes

- **Spec coverage:** rollback (Task 0), button + modal + order input + OK/Cancel (Tasks 9-10), validation not -3 plus already-packed states (Task 5, `NonPrintableStateIds`), print single order onto expedition list (Tasks 1-3), state → 26 (handler sets `DesiredStateId = options.DesiredStateId` which defaults to 26, `ChangeOrderState = true`; the picking source applies it). ✔
- **Type consistency:** `OrderCode` is `string?` on both request contracts and the adapter; `GetOrderByCodeAsync` returns `OrderSummary?`; handler ctor arg order `(IExpeditionListService, IEshopOrderClient, IOptions<PrintPickingListOptions>, ILogger<...>)` matches the test's `CreateHandler`. ✔
- **Reuse:** no new PDF/print/state-change code — only a selection branch + a thin client method. ✔
- **Open item to verify during Task 2/3:** the exact `CreateOrderResponse`/`CreateOrderData` property names and the detail-vs-summary URL routing in the fake handler — confirm by grep before finalizing those tests.
