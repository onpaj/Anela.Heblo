using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class UpdateProductCompositionOrderHandlerTests
{
    private readonly Mock<IProductIngredientOrderRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly Mock<ILogger<UpdateProductCompositionOrderHandler>> _loggerMock = new();
    private readonly UpdateProductCompositionOrderHandler _handler;

    public UpdateProductCompositionOrderHandlerTests()
    {
        _userMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Tester", "t@e.cz", true));

        _handler = new UpdateProductCompositionOrderHandler(
            _repoMock.Object,
            _userMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoExistingRows_CreatesAllRequestedRows()
    {
        _repoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>());

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "A", SortOrder = 1 },
                new() { IngredientProductCode = "B", SortOrder = 2 },
            }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(2);
        _repoMock.Verify(
            x => x.CreateAsync(It.IsAny<ProductIngredientOrder>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_ExistingRowsUpdated_NoDeletes()
    {
        _repoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { Id = 1, ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 5 },
                new() { Id = 2, ParentProductCode = "PRD1", IngredientProductCode = "B", SortOrder = 6 },
            });

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "A", SortOrder = 1 },
                new() { IngredientProductCode = "B", SortOrder = 2 },
            }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        _repoMock.Verify(
            x => x.UpdateAsync(It.IsAny<ProductIngredientOrder>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _repoMock.Verify(
            x => x.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ObsoleteRows_AreDeleted()
    {
        _repoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { Id = 1, ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 1 },
                new() { Id = 2, ParentProductCode = "PRD1", IngredientProductCode = "OBSOLETE", SortOrder = 2 },
            });

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "A", SortOrder = 1 },
            }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        _repoMock.Verify(x => x.DeleteAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }
}
