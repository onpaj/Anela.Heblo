using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogManufactureCatalogSourceAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private IManufactureCatalogSource CreateAdapter() =>
        new CatalogManufactureCatalogSourceAdapter(_repository.Object);

    [Fact]
    public async Task GetByIdAsync_ForwardsCallAndReturnsRepositoryResult()
    {
        var ct = new CancellationTokenSource().Token;
        var expected = new CatalogAggregate { ProductCode = "ABC", ProductName = "Test" };
        _repository.Setup(r => r.GetByIdAsync("ABC", ct)).ReturnsAsync(expected);

        var result = await CreateAdapter().GetByIdAsync("ABC", ct);

        result.Should().BeSameAs(expected);
        _repository.Verify(r => r.GetByIdAsync("ABC", ct), Times.Once);
    }

    [Fact]
    public async Task GetByIdsAsync_ForwardsCallAndReturnsRepositoryResult()
    {
        var ct = new CancellationTokenSource().Token;
        var ids = new[] { "A", "B" };
        IReadOnlyDictionary<string, CatalogAggregate> expected = new Dictionary<string, CatalogAggregate>
        {
            ["A"] = new CatalogAggregate { ProductCode = "A" },
            ["B"] = new CatalogAggregate { ProductCode = "B" },
        };
        _repository.Setup(r => r.GetByIdsAsync(ids, ct)).ReturnsAsync(expected);

        var result = await CreateAdapter().GetByIdsAsync(ids, ct);

        result.Should().BeSameAs(expected);
        _repository.Verify(r => r.GetByIdsAsync(ids, ct), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ForwardsCallAndReturnsRepositoryResult()
    {
        var ct = new CancellationTokenSource().Token;
        IEnumerable<CatalogAggregate> expected = new[]
        {
            new CatalogAggregate { ProductCode = "A" },
            new CatalogAggregate { ProductCode = "B" },
        };
        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(expected);

        var result = await CreateAdapter().GetAllAsync(ct);

        result.Should().BeSameAs(expected);
        _repository.Verify(r => r.GetAllAsync(ct), Times.Once);
    }
}
