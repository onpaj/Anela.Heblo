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
        DateOnly? plannedDate = null,
        params string?[] productLots)
    {
        return new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = createdDate ?? DateTime.UtcNow,
            CreatedByUser = "test",
            PlannedDate = plannedDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
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

        var matchingOrder = CreateOrder("MO-001", semiLot: null, createdDate: null, plannedDate: null, "LOT456");
        var otherOrder = CreateOrder("MO-002", semiLot: null, createdDate: null, plannedDate: null, "DIFFERENT");
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
        var orderWithProductLot = CreateOrder("MO-002", semiLot: null, createdDate: null, plannedDate: null, "LOT456");
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

    [Fact]
    public async Task GetOrdersAsync_WithDateFromFilter_ReturnsOnlyOrdersOnOrAfterDate()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var earlyDate = new DateOnly(2024, 1, 1);
        var lateDate = new DateOnly(2024, 6, 1);
        var filterDate = new DateOnly(2024, 3, 1);

        context.ManufactureOrders.AddRange(
            CreateOrder("MO-001", plannedDate: earlyDate),
            CreateOrder("MO-002", plannedDate: lateDate));
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(dateFrom: filterDate);

        items.Should().HaveCount(1);
        items[0].OrderNumber.Should().Be("MO-002");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_WithDateToFilter_ReturnsOnlyOrdersOnOrBeforeDate()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var earlyDate = new DateOnly(2024, 1, 1);
        var lateDate = new DateOnly(2024, 6, 1);
        var filterDate = new DateOnly(2024, 3, 1);

        context.ManufactureOrders.AddRange(
            CreateOrder("MO-001", plannedDate: earlyDate),
            CreateOrder("MO-002", plannedDate: lateDate));
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(dateTo: filterDate);

        items.Should().HaveCount(1);
        items[0].OrderNumber.Should().Be("MO-001");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_WithDateRangeFilter_TotalCountMatchesFilteredItemsNotAll()
    {
        await using var context = CreateContext();
        var repository = new ManufactureOrderRepository(context);

        var from = new DateOnly(2024, 3, 1);
        var to = new DateOnly(2024, 5, 31);

        context.ManufactureOrders.AddRange(
            CreateOrder("MO-001", plannedDate: new DateOnly(2024, 1, 1)),
            CreateOrder("MO-002", plannedDate: new DateOnly(2024, 4, 1)),
            CreateOrder("MO-003", plannedDate: new DateOnly(2024, 4, 15)),
            CreateOrder("MO-004", plannedDate: new DateOnly(2024, 12, 1)));
        await context.SaveChangesAsync();

        var (items, totalCount) = await repository.GetOrdersAsync(dateFrom: from, dateTo: to);

        items.Should().HaveCount(2);
        totalCount.Should().Be(2);
    }
}
