using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ListLotsHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly ListLotsHandler _handler;

    public ListLotsHandlerTests()
    {
        _handler = new ListLotsHandler(NullLogger<ListLotsHandler>.Instance, _lotRepo.Object);
    }

    [Fact]
    public async Task Handle_FilterByMaterialCode_DelegatesToRepository()
    {
        // Arrange
        var pagedResult = new PagedResult<Lot>
        {
            Items = new List<Lot> { new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };
        _lotRepo.Setup(r => r.GetPaginatedAsync("MAT001", null, null, 1, 20, default)).ReturnsAsync(pagedResult);

        var request = new ListLotsRequest { MaterialCode = "MAT001", Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Lots);
    }
}
