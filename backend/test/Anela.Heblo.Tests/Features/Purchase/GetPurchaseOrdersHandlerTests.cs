using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrders;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseOrdersHandlerTests
{
    private const string TestUser = "test-user";

    private static PurchaseOrder NewOrder(
        string orderNumber,
        string supplierName,
        DateTime orderDate,
        PurchaseOrderStatus status = PurchaseOrderStatus.Draft,
        string? notes = null,
        long supplierId = 1)
    {
        var order = new PurchaseOrder(
            orderNumber,
            supplierId,
            supplierName,
            orderDate,
            expectedDeliveryDate: null,
            contactVia: null,
            notes: notes,
            createdBy: TestUser);

        if (status == PurchaseOrderStatus.Completed)
        {
            order.ChangeStatus(PurchaseOrderStatus.InTransit, TestUser);
            order.ChangeStatus(PurchaseOrderStatus.Completed, TestUser);
        }
        else if (status != PurchaseOrderStatus.Draft)
        {
            order.ChangeStatus(status, TestUser);
        }

        return order;
    }

    private static async Task<InMemoryPurchaseOrderRepository> SeedAsync(params PurchaseOrder[] orders)
    {
        var repo = new InMemoryPurchaseOrderRepository();
        foreach (var order in orders)
        {
            await repo.AddAsync(order);
        }
        return repo;
    }

    private static GetPurchaseOrdersHandler NewHandler(IPurchaseOrderRepository repo) =>
        new(NullLogger<GetPurchaseOrdersHandler>.Instance, repo);

    [Fact]
    public async Task Handle_WithNoFilters_ReturnsAllOrders()
    {
        var repo = await SeedAsync(
            NewOrder("PO-001", "Acme", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
            NewOrder("PO-002", "Globex", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)));
        var handler = NewHandler(repo);

        var result = await handler.Handle(new GetPurchaseOrdersRequest(), CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Orders.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithSearchTerm_FiltersByOrderNumberOrNotes()
    {
        var repo = await SeedAsync(
            NewOrder("PO-AAA", "Acme", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), notes: "winter batch"),
            NewOrder("PO-BBB", "Globex", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), notes: "summer batch"));
        var handler = NewHandler(repo);

        var result = await handler.Handle(
            new GetPurchaseOrdersRequest { SearchTerm = "AAA" },
            CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Orders.Single().OrderNumber.Should().Be("PO-AAA");
    }

    [Fact]
    public async Task Handle_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        var repo = await SeedAsync(
            NewOrder("PO-001", "Acme", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), status: PurchaseOrderStatus.Draft),
            NewOrder("PO-002", "Globex", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), status: PurchaseOrderStatus.InTransit));
        var handler = NewHandler(repo);

        var result = await handler.Handle(
            new GetPurchaseOrdersRequest { Status = "InTransit" },
            CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Orders.Single().Status.Should().Be("InTransit");
    }

    [Fact]
    public async Task Handle_WithDateRange_FiltersByOrderDate()
    {
        var repo = await SeedAsync(
            NewOrder("PO-OLD", "Acme", new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            NewOrder("PO-MID", "Acme", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)),
            NewOrder("PO-NEW", "Acme", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        var handler = NewHandler(repo);

        var result = await handler.Handle(
            new GetPurchaseOrdersRequest
            {
                FromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Orders.Single().OrderNumber.Should().Be("PO-MID");
    }

    [Fact]
    public async Task Handle_WithActiveOrdersOnly_ExcludesCompleted()
    {
        var repo = await SeedAsync(
            NewOrder("PO-DRAFT", "Acme", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), status: PurchaseOrderStatus.Draft),
            NewOrder("PO-DONE", "Globex", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), status: PurchaseOrderStatus.Completed));
        var handler = NewHandler(repo);

        var result = await handler.Handle(
            new GetPurchaseOrdersRequest { ActiveOrdersOnly = true },
            CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Orders.Single().OrderNumber.Should().Be("PO-DRAFT");
    }

    [Fact]
    public async Task Handle_ReturnsPaginationMetadata()
    {
        var orders = Enumerable.Range(1, 25)
            .Select(i => NewOrder($"PO-{i:D3}", "Acme", new DateTime(2026, 1, i % 28 + 1, 0, 0, 0, DateTimeKind.Utc)))
            .ToArray();
        var repo = await SeedAsync(orders);
        var handler = NewHandler(repo);

        var result = await handler.Handle(
            new GetPurchaseOrdersRequest { PageNumber = 2, PageSize = 10 },
            CancellationToken.None);

        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.Orders.Should().HaveCount(10);
    }
}
