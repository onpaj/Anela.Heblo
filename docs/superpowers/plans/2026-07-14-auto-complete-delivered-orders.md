# Auto-Complete Delivered Shoptet Orders — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an hourly Hangfire job that finds Shoptet orders in the "handed to carrier" states (70, 82) whose shipment has reached the `delivered` status and flips them to the "vyřízena" state (-3), appending an audit note.

**Architecture:** A self-contained `IRecurringJob` (`CompleteDeliveredOrdersJob`) — no MediatR handler — mirroring the existing `FillTrackingNumbersJob`. It queries orders per source state via the Shoptet REST client, checks each order's shipments for a `delivered` status, and PATCHes the order status + appends an internal remark. State IDs live in `ShoptetOrdersSettings` (defaults 70/82 → -3, overridable per environment). The job is auto-discovered by `AddRecurringJobs()` (Application-assembly scan) and only runs where `Hangfire:SchedulerEnabled` is true (Production).

**Tech Stack:** .NET 8, Hangfire (`IRecurringJob`), typed `HttpClient` Shoptet REST adapters, xUnit + Moq + FluentAssertions.

**Feasibility (confirmed):** The shipment status lifecycle — including `delivered` — is documented in `docs/integrations/shoptet-api.md:1220-1236`, and `ShoptetShipmentDto.Status` already deserializes it. Orders-by-state (`GET /api/orders?statusId=`) and status change (`PATCH /api/orders/{code}/status`) are already wrapped in `ShoptetOrderClient`.

---

## Design decisions (confirmed with user)

- **Poll cadence:** hourly (`0 * * * *`).
- **Audit note:** on completion, append `"Automaticky vyřízeno – zásilka doručena"` to the order's internal (eshop) remark via read-modify-write, exactly like `BlockOrderProcessingHandler`.
- **Match rule:** an order qualifies if **ANY** of its shipments has status `delivered` (case-insensitive).
- **Logic location:** directly in the job (KISS/YAGNI), following `FillTrackingNumbersJob` — no MediatR request/handler, no controller endpoint (manual runs use the existing `RecurringJobsController` trigger endpoint / Hangfire dashboard).

## File structure

| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` (modify) | Add `DeliveredCompletionSourceStateIds` (default `[70,82]`) + `CompletedStatusId` (default `-3`) |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs` (modify) | Declare `HasDeliveredShipmentAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` (modify) | Implement `HasDeliveredShipmentAsync` (reuse private `FetchShipmentsAsync`) |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` (modify) | Declare `ListOrdersByStatusAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` (modify) | Implement `ListOrdersByStatusAsync` (paginate + map to `EshopOrderSummary`) |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs` (create) | The recurring job — orchestration, logging, per-order error isolation |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs` (modify) | Adapter tests for `HasDeliveredShipmentAsync` |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs` (modify) | Adapter test for `ListOrdersByStatusAsync` (mapping + pagination) |
| `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs` (create) | Job unit tests (primary coverage) |

**Build/test commands** (whole plan): `dotnet build`, `dotnet format`, and
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrders|FullyQualifiedName~ShoptetApi"`.
See the note in `memory/gotchas/gotcha_dotnet_test_worktree_contention.md` if `dotnet test` hangs.

---

## Task 1: Config — source/target state IDs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs`

- [ ] **Step 1: Add the two properties**

Add inside the `ShoptetOrdersSettings` class (after `ProcessingStateId`):

```csharp
    /// <summary>
    /// Shoptet order status IDs whose orders are polled by the auto-completion job.
    /// These are the "handed to carrier" states (70 "Předáno přepravci",
    /// 82 "SMS chlazené-Předáno dopravci"). An order in one of these states is moved to
    /// <see cref="CompletedStatusId"/> once any of its shipments reports "delivered".
    /// </summary>
    public int[] DeliveredCompletionSourceStateIds { get; set; } = [70, 82];

    /// <summary>
    /// Shoptet order status ID assigned when a delivered order is auto-completed.
    /// Defaults to -3 ("Vyřízena").
    /// </summary>
    public int CompletedStatusId { get; set; } = -3;
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs
git commit -m "feat: add delivered-order completion state config to ShoptetOrdersSettings"
```

---

