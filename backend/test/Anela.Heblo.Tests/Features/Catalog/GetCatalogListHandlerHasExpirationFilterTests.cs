using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogList;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Tests for the HasExpiration filter in GetCatalogListHandler
/// (raw materials with expiration vs packaging and labels without).
/// </summary>
public class GetCatalogListHandlerHasExpirationFilterTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();
    private readonly GetCatalogListHandler _handler;
    private readonly List<CatalogAggregate> _items;

    public GetCatalogListHandlerHasExpirationFilterTests()
    {
        var mapperMock = new Mock<IMapper>();
        mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns((List<CatalogAggregate> src) => src.Select(x => new CatalogItemDto { ProductCode = x.ProductCode }).ToList());
        _handler = new GetCatalogListHandler(_catalogRepositoryMock.Object, mapperMock.Object);

        _items = new List<CatalogAggregate>
        {
            CreateItem("OLE001", hasExpiration: true),
            CreateItem("ETI001", hasExpiration: false),
            CreateItem("DOP001", hasExpiration: false)
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken _) => _items.Where(predicate.Compile()).ToList());
    }

    [Fact]
    public async Task Handle_HasExpirationTrue_ReturnsOnlyItemsWithExpiration()
    {
        var result = await _handler.Handle(new GetCatalogListRequest { HasExpiration = true }, CancellationToken.None);

        Assert.Equal(new[] { "OLE001" }, result.Items.Select(i => i.ProductCode).ToArray());
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Handle_HasExpirationFalse_ReturnsOnlyItemsWithoutExpiration()
    {
        var result = await _handler.Handle(new GetCatalogListRequest { HasExpiration = false }, CancellationToken.None);

        Assert.Equal(new[] { "DOP001", "ETI001" }, result.Items.Select(i => i.ProductCode).OrderBy(c => c).ToArray());
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Handle_HasExpirationNull_ReturnsAllItems()
    {
        var result = await _handler.Handle(new GetCatalogListRequest(), CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
    }

    private static CatalogAggregate CreateItem(string code, bool hasExpiration) => new()
    {
        ProductCode = code,
        HasExpiration = hasExpiration
    };
}
