using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Manufacture;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Manufacture;

public class ManufactureOrderRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ManufactureOrderTests_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ManufactureOrder CreateOrder(
        string orderNumber,
        string? semiLot = null,
        DateTime? createdDate = null,
        params string?[] productLots)
    {
        return new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = createdDate ?? DateTime.UtcNow,
            CreatedByUser = "test",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "test",
            SemiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = "SP-" + orderNumber,
                ProductName = "Semi " + orderNumber,
                PlannedQuantity = 1,
                ActualQuantity = 1,
                BatchMultiplier = 1,
                ExpirationMonths = 12,
                LotNumber = semiLot,
            },
            Products = productLots.Select((lot, i) => new ManufactureOrderProduct
            {
                ProductCode = $"P-{orderNumber}-{i}",
                ProductName = $"Product {orderNumber} {i}",
                SemiProductCode = "SP-" + orderNumber,
                PlannedQuantity = 1,
                ActualQuantity = 1,
                LotNumber = lot,
            }).ToList(),
        };
    }

    [Fact]
    public async Task GetOrdersAsync_WithLotFilter_MatchesSemiProductLot()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var matchingOrder = CreateOrder("MO-001", semiLot: "LOT123");
        var otherOrder = CreateOrder("MO-002", semiLot: "DIFFERENT");
        context.ManufactureOrders.AddRange(matchingOrder, otherOrder);
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(lotNumber: "LOT123");

        items.Should().HaveCount(1);
        items[0].OrderNumber.Should().Be("MO-001");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_WithLotFilter_MatchesProductLot()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var matchingOrder = CreateOrder("MO-001", semiLot: null, createdDate: null, "LOT456");
        var otherOrder = CreateOrder("MO-002", semiLot: null, createdDate: null, "DIFFERENT");
        context.ManufactureOrders.AddRange(matchingOrder, otherOrder);
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(lotNumber: "LOT456");

        items.Should().HaveCount(1);
        items[0].OrderNumber.Should().Be("MO-001");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_WithPartialLotFilter_MatchesBothSemiProductAndProductLots()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var orderWithSemiLot = CreateOrder("MO-001", semiLot: "LOT123");
        var orderWithProductLot = CreateOrder("MO-002", semiLot: null, createdDate: null, "LOT456");
        var orderWithNoLot = CreateOrder("MO-003", semiLot: null);
        context.ManufactureOrders.AddRange(orderWithSemiLot, orderWithProductLot, orderWithNoLot);
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(lotNumber: "LOT");

        items.Should().HaveCount(2);
        items.Select(x => x.OrderNumber).Should().Contain(new[] { "MO-001", "MO-002" });
        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrdersAsync_WithLotFilter_ReturnsEmptyWhenNoMatch()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var order = CreateOrder("MO-001", semiLot: "LOT123");
        context.ManufactureOrders.Add(order);
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(lotNumber: "NONEXISTENT");

        items.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_Pagination_ReturnsCorrectPage()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 5; i++)
        {
            context.ManufactureOrders.Add(CreateOrder($"MO-{i:D3}", createdDate: baseDate.AddDays(i)));
        }

        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(pageNumber: 2, pageSize: 2);

        // Ordered by CreatedDate descending: MO-005, MO-004, [MO-003, MO-002], MO-001
        items.Should().HaveCount(2);
        items[0].OrderNumber.Should().Be("MO-003");
        items[1].OrderNumber.Should().Be("MO-002");
        totalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetOrdersAsync_Pagination_TotalCountReflectsAllMatchingOrders()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 5; i++)
        {
            context.ManufactureOrders.Add(CreateOrder($"MO-{i:D3}", createdDate: baseDate.AddDays(i)));
        }

        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(pageNumber: 2, pageSize: 2);

        items.Should().HaveCount(2);
        totalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetOrdersAsync_DefaultPagination_ReturnsFirstPageWithSize20()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 25; i++)
        {
            context.ManufactureOrders.Add(CreateOrder($"MO-{i:D3}", createdDate: baseDate.AddDays(i)));
        }

        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync();

        items.Should().HaveCount(20);
        totalCount.Should().Be(25);
    }

    // -----------------------------------------------------------------------
    // Tests verifying fix for issue #600:
    //   - Count query is separate from includes (no extra joins)
    //   - AsNoTracking applied to data query
    //   - Notes not loaded in list query
    //   - PlannedDate date range filter works correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetOrdersAsync_WithDateFromFilter_ReturnsOnlyOrdersOnOrAfterDate()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var order1 = CreateOrder("MO-001");
        order1.PlannedDate = new DateOnly(2024, 3, 1);

        var order2 = CreateOrder("MO-002");
        order2.PlannedDate = new DateOnly(2024, 4, 1);

        var order3 = CreateOrder("MO-003");
        order3.PlannedDate = new DateOnly(2024, 5, 1);

        context.ManufactureOrders.AddRange(order1, order2, order3);
        await context.SaveChangesAsync();

        var dateFrom = new DateOnly(2024, 4, 1);
        var (items, totalCount) = await repository.GetOrdersAsync(dateFrom: dateFrom);

        items.Should().HaveCount(2);
        items.Select(x => x.OrderNumber).Should().Contain(new[] { "MO-002", "MO-003" });
        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrdersAsync_WithDateToFilter_ReturnsOnlyOrdersOnOrBeforeDate()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var order1 = CreateOrder("MO-001");
        order1.PlannedDate = new DateOnly(2024, 3, 1);

        var order2 = CreateOrder("MO-002");
        order2.PlannedDate = new DateOnly(2024, 4, 1);

        var order3 = CreateOrder("MO-003");
        order3.PlannedDate = new DateOnly(2024, 5, 1);

        context.ManufactureOrders.AddRange(order1, order2, order3);
        await context.SaveChangesAsync();

        var dateTo = new DateOnly(2024, 4, 1);
        var (items, totalCount) = await repository.GetOrdersAsync(dateTo: dateTo);

        items.Should().HaveCount(2);
        items.Select(x => x.OrderNumber).Should().Contain(new[] { "MO-001", "MO-002" });
        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrdersAsync_WithDateRangeFilter_TotalCountMatchesFilteredItems()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        // 5 orders spread across a range; only 3 fall in the filter window
        for (var i = 1; i <= 5; i++)
        {
            var order = CreateOrder($"MO-{i:D3}");
            order.PlannedDate = new DateOnly(2024, i, 15);
            context.ManufactureOrders.Add(order);
        }

        await context.SaveChangesAsync();

        var dateFrom = new DateOnly(2024, 2, 1);
        var dateTo = new DateOnly(2024, 4, 30);

        var (items, totalCount) = await repository.GetOrdersAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Orders in Feb, Mar, Apr → MO-002, MO-003, MO-004
        items.Should().HaveCount(3);
        totalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetOrdersAsync_ListResult_DoesNotLoadNotes()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var order = CreateOrder("MO-001");
        context.ManufactureOrders.Add(order);
        await context.SaveChangesAsync();

        // Add a note via direct context manipulation to avoid touching the repository
        var note = new ManufactureOrderNote
        {
            ManufactureOrderId = order.Id,
            Text = "Test note",
            CreatedAt = DateTime.UtcNow,
            CreatedByUser = "test",
        };
        context.Set<ManufactureOrderNote>().Add(note);
        await context.SaveChangesAsync();

        // Clear change tracker so the context does not serve cached data
        context.ChangeTracker.Clear();

        var (items, _) = await repository.GetOrdersAsync();

        items.Should().HaveCount(1);
        // Notes collection must not be populated in the list query to avoid
        // the N+1 / unnecessary data loading issue described in #600.
        items[0].Notes.Should().BeEmpty("the list query must not eagerly load Notes");
    }

    [Fact]
    public async Task GetOrdersAsync_CountIsCorrectRegardlessOfPageSize()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 10; i++)
        {
            context.ManufactureOrders.Add(CreateOrder($"MO-{i:D3}", createdDate: baseDate.AddDays(i)));
        }

        await context.SaveChangesAsync();

        // Request page 1 with size 3 — only 3 items returned but total must be 10
        var (items, totalCount) = await repository.GetOrdersAsync(pageNumber: 1, pageSize: 3);

        items.Should().HaveCount(3);
        totalCount.Should().Be(10, "CountAsync must reflect all matching rows, not just the current page");
    }
}
