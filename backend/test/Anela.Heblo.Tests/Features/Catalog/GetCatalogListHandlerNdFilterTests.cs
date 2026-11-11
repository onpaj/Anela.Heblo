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
/// Tests for the IsNd ("Nesmí dojít") filter in GetCatalogListHandler.
/// </summary>
public class GetCatalogListHandlerNdFilterTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();
    private readonly GetCatalogListHandler _handler;
    private readonly List<CatalogAggregate> _items;

    public GetCatalogListHandlerNdFilterTests()
    {
        var mapperMock = new Mock<IMapper>();
        mapperMock.Setup(m => m.Map<List<CatalogItemDto>>(It.IsAny<List<CatalogAggregate>>()))
            .Returns((List<CatalogAggregate> src) => src.Select(x => new CatalogItemDto { ProductCode = x.ProductCode }).ToList());
        _handler = new GetCatalogListHandler(_catalogRepositoryMock.Object, mapperMock.Object);

        _items = new List<CatalogAggregate>
        {
            CreateItem("ND1", isNd: true),
            CreateItem("ND2", isNd: true),
            CreateItem("OTHER", isNd: false)
        };

        _catalogRepositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken _) => _items.Where(predicate.Compile()).ToList());
    }

    [Fact]
    public async Task Handle_IsNdTrue_ReturnsOnlyNdItems()
    {
        var result = await _handler.Handle(new GetCatalogListRequest { IsNd = true }, CancellationToken.None);

        Assert.Equal(new[] { "ND1", "ND2" }, result.Items.Select(i => i.ProductCode).OrderBy(c => c).ToArray());
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Handle_IsNdFalse_ReturnsOnlyNonNdItems()
    {
        var result = await _handler.Handle(new GetCatalogListRequest { IsNd = false }, CancellationToken.None);

        Assert.Equal(new[] { "OTHER" }, result.Items.Select(i => i.ProductCode).ToArray());
    }

    [Fact]
    public async Task Handle_IsNdNull_ReturnsAllItems()
    {
        var result = await _handler.Handle(new GetCatalogListRequest(), CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
    }

    private static CatalogAggregate CreateItem(string code, bool isNd) => new()
    {
        ProductCode = code,
        Properties = new CatalogProperties { IsNd = isNd }
    };
}