## Task 2: `HasDeliveredShipmentAsync` on the shipment client

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs`

- [ ] **Step 1: Write the failing adapter tests**

Append to the `ShoptetShipmentClientTests` class (the `BuildClient`/`Json` helpers already exist in the file):

```csharp
    [Fact]
    public async Task HasDeliveredShipmentAsync_ReturnsTrue_WhenAnyShipmentDelivered()
    {
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new { guid = Guid.NewGuid(), orderCode = "0001234", status = "in_transit", packages = Array.Empty<object>() },
                    new { guid = Guid.NewGuid(), orderCode = "0001234", status = "delivered", packages = Array.Empty<object>() },
                },
            },
            errors = Array.Empty<object>(),
        }));

        var result = await client.HasDeliveredShipmentAsync("0001234");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasDeliveredShipmentAsync_ReturnsFalse_WhenNoShipmentDelivered()
    {
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new { guid = Guid.NewGuid(), orderCode = "0001234", status = "in_transit", packages = Array.Empty<object>() },
                },
            },
            errors = Array.Empty<object>(),
        }));

        var result = await client.HasDeliveredShipmentAsync("0001234");

        result.Should().BeFalse();
    }
```

- [ ] **Step 2: Declare the method on the interface**

In `IShipmentClient.cs`, add after `GetLatestActiveTrackingNumberAsync`:

```csharp
    /// <summary>
    /// Returns true if the order has at least one shipment whose status is "delivered".
    /// Uses GET /api/shipments?orderCode={code}; status values follow the Shoptet lifecycle
    /// documented in docs/integrations/shoptet-api.md.
    /// </summary>
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HasDeliveredShipmentAsync"`
Expected: FAIL — `ShoptetShipmentClient` does not implement the new interface member (compile error).

- [ ] **Step 4: Implement the method**

In `ShoptetShipmentClient.cs`, add the constant next to `DeadStatuses`:

```csharp
    private const string DeliveredStatus = "delivered";
```

and add the method (e.g. after `GetLatestActiveTrackingNumberAsync`):

```csharp
    public async Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)
    {
        var items = await FetchShipmentsAsync(orderCode, ct);
        return items.Any(s => string.Equals(s.Status, DeliveredStatus, StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HasDeliveredShipmentAsync"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs
git commit -m "feat: add HasDeliveredShipmentAsync to shipment client"
```

---

## Task 3: `ListOrdersByStatusAsync` on the order client

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

> **Why a new method:** the concrete client already has `GetOrdersByStatusAsync(int statusId, int page)` returning the raw adapter DTO `OrderListResponse` (declared on `IShoptetExpeditionOrderSource`, single page). The job needs an Application-layer type across ALL pages, so we add `ListOrdersByStatusAsync` on `IEshopOrderClient` returning `List<EshopOrderSummary>` and implement it by looping the existing raw method. Do not overload/rename the existing method.

- [ ] **Step 1: Write the failing adapter test (mapping + pagination)**

Append to the `ShoptetOrderClientTests` class (`BuildClient`/`Json` helpers already exist). This test drives two pages using the `?page=` query param:

```csharp
    [Fact]
    public async Task ListOrdersByStatusAsync_PaginatesAndMapsAllOrders()
    {
        var client = BuildClient(request =>
        {
            var page = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"];
            if (page == "1")
            {
                return Json(new
                {
                    data = new
                    {
                        orders = new[]
                        {
                            new { code = "ORD-1", externalCode = "EXT-1", email = "a@example.com", status = new { id = 70 } },
                        },
                        paginator = new { pageCount = 2, page = 1, totalCount = 2 },
                    },
                });
            }

            return Json(new
            {
                data = new
                {
                    orders = new[]
                    {
                        new { code = "ORD-2", externalCode = (string?)null, email = "b@example.com", status = new { id = 70 } },
                    },
                    paginator = new { pageCount = 2, page = 2, totalCount = 2 },
                },
            });
        });

        var result = await client.ListOrdersByStatusAsync(70);

        result.Should().HaveCount(2);
        result[0].Code.Should().Be("ORD-1");
        result[0].ExternalCode.Should().Be("EXT-1");
        result[0].Email.Should().Be("a@example.com");
        result[0].StatusId.Should().Be(70);
        result[1].Code.Should().Be("ORD-2");
    }
```

> If `System.Web` is not referenced by the test project, parse the page instead with:
> `var page = request.RequestUri!.Query.Contains("page=2") ? "2" : "1";`

- [ ] **Step 2: Declare the method on the interface**

In `IEshopOrderClient.cs`, add after `GetRecentOrdersAsync`:

```csharp
    /// <summary>
    /// Returns every order currently in the given Shoptet status, across all pages
    /// (GET /api/orders?statusId={id}, itemsPerPage=50). Maps to the Application-layer
    /// <see cref="EshopOrderSummary"/> (code, externalCode, email, statusId).
    /// </summary>
    Task<List<EshopOrderSummary>> ListOrdersByStatusAsync(int statusId, CancellationToken ct = default);
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListOrdersByStatusAsync"`
Expected: FAIL — `ShoptetOrderClient` does not implement the new interface member (compile error).

- [ ] **Step 4: Implement the method**

In `ShoptetOrderClient.cs`, add (near the existing `GetOrdersByStatusAsync`):

