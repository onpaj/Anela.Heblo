using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ListEansHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly ListEansHandler _handler;

    public ListEansHandlerTests()
    {
        _handler = new ListEansHandler(NullLogger<ListEansHandler>.Instance, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_FilterByLotId_DelegatesToRepository()
    {
        // Arrange
        var paged = new PagedResult<Ean>
        {
            Items = new List<Ean> { new Ean("INT-00000001", 5, 25m, "kg", "user") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };
        _eanRepo.Setup(r => r.GetPaginatedAsync(5, null, 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListEansRequest { LotId = 5, Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Eans);
    }
}
