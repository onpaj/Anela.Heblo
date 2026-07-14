using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogList;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using AutoMapper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Tests for the ExpirationFrom / ExpirationTo range filtering in GetCatalogListHandler.
/// </summary>
public class GetCatalogListHandlerExpirationFilterTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetCatalogListHandler _handler;

    public GetCatalogListHandlerExpirationFilterTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetCatalogListHandler(_catalogRepositoryMock.Object, _mapperMock.Object);

        // Project by input so the applied filter is genuinely asserted
        _mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns((List<CatalogAggregate> src) => src.Select(x => new CatalogItemDto { ProductCode = x.ProductCode }).ToList());
    }

    [Fact]
    public async Task Handle_ExpirationRange_ReturnsOnlyItemsWithExpirationInRange()
    {
        // Arrange
        var items = new List<CatalogAggregate>
        {
            CreateItemWithLot("BEFORE", new DateOnly(2026, 1, 1)),   // before range
            CreateItemWithLot("IN1", new DateOnly(2026, 3, 15)),     // in range
            CreateItemWithLot("IN2", new DateOnly(2026, 4, 30)),     // in range (inclusive upper)
            CreateItemWithLot("AFTER", new DateOnly(2026, 6, 1)),    // after range
            CreateItemWithLot("NONE")                                // no expiration -> excluded
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            ExpirationFrom = "2026-03-01",
            ExpirationTo = "2026-04-30"
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken _) => items.Where(predicate.Compile()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(new[] { "IN1", "IN2" }, result.Items.Select(i => i.ProductCode).OrderBy(c => c).ToArray());
    }

    [Fact]
    public async Task Handle_ExpirationToOnly_ReturnsExpiredItems_ExcludingNulls()
    {
        // Arrange - "Po expiraci" preset: only an upper bound
        var items = new List<CatalogAggregate>
        {
            CreateItemWithLot("EXPIRED", new DateOnly(2000, 12, 31)),
            CreateItemWithLot("FUTURE", new DateOnly(2030, 1, 1)),
            CreateItemWithLot("NONE")
        };

        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            ExpirationTo = "2026-07-13"
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken _) => items.Where(predicate.Compile()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(new[] { "EXPIRED" }, result.Items.Select(i => i.ProductCode).ToArray());
    }

    [Fact]
    public async Task Handle_NoExpirationFilter_ReturnsAllItemsIncludingNulls()
    {
        // Arrange
        var items = new List<CatalogAggregate>
        {
            CreateItemWithLot("WITH", new DateOnly(2026, 5, 1)),
            CreateItemWithLot("NONE")
        };

        var request = new GetCatalogListRequest { PageNumber = 1, PageSize = 10 };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken _) => items.Where(predicate.Compile()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);
    }

    private static CatalogAggregate CreateItemWithLot(string productCode, DateOnly? expiration = null)
    {
        var item = new CatalogAggregate
        {
            Id = productCode,
            ProductName = productCode,
            Type = ProductType.Material
        };

        if (expiration.HasValue)
        {
            item.Stock.Lots = new List<CatalogLot>
            {
                new() { ProductCode = productCode, Amount = 5, Expiration = expiration }
            };
        }

        return item;
    }
}