```csharp
    public async Task<List<EshopOrderSummary>> ListOrdersByStatusAsync(int statusId, CancellationToken ct = default)
    {
        var result = new List<EshopOrderSummary>();
        var page = 1;

        while (true)
        {
            var data = await GetOrdersByStatusAsync(statusId, page, ct);

            result.AddRange(data.Data.Orders.Select(o => new EshopOrderSummary
            {
                Code = o.Code,
                ExternalCode = o.ExternalCode,
                Email = o.Email,
                StatusId = o.Status.Id,
            }));

            if (page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListOrdersByStatusAsync"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs
git commit -m "feat: add ListOrdersByStatusAsync to eshop order client"
```

---

## Task 4: The `CompleteDeliveredOrdersJob` recurring job

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`

- [ ] **Step 1: Write the failing job unit tests**

Create `CompleteDeliveredOrdersJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class CompleteDeliveredOrdersJobTests
{
    private static (
        CompleteDeliveredOrdersJob Sut,
        Mock<IEshopOrderClient> Orders,
        Mock<IShipmentClient> Shipments,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true)
    {
        var orders = new Mock<IEshopOrderClient>();
        var shipments = new Mock<IShipmentClient>();
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);

        var settings = Options.Create(new ShoptetOrdersSettings
        {
            DeliveredCompletionSourceStateIds = [70, 82],
            CompletedStatusId = -3,
        });

        var sut = new CompleteDeliveredOrdersJob(
            orders.Object, shipments.Object, settings,
            statusChecker.Object, NullLogger<CompleteDeliveredOrdersJob>.Instance);
        return (sut, orders, shipments, statusChecker);
    }

    private static EshopOrderSummary Order(string code, int statusId) =>
        new() { Code = code, StatusId = statusId };

    [Fact]
    public async Task ExecuteAsync_SkipsWork_WhenJobDisabled()
    {
        var (sut, orders, shipments, _) = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        orders.Verify(o => o.ListOrdersByStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        shipments.Verify(s => s.HasDeliveredShipmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CompletesOrder_WhenShipmentDelivered()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-1", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        orders.Setup(o => o.GetEshopRemarkAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync("ORD-1", -3, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.UpdateEshopRemarkAsync(
            "ORD-1", "Automaticky vyřízeno – zásilka doručena", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AppendsNote_PreservingExistingRemark()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-1", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        orders.Setup(o => o.GetEshopRemarkAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing");

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateEshopRemarkAsync(
            "ORD-1", "existing\nAutomaticky vyřízeno – zásilka doručena", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotComplete_WhenNoShipmentDelivered()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-1", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesBothSourceStates()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-70", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-82", 82)]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        orders.Setup(o => o.GetEshopRemarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync("ORD-70", -3, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.UpdateStatusAsync("ORD-82", -3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesProcessing_WhenOneOrderThrows()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-FAIL", 70), Order("ORD-OK", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-FAIL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-OK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        orders.Setup(o => o.GetEshopRemarkAsync("ORD-OK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync("ORD-OK", -3, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.UpdateStatusAsync("ORD-FAIL", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CompleteDeliveredOrdersJobTests"`
Expected: FAIL — `CompleteDeliveredOrdersJob` does not exist (compile error).

- [ ] **Step 3: Implement the job**

Create `CompleteDeliveredOrdersJob.cs`:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;

public sealed class CompleteDeliveredOrdersJob : IRecurringJob
{
    private const string CompletionNote = "Automaticky vyřízeno – zásilka doručena";

    private readonly IEshopOrderClient _orderClient;
    private readonly IShipmentClient _shipmentClient;
    private readonly ShoptetOrdersSettings _settings;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<CompleteDeliveredOrdersJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "complete-delivered-orders",
        DisplayName = "Complete Delivered Orders",
        Description = "Moves Shoptet orders in the 'handed to carrier' states to 'vyřízena' once any of their shipments reports delivered.",
        CronExpression = "0 * * * *",
        DefaultIsEnabled = true,
    };

    public CompleteDeliveredOrdersJob(
        IEshopOrderClient orderClient,
        IShipmentClient shipmentClient,
        IOptions<ShoptetOrdersSettings> settings,
        IRecurringJobStatusChecker statusChecker,
        ILogger<CompleteDeliveredOrdersJob> logger)
    {
        _orderClient = orderClient;
        _shipmentClient = shipmentClient;
        _settings = settings.Value;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var targetState = _settings.CompletedStatusId;
        var scanned = 0;
        var completed = 0;

        foreach (var stateId in _settings.DeliveredCompletionSourceStateIds)
        {
            List<EshopOrderSummary> orders;
            try
            {
                orders = await _orderClient.ListOrdersByStatusAsync(stateId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CompleteDeliveredOrders: failed to list orders in state {StateId}. Skipping this state.",
                    stateId);
                continue;
            }

            foreach (var order in orders)
            {
                scanned++;
                try
                {
                    if (!await _shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken))
                        continue;

                    await _orderClient.UpdateStatusAsync(order.Code, targetState, cancellationToken);
                    await AppendCompletionNoteAsync(order.Code, cancellationToken);
                    completed++;

                    _logger.LogInformation(
                        "CompleteDeliveredOrders: order {OrderCode} moved from state {StateId} to {TargetState} (delivered).",
                        order.Code, stateId, targetState);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "CompleteDeliveredOrders: failed to complete order {OrderCode}. Will retry next run.",
                        order.Code);
                }
            }
        }

        _logger.LogInformation(
            "CompleteDeliveredOrders: scanned {Scanned} order(s), completed {Completed}.",
            scanned, completed);
    }

    private async Task AppendCompletionNoteAsync(string orderCode, CancellationToken cancellationToken)
    {
        try
        {
            var currentRemark = await _orderClient.GetEshopRemarkAsync(orderCode, cancellationToken);
            var updatedRemark = string.IsNullOrEmpty(currentRemark)
                ? CompletionNote
                : $"{currentRemark}\n{CompletionNote}";
            await _orderClient.UpdateEshopRemarkAsync(orderCode, updatedRemark, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "CompleteDeliveredOrders: order {OrderCode} was completed but the note could not be appended.",
                orderCode);
        }
    }
}
```

> Note: the state change (`UpdateStatusAsync`) is the source of truth; a note failure is swallowed
> (order still counts as completed), matching `BlockOrderProcessingHandler`. No DI registration is
> needed — the class lives in `Anela.Heblo.Application` and is auto-discovered by `AddRecurringJobs()`
> (assembly scan) and seeded by `SeedRecurringJobConfigurationsAsync`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CompleteDeliveredOrdersJobTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs \
        backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs
git commit -m "feat: add hourly job to complete delivered Shoptet orders"
```

---

## Task 5: Final validation & docs

- [ ] **Step 1: Format and full build**

Run: `dotnet format backend/Anela.Heblo.sln` then `dotnet build backend/Anela.Heblo.sln`
Expected: no formatting diffs left uncommitted; Build succeeded.

- [ ] **Step 2: Run the full touched test scope**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrders|FullyQualifiedName~ShoptetApi"`
Expected: all green (including pre-existing `BlockOrderProcessingHandlerTests` and shipment/order client tests, confirming the new interface members didn't break other implementers/mocks).

- [ ] **Step 3: Note the discovered status usage in the Shoptet doc**

Add a short line to `docs/integrations/shoptet-api.md` under the shipment status lifecycle table noting that Anela consumes `delivered` in `CompleteDeliveredOrdersJob` (states 70/82 → -3). Commit:

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs: note delivered-status consumption in shoptet-api.md"
```

- [ ] **Step 4: Production verification (post-deploy)**

The job only runs where `Hangfire:SchedulerEnabled` is true (Production) — it will NOT fire in staging.
1. Read-only sanity against the live store: `GET /api/shipments?orderCode=126015719` (the order from the request screenshot) and confirm a shipment returns `"status": "delivered"`.
2. Trigger the job on demand via the `RecurringJobsController` trigger endpoint (or the Hangfire dashboard) instead of waiting for the top-of-hour schedule.
3. In App Insights, confirm the `CompleteDeliveredOrders: scanned N, completed M` summary log, and spot-check that a 70/82 order with a delivered shipment moved to -3 and received the "Automaticky vyřízeno – zásilka doručena" remark.
4. Confirm idempotency: a second run does not reprocess already-completed orders (they no longer appear in the 70/82 queries).

---

## Self-review notes

- **Spec coverage:** poll states 70/82 (Task 1 config + Task 4 loop) · detect delivered shipment (Task 2) · move to -3 (Task 4 `UpdateStatusAsync`) · hourly (Task 4 cron) · audit note (Task 4 `AppendCompletionNoteAsync`) · ANY-delivered rule (Task 2 `.Any`). All requirements have a task.
- **Type consistency:** `ListOrdersByStatusAsync(int, CancellationToken)` and `HasDeliveredShipmentAsync(string, CancellationToken)` are declared and consumed with identical signatures across interface, impl, job, and tests. `CompletedStatusId`/`DeliveredCompletionSourceStateIds` names match between settings and job.
- **Rate/volume:** hourly cadence; each qualifying order = 1 shipments GET (+ status PATCH + 2 note calls on match). Steady-state 70/82 backlog is small; a per-run cap is intentionally omitted (YAGNI) — revisit only if the initial backlog is large.
